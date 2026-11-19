// Cliport v1.0 (iOS) - sync engine. Same protocol as the Windows app. All state lives on the main queue.
import Foundation
import UIKit
import CryptoKit
import Network

struct PinItem: Identifiable, Equatable {
    var id: String; var text: String; var ts: Int64; var del: Bool
}
struct FileEntry {
    var name: String; var size: Int64; var mime: String; var url: URL?
}
struct Offer: Identifiable {
    var fid: String; var from: String; var fromName: String; var image: Bool; var ts: Int64; var files: [FileEntry]
    var id: String { fid }
    var total: Int64 { files.reduce(0) { $0 + $1.size } }
}
struct DeviceInfo: Identifiable {
    var id: String
    var name = ""; var plat = ""; var ver = ""; var online = false; var ts: Int64 = 0
    var rdpOn = false; var rdpUser = ""; var rdpPort = 3389; var addrs: [String] = []
    var isOnline: Bool { online && abs(AppInfo.now() - ts) < 300_000 }
}

final class Engine: ObservableObject {
    static let shared = Engine()
    static let defaultRelays = ["broker.emqx.io:8883", "broker.hivemq.com:8883", "test.mosquitto.org:8886"]
    static let maxInlineText = 180_000
    static let maxRelayFile: Int64 = 25 * 1024 * 1024
    static let chunk = 48_000
    static let tgPart = 18 * 1024 * 1024

    @Published var paired = false
    @Published var upCount = 0
    @Published var relayCount = 0
    @Published var pins: [PinItem] = []
    @Published var history: [String] = []
    @Published var devices: [DeviceInfo] = []
    @Published var offer: Offer?
    @Published var offerReady: [URL] = []
    @Published var progress: Int = -1            // -1 = no transfer
    @Published var banner = ""
    @Published var paused = false
    /// "he" / "en" / "" (follow the device language)
    @Published var lang: String = UserDefaults.standard.string(forKey: "lang") ?? "" { didSet { UserDefaults.standard.set(lang, forKey: "lang") } }
    /// "light" / "dark" / "system" (follow the device)
    @Published var theme: String = UserDefaults.standard.string(forKey: "theme") ?? "system" { didSet { UserDefaults.standard.set(theme, forKey: "theme") } }

    private let ud = UserDefaults.standard
    private(set) var keys: KeyRing?
    private var links: [MqttLink] = []
    private var lastClipTs: Int64 = 0
    private var lastHash = ""
    private var lastChangeCount = -1
    private var pending: [String: Any]?
    private var lastOut: [String: Any]?
    private var pace: [String: Double] = [:]          // per-transfer chunk interval, grows when the receiver reports loss
    private var pathMonitor: NWPathMonitor?
    private var lastNetKick = Date.distantPast
    private var seen = Set<String>()
    private var seenQ: [String] = []
    private var outgoing: [String: Offer] = [:]
    private var outQ: [String] = []
    private var rxs: [String: Rx] = [:]
    private var lastDevPub: Int64 = 0
    private var timer: Timer?
    private var tickN = 0
    private var rr = 0
    var isActive = false

    private final class Rx {
        var offer: Offer; var idx: Int; var url: URL; var handle: FileHandle?
        var total = -1; var next = 0; var last = AppInfo.now(); var lastNak: Int64 = 0; var buf: [Int: Data] = [:]
        var done: (URL?, String?) -> Void
        init(offer: Offer, idx: Int, url: URL, done: @escaping (URL?, String?) -> Void) { self.offer = offer; self.idx = idx; self.url = url; self.done = done }
    }

    // ---------- identity / settings ----------
    var deviceId: String {
        if let v = ud.string(forKey: "deviceId") { return v }
        let v = AppInfo.randHex(8); ud.set(v, forKey: "deviceId"); return v
    }
    var deviceName: String { UIDevice.current.name }
    var tgToken: String { ud.string(forKey: "tgToken") ?? "" }
    var tgChat: String { ud.string(forKey: "tgChat") ?? "" }
    var tgReady: Bool { !tgToken.isEmpty && !tgChat.isEmpty }
    var relays: String { ud.string(forKey: "relays") ?? "" }
    var cfgTs: Int64 { Int64(ud.double(forKey: "cfgTs")) }
    var keepAlive: Bool {
        get { ud.object(forKey: "keepAlive") == nil ? true : ud.bool(forKey: "keepAlive") }
        set { ud.set(newValue, forKey: "keepAlive"); objectWillChange.send(); applyKeepAlive() }
    }

    private init() {
        lastClipTs = Int64(ud.double(forKey: "lastClipTs"))
        loadLocal()
    }

    // ---------- lifecycle ----------
    func start() {
        stopLinks()
        guard let code = ud.string(forKey: "code"), let k = KeyRing(code: code) else { paired = false; return }
        keys = k; paired = true
        let me = deviceId, base = k.topic
        var specs = relays.split(whereSeparator: { ",; \n".contains($0) }).map(String.init)
        specs.append(contentsOf: Engine.defaultRelays)
        for spec in specs {
            guard let l = MqttLink.parse(spec) else { continue }
            l.clientIdBase = "sc" + me
            l.subs = [base + "/ev", base + "/st/#", base + "/f/" + me]
            l.willTopic = base + "/st/dev/" + me
            l.willPayload = { [weak self] in
                guard let self = self, let k = self.keys else { return nil }
                var w = self.devMsg(false)
                w["will"] = true
                return k.seal(J.encode(self.stamp(w)))
            }
            l.onMessage = { [weak self] topic, payload in self?.onWire(topic, payload) }
            l.onState = { [weak self] link in
                guard let self = self else { return }
                self.refreshUp()
                if link.up {
                    self.publishDev()
                    self.reannounceClip()
                    if !self.pinStore.isEmpty { self.publishPins() }
                    if self.cfgTs > 0 { self.publishCfg() }
                }
            }
            links.append(l)
        }
        relayCount = links.count
        links.forEach { $0.start() }
        if timer == nil {
            timer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] _ in self?.tick() }
        }
        applyKeepAlive()
        if pathMonitor == nil {
            // Wi-Fi <-> cellular, VPN, coming back into coverage: old sockets are dead - reconnect at once
            let mon = NWPathMonitor()
            mon.pathUpdateHandler = { [weak self] path in
                DispatchQueue.main.async {
                    guard let self = self, path.status == .satisfied, Date().timeIntervalSince(self.lastNetKick) > 3 else { return }
                    self.lastNetKick = Date()
                    Log.write("network changed - reconnecting relays")
                    self.links.forEach { $0.kick() }
                }
            }
            lastNetKick = Date()                         // ignore the initial callback
            mon.start(queue: DispatchQueue.global(qos: .utility))
            pathMonitor = mon
        }
        Log.write("engine started, device \(me)")
    }
    /// A relay (re)connected: send what never left, otherwise re-announce our latest clip as retained state.
    /// (A write into an already-dead socket reports success; receivers ignore anything not newer than what they have.)
    private func reannounceClip() {
        if let p = pending { publishClip(p); return }
        if let l = lastOut, J.l(l, "ts") == lastClipTs { send("st/clip", l, retain: true) }
    }
    private func stopLinks() {
        if !links.isEmpty { send("st/dev/" + deviceId, devMsg(false), retain: true) }
        links.forEach { $0.stop() }
        links = []; refreshUp()
    }
    func pair(code: String) -> Bool {
        guard let k = KeyRing(code: code) else { return false }
        ud.set(k.code, forKey: "code"); start(); return true
    }
    func createGroup() { ud.set(KeyRing.newCode(), forKey: "code"); start() }
    func unpair() { stopLinks(); keys = nil; ud.removeObject(forKey: "code"); paired = false; devStore = [:]; devices = [] }
    private func refreshUp() { let n = links.filter { $0.up }.count; if n != upCount { upCount = n } }
    private func applyKeepAlive() {
        if paired && keepAlive && !Recovery.safeMode { KeepAlive.shared.start() } else { KeepAlive.shared.stop(); Notifier.cancelDeadman() }
    }

    func becameActive() {
        isActive = true
        Recovery.markAlive(true)
        links.forEach { $0.healIfStale() }
        checkLocalClipboard()
    }
    func enteredBackground() {
        isActive = false
        if !(keepAlive && paired) { Recovery.markAlive(false) }
    }

    private func tick() {
        tickN += 1
        if isActive { checkLocalClipboard() }
        requestMissingChunks()
        guard tickN % 4 == 0 else { return }                       // every ~6s
        links.forEach { $0.healIfStale() }
        refreshUp()
        let now = AppInfo.now()
        if now - lastDevPub > 60_000 { publishDev() }
        for (k, rx) in rxs where now - rx.last > 90_000 { finishRx(k, L.t("תם הזמן - המכשיר המקורי לא הגיב", "Timed out - the source device did not respond")) }
        if paired && keepAlive && tickN % 16 == 0 { Notifier.armDeadman() }   // dead-man's switch: fires only if we stop running
    }

    // ---------- wire ----------
    private func stamp(_ m: [String: Any]) -> [String: Any] {
        var m = m
        if m["id"] == nil { m["id"] = AppInfo.randHex(8) }
        m["from"] = deviceId
        if m["ts"] == nil { m["ts"] = AppInfo.now() }
        return m
    }
    @discardableResult
    private func send(_ sub: String, _ m: [String: Any], retain: Bool) -> Int {
        guard let k = keys else { return 0 }
        let payload = k.seal(J.encode(stamp(m)))
        var n = 0
        for l in links where l.publish(k.topic + "/" + sub, payload, retain: retain) { n += 1 }
        return n
    }
    private func sendOne(_ sub: String, _ m: [String: Any]) -> Bool {
        guard let k = keys else { return false }
        let payload = k.seal(J.encode(stamp(m)))
        let n = links.count
        if n == 0 { return false }
        rr = (rr + 1) % n                               // one relay per chunk, rotating: no single relay carries the whole load
        for i in 0..<n where links[(rr + i) % n].publish(k.topic + "/" + sub, payload, retain: false) { return true }
        return false
    }
    private func onWire(_ topic: String, _ payload: Data) {
        guard let k = keys, !payload.isEmpty, topic.count > k.topic.count + 1, topic.hasPrefix(k.topic) else { return }
        guard let plain = k.open(payload), let d = J.decode(plain) else { return }     // not ours / tampered
        let sub = String(topic.dropFirst(k.topic.count + 1))
        handle(sub, d)
    }
    private func handle(_ sub: String, _ d: [String: Any]) {
        let from = J.s(d, "from"), t = J.s(d, "t")
        if sub.hasPrefix("st/dev/") { onDev(String(sub.dropFirst(7)), d); return }
        if sub == "st/pins" { mergePins(d); return }
        if sub == "st/cfg" { mergeCfg(d); return }
        if from == deviceId { return }
        if sub == "ev" || sub.hasPrefix("f/") {
            let id = J.s(d, "id")
            if seen.contains(id) { return }
            seen.insert(id); seenQ.append(id)
            if seenQ.count > 1500 { seen.remove(seenQ.removeFirst()) }
        }
        switch t {
        case "clip": onClip(d)
        case "offer": onOffer(d)
        case "freq": if J.s(d, "to") == deviceId { serveFile(d) }
        case "fres": if J.s(d, "to") == deviceId { onFres(d) }
        case "fnak": if J.s(d, "to") == deviceId { serveNak(d) }
        case "fchunk": onChunk(d)
        default: break
        }
    }
    private func nameOf(_ id: String) -> String {
        if let n = devStore[id]?.name, !n.isEmpty { return n }
        return L.t("מכשיר אחר", "another device")
    }

    // ---------- clipboard ----------
    private static func hash(_ s: String) -> String {
        Data(SHA256.hash(data: Data(s.utf8))).prefix(12).base64EncodedString()
    }
    private func fresh(_ d: [String: Any]) -> Bool {
        let ts = J.l(d, "ts")
        if ts <= lastClipTs { return false }
        if AppInfo.now() - ts > 24 * 3600 * 1000 { return false }
        lastClipTs = ts; ud.set(Double(ts), forKey: "lastClipTs")
        return true
    }
    private func onClip(_ d: [String: Any]) {
        guard !paused, fresh(d) else { return }
        let text = J.s(d, "text")
        guard !text.isEmpty else { return }
        lastHash = Engine.hash(text)
        // Writing the pasteboard is allowed while we run in the background (kept alive); reading is not.
        UIPasteboard.general.string = text
        lastChangeCount = UIPasteboard.general.changeCount
        addHistory(text)
        banner = L.t("התקבל מ-", "Received from ") + nameOf(J.s(d, "from"))
        Log.write("clip in (\(text.count) chars)")
    }
    /// Foreground only (iOS does not let any app read the pasteboard in the background).
    func checkLocalClipboard() {
        guard paired, !paused else { return }
        let pb = UIPasteboard.general
        let cc = pb.changeCount
        guard cc != lastChangeCount else { return }
        lastChangeCount = cc
        if pb.hasStrings {
            if let s = pb.string, !s.isEmpty { localText(s) }
        } else if pb.hasImages {
            if let img = pb.image, let data = img.pngData() {
                let url = tempURL("image-\(AppInfo.now()).png")
                if (try? data.write(to: url)) != nil { localFiles([url], image: true) }
            }
        } else if let provider = pb.itemProviders.first, let type = provider.registeredTypeIdentifiers.first {
            let suggested = provider.suggestedName
            provider.loadFileRepresentation(forTypeIdentifier: type) { [weak self] url, _ in
                guard let self = self, let url = url else { return }
                var name = url.lastPathComponent
                if let s = suggested, !s.isEmpty { name = url.pathExtension.isEmpty || s.hasSuffix("." + url.pathExtension) ? s : s + "." + url.pathExtension }
                let dst = self.tempURL(name)
                try? FileManager.default.removeItem(at: dst)
                guard (try? FileManager.default.copyItem(at: url, to: dst)) != nil else { return }
                DispatchQueue.main.async { self.localFiles([dst], image: false) }
            }
        }
    }
    /// Manual "send now" button: forces a re-read of the pasteboard.
    func sendClipboardNow() { lastChangeCount = -1; lastHash = ""; checkLocalClipboard() }
    private func tempURL(_ name: String) -> URL {
        let dir = AppInfo.caches.appendingPathComponent("outgoing", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent(name)
    }
    func localText(_ text: String) {
        guard paired, !paused, !text.isEmpty else { return }
        let h = Engine.hash(text)
        if h == lastHash { return }
        addHistory(text)
        if text.utf8.count > Engine.maxInlineText {
            let url = tempURL("clipboard-text.txt")
            try? Data(text.utf8).write(to: url)
            localFiles([url], image: false); lastHash = h; return
        }
        lastHash = h
        publishClip(["t": "clip", "kind": "text", "text": text])
        banner = L.t("נשלח לכל המכשירים", "Sent to all devices")
        Log.write("clip out (\(text.count) chars)")
    }
    /// Copies an item (pin / history) to this device and to every other device.
    func copy(_ text: String) {
        UIPasteboard.general.string = text
        lastChangeCount = UIPasteboard.general.changeCount
        localText(text)
    }
    private func publishClip(_ msg: [String: Any]) {
        var m = msg
        if m["ts"] == nil {
            lastClipTs = max(AppInfo.now(), lastClipTs + 1); m["ts"] = lastClipTs; ud.set(Double(lastClipTs), forKey: "lastClipTs")
        }
        if m["id"] == nil { m["id"] = AppInfo.randHex(8) }
        let n = send("ev", m, retain: false)
        send("st/clip", m, retain: true)
        pending = n == 0 ? m : nil
        lastOut = m
    }
    /// Used by the Shortcuts action: connect if needed, send, give the socket a moment to flush.
    func sendFromIntent(text: String) async -> Bool {
        // Unchanged content is deliberately NOT re-sent: an automation that fires on every app switch
        // must never push an old clipboard over something newer copied on another device.
        await MainActor.run { if self.links.isEmpty { self.start() }; self.localText(text) }
        for _ in 0..<40 {
            let delivered = await MainActor.run { self.pending == nil && self.upCount > 0 }
            if delivered { break }
            try? await Task.sleep(nanoseconds: 200_000_000)
        }
        try? await Task.sleep(nanoseconds: 400_000_000)
        return await MainActor.run { self.pending == nil }
    }

    // ---------- history ----------
    private func addHistory(_ t: String) {
        let text = t.count > 100_000 ? String(t.prefix(100_000)) : t
        history.removeAll { $0 == text }
        history.insert(text, at: 0)
        if history.count > 60 { history.removeLast(history.count - 60) }
        ud.set(history, forKey: "history")
    }
    func removeHistory(_ text: String) { history.removeAll { $0 == text }; ud.set(history, forKey: "history") }

    // ---------- pins (last-writer-wins set with tombstones) ----------
    private var pinStore: [PinItem] = []
    func isPinned(_ text: String) -> Bool { pinStore.contains { !$0.del && $0.text == text } }
    func pin(_ text: String) {
        guard !text.isEmpty, !isPinned(text) else { return }
        let live = pinStore.filter { !$0.del }
        if text.count > 16_000 || live.count >= 60 || live.reduce(0, { $0 + $1.text.count }) + text.count > 60_000 { banner = L.t("לא ניתן לנעוץ: חריגה ממגבלת הנעוצים", "Cannot pin: the pin limit was reached"); return }
        pinStore.append(PinItem(id: AppInfo.randHex(6), text: text, ts: AppInfo.now(), del: false))
        pinsChanged(); publishPins()
    }
    func unpin(_ text: String) {
        var changed = false
        for i in pinStore.indices where !pinStore[i].del && pinStore[i].text == text {
            pinStore[i].del = true; pinStore[i].text = ""; pinStore[i].ts = AppInfo.now(); changed = true
        }
        if changed { pinsChanged(); publishPins() }
    }
    private func pinsChanged() {
        pins = pinStore.filter { !$0.del }.sorted { $0.ts > $1.ts }
        ud.set(pinStore.map { ["id": $0.id, "text": $0.text, "ts": Double($0.ts), "del": $0.del] as [String: Any] }, forKey: "pins")
    }
    private func pinsMsg() -> [String: Any] {
        ["t": "pins", "items": pinStore.map { ["id": $0.id, "text": $0.text, "ts": $0.ts, "del": $0.del] as [String: Any] }]
    }
    private func publishPins() { send("st/pins", pinsMsg(), retain: true) }
    private func mergePins(_ d: [String: Any]) {
        var changed = false
        var incoming: [String: PinItem] = [:]
        for o in J.list(d, "items") {
            let p = PinItem(id: J.s(o, "id"), text: J.s(o, "text"), ts: J.l(o, "ts"), del: J.b(o, "del"))
            if !p.id.isEmpty { incoming[p.id] = p }
        }
        for p in incoming.values {
            if let i = pinStore.firstIndex(where: { $0.id == p.id }) {
                if p.ts > pinStore[i].ts { pinStore[i] = p; changed = true }
            } else { pinStore.append(p); changed = true }
        }
        let cutoff = AppInfo.now() - 30 * 24 * 3600 * 1000
        pinStore.removeAll { $0.del && $0.ts < cutoff }
        let localNewer = pinStore.contains { p in
            guard let i = incoming[p.id] else { return true }
            return p.ts > i.ts
        }
        if changed { pinsChanged() }
        if localNewer { publishPins() }
    }

    // ---------- shared config ----------
    func setTelegram(token: String, chat: String) {
        ud.set(token, forKey: "tgToken"); ud.set(chat, forKey: "tgChat"); ud.set(Double(AppInfo.now()), forKey: "cfgTs")
        publishCfg(); objectWillChange.send()
    }
    func setRelays(_ value: String) {
        ud.set(value.trimmingCharacters(in: .whitespacesAndNewlines), forKey: "relays"); ud.set(Double(AppInfo.now()), forKey: "cfgTs")
        publishCfg()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { self.start() }
    }
    private func publishCfg() {
        send("st/cfg", ["t": "cfg", "tgToken": tgToken, "tgChat": tgChat, "relays": relays, "cts": cfgTs], retain: true)
    }
    private func mergeCfg(_ d: [String: Any]) {
        let cts = J.l(d, "cts"), mine = cfgTs
        if cts > mine {
            let relaysChanged = J.s(d, "relays") != relays
            ud.set(J.s(d, "tgToken"), forKey: "tgToken"); ud.set(J.s(d, "tgChat"), forKey: "tgChat")
            ud.set(J.s(d, "relays"), forKey: "relays"); ud.set(Double(cts), forKey: "cfgTs")
            objectWillChange.send()
            if relaysChanged { Log.write("relay list updated by another device"); DispatchQueue.main.async { self.start() } }
        } else if cts < mine { publishCfg() }
    }

    // ---------- devices ----------
    private var devStore: [String: DeviceInfo] = [:]
    private func devMsg(_ online: Bool) -> [String: Any] {
        ["t": "dev", "name": deviceName, "plat": "ios", "ver": AppInfo.version, "online": online]
    }
    func publishDev() { lastDevPub = AppInfo.now(); send("st/dev/" + deviceId, devMsg(true), retain: true) }
    private func onDev(_ id: String, _ d: [String: Any]) {
        guard J.s(d, "from") == id else { return }
        if id == deviceId { if !J.b(d, "online") && upCount > 0 { publishDev() }; return }
        var di = devStore[id] ?? DeviceInfo(id: id)
        if J.b(d, "will") {
            // Last-will from a relay: its timestamp is the connect time, so it bypasses the ordering check.
            // A device that is still alive on another relay re-announces itself right away.
            if di.name.isEmpty { di.name = J.s(d, "name"); di.plat = J.s(d, "plat") }
            di.online = false
            devStore[id] = di
            devices = devStore.values.sorted { $0.name < $1.name }
            return
        }
        let ts = J.l(d, "ts")
        if ts < di.ts { return }
        di.ts = ts; di.online = J.b(d, "online"); di.name = J.s(d, "name"); di.plat = J.s(d, "plat"); di.ver = J.s(d, "ver")
        if let r = J.d(d, "rdp") {
            di.rdpOn = J.b(r, "on"); di.rdpUser = J.s(r, "user")
            let p = Int(J.l(r, "port")); di.rdpPort = p == 0 ? 3389 : p
            di.addrs = J.strs(r, "addrs")
        }
        devStore[id] = di
        devices = devStore.values.sorted { $0.name < $1.name }
    }
    func forget(_ id: String) {
        devStore[id] = nil; devices = devStore.values.sorted { $0.name < $1.name }
        if let k = keys { for l in links { l.publish(k.topic + "/st/dev/" + id, Data(), retain: true) } }
    }

    // ---------- files: announce ----------
    private static func signature(_ files: [FileEntry]) -> String {
        hash("F:" + files.map { "\($0.name)|\($0.size);" }.joined())
    }
    func localFiles(_ urls: [URL], image: Bool) {
        guard paired, !paused else { return }
        var files: [FileEntry] = []
        for u in urls {
            guard let attr = try? FileManager.default.attributesOfItem(atPath: u.path), let size = attr[.size] as? NSNumber else { continue }
            files.append(FileEntry(name: u.lastPathComponent, size: size.int64Value, mime: Mime.of(u.lastPathComponent), url: u))
        }
        guard !files.isEmpty else { return }
        let sig = Engine.signature(files)
        if sig == lastHash { return }
        lastHash = sig
        let o = Offer(fid: AppInfo.randHex(8), from: deviceId, fromName: deviceName, image: image, ts: AppInfo.now(), files: files)
        outgoing[o.fid] = o; outQ.append(o.fid)
        if outQ.count > 8 { outgoing[outQ.removeFirst()] = nil }
        publishClip(["t": "offer", "fid": o.fid, "image": image,
                     "files": files.map { ["name": $0.name, "size": $0.size, "mime": $0.mime] as [String: Any] }])
        banner = L.t("הקובץ זמין להדבקה בכל המכשירים", "The file is ready to paste on all devices")
        Log.write("offer out: \(files.count) file(s)")
    }
    private func onOffer(_ d: [String: Any]) {
        guard !paused, fresh(d) else { return }
        let files = J.list(d, "files").map { FileEntry(name: Engine.safeName(J.s($0, "name")), size: J.l($0, "size"), mime: J.s($0, "mime"), url: nil) }
        let o = Offer(fid: J.s(d, "fid"), from: J.s(d, "from"), fromName: nameOf(J.s(d, "from")), image: J.b(d, "image"), ts: J.l(d, "ts"), files: files)
        guard !o.fid.isEmpty, !files.isEmpty else { return }
        lastHash = Engine.signature(files)
        Log.write("offer in: \(files[0].name)")
        offerReady = []
        if o.image && o.total <= 15 * 1024 * 1024 {
            requestFile(o, 0) { url, _ in
                guard let url = url, let img = UIImage(contentsOfFile: url.path) else { return }
                UIPasteboard.general.image = img
                self.lastChangeCount = UIPasteboard.general.changeCount
                self.banner = L.t("תמונה התקבלה מ-", "Image received from ") + o.fromName
            }
            return
        }
        offer = o
        Notifier.fileOffer(o)     // asks for approval, showing name, size and type
    }
    private static func safeName(_ n: String) -> String {
        let last = n.replacingOccurrences(of: "\\", with: "/").split(separator: "/").last.map(String.init) ?? ""
        let cleaned = last.replacingOccurrences(of: ":", with: "_")
        return cleaned.isEmpty || cleaned == "." || cleaned == ".." ? "file" : cleaned
    }

    /// The user approved pasting the pending file(s): download all, then place them on the pasteboard.
    func acceptOffer() {
        guard let o = offer, progress < 0 else { return }
        progress = 0
        var got: [URL] = []
        func next(_ idx: Int) {
            requestFile(o, idx) { url, err in
                guard let url = url else {
                    self.progress = -1; self.banner = L.t("העברת הקובץ נכשלה: ", "File transfer failed: ") + (err ?? ""); Notifier.info(L.t("העברת הקובץ נכשלה", "File transfer failed"), err ?? ""); return
                }
                got.append(url)
                if idx + 1 < o.files.count { next(idx + 1); return }
                self.progress = -1; self.offerReady = got; self.offer = nil
                if got.count == 1, o.files[0].mime.hasPrefix("image/"), let img = UIImage(contentsOfFile: got[0].path) {
                    UIPasteboard.general.image = img
                } else {
                    UIPasteboard.general.setItemProviders(got.compactMap { NSItemProvider(contentsOf: $0) }, localOnly: false, expirationDate: nil)
                }
                self.lastChangeCount = UIPasteboard.general.changeCount
                self.banner = L.t("הקובץ מוכן - אפשר להדביק. נשמר גם באפליקציית קבצים, בתיקיית Cliport", "The file is ready to paste. It is also saved in the Files app, in the Cliport folder")
                Notifier.info(L.t("הקובץ מוכן להדבקה", "The file is ready to paste"), o.files[0].name)
            }
        }
        next(0)
    }
    func declineOffer() { offer = nil }

    // ---------- files: receive ----------
    private func requestFile(_ o: Offer, _ idx: Int, done: @escaping (URL?, String?) -> Void) {
        let key = "\(o.fid):\(idx)"
        if rxs[key] != nil { done(nil, L.t("ההעברה כבר מתבצעת", "The transfer is already in progress")); return }
        let dir = AppInfo.docs.appendingPathComponent("Received", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        // never overwrite a file received earlier under the same name: "name (2).ext"
        let name = o.files[idx].name as NSString
        var url = dir.appendingPathComponent(name as String)
        var n = 2
        while FileManager.default.fileExists(atPath: url.path) {
            let stem = name.deletingPathExtension, ext = name.pathExtension
            url = dir.appendingPathComponent(ext.isEmpty ? "\(stem) (\(n))" : "\(stem) (\(n)).\(ext)")
            n += 1
        }
        rxs[key] = Rx(offer: o, idx: idx, url: url, done: done)
        if send("ev", ["t": "freq", "fid": o.fid, "idx": idx, "to": o.from], retain: false) == 0 { finishRx(key, L.t("אין חיבור לשרתי הממסר", "No connection to the relay servers")) }
    }
    private func openRx(_ rx: Rx) {
        guard rx.handle == nil else { return }
        try? FileManager.default.removeItem(at: rx.url)
        FileManager.default.createFile(atPath: rx.url.path, contents: nil)
        rx.handle = try? FileHandle(forWritingTo: rx.url)
    }
    private func finishRx(_ key: String, _ err: String?) {
        guard let rx = rxs[key] else { return }
        rxs[key] = nil
        try? rx.handle?.close()
        if let e = err { Log.write("file rx failed: \(e)"); try? FileManager.default.removeItem(at: rx.url); rx.done(nil, e) }
        else { rx.done(rx.url, nil) }
    }
    private func onFres(_ d: [String: Any]) {
        let key = "\(J.s(d, "fid")):\(J.l(d, "idx"))"
        guard let rx = rxs[key] else { return }
        rx.last = AppInfo.now()
        switch J.s(d, "mode") {
        case "err": finishRx(key, J.s(d, "err"))
        case "mqtt":
            rx.total = Int(J.l(d, "total")); openRx(rx)
            if rx.total == 0 { finishRx(key, nil) }
        case "tg":
            let parts = J.strs(d, "parts"), token = tgToken
            guard let k = keys else { return }
            rx.last = AppInfo.now() + 3_600_000
            openRx(rx)
            Task {
                var failure: String?
                do {
                    for (i, part) in parts.enumerated() {
                        let sealed = try await Telegram.download(token: token, fileId: part)
                        guard let plain = k.open(sealed) else { throw NSError(domain: "sc", code: 1, userInfo: [NSLocalizedDescriptionKey: L.t("פענוח נכשל", "decryption failed")]) }
                        await MainActor.run { rx.handle?.write(plain); self.progress = (i + 1) * 100 / max(parts.count, 1) }
                    }
                } catch { failure = L.t("הורדה מ-Telegram נכשלה: ", "Download from Telegram failed: ") + error.localizedDescription }
                let f = failure
                await MainActor.run { self.finishRx(key, f) }
            }
        default: break
        }
    }
    private func onChunk(_ d: [String: Any]) {
        let key = "\(J.s(d, "fid")):\(J.l(d, "idx"))"
        guard let rx = rxs[key] else { return }
        let seq = Int(J.l(d, "seq"))
        if seq < rx.next { return }
        rx.total = Int(J.l(d, "total")); rx.last = AppInfo.now()
        rx.buf[seq] = Data(base64Encoded: J.s(d, "data")) ?? Data()
        openRx(rx)
        while let b = rx.buf[rx.next] { rx.handle?.write(b); rx.buf[rx.next] = nil; rx.next += 1 }
        if progress >= 0, rx.total > 0 { progress = rx.next * 100 / rx.total }
        if rx.next >= rx.total { finishRx(key, nil) }
    }

    // ---------- files: serve (this device is the origin) ----------
    private func serveFile(_ d: [String: Any]) {
        let fid = J.s(d, "fid"), to = J.s(d, "from"), idx = Int(J.l(d, "idx"))
        let base: [String: Any] = ["t": "fres", "fid": fid, "idx": idx, "to": to]
        let fail: (String) -> Void = { [weak self] msg in self?.reply(base, ["mode": "err", "err": msg]); Log.write("serve failed: \(msg)") }
        guard let o = outgoing[fid], idx >= 0, idx < o.files.count, let url = o.files[idx].url, FileManager.default.fileExists(atPath: url.path) else {
            fail(L.t("הקובץ כבר לא זמין במכשיר המקורי", "The file is no longer available on the source device")); return
        }
        let size = o.files[idx].size
        if tgReady, let k = keys {
            let token = tgToken, chat = tgChat
            Task {
                do {
                    guard let h = try? FileHandle(forReadingFrom: url) else { throw NSError(domain: "sc", code: 2, userInfo: [NSLocalizedDescriptionKey: L.t("לא ניתן לקרוא את הקובץ", "The file cannot be read")]) }
                    var parts: [String] = []; var n = 0
                    while true {
                        let piece = h.readData(ofLength: Engine.tgPart)
                        if piece.isEmpty { break }
                        parts.append(try await Telegram.upload(token: token, chat: chat, data: k.seal(piece, compress: false), name: "sc-\(fid)-\(n).bin"))
                        n += 1
                    }
                    try? h.close()
                    let p = parts
                    await MainActor.run { self.reply(base, ["mode": "tg", "parts": p, "size": size]) }
                } catch {
                    let msg = error.localizedDescription
                    await MainActor.run { fail(L.t("העלאה ל-Telegram נכשלה: ", "Upload to Telegram failed: ") + msg) }
                }
            }
            return
        }
        if size > Engine.maxRelayFile { fail(L.t("הקובץ גדול מ-25MB. הגדר אחסון Telegram בהגדרות כדי להעביר קבצים גדולים.", "The file is larger than 25 MB. Set up Telegram storage in Settings to transfer large files.")); return }
        guard let h = try? FileHandle(forReadingFrom: url) else { fail(L.t("לא ניתן לקרוא את הקובץ", "The file cannot be read")); return }
        let total = Int((size + Int64(Engine.chunk) - 1) / Int64(Engine.chunk))
        reply(base, ["mode": "mqtt", "total": total, "size": size])
        let paceKey = "\(fid):\(idx):\(to)"
        pace[paceKey] = 0.03
        func sendChunk(_ seq: Int, _ tries: Int, _ carried: Data?) {
            if seq >= total { try? h.close(); self.pace[paceKey] = nil; Log.write("served via relay: \(total) chunks"); return }
            let data = carried ?? h.readData(ofLength: Engine.chunk)
            let m: [String: Any] = ["t": "fchunk", "fid": fid, "idx": idx, "seq": seq, "total": total, "data": data.base64EncodedString()]
            if self.sendOne("f/" + to, m) {
                DispatchQueue.main.asyncAfter(deadline: .now() + (self.pace[paceKey] ?? 0.03)) { sendChunk(seq + 1, 0, nil) }
            } else if tries < 40 {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { sendChunk(seq, tries + 1, data) }
            } else { try? h.close(); self.pace[paceKey] = nil; fail(L.t("החיבור אבד במהלך ההעברה", "The connection was lost during the transfer")) }
        }
        sendChunk(0, 0, nil)
    }

    /// Relays are best-effort: ask again for exactly the chunks that are missing (holes, or everything left after a stall).
    private func requestMissingChunks() {
        guard !rxs.isEmpty else { return }
        let now = AppInfo.now()
        for rx in rxs.values {
            if rx.total <= 0 || now - rx.lastNak < 2500 { continue }
            let stalled = now - rx.last >= 2500
            if !stalled && rx.buf.count < 12 { continue }
            let hi = stalled ? rx.total : (rx.buf.keys.max() ?? 0)
            var need: [Int] = []
            var s = rx.next
            while s < hi && need.count < 48 { if rx.buf[s] == nil { need.append(s) }; s += 1 }
            if need.isEmpty { continue }
            rx.lastNak = now
            Log.write("transfer stalled at chunk \(rx.next)/\(rx.total) - re-requesting \(need.count) chunk(s)")
            send("ev", ["t": "fnak", "fid": rx.offer.fid, "idx": rx.idx, "to": rx.offer.from, "seqs": need], retain: false)
        }
    }
    /// The receiver is missing specific chunks: send just those again, and slow down a transfer that is still running.
    private func serveNak(_ d: [String: Any]) {
        let fid = J.s(d, "fid"), to = J.s(d, "from"), idx = Int(J.l(d, "idx"))
        let seqs = J.strs(d, "seqs").compactMap { Int($0) }
        guard let o = outgoing[fid], idx >= 0, idx < o.files.count, let url = o.files[idx].url, !seqs.isEmpty, seqs.count <= 64,
              let h = try? FileHandle(forReadingFrom: url) else { return }
        let paceKey = "\(fid):\(idx):\(to)"
        if let cur = pace[paceKey] { pace[paceKey] = min(cur * 2, 0.4) }
        let size = o.files[idx].size
        let total = Int((size + Int64(Engine.chunk) - 1) / Int64(Engine.chunk))
        func resend(_ i: Int) {
            if i >= seqs.count { try? h.close(); Log.write("re-sent \(seqs.count) chunk(s)"); return }
            let seq = seqs[i]
            if seq >= 0 && seq < total {
                h.seek(toFileOffset: UInt64(seq) * UInt64(Engine.chunk))
                let data = h.readData(ofLength: Engine.chunk)
                _ = self.sendOne("f/" + to, ["t": "fchunk", "fid": fid, "idx": idx, "seq": seq, "total": total, "data": data.base64EncodedString()])
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.03) { resend(i + 1) }
        }
        resend(0)
    }
    private func reply(_ base: [String: Any], _ extra: [String: Any]) {
        var r = base
        for (k, v) in extra { r[k] = v }
        send("ev", r, retain: false)
    }

    // ---------- persistence ----------
    private func loadLocal() {
        history = ud.stringArray(forKey: "history") ?? []
        if let arr = ud.array(forKey: "pins") as? [[String: Any]] {
            pinStore = arr.map { PinItem(id: J.s($0, "id"), text: J.s($0, "text"), ts: J.l($0, "ts"), del: J.b($0, "del")) }.filter { !$0.id.isEmpty }
            pins = pinStore.filter { !$0.del }.sorted { $0.ts > $1.ts }
        }
    }
    /// Crash-recovery hooks.
    func resetCaches() {
        history = []; ud.removeObject(forKey: "history")
        try? FileManager.default.removeItem(at: AppInfo.caches.appendingPathComponent("outgoing"))
    }
    func resetSettingsKeepingPairing() {
        for k in ["pins", "tgToken", "tgChat", "relays", "cfgTs", "lastClipTs"] { ud.removeObject(forKey: k) }
        pinStore = []; pins = []; lastClipTs = 0
    }
}
