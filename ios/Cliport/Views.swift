// Cliport v1.0 (iOS) - SwiftUI interface. Two languages (Hebrew / English) and three appearance modes (light / device / dark).
import SwiftUI
import UIKit
import WebKit
import UniformTypeIdentifiers

struct RootView: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        Group {
            if eng.paired { MainTabs() } else { PairView() }
        }
        .environment(\.layoutDirection, L.en ? .leftToRight : .rightToLeft)
        .preferredColorScheme(eng.theme == "light" ? .light : (eng.theme == "dark" ? .dark : nil))
        .tint(Color(red: 0.36, green: 0.36, blue: 0.94))
        .id(eng.lang)            // rebuild every screen when the language changes
    }
}

struct PairView: View {
    @EnvironmentObject var eng: Engine
    @State private var code = ""
    @State private var bad = false
    var body: some View {
        NavigationStack {
            Form {
                Section(footer: Text(L.t("כל המכשירים חולקים קוד צימוד סודי אחד. הקוד הוא מפתח ההצפנה - התוכן מוצפן מקצה לקצה.",
                                         "All devices share one secret pairing code. The code is the encryption key - content is end-to-end encrypted."))) {
                    TextField(L.t("קוד צימוד (26 תווים)", "Pairing code (26 characters)"), text: $code)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled(true)
                        .font(.system(.body, design: .monospaced))
                        .environment(\.layoutDirection, .leftToRight)
                    Button(L.t("הצטרף עם הקוד", "Join with this code")) { bad = !eng.pair(code: code) }
                        .disabled(code.isEmpty)
                    PasteButton(payloadType: String.self) { items in
                        if let s = items.first { DispatchQueue.main.async { code = s; bad = !eng.pair(code: s) } }
                    }
                }
                Section(footer: Text(L.t("אם זה המכשיר הראשון, צור קבוצה חדשה והזן את הקוד שיוצג בשאר המכשירים.",
                                         "If this is the first device, create a new group and enter the code it shows on your other devices."))) {
                    Button(L.t("צור קבוצה חדשה", "Create a new group")) { eng.createGroup() }
                }
                Section(header: Text(L.t("שחזור מגיבוי", "Restore from a backup")),
                        footer: Text(L.t("שחזור מחזיר את הצימוד, ההגדרות והנעוצים בבת אחת, בלי פעולה נוספת.", "Restoring brings back pairing, settings and pins at once, with no further steps."))) {
                    BackupImportButton(title: L.t("שחזר מקובץ גיבוי", "Restore from a backup file"), restoreNow: true)
                    ForEach(eng.backups) { b in
                        Button { if let h = BackupFile.header(b.data), let p = BackupFile.payload(h, password: "") { _ = eng.restore(p) } } label: {
                            Label(L.t("שחזר מקומי: ", "Restore local: ") + b.name, systemImage: "clock.arrow.circlepath")
                        }
                    }
                }
                AppearanceSection()
            }
            .navigationTitle("Cliport")
            .onAppear { eng.reloadBackups() }
            .alert(L.t("הקוד אינו תקין", "The code is not valid"), isPresented: $bad) { Button(L.t("אישור", "OK"), role: .cancel) { } }
        }
    }
}

/// Language and appearance pickers (shared by the pairing screen and Settings).
struct AppearanceSection: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        Section(header: Text(L.t("תצוגה", "Appearance"))) {
            Picker(L.t("שפה", "Language"), selection: Binding(get: { L.en ? "en" : "he" }, set: { eng.lang = $0 })) {
                Text("עברית").tag("he")
                Text("English").tag("en")
            }
            Picker(L.t("ערכת נושא", "Theme"), selection: $eng.theme) {
                Text(L.t("בהיר", "Light")).tag("light")
                Text(L.t("מכשיר", "Device")).tag("system")
                Text(L.t("כהה", "Dark")).tag("dark")
            }
            .pickerStyle(.segmented)
        }
    }
}

struct MainTabs: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        TabView {
            ClipView().tabItem { Label(L.t("לוח", "Clipboard"), systemImage: "doc.on.clipboard") }
            RdpView().tabItem { Label(L.t("מחשבים", "Computers"), systemImage: "desktopcomputer") }
            SettingsView().tabItem { Label(L.t("הגדרות", "Settings"), systemImage: "gearshape") }
        }
        .alert(L.t("לאשר הדבקת קובץ?", "Allow pasting this file?"), isPresented: Binding(get: { eng.offer != nil && eng.progress < 0 }, set: { _ in }), presenting: eng.offer) { _ in
            Button(L.t("אשר הדבקה", "Allow paste")) { eng.acceptOffer() }
            Button(L.t("דחה", "Decline"), role: .cancel) { eng.declineOffer() }
        } message: { o in
            Text(OfferText.describe(o))
        }
    }
}

enum OfferText {
    static func describe(_ o: Offer) -> String {
        var lines = [L.t("מאת: ", "From: ") + o.fromName]
        for f in o.files {
            lines.append(L.t("שם: ", "Name: ") + f.name)
            lines.append(L.t("גודל: ", "Size: ") + AppInfo.sizeText(f.size))
            lines.append(L.t("סוג: ", "Type: ") + Mime.label(f.name))
        }
        return lines.joined(separator: "\n")
    }
}

struct ClipView: View {
    @EnvironmentObject var eng: Engine
    @State private var query = ""
    private func matches(_ t: String) -> Bool { query.isEmpty || t.localizedCaseInsensitiveContains(query) }
    var body: some View {
        NavigationStack {
            List {
                Section {
                    HStack {
                        Circle().fill(eng.upCount > 0 && !eng.paused ? Color.green : Color.orange).frame(width: 10, height: 10)
                        Text(eng.paused ? L.t("הסנכרון מושהה", "Sync is paused")
                             : (eng.upCount > 0 ? L.t("מחובר ומסנכרן (\(eng.upCount)/\(eng.relayCount) שרתי ממסר)", "Connected and syncing (\(eng.upCount)/\(eng.relayCount) relays)")
                                : L.t("מתחבר מחדש...", "Reconnecting...")))
                        Spacer()
                    }
                    if !eng.banner.isEmpty { Text(eng.banner).font(.footnote).foregroundColor(.secondary) }
                    if eng.progress >= 0 { ProgressView(value: Double(eng.progress), total: 100) }
                    HStack {
                        Button { eng.sendClipboardNow() } label: { Label(L.t("שלח את הלוח עכשיו", "Send clipboard now"), systemImage: "arrow.up.doc.on.clipboard") }
                            .buttonStyle(.borderedProminent)
                        Spacer()
                        PasteButton(payloadType: String.self) { items in
                            if let s = items.first { DispatchQueue.main.async { eng.localText(s) } }
                        }
                    }
                }
                if let o = eng.offer {
                    Section(L.t("קובץ ממתין לאישור", "File waiting for approval")) {
                        Text(OfferText.describe(o)).font(.callout)
                        Button(L.t("אשר הדבקה", "Allow paste")) { eng.acceptOffer() }.disabled(eng.progress >= 0)
                        Button(L.t("דחה", "Decline"), role: .destructive) { eng.declineOffer() }
                    }
                }
                if !eng.offerReady.isEmpty {
                    Section(L.t("קבצים שהתקבלו (הועתקו ללוח)", "Received files (copied to the clipboard)")) {
                        ForEach(eng.offerReady, id: \.self) { url in
                            ShareLink(item: url) { Label(url.lastPathComponent, systemImage: "doc") }
                        }
                    }
                }
                let pins = eng.pins.filter { matches($0.text) }
                if !pins.isEmpty {
                    Section(L.t("נעוצים", "Pinned")) {
                        ForEach(pins) { p in ClipRow(text: p.text, pinned: true) }
                    }
                }
                Section(L.t("היסטוריה", "History")) {
                    let items = eng.history.filter { !eng.isPinned($0) && matches($0) }
                    if items.isEmpty { Text(query.isEmpty ? L.t("עדיין אין פריטים", "Nothing here yet") : L.t("לא נמצאו תוצאות", "No results")).foregroundColor(.secondary) }
                    ForEach(items, id: \.self) { t in ClipRow(text: t, pinned: false) }
                }
            }
            .searchable(text: $query, prompt: L.t("חיפוש", "Search"))
            .navigationTitle("Cliport")
        }
    }
}

struct ClipRow: View {
    @EnvironmentObject var eng: Engine
    let text: String
    let pinned: Bool
    var body: some View {
        Button { eng.copy(text) } label: {
            HStack(alignment: .top) {
                if pinned { Image(systemName: "pin.fill").foregroundColor(.accentColor) }
                Text(text).lineLimit(3).foregroundColor(.primary).frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .swipeActions(edge: .leading) {
            Button { if pinned { eng.unpin(text) } else { eng.pin(text) } } label: { Label(pinned ? L.t("בטל נעיצה", "Unpin") : L.t("נעץ", "Pin"), systemImage: pinned ? "pin.slash" : "pin") }
                .tint(.orange)
        }
        .swipeActions(edge: .trailing) {
            if !pinned { Button(role: .destructive) { eng.removeHistory(text) } label: { Label(L.t("מחק", "Delete"), systemImage: "trash") } }
        }
        .contextMenu {
            Button { eng.copy(text) } label: { Label(L.t("העתק", "Copy"), systemImage: "doc.on.doc") }
            Button { if pinned { eng.unpin(text) } else { eng.pin(text) } } label: { Label(pinned ? L.t("בטל נעיצה", "Unpin") : L.t("נעץ", "Pin"), systemImage: pinned ? "pin.slash" : "pin") }
            if !pinned { Button(role: .destructive) { eng.removeHistory(text) } label: { Label(L.t("מחק", "Delete"), systemImage: "trash") } }
        }
    }
}

/// Remote Desktop: opens Microsoft's "Windows App" (RDP client) straight into the chosen PC.
enum RdpLauncher {
    static func file(for d: DeviceInfo, address: String) -> URL? {
        let dir = AppInfo.caches.appendingPathComponent("rdp", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let safe = d.name.replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: ":", with: "_")
        let url = dir.appendingPathComponent((safe.isEmpty ? "pc" : safe) + ".rdp")
        var lines = ["full address:s:\(address):\(d.rdpPort)"]
        if !d.rdpUser.isEmpty { lines.append("username:s:\(d.rdpUser)") }
        lines += ["session bpp:i:32", "connection type:i:7", "networkautodetect:i:1", "bandwidthautodetect:i:1", "compression:i:1",
                  "autoreconnection enabled:i:1", "audiomode:i:0", "redirectclipboard:i:1", "authentication level:i:2", "dynamic resolution:i:1"]
        guard (try? Data(lines.joined(separator: "\r\n").utf8).write(to: url)) != nil else { return nil }
        return url
    }
    static func deepLink(for d: DeviceInfo, address: String) -> URL? {
        var s = "rdp://full%20address=s:\(address):\(d.rdpPort)"
        if !d.rdpUser.isEmpty, let u = d.rdpUser.addingPercentEncoding(withAllowedCharacters: .alphanumerics) { s += "&username=s:\(u)" }
        return URL(string: s)
    }
    /// Tries the rdp:// deep link; reports false when it could not be opened (caller then offers the .rdp file).
    static func open(_ d: DeviceInfo, address: String, done: @escaping (Bool) -> Void) {
        guard let url = deepLink(for: d, address: address) else { done(false); return }
        UIApplication.shared.open(url, options: [:]) { ok in done(ok) }
    }
}

struct RdpView: View {
    @EnvironmentObject var eng: Engine
    @State private var fallback: URL?
    @State private var showFallback = false
    var body: some View {
        NavigationStack {
            List {
                let pcs = eng.devices.filter { $0.plat == "win" }
                if pcs.isEmpty {
                    Text(L.t("לא נמצאו מחשבים בקבוצה. התקן את Cliport במחשב וצמד אותו עם אותו קוד.", "No computers in the group. Install Cliport on a PC and pair it with the same code.")).foregroundColor(.secondary)
                }
                ForEach(pcs) { d in
                    Section(header: Text(d.name),
                            footer: Text(d.isOnline ? (d.rdpOn ? L.t("זמין לחיבור", "Ready to connect") : L.t("Remote Desktop כבוי במחשב זה", "Remote Desktop is off on this PC")) : L.t("המחשב לא מחובר כרגע", "This PC is offline right now"))) {
                        ForEach(d.addrs, id: \.self) { a in
                            Button {
                                RdpLauncher.open(d, address: a) { ok in
                                    if !ok { fallback = RdpLauncher.file(for: d, address: a); showFallback = fallback != nil }
                                }
                            } label: {
                                HStack {
                                    Image(systemName: "display")
                                    Text(a).environment(\.layoutDirection, .leftToRight)
                                    Spacer()
                                    Text(a.hasPrefix("100.") ? L.t("Tailscale - מכל רשת", "Tailscale - any network") : L.t("רשת מקומית", "Local network")).font(.footnote).foregroundColor(.secondary)
                                }
                            }
                        }
                    }
                }
                Section(footer: Text(L.t("החיבור נפתח באפליקציית Windows App של מיקרוסופט (חינם ב-App Store). לחיבור מכל רשת התקן Tailscale באייפון ובמחשבים. לקיצור דרך במסך הבית: אפליקציית \"קיצורים\", פעולה \"התחבר למחשב\" של Cliport.",
                                         "The connection opens in Microsoft's Windows App (free on the App Store). To connect from any network, install Tailscale on the iPhone and on the PCs. For a Home Screen shortcut: the Shortcuts app, Cliport action \"Connect to a PC\"."))) { EmptyView() }
            }
            .navigationTitle(L.t("שולחן עבודה מרוחק", "Remote Desktop"))
            .sheet(isPresented: $showFallback) {
                VStack(spacing: 20) {
                    Text(L.t("לא ניתן היה לפתוח את החיבור ישירות.", "The connection could not be opened directly.")).font(.headline)
                    Text(L.t("פתח את קובץ החיבור ב-Windows App:", "Open the connection file in Windows App:"))
                    if let f = fallback { ShareLink(item: f) { Label(L.t("פתח קובץ חיבור", "Open connection file"), systemImage: "square.and.arrow.up") }.buttonStyle(.borderedProminent) }
                    Button(L.t("סגור", "Close")) { showFallback = false }
                }
                .padding()
                .environment(\.layoutDirection, L.en ? .leftToRight : .rightToLeft)
                .presentationDetents([.medium])
            }
        }
    }
}

/// The user guide ships inside the app (one HTML file per language, pictures inline).
struct GuideView: UIViewRepresentable {
    let url: URL
    func makeUIView(context: Context) -> WKWebView { let w = WKWebView(); w.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent()); return w }
    func updateUIView(_ uiView: WKWebView, context: Context) { }
}

struct SettingsView: View {
    @EnvironmentObject var eng: Engine
    @State private var token = ""
    @State private var chat = ""
    @State private var relays = ""
    @State private var msg = ""
    @State private var confirmUnpair = false
    private var guideURL: URL? { Bundle.main.url(forResource: L.en ? "guide.en" : "guide.he", withExtension: "html") }
    var body: some View {
        NavigationStack {
            Form {
                AppearanceSection()
                Section {
                    NavigationLink { BackupView() } label: { Label(L.t("גיבוי ושחזור", "Backup & restore"), systemImage: "externaldrive") }
                }
                Section(header: Text(L.t("צימוד", "Pairing")), footer: Text(L.t("הזן קוד זה במכשירים נוספים. אל תשתף אותו - זהו מפתח ההצפנה.", "Enter this code on your other devices. Do not share it - it is the encryption key."))) {
                    Text(Base32.pretty(eng.keys?.code ?? "")).font(.system(.footnote, design: .monospaced)).textSelection(.enabled)
                        .environment(\.layoutDirection, .leftToRight)
                    Button(L.t("העתק קוד", "Copy code")) { UIPasteboard.general.string = eng.keys?.code ?? "" }
                    Button(L.t("התנתק מהקבוצה", "Leave the group"), role: .destructive) { confirmUnpair = true }
                }
                Section(header: Text(L.t("פעילות ברקע", "Background activity")),
                        footer: Text(L.t("כשהאפשרות פעילה, האפליקציה נשארת מחוברת ברקע ומקבלת מה שמועתק במחשב. אם iOS אינו מאפשר לכתוב ללוח ברקע, הפריט נכנס ללוח ברגע שפותחים את Cliport, וההתראה מביאה לשם בהקשה אחת. צריכת הסוללה עולה מעט.",
                                         "When on, the app stays connected in the background and receives what you copy on a PC. If iOS does not allow writing to the clipboard in the background, the item lands on the clipboard the moment you open Cliport, and the notification takes you there in one tap. Battery use rises slightly."))) {
                    Toggle(L.t("השאר מחובר ברקע", "Stay connected in the background"), isOn: Binding(get: { eng.keepAlive }, set: { eng.keepAlive = $0 }))
                    Toggle(L.t("התראה על פריט שהגיע ברקע", "Notify about items received in the background"), isOn: Binding(get: { eng.notifyClips }, set: { eng.notifyClips = $0 }))
                    Toggle(L.t("השהה סנכרון", "Pause sync"), isOn: $eng.paused)
                }
                Section(header: Text(L.t("טקסט גדול מאוד", "Very large text")),
                        footer: Text(L.t("טקסט מעל 180KB עובר כקובץ. כאן בוחרים איך הוא מגיע למכשיר הזה: כטקסט בלוח וגם כקובץ באפליקציית קבצים, כטקסט בלבד, או כקובץ בלבד.",
                                         "Text over 180 KB travels as a file. Choose how it arrives on this device: as text on the clipboard plus a file in the Files app, text only, or file only."))) {
                    Picker(L.t("מגיע כ", "Arrives as"), selection: Binding(get: { eng.bigText }, set: { eng.bigText = $0 })) {
                        Text(L.t("טקסט וקובץ", "Text and file")).tag("both")
                        Text(L.t("טקסט בלבד", "Text only")).tag("text")
                        Text(L.t("קובץ בלבד", "File only")).tag("file")
                    }
                }
                Section(header: Text(L.t("שליחה אוטומטית מהמכשיר", "Automatic sending from this device")),
                        footer: Text(L.t("iOS אינו מאפשר לאף אפליקציה לקרוא את הלוח ברקע. כדי שהעתקה במכשיר תישלח לבד:\n1. הגדרות, Cliport, \"הדבקה מיישומים אחרים\": אפשר.\n2. אפליקציית \"קיצורים\", אוטומציה חדשה, \"יישום\", בחר יישומים, \"נסגר\", \"הפעל מיד\".\n3. הוסף פעולה \"קבל לוח\" ואחריה \"שלח טקסט לכל המכשירים\" של Cliport.\nמעכשיו כל יציאה מיישום שולחת את הלוח. בנוסף, פתיחת Cliport שולחת את הלוח מיד.",
                                         "iOS does not let any app read the clipboard in the background. To send what you copy automatically:\n1. Settings, Cliport, \"Paste from Other Apps\": Allow.\n2. Shortcuts app, new Automation, \"App\", choose apps, \"Is Closed\", \"Run Immediately\".\n3. Add the action \"Get Clipboard\" followed by Cliport's \"Send text to all devices\".\nFrom now on, leaving an app sends the clipboard. Opening Cliport also sends it right away."))) { EmptyView() }
                Section(header: Text(L.t("אחסון Telegram לקבצים גדולים (אופציונלי)", "Telegram storage for large files (optional)")),
                        footer: Text(eng.tgReady ? L.t("מוגדר ומשותף לכל המכשירים.", "Set up and shared with all devices.")
                                     : L.t("ללא Telegram, קבצים עד 25MB עוברים דרך הממסר. צור בוט אצל BotFather, שלח לו הודעה ולחץ \"זהה צ'אט ושמור\".", "Without Telegram, files up to 25 MB travel through the relay. Create a bot with BotFather, send it a message and tap \"Detect chat and save\"."))) {
                    SecureField("Bot Token", text: $token).environment(\.layoutDirection, .leftToRight)
                    TextField("Chat ID", text: $chat).environment(\.layoutDirection, .leftToRight)
                    Button(L.t("זהה צ'אט ושמור", "Detect chat and save")) { saveTelegram() }
                }
                Section(header: Text(L.t("שרת ממסר פרטי (אופציונלי)", "Private relay server (optional)")),
                        footer: Text(L.t("MQTT על TLS בפורמט host:port או user:pass@host:port. מופץ לכל המכשירים.", "MQTT over TLS as host:port or user:pass@host:port. Shared with all devices."))) {
                    TextField("host:port", text: $relays).environment(\.layoutDirection, .leftToRight).autocorrectionDisabled(true).textInputAutocapitalization(.never)
                    Button(L.t("שמור ממסר", "Save relay")) { eng.setRelays(relays); msg = L.t("רשימת הממסרים עודכנה", "The relay list was updated") }
                }
                Section(header: Text(L.t("מכשירים בקבוצה", "Devices in the group"))) {
                    if eng.devices.isEmpty { Text(L.t("אין מכשירים נוספים עדיין", "No other devices yet")).foregroundColor(.secondary) }
                    ForEach(eng.devices) { d in
                        HStack {
                            Image(systemName: d.plat == "ios" ? "iphone" : "desktopcomputer")
                            Text(d.name)
                            Spacer()
                            Text(d.isOnline ? L.t("מחובר", "Online") : L.t("לא מחובר", "Offline")).font(.footnote).foregroundColor(d.isOnline ? .green : .secondary)
                        }
                        .swipeActions { Button(L.t("שכח", "Forget"), role: .destructive) { eng.forget(d.id) } }
                    }
                }
                Section(header: Text(L.t("עזרה ואודות", "Help and about"))) {
                    if let g = guideURL {
                        NavigationLink { GuideView(url: g).navigationTitle(L.t("מדריך למשתמש", "User guide")).navigationBarTitleDisplayMode(.inline) } label: { Label(L.t("מדריך למשתמש", "User guide"), systemImage: "book") }
                    }
                    if let u = URL(string: "https://github.com/bennymat93/Cliport") { Link(destination: u) { Label(L.t("דף הפרויקט ב-GitHub", "Project page on GitHub"), systemImage: "link") } }
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Cliport").font(.headline)
                        Text(L.t("גרסה ", "Version ") + AppInfo.version).font(.footnote).foregroundColor(.secondary)
                        Text(L.t("לוח משותף בזמן אמת בין מחשבים, אייפון ואייפד. מוצפן מקצה לקצה, עובד מכל רשת, כולל שיתוף קבצים, נעוצים ושולחן עבודה מרוחק.",
                                 "A real-time shared clipboard for PCs, iPhone and iPad. End-to-end encrypted, works from any network, with file sharing, pins and Remote Desktop.")).font(.footnote)
                    }
                    if let n = Recovery.notice { Text(L.t("התאוששות (דרגה \(Recovery.level)): ", "Recovery (level \(Recovery.level)): ") + n).font(.footnote) }
                    if !msg.isEmpty { Text(msg).font(.footnote).foregroundColor(.secondary) }
                }
            }
            .navigationTitle(L.t("הגדרות", "Settings"))
            .onAppear { token = eng.tgToken; chat = eng.tgChat; relays = eng.relays }
            .confirmationDialog(L.t("לנתק את המכשיר מקבוצת הסנכרון?", "Disconnect this device from the sync group?"), isPresented: $confirmUnpair, titleVisibility: .visible) {
                Button(L.t("התנתק", "Leave"), role: .destructive) { eng.unpair() }
                Button(L.t("ביטול", "Cancel"), role: .cancel) { }
            }
        }
    }
    private func saveTelegram() {
        let t = token.trimmingCharacters(in: .whitespacesAndNewlines), c = chat.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.isEmpty { eng.setTelegram(token: "", chat: ""); msg = L.t("אחסון Telegram בוטל", "Telegram storage was turned off"); return }
        if !c.isEmpty { eng.setTelegram(token: t, chat: c); msg = L.t("אחסון Telegram נשמר", "Telegram storage was saved"); return }
        Task {
            let found = try? await Telegram.findChat(token: t)
            await MainActor.run {
                if let id = found ?? nil, !id.isEmpty { chat = id; eng.setTelegram(token: t, chat: id); msg = L.t("אחסון Telegram נשמר", "Telegram storage was saved") }
                else { msg = L.t("לא נמצאה הודעה לבוט. שלח לו הודעה ונסה שוב.", "No message to the bot was found. Send it a message and try again.") }
            }
        }
    }
}

// MARK: - Backup and restore

/// Picks a .clipbak file, asks for its password when needed, keeps a copy inside the app and (optionally) restores it at once.
struct BackupImportButton: View {
    @EnvironmentObject var eng: Engine
    let title: String
    let restoreNow: Bool
    @State private var picking = false
    @State private var askPassword = false
    @State private var password = ""
    @State private var header: [String: Any]?
    @State private var message = ""
    @State private var showMessage = false
    @State private var confirmRestore = false
    @State private var pending: [String: Any]?

    var body: some View {
        Button { picking = true } label: { Label(title, systemImage: "square.and.arrow.down") }
            .fileImporter(isPresented: $picking, allowedContentTypes: [.data, .json, .item]) { result in
                guard case .success(let url) = result else { return }
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                guard let data = try? Data(contentsOf: url), let h = BackupFile.header(data) else {
                    message = L.t("זה אינו קובץ גיבוי של Cliport.", "This is not a Cliport backup file."); showMessage = true; return
                }
                header = h
                if J.b(h, "protected") { password = ""; askPassword = true } else { finish(BackupFile.payload(h, password: "")) }
            }
            .alert(L.t("סיסמת הגיבוי", "Backup password"), isPresented: $askPassword) {
                SecureField(L.t("סיסמה", "Password"), text: $password)
                Button(L.t("אישור", "OK")) {
                    if let h = header {
                        if let p = BackupFile.payload(h, password: password) { finish(p) }
                        else { message = L.t("הסיסמה שגויה.", "Wrong password."); showMessage = true }
                    }
                }
                Button(L.t("ביטול", "Cancel"), role: .cancel) { }
            } message: { Text(L.t("הגיבוי \"", "The backup \"") + J.s(header, "name") + L.t("\" מוגן בסיסמה.", "\" is protected with a password.")) }
            .alert(message, isPresented: $showMessage) { Button(L.t("אישור", "OK"), role: .cancel) { } }
            .confirmationDialog(L.t("הגיבוי יובא ונשמר באפליקציה. לשחזר אותו עכשיו?", "The backup was imported and saved in the app. Restore it now?"), isPresented: $confirmRestore, titleVisibility: .visible) {
                Button(L.t("שחזר", "Restore")) { if let p = pending { _ = eng.restoreWithSafetyCopy(p, name: J.s(header, "name")) } }
                Button(L.t("לא עכשיו", "Not now"), role: .cancel) { }
            }
    }
    private func finish(_ payload: [String: Any]?) {
        guard let p = payload, let h = header else { message = L.t("קובץ הגיבוי פגום.", "The backup file is damaged."); showMessage = true; return }
        eng.storeLocal(BackupFile.create(name: J.s(h, "name"), desc: J.s(h, "description"), device: J.s(h, "device"), created: J.l(h, "created"), payload: p, password: ""))
        if restoreNow { _ = eng.restore(p) } else { pending = p; confirmRestore = true }
    }
}

struct BackupView: View {
    @EnvironmentObject var eng: Engine
    @State private var name = ""
    @State private var desc = ""
    @State private var withHistory = false
    @State private var exportPassword = ""
    @State private var exportURL: URL?
    @State private var showExport = false
    @State private var toRestore: BackupInfo?
    @State private var note = ""

    private func dateText(_ ms: Int64) -> String {
        let f = DateFormatter(); f.dateStyle = .medium; f.timeStyle = .short
        return f.string(from: Date(timeIntervalSince1970: Double(ms) / 1000))
    }
    var body: some View {
        Form {
            Section(header: Text(L.t("גיבוי חדש", "New backup")),
                    footer: Text(L.t("גיבוי כולל את הצימוד, ההגדרות המשותפות, הנעוצים, רשימת המכשירים וההעדפות. שחזור מחזיר הכול בלי פעולה נוספת.",
                                     "A backup holds the pairing, shared settings, pins, device list and preferences. Restoring brings everything back with no further steps."))) {
                TextField(L.t("שם הגיבוי", "Backup name"), text: $name)
                TextField(L.t("תיאור (אופציונלי)", "Description (optional)"), text: $desc)
                Toggle(L.t("כלול גם את היסטוריית הלוח", "Include the clipboard history too"), isOn: $withHistory)
                Button(L.t("צור גיבוי", "Create backup")) {
                    let f = DateFormatter(); f.dateStyle = .short; f.timeStyle = .short
                    let n = name.trimmingCharacters(in: .whitespaces)
                    eng.saveLocal(name: n.isEmpty ? L.t("גיבוי ", "Backup ") + f.string(from: Date()) : n, desc: desc.trimmingCharacters(in: .whitespaces), withHistory: withHistory)
                    name = ""; desc = ""; note = L.t("הגיבוי נשמר", "Backup saved")
                }
                if !note.isEmpty { Text(note).font(.footnote).foregroundColor(.secondary) }
            }
            Section(header: Text(L.t("ייבוא", "Import"))) {
                BackupImportButton(title: L.t("ייבא מקובץ גיבוי", "Import a backup file"), restoreNow: false)
            }
            Section(header: Text(L.t("הורדת קובץ", "Download a file")),
                    footer: Text(L.t("הקובץ מכיל את מפתח ההצפנה של הקבוצה. מומלץ להגן עליו בסיסמה. השאר ריק לקובץ ללא סיסמה.", "The file contains your group's encryption key. Protecting it with a password is recommended. Leave empty for no password."))) {
                SecureField(L.t("סיסמה לקובץ המיוצא (אופציונלי)", "Password for the exported file (optional)"), text: $exportPassword)
            }
            Section(header: Text(L.t("גיבויים מקומיים", "Local backups"))) {
                if eng.backups.isEmpty { Text(L.t("אין גיבויים מקומיים עדיין", "No local backups yet")).foregroundColor(.secondary) }
                ForEach(eng.backups) { b in
                    VStack(alignment: .leading, spacing: 3) {
                        Text(b.name).font(.headline)
                        if !b.desc.isEmpty { Text(b.desc).font(.subheadline) }
                        Text("\(dateText(b.created))  •  \(b.device)  •  \(b.pinCount) \(L.t("נעוצים", "pins"))  •  \(b.deviceCount + 1) \(L.t("מכשירים", "devices"))\(b.hasHistory ? L.t("  •  היסטוריה", "  •  history") : "")")
                            .font(.footnote).foregroundColor(.secondary)
                        HStack {
                            Button(L.t("שחזר", "Restore")) { toRestore = b }.buttonStyle(.borderedProminent)
                            Button(L.t("הורד קובץ", "Download file")) {
                                exportURL = eng.exportFile(b, password: exportPassword); showExport = exportURL != nil
                            }.buttonStyle(.bordered)
                        }
                        .padding(.top, 4)
                    }
                    .padding(.vertical, 4)
                    .swipeActions { Button(L.t("מחק", "Delete"), role: .destructive) { eng.deleteLocal(b) } }
                }
            }
        }
        .navigationTitle(L.t("גיבוי ושחזור", "Backup & restore"))
        .navigationBarTitleDisplayMode(.inline)
        .onAppear { eng.reloadBackups() }
        .confirmationDialog(L.t("לשחזר את הגיבוי? המצב הנוכחי יישמר קודם כגיבוי אוטומטי.", "Restore this backup? The current state is saved first as an automatic backup."),
                            isPresented: Binding(get: { toRestore != nil }, set: { if !$0 { toRestore = nil } }), titleVisibility: .visible) {
            Button(L.t("שחזר", "Restore")) {
                if let b = toRestore, let h = BackupFile.header(b.data), let p = BackupFile.payload(h, password: "") {
                    note = eng.restoreWithSafetyCopy(p, name: b.name) ? L.t("השחזור הושלם", "Restore completed") : L.t("השחזור נכשל", "Restore failed")
                }
                toRestore = nil
            }
            Button(L.t("ביטול", "Cancel"), role: .cancel) { toRestore = nil }
        }
        .sheet(isPresented: $showExport) {
            VStack(spacing: 18) {
                Text(L.t("קובץ הגיבוי מוכן", "The backup file is ready")).font(.headline)
                if let u = exportURL { ShareLink(item: u) { Label(L.t("שמור או שתף את הקובץ", "Save or share the file"), systemImage: "square.and.arrow.up") }.buttonStyle(.borderedProminent) }
                Button(L.t("סגור", "Close")) { showExport = false }
            }
            .padding()
            .environment(\.layoutDirection, L.en ? .leftToRight : .rightToLeft)
            .presentationDetents([.medium])
        }
    }
}