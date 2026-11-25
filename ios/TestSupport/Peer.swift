// Cliport tests - a second device built from the app's own protocol pieces (used by the unit tests and the UI tests).
import Foundation
#if HOSTED
@testable import Cliport
#endif

/// Spins the main run loop (the network client lives on the main queue) until the condition holds.
@discardableResult
func waitFor(_ timeout: TimeInterval, _ cond: () -> Bool) -> Bool {
    let end = Date().addingTimeInterval(timeout)
    while Date() < end {
        if cond() { return true }
        RunLoop.current.run(until: Date().addingTimeInterval(0.1))
    }
    return cond()
}

final class Peer {
    static let relays = ["broker.emqx.io:8883", "broker.hivemq.com:8883", "test.mosquitto.org:8886"]
    let id = AppInfo.randHex(8)
    let keys: KeyRing
    var links: [MqttLink] = []
    var got: [(sub: String, d: [String: Any])] = []
    var onMsg: ((String, [String: Any]) -> Void)?
    private var seen = Set<String>()

    init(code: String) {
        keys = KeyRing(code: code)!
        for spec in Peer.relays {
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
    /// A text copied on this "device": an event for whoever is listening, and retained state for whoever connects later.
    func copy(_ text: String) {
        let m: [String: Any] = ["t": "clip", "kind": "text", "text": text, "id": AppInfo.randHex(8), "ts": AppInfo.now()]
        send("ev", m); send("st/clip", m, retain: true)
    }
    func first(_ sub: String? = nil, _ f: ([String: Any]) -> Bool) -> [String: Any]? {
        got.first { (sub == nil || $0.sub == sub) && f($0.d) }?.d
    }
    func gotText(_ text: String) -> Bool { first { J.s($0, "t") == "clip" && J.s($0, "text") == text } != nil }
    func stop() { links.forEach { $0.stop() } }
}
