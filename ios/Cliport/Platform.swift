// Cliport v1.0 (iOS) - background keep-alive, notifications, crash recovery, Telegram storage.
import Foundation
import AVFoundation
import UserNotifications
import UIKit

/// Keeps the process (and its relay connections) alive in the background by playing silence.
/// iOS has no official "run forever" mode for sideloaded apps without push; this is the standard workaround.
final class KeepAlive {
    static let shared = KeepAlive()
    private var player: AVAudioPlayer?
    private var wanted = false

    private init() {
        let nc = NotificationCenter.default
        nc.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] n in
            guard let self = self, self.wanted else { return }
            let raw = (n.userInfo?[AVAudioSessionInterruptionTypeKey] as? NSNumber)?.uintValue ?? 0
            if raw == AVAudioSession.InterruptionType.ended.rawValue { self.start() }
        }
        nc.addObserver(forName: AVAudioSession.mediaServicesWereResetNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self = self, self.wanted else { return }
            self.player = nil; self.start()
        }
    }
    func start() {
        wanted = true
        do {
            let s = AVAudioSession.sharedInstance()
            try s.setCategory(.playback, mode: .default, options: [.mixWithOthers])
            try s.setActive(true)
            if player == nil {
                let p = try AVAudioPlayer(data: KeepAlive.silentWav())
                p.numberOfLoops = -1
                player = p
            }
            if player?.isPlaying != true { player?.play() }
        } catch { Log.write("keep-alive failed: \(error.localizedDescription)") }
    }
    func stop() {
        wanted = false
        player?.stop()
    }
    private static func silentWav() -> Data {
        let rate: UInt32 = 8000, dataLen: UInt32 = 16000
        var d = Data()
        func u32(_ v: UInt32) { d.append(contentsOf: [UInt8(v & 255), UInt8((v >> 8) & 255), UInt8((v >> 16) & 255), UInt8((v >> 24) & 255)]) }
        func u16(_ v: UInt16) { d.append(contentsOf: [UInt8(v & 255), UInt8(v >> 8)]) }
        d.append(contentsOf: Array("RIFF".utf8)); u32(36 + dataLen); d.append(contentsOf: Array("WAVE".utf8))
        d.append(contentsOf: Array("fmt ".utf8)); u32(16); u16(1); u16(1); u32(rate); u32(rate * 2); u16(2); u16(16)
        d.append(contentsOf: Array("data".utf8)); u32(dataLen)
        d.append(Data(count: Int(dataLen)))
        return d
    }
}

enum Notifier {
    static let offerCategory = "FILE_OFFER"
    static func setup() {
        let c = UNUserNotificationCenter.current()
        c.requestAuthorization(options: [.alert, .sound]) { _, _ in }
        let accept = UNNotificationAction(identifier: "ACCEPT", title: L.t("אשר הדבקה", "Allow paste"), options: [])
        let decline = UNNotificationAction(identifier: "DECLINE", title: L.t("דחה", "Decline"), options: [.destructive])
        c.setNotificationCategories([UNNotificationCategory(identifier: offerCategory, actions: [accept, decline], intentIdentifiers: [], options: [])])
    }
    private static func post(id: String, title: String, body: String, category: String? = nil, after: TimeInterval = 0) {
        let content = UNMutableNotificationContent()
        content.title = title; content.body = body; content.sound = .default
        if let c = category { content.categoryIdentifier = c }
        let trigger: UNNotificationTrigger? = after > 0 ? UNTimeIntervalNotificationTrigger(timeInterval: after, repeats: false) : nil
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: trigger), withCompletionHandler: nil)
    }
    /// Something arrived while the app is in the background and iOS did not let it onto the pasteboard.
    /// One silent notification (replaced each time, no content shown on the lock screen); tapping it opens the app, which applies the item.
    static func clip(_ from: String) {
        let content = UNMutableNotificationContent()
        content.title = L.t("פריט חדש מ-\(from)", "New item from \(from)")
        content.body = L.t("הקש כדי להעביר אותו ללוח של המכשיר.", "Tap to put it on this device's clipboard.")
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: "clip", content: content, trigger: nil), withCompletionHandler: nil)
    }
    static func clearClip() { UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: ["clip"]) }
    static func info(_ title: String, _ body: String) { post(id: "info-" + AppInfo.randHex(4), title: title, body: body) }
    static func fileOffer(_ o: Offer) {
        let f = o.files[0]
        let more = o.files.count > 1 ? " (+\(o.files.count - 1))" : ""
        post(id: "offer", title: L.t("לאשר הדבקת קובץ מ-\(o.fromName)?", "Allow pasting a file from \(o.fromName)?"),
             body: L.t("שם: \(f.name)\(more)\nגודל: \(AppInfo.sizeText(o.total))\nסוג: \(Mime.label(f.name))", "Name: \(f.name)\(more)\nSize: \(AppInfo.sizeText(o.total))\nType: \(Mime.label(f.name))"), category: offerCategory)
    }
    /// Dead-man's switch: a notification scheduled 2.5 minutes ahead and pushed forward while we are alive.
    /// If the app crashes or iOS stops it, the notification fires and one tap brings the sync back.
    static func armDeadman() {
        post(id: "deadman", title: L.t("הסנכרון של Cliport נעצר", "Cliport sync has stopped"), body: L.t("האפליקציה הופסקה ברקע. הקש כדי לחדש את הסנכרון.", "The app was stopped in the background. Tap to resume sync."), after: 150)
    }
    static func cancelDeadman() { UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: ["deadman"]) }
}

/// Crash recovery with escalating levels (mirrors the Windows app as far as iOS allows):
///  0. relay links reconnect by themselves, stale links are replaced, the audio session restarts after interruptions.
///  1. after an unclean stop: reconnect + notification (iOS forbids self-relaunch, so the dead-man notification asks for one tap).
///  2. repeated crashes: caches and history are reset.
///  3. still crashing: settings are rebuilt, pairing kept.
///  4. still crashing: safe mode - background keep-alive is switched off until the next clean run.
enum Recovery {
    static var level = 0
    static var safeMode = false
    static var notice: String?
    private static let ud = UserDefaults.standard

    static func onLaunch() {
        let now = Date().timeIntervalSince1970
        var crashes = (ud.array(forKey: "crashes") as? [Double] ?? []).filter { now - $0 < 15 * 60 }
        if ud.bool(forKey: "alive") {
            crashes.append(now)
            let n = crashes.count
            level = n <= 1 ? 1 : (n <= 3 ? 2 : (n <= 5 ? 3 : 4))
            Log.write("unclean stop detected, #\(n) in 15 min -> recovery level \(level)")
            if level >= 2 { Engine.shared.resetCaches() }
            if level >= 3 { Engine.shared.resetSettingsKeepingPairing() }
            if level >= 4 { safeMode = true }
            switch level {
            case 1: notice = L.t("האפליקציה נעצרה באופן לא תקין והסנכרון חודש.", "The app stopped unexpectedly and sync was resumed.")
            case 2: notice = L.t("זוהו קריסות חוזרות. המטמון וההיסטוריה אופסו והסנכרון חודש.", "Repeated crashes were detected. The cache and history were reset and sync was resumed.")
            case 3: notice = L.t("הקריסות נמשכו. ההגדרות נבנו מחדש (הצימוד נשמר).", "Crashes continued. Settings were rebuilt (pairing kept).")
            default: notice = L.t("מצב בטוח: הפעילות ברקע הושבתה זמנית עקב קריסות חוזרות.", "Safe mode: background activity is temporarily off after repeated crashes.")
            }
        }
        ud.set(crashes, forKey: "crashes")
        ud.set(true, forKey: "alive")
        NSSetUncaughtExceptionHandler { e in Log.write("FATAL: \(e.name.rawValue) \(e.reason ?? "")") }
    }
    static func markAlive(_ alive: Bool) { ud.set(alive, forKey: "alive") }
}

/// Optional encrypted blob storage for large files, through a private Telegram bot chat.
enum Telegram {
    private static func api(_ token: String, _ method: String, _ query: String = "") async throws -> [String: Any] {
        guard let url = URL(string: "https://api.telegram.org/bot\(token)/\(method)\(query.isEmpty ? "" : "?" + query)") else { throw URLError(.badURL) }
        let (data, _) = try await URLSession.shared.data(from: url)
        return J.decode(data) ?? [:]
    }
    static func findChat(token: String) async throws -> String? {
        let d = try await api(token, "getUpdates", "limit=20")
        var chat: String?
        for u in J.list(d, "result") {
            let msg = J.d(u, "message") ?? J.d(u, "channel_post")
            if let c = J.d(msg, "chat") { chat = J.s(c, "id") }
        }
        return chat
    }
    static func upload(token: String, chat: String, data: Data, name: String) async throws -> String {
        guard let url = URL(string: "https://api.telegram.org/bot\(token)/sendDocument") else { throw URLError(.badURL) }
        let boundary = "----sc" + AppInfo.randHex(12)
        var rq = URLRequest(url: url)
        rq.httpMethod = "POST"; rq.timeoutInterval = 600
        rq.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        var body = Data()
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"chat_id\"\r\n\r\n\(chat)\r\n".utf8))
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"disable_notification\"\r\n\r\ntrue\r\n".utf8))
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"document\"; filename=\"\(name)\"\r\nContent-Type: application/octet-stream\r\n\r\n".utf8))
        body.append(data)
        body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        let (resp, _) = try await URLSession.shared.upload(for: rq, from: body)
        let id = J.s(J.d(J.d(J.decode(resp), "result"), "document"), "file_id")
        if id.isEmpty { throw NSError(domain: "sc", code: 3, userInfo: [NSLocalizedDescriptionKey: "Telegram: no file_id"]) }
        return id
    }
    static func download(token: String, fileId: String) async throws -> Data {
        let q = "file_id=" + (fileId.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? fileId)
        let d = try await api(token, "getFile", q)
        let path = J.s(J.d(d, "result"), "file_path")
        guard !path.isEmpty, let url = URL(string: "https://api.telegram.org/file/bot\(token)/\(path)") else {
            throw NSError(domain: "sc", code: 4, userInfo: [NSLocalizedDescriptionKey: "Telegram: no file_path"])
        }
        let (data, _) = try await URLSession.shared.data(from: url)
        return data
    }
}
