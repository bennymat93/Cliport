// Cliport - drives the real user interface on an iOS simulator and keeps a screenshot of every screen,
// in both languages. The screenshots are published by .github/workflows/test-ios.yml.
import XCTest
import UIKit

final class CliportUITests: XCTestCase {
    override func setUp() { continueAfterFailure = true }

    private func shot(_ name: String) {
        let a = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        a.name = name
        a.lifetime = .keepAlways
        add(a)
    }

    private func walk(lang: String) {
        let app = XCUIApplication()
        app.launchArguments = ["-lang", lang]
        app.launch()
        // the system's notification-permission question
        let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
        for label in ["Allow", "אפשר"] {
            let b = springboard.buttons[label]
            if b.waitForExistence(timeout: 3) { b.tap(); break }
        }
        XCTAssertTrue(app.wait(for: .runningForeground, timeout: 15), "the app starts and stays in front")

        let create = app.buttons[lang == "he" ? "צור קבוצה חדשה" : "Create a new group"]
        if create.waitForExistence(timeout: 8) {
            shot("\(lang)-1-pairing")
            create.tap()
        }
        let tabs = app.tabBars.buttons
        XCTAssertTrue(tabs.element(boundBy: 0).waitForExistence(timeout: 15), "the main screen appears once a group exists")
        XCTAssertEqual(tabs.count, 3)
        sleep(7)                                         // time to connect to the relays
        shot("\(lang)-2-clipboard")
        let status = app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", lang == "he" ? "מחובר ומסנכרן" : "Connected and syncing")).firstMatch
        XCTAssertTrue(status.waitForExistence(timeout: 30), "the status line shows a live connection")
        shot("\(lang)-2-clipboard-connected")
        tabs.element(boundBy: 1).tap(); sleep(1); shot("\(lang)-3-computers")
        tabs.element(boundBy: 2).tap(); sleep(1); shot("\(lang)-4-settings")
        for i in 5...8 { app.swipeUp(); usleep(600_000); shot("\(lang)-\(i)-settings") }
        XCTAssertEqual(app.state, .runningForeground, "no crash while walking through every screen")
        app.terminate()
    }

    func test1English() { walk(lang: "en") }
    func test2Hebrew() { walk(lang: "he") }

    // ---------- the real clipboard, the real app life cycle, a real second device ----------
    private let springboard = XCUIApplication(bundleIdentifier: "com.apple.springboard")
    /// iOS asks before an app reads what another app copied. Answers "Allow Paste" wherever the question shows up.
    @discardableResult
    private func allowPaste(_ app: XCUIApplication, seconds: TimeInterval) -> Bool {
        var tapped = false
        let end = Date().addingTimeInterval(seconds)
        while Date() < end {
            for owner in [springboard, app] {
                let b = owner.buttons["Allow Paste"]
                if b.exists { b.tap(); tapped = true }
            }
            waitFor(0.4) { false }
        }
        return tapped
    }
    /// Reading the pasteboard can make iOS ask for permission and block until the question is answered,
    /// so the read runs on another thread while this one answers the question.
    private func readPasteboard(_ app: XCUIApplication) -> String? {
        let box = PasteBox()
        DispatchQueue.global().async { box.value = UIPasteboard.general.string; box.done = true }
        let end = Date().addingTimeInterval(8)
        while !box.done && Date() < end {
            for owner in [springboard, app] { let b = owner.buttons["Allow Paste"]; if b.exists { b.tap() } }
            waitFor(0.3) { false }
        }
        if !box.done { print("CLIPBOARD the test runner could not read the pasteboard (no answer within 8 s)") }
        return box.done ? box.value : nil
    }
    private final class PasteBox { var value: String?; var done = false }
    private func shows(_ app: XCUIApplication, _ text: String, _ seconds: TimeInterval) -> Bool {
        app.staticTexts.matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch.waitForExistence(timeout: seconds)
    }

    func test3LiveSyncWithTheRealClipboard() {
        let code = KeyRing.newCode()
        let args = ["-lang", "en", "-code", code]
        let app = XCUIApplication()
        app.launchArguments = args
        app.launch()
        for label in ["Allow", "אפשר"] { let b = springboard.buttons[label]; if b.waitForExistence(timeout: 2) { b.tap(); break } }
        XCTAssertTrue(shows(app, "Connected and syncing", 40), "the app joins the group given on the command line")
        let peer = Peer(code: code)
        defer { peer.stop() }
        XCTAssertTrue(waitFor(40) { peer.up > 0 }, "the second device connects")
        waitFor(3) { false }
        func rand() -> String { AppInfo.randHex(3) }

        // 1. the other device copies: the app shows it and puts it on the pasteboard
        let t1 = "from-pc-\(rand())"
        peer.copy(t1)
        XCTAssertTrue(shows(app, t1, 20), "text copied on the other device appears in the app")
        waitFor(1.5) { false }
        let read1 = readPasteboard(app)
        allowPaste(app, seconds: 2)
        print("CLIPBOARD foreground arrival: pasteboard holds the text = \(read1 == t1) (read by the test runner: \(read1 == nil ? "nothing" : "a value"))")

        // 2. something is copied in another app while Cliport is on screen: Cliport notices and sends it
        let t2 = "copied-on-iphone-\(rand())"
        UIPasteboard.general.string = t2
        let asked2 = allowPaste(app, seconds: 6)
        let sent2 = waitFor(15) { peer.gotText(t2) }
        print("CLIPBOARD foreground copy: sent automatically = \(sent2), iOS asked for paste permission = \(asked2)")
        XCTAssertTrue(sent2, "a copy made while the app is on screen is sent automatically")
        shot("live-1-foreground")

        // 3. the app goes to the background (home button), the other device copies
        XCUIDevice.shared.press(.home)
        waitFor(3) { false }
        let t3 = "to-background-\(rand())"
        peer.copy(t3)
        waitFor(6) { false }
        let read3 = readPasteboard(app)
        print("CLIPBOARD background arrival: written to the pasteboard while in the background = \(read3 == t3)")
        shot("live-2-background")                       // shows the notification, when the write was not allowed
        app.activate()
        XCTAssertTrue(shows(app, t3, 20), "what arrived in the background is in the app after reopening it")
        waitFor(2) { false }
        let read3b = readPasteboard(app)
        allowPaste(app, seconds: 2)
        print("CLIPBOARD after reopening: pasteboard holds the background arrival = \(read3b == t3)")
        XCTAssertNil(peer.first("ev") { J.s($0, "text") == t3 }, "a received text is never echoed back")

        // 4. background again: first a copy on the phone (never sent), then a newer copy on the other device -> the newer one wins, no question
        XCUIDevice.shared.press(.home)
        waitFor(2) { false }
        let stale = "old-phone-copy-\(rand())"
        UIPasteboard.general.string = stale
        waitFor(2) { false }
        let t4 = "newer-from-pc-\(rand())"
        peer.copy(t4)
        waitFor(5) { false }
        app.activate()
        allowPaste(app, seconds: 4)
        XCTAssertTrue(shows(app, t4, 20), "the newer item from the other device is shown")
        let overwritten = waitFor(8) { peer.gotText(stale) }
        print("CLIPBOARD stale local copy sent over a newer remote one = \(overwritten) (must be false)")
        XCTAssertFalse(overwritten, "an older copy on the phone must not overwrite a newer item from another device")
        XCTAssertFalse(app.alerts["Which item is newer?"].exists, "no question when the order is known")

        // 5. the app is closed; both sides copy; on opening, the app must ask instead of overwriting either
        app.terminate()
        let local = "phone-while-closed-\(rand())"
        UIPasteboard.general.string = local
        waitFor(1) { false }
        let remote = "pc-while-closed-\(rand())"
        peer.copy(remote)
        waitFor(3) { false }
        app.launchArguments = args
        app.launch()
        allowPaste(app, seconds: 8)
        let question = app.alerts["Which item is newer?"]
        let asked = question.waitForExistence(timeout: 25)
        print("CLIPBOARD both sides changed while closed: the app asks = \(asked)")
        shot("live-3-conflict-question")
        XCTAssertTrue(asked, "both sides changed while the app was closed: the app asks which is newer")
        XCTAssertFalse(peer.gotText(local), "nothing is sent before the user answers")
        if asked {
            question.buttons["Send from this device"].tap()
            XCTAssertTrue(waitFor(15) { peer.gotText(local) }, "after choosing this device, its text is sent")
        }

        // 6. same situation, the other answer
        app.terminate()
        UIPasteboard.general.string = "phone-again-\(rand())"
        waitFor(1) { false }
        let remote2 = "pc-again-\(rand())"
        peer.copy(remote2)
        waitFor(3) { false }
        app.launch()
        allowPaste(app, seconds: 8)
        if question.waitForExistence(timeout: 25) {
            question.buttons.matching(NSPredicate(format: "label BEGINSWITH %@", "Take from")).firstMatch.tap()
            waitFor(2) { false }
            let read6 = readPasteboard(app)
            allowPaste(app, seconds: 2)
            print("CLIPBOARD conflict answered 'take from the other device': pasteboard holds it = \(read6 == remote2)")
            XCTAssertTrue(shows(app, remote2, 10))
        } else { XCTFail("the question did not appear the second time") }
        shot("live-4-end")
        XCTAssertEqual(app.state, .runningForeground)
    }
}
