// Cliport - drives the real user interface on an iOS simulator and keeps a screenshot of every screen,
// in both languages. The screenshots are published by .github/workflows/test-ios.yml.
import XCTest

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
}
