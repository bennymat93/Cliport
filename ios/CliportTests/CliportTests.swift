// Cliport - automated tests that run inside the real app on an iOS simulator (see .github/workflows/test-ios.yml).
// The "live" tests talk to the public relays exactly as a second device would.
import XCTest
import UIKit
@testable import Cliport

/// Spins the main run loop (the engine lives on the main queue) until the condition holds.
@discardableResult
func waitFor(_ timeout: TimeInterval, _ cond: () -> Bool) -> Bool {
    let end = Date().addingTimeInterval(timeout)
    while Date() < end {
        if cond() { return true }
        RunLoop.current.run(until: Date().addingTimeInterval(0.1))
    }
    return cond()
}

/// A second device, built from the app's own protocol pieces.
final class Peer {
    let id = AppInfo.randHex(8)
    let keys: KeyRing
    var links: [MqttLink] = []
    var got: [(sub: String, d: [String: Any])] = []
    var onMsg: ((String, [String: Any]) -> Void)?
    private var seen = Set<String>()

    init(code: String) {
        keys = KeyRing(code: code)!
        for spec in Engine.defaultRelays {
            guard let l = MqttLink.parse(spec) else { continue }
            l.clientIdBase = "sc" + id
            l.subs = [keys.topic + "/ev", keys.topic + "/st/#", keys.topic + "/f/" + id]
            l.onMessage = { [weak self] topic, payload in self?.wire(topic, payload) }
            links.append(l)
            l.start()
        }
    }
    var up: Int { links.filter { $0.up }.count }
    private func wire(_ topic: String, _ payload: Data) {
        guard !payload.isEmpty, topic.count > keys.topic.count + 1, let plain = keys.open(payload), let d = J.decode(plain) else { return }
        if J.s(d, "from") == id { return }
        let sub = String(topic.dropFirst(keys.topic.count + 1))
        let key = J.s(d, "id") + "|" + sub
        if seen.contains(key) { return }
        seen.insert(key)
        got.append((sub, d))
        onMsg?(sub, d)
    }
    @discardableResult
    func send(_ sub: String, _ msg: [String: Any], retain: Bool = false) -> Int {
        var m = msg
        if m["id"] == nil { m["id"] = AppInfo.randHex(8) }
        m["from"] = id
        if m["ts"] == nil { m["ts"] = AppInfo.now() }
        let p = keys.seal(J.encode(m))
        var n = 0
        for l in links where l.publish(keys.topic + "/" + sub, p, retain: retain) { n += 1 }
        return n
    }
    func first(_ sub: String? = nil, _ f: ([String: Any]) -> Bool) -> [String: Any]? {
        got.first { (sub == nil || $0.sub == sub) && f($0.d) }?.d
    }
    func stop() { links.forEach { $0.stop() } }
}

final class CliportTests: XCTestCase {

    // ---------- pure logic ----------
    func testCryptoInteropWithOtherPlatforms() {
        // vectors produced by the web client, whose crypto is verified against the Windows program
        let k = KeyRing(code: "ABCDEFGHIJKLMNOPQRSTUVWXYZ")!
        XCTAssertEqual(k.topic, "sc1/fff6297b519d5ea75751402263f754aa")
        let small = Data(base64Encoded: "Ac51vGuHSf2tEopPdc67Wc89OWjdVyJdwlRlbmxku0IY52zV4fsv1taSYIi5p1vVb9IAb6HAjV8EZ3kcZ1wR3jKbeXbOmpgNTgeQmpaOMn3qSWTBEOYc/G3JBkv9SKNQ9A==")!
        XCTAssertEqual(small[0], 1)
        XCTAssertEqual(J.s(k.open(small).flatMap { J.decode($0) }, "text"), "vector שלום")
        let big = Data(base64Encoded: "AnzppDEL7YYm5mXpV54HRMMtd2vdcPolILG3yo1HIyJiijPRVOckZzozzmuCISmkJs2PRJnNTDnzZOMG5K49P3fLRuLcEYcgHyi3QaLp/LEIbYZv3OE/Tljo2/voNgXiuyn+zS/C2u21IgyFWdcOO6eAinwJwSO9uD92HHdfwQxZ")!
        XCTAssertEqual(big[0], 2, "compressed envelope")
        XCTAssertEqual(J.s(k.open(big).flatMap { J.decode($0) }, "text").count, 1360)
        // tampering is rejected, another group's key opens nothing
        var bad = small; bad[20] ^= 1
        XCTAssertNil(k.open(bad))
        XCTAssertNil(KeyRing(code: KeyRing.newCode())!.open(small))
        // this device's own envelopes: plain and compressed
        let msg = J.encode(["text": String(repeating: "abc ", count: 500)])
        let sealed = k.seal(msg)
        XCTAssertEqual(sealed[0], 2)
        XCTAssertLessThan(sealed.count, msg.count)
        XCTAssertEqual(k.open(sealed), msg)
        XCTAssertEqual(k.open(k.seal(Data("x".utf8))), Data("x".utf8))
    }

    func testPairingCode() {
        let code = KeyRing.newCode()
        XCTAssertEqual(code.count, 26)
        XCTAssertEqual(KeyRing(code: code)?.code, code)
        XCTAssertEqual(KeyRing(code: Base32.pretty(code).lowercased())?.code, code, "dashes and lower case are accepted")
        XCTAssertNil(KeyRing(code: "ABC"))
        XCTAssertNil(KeyRing(code: ""))
    }

    func testBackupMadeByWindowsOpensHere() {
        let file = #"{"cliportBackup":1,"name":"Backup 26/11/2026 01:12","description":"","created":1795648338734,"device":"TEST-PC","app":"2.0","protected":true,"salt":"rwJqYEZd/MYWWKfLbvbFEA==","data":"AfbJLyTsPC0e2fMhpD4DRYbL0tnTsXYIk2D8MxE0S/wVpu+PTt0Ngc8ensXv5wJNNtwIbC3afoLTwlfU1AVvoValEYeSLM3Rs3JiDdX0RISlpvc4mESCdzlQQTgq+EbeKSWfdBLjxcp4ICnznns4uTKgjN612xWYFGD2pqSfzpRJTVuQsN5FtfKHCYq0U505ucmJJsRqTnxSmomEfdODYx9qbPqiWlH/uKcyHMIOySZ77JRl1p1nd9s6oO8yJIDTi4ewhJINS6jVtsXabbElE6fQD9eUk4dDFvyux5VyrxJnr71BnvQBQxv+5xtE7CGfOw=="}"#
        guard let h = BackupFile.header(Data(file.utf8)) else { return XCTFail("header not recognised") }
        XCTAssertTrue(J.b(h, "protected"))
        XCTAssertEqual(J.s(h, "device"), "TEST-PC")
        XCTAssertNil(BackupFile.payload(h, password: "wrong"), "wrong password must not open the backup")
        guard let p = BackupFile.payload(h, password: "pw-123") else { return XCTFail("the right password did not open the backup") }
        XCTAssertEqual(KeyRing(code: J.s(p, "code"))?.topic.hasPrefix("sc1/5976871c"), true, "pairing code restored")
        XCTAssertNotNil(J.d(p, "prefs"))
    }

    func testBackupRoundTrip() {
        let payload: [String: Any] = ["code": KeyRing.newCode(), "pins": [["id": "a1", "text": "pin שלום", "ts": 5, "del": false] as [String: Any]]]
        for password in ["", "s3cret"] {
            let data = BackupFile.create(name: "N", desc: "D", device: "iPhone", created: 123, payload: payload, password: password)
            guard let h = BackupFile.header(data) else { XCTFail("header"); continue }
            XCTAssertEqual(J.s(h, "name"), "N")
            XCTAssertEqual(J.b(h, "protected"), !password.isEmpty)
            let p = BackupFile.payload(h, password: password)
            XCTAssertEqual(J.s(p, "code"), J.s(payload, "code"))
            XCTAssertEqual(J.s(J.list(p, "pins").first, "text"), "pin שלום")
            if !password.isEmpty { XCTAssertNil(BackupFile.payload(h, password: "")) }
        }
    }

    // ---------- live: the app's network client against every default relay ----------
    func testLiveEveryRelayCarriesMessages() {
        var ok = 0
        for spec in Engine.defaultRelays {
            let topic = "sc1/test" + AppInfo.randHex(12) + "/ev"
            guard let rx = MqttLink.parse(spec), let tx = MqttLink.parse(spec) else { XCTFail("bad relay \(spec)"); continue }
            var received: Data?
            rx.clientIdBase = "scT" + AppInfo.randHex(4); tx.clientIdBase = "scT" + AppInfo.randHex(4)
            rx.subs = [topic]
            rx.onMessage = { t, p in if t == topic { received = p } }
            rx.start(); tx.start()
            let up = waitFor(25) { rx.up && tx.up }
            if up {
                waitFor(1.5) { false }
                let payload = Data((0..<70_000).map { _ in UInt8.random(in: 0...255) })      // larger than one read: exercises packet reassembly
                tx.publish(topic, payload, retain: false)
                if waitFor(15, { received != nil }) && received == payload { ok += 1; print("RELAY OK \(spec)") } else { print("RELAY NO DELIVERY \(spec)") }
            } else { print("RELAY NO CONNECTION \(spec)") }
            rx.stop(); tx.stop()
        }
        XCTAssertGreaterThanOrEqual(ok, 2, "at least two of the three public relays must carry messages")
    }

    // ---------- live: the whole engine against a second device ----------
    func testLiveEndToEnd() throws {
        let eng = Engine.shared
        eng.unpair()
        eng.createGroup()
        XCTAssertTrue(waitFor(40) { eng.upCount > 0 }, "the app connects to a relay")
        let code = try XCTUnwrap(eng.keys?.code)
        let peer = Peer(code: code)
        defer { peer.stop(); eng.unpair() }
        XCTAssertTrue(waitFor(40) { peer.up > 0 }, "the second device connects")
        waitFor(3) { false }
        let active = UIApplication.shared.applicationState == .active
        print("APP STATE active=\(active) isActive=\(eng.isActive) relays=\(eng.upCount)/\(eng.relayCount) peerRelays=\(peer.up)")
        func rand() -> String { AppInfo.randHex(4) }

        // presence, both ways
        XCTAssertTrue(waitFor(20) { peer.first("st/dev/" + eng.deviceId) { J.b($0, "online") && J.s($0, "plat") == "ios" } != nil }, "the app announces itself")
        peer.send("st/dev/" + peer.id, ["t": "dev", "name": "Test Peer", "plat": "win", "ver": "t", "online": true])
        XCTAssertTrue(waitFor(15) { eng.devices.contains { $0.name == "Test Peer" && $0.online } }, "the app lists the other device")

        // text: other device -> app
        let t1 = "from-peer-\(rand()) שלום"
        peer.send("ev", ["t": "clip", "kind": "text", "text": t1])
        XCTAssertTrue(waitFor(15) { eng.history.first == t1 }, "incoming text reaches the history")
        if active { XCTAssertEqual(UIPasteboard.general.string, t1, "incoming text is on the pasteboard") }

        // text: app -> other device (history / pin "copy" action)
        let t2 = "from-ios-\(rand()) 😀"
        eng.copy(t2)
        XCTAssertTrue(waitFor(15) { peer.first("ev") { J.s($0, "t") == "clip" && J.s($0, "text") == t2 } != nil }, "text sent from the app arrives")

        // text: copied on the device, picked up by the app on its own
        if active {
            let t3 = "copied-\(rand())"
            UIPasteboard.general.string = t3
            XCTAssertTrue(waitFor(12) { peer.first("ev") { J.s($0, "text") == t3 } != nil }, "a copy on the device is detected and sent automatically")
        }
        // an echo of what the app itself received must never be sent back
        XCTAssertNil(peer.first("ev") { J.s($0, "text") == t1 }, "no echo of received text")

        // pins, both ways
        let p1 = "pin-\(rand())"
        eng.pin(p1)
        XCTAssertTrue(waitFor(15) { peer.first("st/pins") { J.list($0, "items").contains { J.s($0, "text") == p1 && !J.b($0, "del") } } != nil }, "a pin made in the app is published")
        let p2 = "peer-pin-\(rand())"
        peer.send("st/pins", ["t": "pins", "items": [["id": AppInfo.randHex(6), "text": p2, "ts": AppInfo.now(), "del": false] as [String: Any]]], retain: true)
        XCTAssertTrue(waitFor(15) { eng.pins.contains { $0.text == p2 } && eng.pins.contains { $0.text == p1 } }, "a pin from the other device is merged, the local one kept")
        eng.unpin(p1)
        XCTAssertTrue(waitFor(15) { peer.first("st/pins") { J.list($0, "items").contains { J.b($0, "del") } } != nil }, "unpinning is published as a tombstone")

        // diagnostics
        peer.send("ev", ["t": "diagreq"])
        XCTAssertTrue(waitFor(15) { peer.first("f/" + peer.id) { J.s($0, "t") == "diag" && J.s($0, "info").contains("iOS") && !J.s($0, "log").contains(t1) } != nil },
                      "diagnostics arrive, without clipboard content")

        // a very large text arrives as a file and is fetched in chunks
        let bigText = String(repeating: "Large text line \(rand()) שורה ארוכה\n", count: 9000)
        let big = Data(bigText.utf8)
        let fid = AppInfo.randHex(8), chunk = Engine.chunk, total = (big.count + chunk - 1) / chunk
        func chunkMsg(_ seq: Int) -> [String: Any] {
            ["t": "fchunk", "fid": fid, "idx": 0, "seq": seq, "total": total, "data": big.subdata(in: seq * chunk..<min((seq + 1) * chunk, big.count)).base64EncodedString()]
        }
        peer.onMsg = { [unowned peer] _, d in
            guard J.s(d, "fid") == fid, J.s(d, "to") == peer.id else { return }
            let to = J.s(d, "from")
            if J.s(d, "t") == "freq" {
                peer.send("ev", ["t": "fres", "fid": fid, "idx": 0, "to": to, "mode": "mqtt", "total": total, "size": big.count])
                for seq in 0..<total { peer.send("f/" + to, chunkMsg(seq)) }
            } else if J.s(d, "t") == "fnak" {
                for s in J.strs(d, "seqs").compactMap({ Int($0) }) where s >= 0 && s < total { peer.send("f/" + to, chunkMsg(s)) }
            }
        }
        peer.send("ev", ["t": "offer", "fid": fid, "image": false, "text": true, "files": [["name": "Cliport-text-1.txt", "size": big.count, "mime": "text/plain"] as [String: Any]]])
        XCTAssertTrue(waitFor(60) { !eng.offerReady.isEmpty }, "the large text is fetched (\(total) chunks)")
        if let u = eng.offerReady.first { XCTAssertEqual(try Data(contentsOf: u), big, "the received file is identical") }
        if active { XCTAssertEqual(UIPasteboard.general.string, bigText, "the large text is on the pasteboard") }
        peer.onMsg = nil

        // a file offered by the app is served in chunks, with re-requests answered
        let bytes = Data((0..<130_000).map { _ in UInt8.random(in: 0...255) })
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("send-\(rand()).bin")
        try bytes.write(to: url)
        eng.localFiles([url], image: false)
        var offer: [String: Any]?
        XCTAssertTrue(waitFor(15) { offer = peer.first("ev") { J.s($0, "t") == "offer" && J.s(J.list($0, "files").first, "name") == url.lastPathComponent }; return offer != nil }, "the app offers the file")
        XCTAssertEqual(J.l(J.list(offer, "files").first, "size"), Int64(bytes.count))
        let fid2 = J.s(offer, "fid")
        peer.send("ev", ["t": "freq", "fid": fid2, "idx": 0, "to": eng.deviceId])
        func parts() -> [Int: Data] {
            var m: [Int: Data] = [:]
            for g in peer.got where g.sub == "f/" + peer.id && J.s(g.d, "t") == "fchunk" && J.s(g.d, "fid") == fid2 { m[Int(J.l(g.d, "seq"))] = Data(base64Encoded: J.s(g.d, "data")) ?? Data() }
            return m
        }
        let want = (bytes.count + chunk - 1) / chunk
        if !waitFor(20, { parts().count >= want }) {
            let missing = (0..<want).filter { parts()[$0] == nil }
            print("RE-REQUESTING \(missing.count) chunk(s)")
            peer.send("ev", ["t": "fnak", "fid": fid2, "idx": 0, "to": eng.deviceId, "seqs": missing])
        }
        XCTAssertTrue(waitFor(30) { parts().count >= want }, "all \(want) chunks arrive")
        let got = (0..<want).reduce(into: Data()) { $0.append(parts()[$1] ?? Data()) }
        XCTAssertEqual(got, bytes, "the served file is identical")
    }
}
