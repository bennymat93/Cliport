// Cliport v1.0 (iOS) - SwiftUI interface (Hebrew, right-to-left).
import SwiftUI
import UIKit

struct RootView: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        Group {
            if eng.paired { MainTabs() } else { PairView() }
        }
        .environment(\.layoutDirection, .rightToLeft)
    }
}

struct PairView: View {
    @EnvironmentObject var eng: Engine
    @State private var code = ""
    @State private var bad = false
    var body: some View {
        NavigationStack {
            Form {
                Section(footer: Text("כל המכשירים חולקים קוד צימוד סודי אחד. הקוד הוא מפתח ההצפנה - התוכן מוצפן מקצה לקצה.")) {
                    TextField("קוד צימוד (26 תווים)", text: $code)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled(true)
                        .font(.system(.body, design: .monospaced))
                        .environment(\.layoutDirection, .leftToRight)
                    Button("הצטרף עם הקוד") { bad = !eng.pair(code: code) }
                        .disabled(code.isEmpty)
                    PasteButton(payloadType: String.self) { items in
                        if let s = items.first { DispatchQueue.main.async { code = s; bad = !eng.pair(code: s) } }
                    }
                }
                Section(footer: Text("אם זה המכשיר הראשון - צור קבוצה חדשה והזן את הקוד שיוצג בשאר המכשירים.")) {
                    Button("צור קבוצה חדשה") { eng.createGroup() }
                }
            }
            .navigationTitle("Cliport")
            .alert("הקוד אינו תקין", isPresented: $bad) { Button("אישור", role: .cancel) { } }
        }
    }
}

struct MainTabs: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        TabView {
            ClipView().tabItem { Label("לוח", systemImage: "doc.on.clipboard") }
            RdpView().tabItem { Label("מחשבים", systemImage: "desktopcomputer") }
            SettingsView().tabItem { Label("הגדרות", systemImage: "gearshape") }
        }
        .alert("לאשר הדבקת קובץ?", isPresented: Binding(get: { eng.offer != nil && eng.progress < 0 }, set: { _ in }), presenting: eng.offer) { _ in
            Button("אשר הדבקה") { eng.acceptOffer() }
            Button("דחה", role: .cancel) { eng.declineOffer() }
        } message: { o in
            Text(OfferText.describe(o))
        }
    }
}

enum OfferText {
    static func describe(_ o: Offer) -> String {
        var lines = ["מאת: " + o.fromName]
        for f in o.files {
            lines.append("שם: " + f.name)
            lines.append("גודל: " + AppInfo.sizeText(f.size))
            lines.append("סוג: " + Mime.label(f.name))
        }
        return lines.joined(separator: "\n")
    }
}

struct ClipView: View {
    @EnvironmentObject var eng: Engine
    var body: some View {
        NavigationStack {
            List {
                Section {
                    HStack {
                        Circle().fill(eng.upCount > 0 ? Color.green : Color.orange).frame(width: 10, height: 10)
                        Text(eng.paused ? "הסנכרון מושהה" : (eng.upCount > 0 ? "מחובר (\(eng.upCount)/\(eng.relayCount) שרתי ממסר)" : "מתחבר מחדש..."))
                        Spacer()
                    }
                    if !eng.banner.isEmpty { Text(eng.banner).font(.footnote).foregroundColor(.secondary) }
                    if eng.progress >= 0 { ProgressView(value: Double(eng.progress), total: 100) }
                    HStack {
                        Button { eng.sendClipboardNow() } label: { Label("שלח את הלוח עכשיו", systemImage: "arrow.up.doc.on.clipboard") }
                            .buttonStyle(.borderedProminent)
                        Spacer()
                        PasteButton(payloadType: String.self) { items in
                            if let s = items.first { DispatchQueue.main.async { eng.localText(s) } }
                        }
                    }
                }
                if let o = eng.offer {
                    Section("קובץ ממתין לאישור") {
                        Text(OfferText.describe(o)).font(.callout)
                        Button("אשר הדבקה") { eng.acceptOffer() }.disabled(eng.progress >= 0)
                        Button("דחה", role: .destructive) { eng.declineOffer() }
                    }
                }
                if !eng.offerReady.isEmpty {
                    Section("קבצים שהתקבלו (הועתקו ללוח)") {
                        ForEach(eng.offerReady, id: \.self) { url in
                            ShareLink(item: url) { Label(url.lastPathComponent, systemImage: "doc") }
                        }
                    }
                }
                if !eng.pins.isEmpty {
                    Section("נעוצים") {
                        ForEach(eng.pins) { p in ClipRow(text: p.text, pinned: true) }
                    }
                }
                Section("היסטוריה") {
                    let items = eng.history.filter { !eng.isPinned($0) }
                    if items.isEmpty { Text("עדיין אין פריטים").foregroundColor(.secondary) }
                    ForEach(items, id: \.self) { t in ClipRow(text: t, pinned: false) }
                }
            }
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
                if pinned { Image(systemName: "pin.fill").foregroundColor(.red) }
                Text(text).lineLimit(3).foregroundColor(.primary).frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .swipeActions(edge: .leading) {
            Button { if pinned { eng.unpin(text) } else { eng.pin(text) } } label: { Label(pinned ? "בטל נעיצה" : "נעץ", systemImage: pinned ? "pin.slash" : "pin") }
                .tint(.orange)
        }
        .swipeActions(edge: .trailing) {
            if !pinned { Button(role: .destructive) { eng.removeHistory(text) } label: { Label("מחק", systemImage: "trash") } }
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
                if pcs.isEmpty { Text("לא נמצאו מחשבים בקבוצה. התקן את Cliport במחשב וצמד אותו עם אותו קוד.").foregroundColor(.secondary) }
                ForEach(pcs) { d in
                    Section(header: Text(d.name), footer: Text(d.isOnline ? (d.rdpOn ? "זמין לחיבור" : "Remote Desktop כבוי במחשב זה") : "המחשב לא מחובר כרגע")) {
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
                                    Text(a.hasPrefix("100.") ? "Tailscale - מכל רשת" : "רשת מקומית").font(.footnote).foregroundColor(.secondary)
                                }
                            }
                        }
                    }
                }
                Section(footer: Text("החיבור נפתח באפליקציית Windows App של מיקרוסופט (חינם ב-App Store). לחיבור מכל רשת התקן Tailscale באייפון ובמחשבים. לקיצור דרך במסך הבית: אפליקציית \"קיצורים\" ← פעולה \"התחבר למחשב\" של Cliport.")) { EmptyView() }
            }
            .navigationTitle("שולחן עבודה מרוחק")
            .sheet(isPresented: $showFallback) {
                VStack(spacing: 20) {
                    Text("לא ניתן היה לפתוח את החיבור ישירות.").font(.headline)
                    Text("פתח את קובץ החיבור ב-Windows App:")
                    if let f = fallback { ShareLink(item: f) { Label("פתח קובץ ‎.rdp", systemImage: "square.and.arrow.up") }.buttonStyle(.borderedProminent) }
                    Button("סגור") { showFallback = false }
                }
                .padding()
                .environment(\.layoutDirection, .rightToLeft)
                .presentationDetents([.medium])
            }
        }
    }
}

struct SettingsView: View {
    @EnvironmentObject var eng: Engine
    @State private var token = ""
    @State private var chat = ""
    @State private var relays = ""
    @State private var msg = ""
    @State private var confirmUnpair = false
    var body: some View {
        NavigationStack {
            Form {
                Section(header: Text("צימוד"), footer: Text("הזן קוד זה במכשירים נוספים. אל תשתף אותו - זהו מפתח ההצפנה.")) {
                    Text(Base32.pretty(eng.keys?.code ?? "")).font(.system(.footnote, design: .monospaced)).textSelection(.enabled)
                        .environment(\.layoutDirection, .leftToRight)
                    Button("העתק קוד") { UIPasteboard.general.string = eng.keys?.code ?? "" }
                    Button("התנתק מהקבוצה", role: .destructive) { confirmUnpair = true }
                }
                Section(header: Text("פעילות ברקע"), footer: Text("כשהאפשרות פעילה, האפליקציה נשארת מחוברת ברקע ומה שמועתק במחשב נכנס ללוח של המכשיר אוטומטית. צריכת הסוללה עולה מעט.")) {
                    Toggle("השאר מחובר ברקע", isOn: Binding(get: { eng.keepAlive }, set: { eng.keepAlive = $0 }))
                    Toggle("השהה סנכרון", isOn: $eng.paused)
                }
                Section(header: Text("שליחה אוטומטית מהמכשיר"), footer: Text("iOS אינו מאפשר לאף אפליקציה לקרוא את הלוח ברקע. כדי שהעתקה במכשיר תישלח לבד:\n1. הגדרות ← Cliport ← \"הדבקה מיישומים אחרים\" ← אפשר.\n2. אפליקציית \"קיצורים\" ← אוטומציה ← חדשה ← \"יישום\" ← בחר את היישומים ← \"נסגר\" ← \"הפעל מיד\".\n3. הוסף פעולה \"קבל לוח\" ואחריה \"שלח טקסט לכל המכשירים\" (Cliport).\nמעכשיו כל יציאה מיישום שולחת את הלוח. בנוסף, פתיחת Cliport שולחת את הלוח מיד.")) { EmptyView() }
                Section(header: Text("אחסון Telegram לקבצים גדולים (אופציונלי)"), footer: Text(eng.tgReady ? "מוגדר ומשותף לכל המכשירים." : "ללא Telegram: קבצים עד 25MB עוברים דרך הממסר. צור בוט אצל ‎@BotFather‎, שלח לו ‎/start‎ ולחץ \"זהה צ'אט ושמור\".")) {
                    SecureField("Bot Token", text: $token).environment(\.layoutDirection, .leftToRight)
                    TextField("Chat ID", text: $chat).environment(\.layoutDirection, .leftToRight)
                    Button("זהה צ'אט ושמור") { saveTelegram() }
                }
                Section(header: Text("שרת ממסר פרטי (אופציונלי)"), footer: Text("MQTT על TLS: ‎host:port‎ או ‎user:pass@host:port‎. מופץ לכל המכשירים.")) {
                    TextField("host:port", text: $relays).environment(\.layoutDirection, .leftToRight).autocorrectionDisabled(true).textInputAutocapitalization(.never)
                    Button("שמור ממסר") { eng.setRelays(relays); msg = "רשימת הממסרים עודכנה" }
                }
                Section(header: Text("מכשירים בקבוצה")) {
                    ForEach(eng.devices) { d in
                        HStack {
                            Image(systemName: d.plat == "ios" ? "iphone" : "desktopcomputer")
                            Text(d.name)
                            Spacer()
                            Text(d.isOnline ? "מחובר" : "לא מחובר").font(.footnote).foregroundColor(d.isOnline ? .green : .secondary)
                        }
                        .swipeActions { Button("שכח", role: .destructive) { eng.forget(d.id) } }
                    }
                }
                Section(header: Text("אודות")) {
                    Text("Cliport v\(AppInfo.version)")
                    if let n = Recovery.notice { Text("התאוששות (דרגה \(Recovery.level)): \(n)").font(.footnote) }
                    if !msg.isEmpty { Text(msg).font(.footnote).foregroundColor(.secondary) }
                }
            }
            .navigationTitle("הגדרות")
            .onAppear { token = eng.tgToken; chat = eng.tgChat; relays = eng.relays }
            .confirmationDialog("לנתק את המכשיר מקבוצת הסנכרון?", isPresented: $confirmUnpair, titleVisibility: .visible) {
                Button("התנתק", role: .destructive) { eng.unpair() }
                Button("ביטול", role: .cancel) { }
            }
        }
    }
    private func saveTelegram() {
        let t = token.trimmingCharacters(in: .whitespacesAndNewlines), c = chat.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.isEmpty { eng.setTelegram(token: "", chat: ""); msg = "אחסון Telegram בוטל"; return }
        if !c.isEmpty { eng.setTelegram(token: t, chat: c); msg = "אחסון Telegram נשמר"; return }
        Task {
            let found = try? await Telegram.findChat(token: t)
            await MainActor.run {
                if let id = found ?? nil, !id.isEmpty { chat = id; eng.setTelegram(token: t, chat: id); msg = "אחסון Telegram נשמר" }
                else { msg = "לא נמצאה הודעה לבוט. שלח לו ‎/start‎ ונסה שוב." }
            }
        }
    }
}
