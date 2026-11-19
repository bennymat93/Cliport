// Cliport v1.0 - core: logging, JSON helpers, config store, crypto.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("Cliport")]
[assembly: System.Reflection.AssemblyProduct("Cliport")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Cliport
{
    static class App
    {
        public const string Version = "1.0";
        public const string Name = "Cliport";
        public static string DataDir;
        public static string PathOf(string n) { return Path.Combine(DataDir, n); }
        public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; }
        public static string RandHex(int bytes)
        {
            var b = new byte[bytes];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return Hex(b);
        }
        public static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
        public static string SizeText(long n)
        {
            if (n < 1024) return n + " B";
            if (n < 1024 * 1024) return (n / 1024.0).ToString("0.#") + " KB";
            if (n < 1024L * 1024 * 1024) return (n / 1048576.0).ToString("0.#") + " MB";
            return (n / 1073741824.0).ToString("0.##") + " GB";
        }
    }

    /// <summary>Two-language UI: every user-facing string is written as L.T(hebrew, english).</summary>
    static class L
    {
        public static bool En;
        public static bool Rtl { get { return !En; } }
        public static string T(string he, string en) { return En ? en : he; }
    }

    static class Log
    {
        static readonly object L = new object();
        public static string Tag = "main";
        public static void Write(string msg)
        {
            try
            {
                lock (L)
                {
                    string p = App.PathOf("app.log");
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 2000000) { File.Copy(p, p + ".old", true); File.Delete(p); }
                    File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + Tag + "] " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    static class Json
    {
        public static string Ser(object o)
        {
            var s = new JavaScriptSerializer(); s.MaxJsonLength = int.MaxValue; return s.Serialize(o);
        }
        public static Dictionary<string, object> Parse(string s)
        {
            var j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue;
            return j.Deserialize<Dictionary<string, object>>(s);
        }
        public static string S(IDictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return "";
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }
        public static long L(IDictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
        }
        public static bool B(IDictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            try { return Convert.ToInt64(v) != 0; } catch { return false; }
        }
        public static Dictionary<string, object> D(IDictionary<string, object> d, string k)
        {
            object v; if (d == null || !d.TryGetValue(k, out v)) return null;
            return v as Dictionary<string, object>;
        }
        public static List<Dictionary<string, object>> List(IDictionary<string, object> d, string k)
        {
            var r = new List<Dictionary<string, object>>();
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null || v is string) return r;
            var e = v as IEnumerable; if (e == null) return r;
            foreach (object o in e) { var x = o as Dictionary<string, object>; if (x != null) r.Add(x); }
            return r;
        }
        public static List<string> Strs(IDictionary<string, object> d, string k)
        {
            var r = new List<string>();
            object v; if (d == null || !d.TryGetValue(k, out v) || v == null || v is string) return r;
            var e = v as IEnumerable; if (e == null) return r;
            foreach (object o in e) if (o != null) r.Add(o.ToString());
            return r;
        }
    }

    /// <summary>JSON file store with atomic write and .bak fallback (used by crash recovery).</summary>
    static class Store
    {
        public static Dictionary<string, object> Load(string name)
        {
            foreach (string p in new[] { App.PathOf(name), App.PathOf(name + ".bak") })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    var d = Json.Parse(File.ReadAllText(p, Encoding.UTF8));
                    if (d != null) return d;
                }
                catch (Exception e) { Log.Write("store load failed " + p + ": " + e.Message); }
            }
            return new Dictionary<string, object>();
        }
        public static void Save(string name, Dictionary<string, object> d)
        {
            try
            {
                string p = App.PathOf(name), tmp = p + ".tmp";
                File.WriteAllText(tmp, Json.Ser(d), Encoding.UTF8);
                if (File.Exists(p)) File.Replace(tmp, p, p + ".bak"); else File.Move(tmp, p);
            }
            catch (Exception e) { Log.Write("store save failed " + name + ": " + e.Message); }
        }
    }

    static class B32
    {
        const string A = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        public static string Encode(byte[] data)
        {
            var sb = new StringBuilder(); int buf = 0, bits = 0;
            foreach (byte b in data)
            {
                buf = (buf << 8) | b; bits += 8;
                while (bits >= 5) { sb.Append(A[(buf >> (bits - 5)) & 31]); bits -= 5; }
            }
            if (bits > 0) sb.Append(A[(buf << (5 - bits)) & 31]);
            return sb.ToString();
        }
        public static byte[] Decode(string s)
        {
            var o = new List<byte>(); int buf = 0, bits = 0;
            foreach (char ch in s.ToUpperInvariant())
            {
                char c = ch; if (c == '0') c = 'O'; if (c == '1') c = 'I'; if (c == '8') c = 'B';
                int v = A.IndexOf(c); if (v < 0) continue;
                buf = ((buf << 5) | v) & 0xFFFFFF; bits += 5;
                if (bits >= 8) { o.Add((byte)((buf >> (bits - 8)) & 255)); bits -= 8; }
            }
            return o.ToArray();
        }
    }

    /// <summary>
    /// End-to-end encryption. All devices share a 128-bit secret (the pairing code).
    /// Keys: HKDF-SHA256(secret, salt "Cliport-v1", info "enc" | "mac" | "topic").
    /// Envelope: VER | IV(16) | AES-256-CBC(PKCS7) | HMAC-SHA256(VER|IV|CT). VER 1 = plain, 2 = raw-deflate compressed. The relay sees only ciphertext.
    /// </summary>
    sealed class KeyRing
    {
        public readonly byte[] Secret; readonly byte[] encKey, macKey; public readonly string Topic;
        public KeyRing(byte[] secret)
        {
            Secret = secret;
            encKey = Hkdf(secret, "enc"); macKey = Hkdf(secret, "mac");
            var t = Hkdf(secret, "topic"); var t16 = new byte[16]; Array.Copy(t, t16, 16);
            Topic = "sc1/" + App.Hex(t16);
        }
        static byte[] Hkdf(byte[] ikm, string info)
        {
            byte[] prk;
            using (var h = new HMACSHA256(Encoding.ASCII.GetBytes("Cliport-v1"))) prk = h.ComputeHash(ikm);
            var i = Encoding.ASCII.GetBytes(info); var m = new byte[i.Length + 1];
            Array.Copy(i, m, i.Length); m[i.Length] = 1;
            using (var h = new HMACSHA256(prk)) return h.ComputeHash(m);
        }
        public static string NewCode() { var b = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return B32.Encode(b); }
        public static KeyRing FromCode(string code)
        {
            if (code == null) return null;
            var b = B32.Decode(code); if (b.Length != 16) return null;
            return new KeyRing(b);
        }
        public string Code { get { return B32.Encode(Secret); } }
        public static string Pretty(string code)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < code.Length; i++) { if (i > 0 && i % 5 == 0) sb.Append('-'); sb.Append(code[i]); }
            return sb.ToString();
        }
        static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true)) z.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }
        static byte[] Inflate(byte[] data)
        {
            using (var src = new MemoryStream(data)) using (var z = new System.IO.Compression.DeflateStream(src, System.IO.Compression.CompressionMode.Decompress)) using (var dst = new MemoryStream())
            {
                var buf = new byte[65536]; int n;
                while ((n = z.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, n); if (dst.Length > 64 * 1024 * 1024) throw new InvalidDataException("too large"); }
                return dst.ToArray();
            }
        }
        public byte[] Seal(byte[] plain) { return Seal(plain, true); }
        /// <summary>compress=true: the payload is deflated first when that makes it smaller (less data on weak links).</summary>
        public byte[] Seal(byte[] plain, bool compress)
        {
            byte ver = 1;
            if (compress && plain.Length >= 200)
            {
                byte[] z = Deflate(plain);
                if (z.Length < plain.Length - plain.Length / 10) { plain = z; ver = 2; }
            }
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey; aes.GenerateIV();
                byte[] ct; using (var e = aes.CreateEncryptor()) ct = e.TransformFinalBlock(plain, 0, plain.Length);
                var o = new byte[1 + 16 + ct.Length + 32];
                o[0] = ver; Array.Copy(aes.IV, 0, o, 1, 16); Array.Copy(ct, 0, o, 17, ct.Length);
                using (var h = new HMACSHA256(macKey)) { var tag = h.ComputeHash(o, 0, 17 + ct.Length); Array.Copy(tag, 0, o, 17 + ct.Length, 32); }
                return o;
            }
        }
        public byte[] Open(byte[] env)
        {
            try
            {
                if (env == null || env.Length < 1 + 16 + 16 + 32 || (env[0] != 1 && env[0] != 2)) return null;
                int n = env.Length - 32;
                byte[] tag; using (var h = new HMACSHA256(macKey)) tag = h.ComputeHash(env, 0, n);
                int diff = 0; for (int i = 0; i < 32; i++) diff |= tag[i] ^ env[n + i];
                if (diff != 0) return null;
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey;
                    var iv = new byte[16]; Array.Copy(env, 1, iv, 0, 16); aes.IV = iv;
                    byte[] plain; using (var d = aes.CreateDecryptor()) plain = d.TransformFinalBlock(env, 17, n - 17);
                    return env[0] == 2 ? Inflate(plain) : plain;
                }
            }
            catch { return null; }
        }
    }

    /// <summary>Local settings. The pairing secret is stored encrypted with Windows DPAPI (current user).</summary>
    sealed class Config
    {
        Dictionary<string, object> d;
        public Config() { d = Store.Load("config.json"); if (S("deviceId") == "") { d["deviceId"] = App.RandHex(8); } }
        public void Save() { Store.Save("config.json", d); }
        public string S(string k) { return Json.S(d, k); }
        public long L(string k) { return Json.L(d, k); }
        public bool B(string k, bool def) { return d.ContainsKey(k) ? Json.B(d, k) : def; }
        public void Set(string k, object v) { d[k] = v; }
        public string DeviceId { get { return S("deviceId"); } }
        public string DeviceName { get { string n = S("name"); return n == "" ? Environment.MachineName : n; } }
        public string Code
        {
            get
            {
                try
                {
                    string p = S("secret"); if (p == "") return "";
                    return Encoding.ASCII.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser));
                }
                catch { return ""; }
            }
            set { d["secret"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.ASCII.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        }
    }
}
