// Cliport v1.6 (iOS) - minimal MQTT 3.1.1 client over TLS (Network.framework). Everything runs on the main queue.
import Foundation
import Network

final class MqttLink {
    let host: String
    let port: UInt16
    var user: String?
    var pass: String?
    private(set) var up = false
    var onMessage: ((String, Data) -> Void)?
    var onState: ((MqttLink) -> Void)?
    var clientIdBase = ""
    var subs: [String] = []
    var willTopic = ""
    var willPayload: (() -> Data?)?

    private var conn: NWConnection?
    private var buf = Data()
    private var stopped = true
    private var backoff = 1.0
    private var gen = 0
    private var pingTimer: Timer?
    private var lastRx = Date()

    init(host: String, port: UInt16) { self.host = host; self.port = port }

    /// "host:port" or "user:pass@host:port"
    static func parse(_ specIn: String) -> MqttLink? {
        var spec = specIn.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !spec.isEmpty else { return nil }
        var user: String?; var pass: String?
        if let at = spec.lastIndex(of: "@") {
            let cred = String(spec[spec.startIndex..<at]); spec = String(spec[spec.index(after: at)...])
            if let c = cred.firstIndex(of: ":") { user = String(cred[cred.startIndex..<c]); pass = String(cred[cred.index(after: c)...]) } else { user = cred; pass = "" }
        }
        guard let colon = spec.lastIndex(of: ":"), let p = UInt16(spec[spec.index(after: colon)...]) else { return nil }
        let l = MqttLink(host: String(spec[spec.startIndex..<colon]), port: p)
        l.user = user; l.pass = pass
        return l
    }

    func start() { stopped = false; if conn == nil { connect() } }
    func stop() { stopped = true; teardown() }
    /// Self-healing: drop a link that has been silent (no ping replies) and reconnect right away.
    func healIfStale() {
        if up && Date().timeIntervalSince(lastRx) > 32 { Log.write("healing stale relay \(host)"); dropped(gen) }
        else if !up && conn == nil && !stopped { connect() }
    }
    func kick() { backoff = 1.0; dropped(gen) }

    private func teardown() {
        gen += 1
        pingTimer?.invalidate(); pingTimer = nil
        if let c = conn {
            c.stateUpdateHandler = nil
            if up {
                // a clean MQTT DISCONNECT: without it the relay publishes our last-will and every device sees us "offline"
                c.send(content: Data([0xE0, 0]), completion: .contentProcessed { _ in c.cancel() })
                DispatchQueue.main.asyncAfter(deadline: .now() + 2) { c.cancel() }
            } else { c.cancel() }
        }
        conn = nil
        if up { up = false; onState?(self) }
    }

    private func connect() {
        gen += 1
        let g = gen
        buf = Data()
        let tcp = NWProtocolTCP.Options()
        tcp.noDelay = true
        tcp.connectionTimeout = 10
        guard let nwPort = NWEndpoint.Port(rawValue: port) else { return }
        let c = NWConnection(host: NWEndpoint.Host(host), port: nwPort, using: NWParameters(tls: NWProtocolTLS.Options(), tcp: tcp))
        conn = c
        c.stateUpdateHandler = { [weak self] state in
            guard let self = self, g == self.gen else { return }
            switch state {
            case .ready:
                self.sendConnect()
                self.receive(c, g)
            case .failed, .cancelled, .waiting:
                self.dropped(g)
            default:
                break
            }
        }
        c.start(queue: .main)
    }

    private func dropped(_ g: Int) {
        guard g == gen else { return }
        if up { Log.write("relay down: \(host)") }
        teardown()
        guard !stopped else { return }
        let delay = backoff + Double.random(in: 0...0.7)
        backoff = min(backoff * 2, 30)
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            guard let self = self, !self.stopped, self.conn == nil else { return }
            self.connect()
        }
    }

    private func receive(_ c: NWConnection, _ g: Int) {
        c.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            guard let self = self, g == self.gen else { return }
            if let d = data, !d.isEmpty {
                self.lastRx = Date()
                self.buf.append(d)
                self.parse()
            }
            if isComplete || error != nil { self.dropped(g); return }
            self.receive(c, g)
        }
    }

    private func parse() {
        while true {
            let bytes = [UInt8](buf)
            guard bytes.count >= 2 else { return }
            var len = 0, mul = 1, i = 1
            var complete = false
            while i < bytes.count && i <= 4 {
                let d = Int(bytes[i]); len += (d & 127) * mul; mul *= 128; i += 1
                if d & 128 == 0 { complete = true; break }
            }
            guard complete else { return }
            guard bytes.count >= i + len else { return }
            let header = bytes[0]
            let body = Array(bytes[i..<(i + len)])
            buf = Data(bytes[(i + len)...])
            handle(header, body)
        }
    }

    private func handle(_ header: UInt8, _ b: [UInt8]) {
        switch header >> 4 {
        case 2:   // CONNACK
            guard b.count >= 2, b[1] == 0 else { Log.write("relay \(host): CONNACK refused"); dropped(gen); return }
            var body: [UInt8] = [0, 1]
            for s in subs { body += Self.str(s); body.append(0) }
            send(0x82, body)
            up = true; backoff = 1.0
            Log.write("relay up: \(host)")
            pingTimer?.invalidate()
            pingTimer = Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in self?.send(0xC0, []) }
            onState?(self)
        case 3:   // PUBLISH
            guard b.count >= 2 else { return }
            let tl = (Int(b[0]) << 8) | Int(b[1])
            var off = 2 + tl
            guard b.count >= off else { return }
            let topic = String(decoding: b[2..<off], as: UTF8.self)
            if (header >> 1) & 3 > 0 { off += 2 }
            guard b.count >= off else { return }
            onMessage?(topic, Data(b[off...]))
        default:
            break
        }
    }

    private static func str(_ s: String) -> [UInt8] { bin([UInt8](s.utf8)) }
    private static func bin(_ b: [UInt8]) -> [UInt8] { [UInt8(b.count >> 8), UInt8(b.count & 255)] + b }

    private func sendConnect() {
        var body = Self.str("MQTT") + [4]
        let will = willPayload?()
        let auth = !(user ?? "").isEmpty
        var flags: UInt8 = 0x02
        if will != nil { flags |= 0x04 | 0x20 }
        if auth { flags |= 0x80 | 0x40 }
        body += [flags, 0, 30]
        body += Self.str(clientIdBase + AppInfo.randHex(2))
        if let w = will { body += Self.str(willTopic); body += Self.bin([UInt8](w)) }
        if auth { body += Self.str(user ?? ""); body += Self.str(pass ?? "") }
        send(0x10, body)
    }

    private func send(_ header: UInt8, _ body: [UInt8]) {
        guard let c = conn else { return }
        var pkt: [UInt8] = [header]
        var x = body.count
        repeat { var e = UInt8(x % 128); x /= 128; if x > 0 { e |= 128 }; pkt.append(e) } while x > 0
        pkt += body
        let g = gen
        c.send(content: Data(pkt), completion: .contentProcessed { [weak self] err in
            if err != nil { self?.dropped(g) }
        })
    }

    @discardableResult
    func publish(_ topic: String, _ payload: Data, retain: Bool) -> Bool {
        guard up else { return false }
        send(0x30 | (retain ? 1 : 0), Self.str(topic) + [UInt8](payload))
        return true
    }
}
