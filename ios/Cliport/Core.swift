// Cliport v1.0 (iOS / iPadOS) - logging, helpers, end-to-end crypto. Wire-compatible with the Windows app.
import Foundation
import CryptoKit
import CommonCrypto

enum AppInfo {
    static let version = "1.0"
    static func now() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }
    static func randHex(_ bytes: Int) -> String {
        var b = [UInt8](repeating: 0, count: bytes)
        _ = SecRandomCopyBytes(kSecRandomDefault, bytes, &b)
        return b.map { String(format: "%02x", $0) }.joined()
    }
    static func sizeText(_ n: Int64) -> String {
        let f = ByteCountFormatter(); f.countStyle = .file
        return f.string(fromByteCount: n)
    }
    static var docs: URL { FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0] }
    static var caches: URL { FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0] }
}

enum Log {
    private static let q = DispatchQueue(label: "sc.log")
    static func write(_ msg: String) {
        let line = ISO8601DateFormatter().string(from: Date()) + " " + msg + "\n"
        q.async {
            let url = AppInfo.caches.appendingPathComponent("app.log")
            if let attr = try? FileManager.default.attributesOfItem(atPath: url.path), let size = attr[.size] as? NSNumber, size.intValue > 1_000_000 {
                try? FileManager.default.removeItem(at: url)
            }
            if let h = try? FileHandle(forWritingTo: url) {
                h.seekToEndOfFile(); h.write(Data(line.utf8)); try? h.close()
            } else {
                try? Data(line.utf8).write(to: url)
            }
        }
    }
}

/// JSON dictionary accessors that tolerate the number/bool/string variations of both platforms.
enum J {
    static func s(_ d: [String: Any]?, _ k: String) -> String {
        guard let v = d?[k] else { return "" }
        if let s = v as? String { return s }
        if let n = v as? NSNumber { return n.stringValue }
        return ""
    }
    static func l(_ d: [String: Any]?, _ k: String) -> Int64 {
        guard let v = d?[k] else { return 0 }
        if let n = v as? NSNumber { return n.int64Value }
        if let s = v as? String { return Int64(s) ?? 0 }
        return 0
    }
    static func b(_ d: [String: Any]?, _ k: String) -> Bool {
        guard let v = d?[k] else { return false }
        if let n = v as? NSNumber { return n.boolValue }
        return false
    }
    static func d(_ d: [String: Any]?, _ k: String) -> [String: Any]? { d?[k] as? [String: Any] }
    static func list(_ d: [String: Any]?, _ k: String) -> [[String: Any]] { (d?[k] as? [[String: Any]]) ?? [] }
    static func strs(_ d: [String: Any]?, _ k: String) -> [String] {
        guard let a = d?[k] as? [Any] else { return [] }
        return a.compactMap { ($0 as? String) ?? ($0 as? NSNumber)?.stringValue }
    }
    static func encode(_ d: [String: Any]) -> Data { (try? JSONSerialization.data(withJSONObject: d, options: [])) ?? Data() }
    static func decode(_ data: Data) -> [String: Any]? { (try? JSONSerialization.jsonObject(with: data, options: [])) as? [String: Any] }
}

enum Base32 {
    private static let alphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567")
    static func encode(_ data: Data) -> String {
        var out = ""; var buf = 0; var bits = 0
        for byte in data {
            buf = (buf << 8) | Int(byte); bits += 8
            while bits >= 5 { out.append(alphabet[(buf >> (bits - 5)) & 31]); bits -= 5 }
            buf &= 0xFFFF
        }
        if bits > 0 { out.append(alphabet[(buf << (5 - bits)) & 31]) }
        return out
    }
    static func decode(_ s: String) -> Data {
        var out = Data(); var buf = 0; var bits = 0
        for ch in s.uppercased() {
            var c = ch
            if c == "0" { c = "O" }; if c == "1" { c = "I" }; if c == "8" { c = "B" }
            guard let v = alphabet.firstIndex(of: c) else { continue }
            buf = ((buf << 5) | v) & 0xFFFFFF; bits += 5
            if bits >= 8 { out.append(UInt8((buf >> (bits - 8)) & 255)); bits -= 8 }
        }
        return out
    }
    static func pretty(_ code: String) -> String {
        var out = ""
        for (i, c) in code.enumerated() { if i > 0 && i % 5 == 0 { out.append("-") }; out.append(c) }
        return out
    }
}

/// Shared-secret keys. HKDF-SHA256(secret, salt "Cliport-v1", info "enc" | "mac" | "topic").
/// Envelope: 0x01 | IV(16) | AES-256-CBC(PKCS7) | HMAC-SHA256(0x01|IV|CT).
struct KeyRing {
    let secret: Data
    private let encKey: Data
    private let macKey: SymmetricKey
    let topic: String

    init?(code: String) {
        let s = Base32.decode(code)
        guard s.count == 16 else { return nil }
        self.init(secret: s)
    }
    init(secret: Data) {
        self.secret = secret
        func derive(_ info: String) -> Data {
            let k = HKDF<SHA256>.deriveKey(inputKeyMaterial: SymmetricKey(data: secret), salt: Data("Cliport-v1".utf8), info: Data(info.utf8), outputByteCount: 32)
            return k.withUnsafeBytes { Data($0) }
        }
        encKey = derive("enc")
        macKey = SymmetricKey(data: derive("mac"))
        topic = "sc1/" + derive("topic").prefix(16).map { String(format: "%02x", $0) }.joined()
    }
    var code: String { Base32.encode(secret) }
    static func newCode() -> String {
        var b = [UInt8](repeating: 0, count: 16)
        _ = SecRandomCopyBytes(kSecRandomDefault, 16, &b)
        return Base32.encode(Data(b))
    }

    private func aes(_ op: Int, iv: Data, input: Data) -> Data? {
        let capacity = input.count + kCCBlockSizeAES128
        var out = [UInt8](repeating: 0, count: capacity)
        var moved = 0
        let keyBytes = [UInt8](encKey), ivBytes = [UInt8](iv), inBytes = [UInt8](input)
        let status = CCCrypt(CCOperation(op), CCAlgorithm(kCCAlgorithmAES), CCOptions(kCCOptionPKCS7Padding),
                             keyBytes, keyBytes.count, ivBytes, inBytes, inBytes.count, &out, capacity, &moved)
        guard status == Int32(kCCSuccess) else { return nil }
        return Data(out[0..<moved])
    }
    func seal(_ plain: Data) -> Data {
        var iv = [UInt8](repeating: 0, count: 16)
        _ = SecRandomCopyBytes(kSecRandomDefault, 16, &iv)
        let ct = aes(kCCEncrypt, iv: Data(iv), input: plain) ?? Data()
        var env = Data([1]); env.append(contentsOf: iv); env.append(ct)
        let tag = HMAC<SHA256>.authenticationCode(for: env, using: macKey)
        env.append(contentsOf: Data(tag))
        return env
    }
    func open(_ envelope: Data) -> Data? {
        let env = Data(envelope)                      // normalise slice indices
        guard env.count >= 1 + 16 + 16 + 32, env[0] == 1 else { return nil }
        let n = env.count - 32
        let body = Data(env[0..<n]), tag = Data(env[n..<env.count])
        guard HMAC<SHA256>.isValidAuthenticationCode(tag, authenticating: body, using: macKey) else { return nil }
        return aes(kCCDecrypt, iv: Data(env[1..<17]), input: Data(env[17..<n]))
    }
}

enum Mime {
    static func of(_ name: String) -> String {
        switch (name as NSString).pathExtension.lowercased() {
        case "pdf": return "application/pdf"
        case "png": return "image/png"
        case "jpg", "jpeg": return "image/jpeg"
        case "gif": return "image/gif"
        case "heic": return "image/heic"
        case "txt": return "text/plain"
        case "doc", "docx": return "application/msword"
        case "xls", "xlsx": return "application/vnd.ms-excel"
        case "ppt", "pptx": return "application/vnd.ms-powerpoint"
        case "zip": return "application/zip"
        case "mp4": return "video/mp4"
        case "mov": return "video/quicktime"
        case "mp3": return "audio/mpeg"
        default: return "application/octet-stream"
        }
    }
    static func label(_ name: String) -> String {
        let e = (name as NSString).pathExtension.uppercased()
        return e.isEmpty ? "קובץ" : "קובץ " + e
    }
}
