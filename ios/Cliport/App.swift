// Cliport v1.0 (iOS) - app entry, lifecycle, notification actions, Shortcuts actions.
import SwiftUI
import UIKit
import UserNotifications
import AppIntents

@main
struct CliportApp: App {
    @UIApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    @Environment(\.scenePhase) private var phase
    @StateObject private var eng = Engine.shared

    var body: some Scene {
        WindowGroup {
            RootView().environmentObject(eng)
        }
        .onChange(of: phase) { p in
            if p == .active { eng.becameActive() } else if p == .background { eng.enteredBackground() }
        }
    }
}

final class AppDelegate: NSObject, UIApplicationDelegate, UNUserNotificationCenterDelegate {
    func application(_ application: UIApplication, didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil) -> Bool {
        Recovery.onLaunch()
        UNUserNotificationCenter.current().delegate = self
        Notifier.setup()
        Engine.shared.start()
        if let n = Recovery.notice { Notifier.info("Cliport - התאוששות (דרגה \(Recovery.level))", n) }
        return true
    }
    func applicationWillTerminate(_ application: UIApplication) {
        Recovery.markAlive(false)
        if Engine.shared.paired && Engine.shared.keepAlive {
            Notifier.cancelDeadman()
            Notifier.info("הסנכרון של Cliport נעצר", "האפליקציה נסגרה. הקש כדי לחדש את הסנכרון.")
        }
    }
    func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification,
                                withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) {
        // the in-app dialog already asks about pending files while we are in the foreground
        completionHandler(notification.request.identifier == "offer" ? [] : [.banner, .sound])
    }
    func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse,
                                withCompletionHandler completionHandler: @escaping () -> Void) {
        let action = response.actionIdentifier
        DispatchQueue.main.async {
            if action == "ACCEPT" { Engine.shared.acceptOffer() }
            else if action == "DECLINE" { Engine.shared.declineOffer() }
            completionHandler()
        }
    }
}

// ---------- Shortcuts actions (used by automations, Back Tap, Action Button, Home Screen shortcuts) ----------

struct SendTextIntent: AppIntent {
    static var title: LocalizedStringResource = "שלח טקסט לכל המכשירים"
    static var description = IntentDescription("שולח טקסט (למשל את תוכן הלוח) לכל המכשירים המצומדים ב-Cliport.")
    static var openAppWhenRun: Bool = false

    @Parameter(title: "טקסט")
    var text: String

    func perform() async throws -> some IntentResult {
        _ = await Engine.shared.sendFromIntent(text: text)
        return .result()
    }
}

struct LatestClipIntent: AppIntent {
    static var title: LocalizedStringResource = "קבל את הפריט האחרון מ-Cliport"
    static var description = IntentDescription("מחזיר את הטקסט האחרון שסונכרן.")
    static var openAppWhenRun: Bool = false

    func perform() async throws -> some IntentResult & ReturnsValue<String> {
        let latest = await MainActor.run { Engine.shared.history.first ?? "" }
        return .result(value: latest)
    }
}

struct ConnectPCIntent: AppIntent {
    static var title: LocalizedStringResource = "התחבר למחשב (RDP)"
    static var description = IntentDescription("פותח חיבור שולחן עבודה מרוחק למחשב מהקבוצה לפי שמו.")
    static var openAppWhenRun: Bool = true

    @Parameter(title: "שם המחשב")
    var name: String

    func perform() async throws -> some IntentResult {
        let wanted = name.lowercased()
        for _ in 0..<30 {       // wait for the device list to arrive from the relay
            let ready = await MainActor.run { Engine.shared.devices.contains { $0.plat == "win" } }
            if ready { break }
            try? await Task.sleep(nanoseconds: 200_000_000)
        }
        await MainActor.run {
            let pcs = Engine.shared.devices.filter { $0.plat == "win" }
            let pc = pcs.first { $0.name.lowercased() == wanted } ?? pcs.first { $0.name.lowercased().contains(wanted) } ?? pcs.first
            if let d = pc, let a = d.addrs.first { RdpLauncher.open(d, address: a) { _ in } }
        }
        return .result()
    }
}
