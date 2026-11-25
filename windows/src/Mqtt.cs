// Cliport v1.6 - minimal MQTT 3.1.1 client over TLS + multi-relay hub (redundant brokers).
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace Cliport
{
    sealed class MqttLink
    {
        public readonly string Host; public readonly int Port;
        public volatile bool Up;
        public Action<MqttLink, string, byte[]> OnMessage;
        public Action<MqttLink> OnUp, OnDown;
        public Func<byte[]> WillPayload;          // evaluated on each connect
        public string WillTopic; public string[] Subs; public string ClientIdBase;
        TcpClient tcp; Stream stream; readonly object wlock = new object();
        Thread th; volatile bool stop; Timer ping; long lastRx;
        static readonly Random rnd = new Random();

        public string User, Pass;                 // optional (private relay)
        public MqttLink(string host, int port) { Host = host; Port = port; }
        /// <summary>Parses "host:port" or "user:pass@host:port". Returns null when malformed.</summary>
        public static MqttLink Parse(string spec)
        {
            try
            {
                spec = spec.Trim(); if (spec == "") return null;
                string user = null, pass = null; int at = spec.LastIndexOf('@');
                if (at > 0)
                {
                    string cred = spec.Substring(0, at); spec = spec.Substring(at + 1);
                    int c = cred.IndexOf(':'); user = c < 0 ? cred : cred.Substring(0, c); pass = c < 0 ? "" : cred.Substring(c + 1);
                }
                int i = spec.LastIndexOf(':'); if (i <= 0) return null;
                var l = new MqttLink(spec.Substring(0, i), int.Parse(spec.Substring(i + 1))); l.User = user; l.Pass = pass; return l;
            }
            catch { return null; }
        }
        public void Start() { stop = false; th = new Thread(Run); th.IsBackground = true; th.Name = "mqtt-" + Host; th.Start(); }
        public void Stop() { stop = true; Close(); }
        volatile bool kicked;
        public void Kick() { kicked = true; Close(); }   // force an immediate reconnect (self-healing, network change)
        public long IdleMs { get { return App.Now() - Interlocked.Read(ref lastRx); } }

        void Close()
        {
            try { if (ping != null) ping.Dispose(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        void Run()
        {
            int backoff = 1000;
            while (!stop)
            {
                try
                {
                    Connect();
                    backoff = 1000;
                    Up = true; Log.Write("relay up: " + Host);
                    if (OnUp != null) OnUp(this);
                    ReadLoop();
                }
                catch (Exception e) { if (!stop && !kicked) Log.Write("relay " + Host + ": " + e.GetType().Name + " " + e.Message); }
                finally
                {
                    Close();
                    if (Up) { Up = false; if (OnDown != null && !stop) { try { OnDown(this); } catch { } } }
                }
                if (stop) break;
                if (kicked) { kicked = false; backoff = 1000; Thread.Sleep(150); continue; }
                int j; lock (rnd) j = rnd.Next(700);
                // sleep in slices so a network-change kick ends the wait at once
                for (int waited = 0; waited < backoff + j && !stop && !kicked; waited += 200) Thread.Sleep(200);
                kicked = false;
                backoff = Math.Min(backoff * 2, 30000);
            }
        }

        void Connect()
        {
            tcp = new TcpClient(); tcp.NoDelay = true;
            var ar = tcp.BeginConnect(Host, Port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(10000)) { tcp.Close(); throw new TimeoutException("connect timeout"); }
            tcp.EndConnect(ar);
            var ssl = new SslStream(tcp.GetStream(), false);
            ssl.AuthenticateAsClient(Host, null, SslProtocols.Tls12, false);
            ssl.ReadTimeout = 15000; ssl.WriteTimeout = 15000;
            stream = ssl;

            var ms = new MemoryStream();
            WStr(ms, "MQTT"); ms.WriteByte(4);
            byte[] will = WillPayload != null ? WillPayload() : null;
            bool auth = !string.IsNullOrEmpty(User);
            ms.WriteByte((byte)(0x02 | (will != null ? 0x04 | 0x20 : 0) | (auth ? 0x80 | 0x40 : 0)));
            ms.WriteByte(0); ms.WriteByte(30);                       // keepalive 30s
            WStr(ms, ClientIdBase + App.RandHex(2));
            if (will != null) { WStr(ms, WillTopic); WBin(ms, will); }
            if (auth) { WStr(ms, User); WStr(ms, Pass ?? ""); }
            Send(0x10, ms.ToArray());
            byte h; byte[] body = ReadPacket(out h);
            if ((h >> 4) != 2 || body.Length < 2 || body[1] != 0) throw new IOException("CONNACK refused");

            ms = new MemoryStream(); ms.WriteByte(0); ms.WriteByte(1);
            foreach (string s in Subs) { WStr(ms, s); ms.WriteByte(0); }
            Send(0x82, ms.ToArray());
            stream.ReadTimeout = 32000;        // pings go out every 10s, so 32s of silence means the link is dead
            Interlocked.Exchange(ref lastRx, App.Now());
            ping = new Timer(delegate { try { Send(0xC0, new byte[0]); } catch { Close(); } }, null, 10000, 10000);
        }

        void ReadLoop()
        {
            while (!stop)
            {
                byte h; byte[] b = ReadPacket(out h);
                Interlocked.Exchange(ref lastRx, App.Now());
                if ((h >> 4) != 3) continue;                       // only PUBLISH matters (QoS 0)
                int tl = (b[0] << 8) | b[1]; string topic = Encoding.UTF8.GetString(b, 2, tl);
                int off = 2 + tl; if (((h >> 1) & 3) > 0) off += 2;
                var payload = new byte[b.Length - off]; Array.Copy(b, off, payload, 0, payload.Length);
                try { if (OnMessage != null) OnMessage(this, topic, payload); } catch (Exception e) { Log.Write("msg handler: " + e); }
            }
        }

        byte[] ReadPacket(out byte header)
        {
            header = (byte)ReadByte();
            int len = 0, mul = 1, d;
            do { d = ReadByte(); len += (d & 127) * mul; mul *= 128; } while ((d & 128) != 0);
            if (len > 8 * 1024 * 1024) throw new IOException("packet too large");
            var b = new byte[len]; int got = 0;
            while (got < len) { int n = stream.Read(b, got, len - got); if (n <= 0) throw new EndOfStreamException(); got += n; }
            return b;
        }
        int ReadByte() { int v = stream.ReadByte(); if (v < 0) throw new EndOfStreamException(); return v; }

        static void WStr(Stream s, string v) { WBin(s, Encoding.UTF8.GetBytes(v)); }
        static void WBin(Stream s, byte[] b) { s.WriteByte((byte)(b.Length >> 8)); s.WriteByte((byte)b.Length); s.Write(b, 0, b.Length); }

        void Send(byte header, byte[] body)
        {
            var ms = new MemoryStream(body.Length + 5); ms.WriteByte(header);
            int x = body.Length;
            do { int e = x % 128; x /= 128; if (x > 0) e |= 128; ms.WriteByte((byte)e); } while (x > 0);
            ms.Write(body, 0, body.Length);
            var pkt = ms.ToArray();
            lock (wlock) { stream.Write(pkt, 0, pkt.Length); stream.Flush(); }
        }

        public bool Publish(string topic, byte[] payload, bool retain)
        {
            if (!Up) return false;
            try
            {
                var ms = new MemoryStream(payload.Length + topic.Length + 4);
                WStr(ms, topic); ms.Write(payload, 0, payload.Length);
                Send((byte)(0x30 | (retain ? 1 : 0)), ms.ToArray());
                return true;
            }
            catch (Exception e) { Log.Write("publish " + Host + ": " + e.Message); Close(); return false; }
        }
    }

    /// <summary>Publishes to every connected relay; receivers de-duplicate. Any single relay being up is enough.</summary>
    sealed class RelayHub
    {
        public readonly List<MqttLink> Links = new List<MqttLink>();
        public static readonly string[] DefaultRelays = { "broker.emqx.io:8883", "broker.hivemq.com:8883", "test.mosquitto.org:8886" };
        public int UpCount { get { int n = 0; foreach (var l in Links) if (l.Up) n++; return n; } }
        public int Publish(string topic, byte[] payload, bool retain)
        {
            int n = 0; foreach (var l in Links) if (l.Publish(topic, payload, retain)) n++; return n;
        }
        int rr;
        /// <summary>Bulk traffic (file chunks): one relay per message, rotating, so no single relay carries the whole load.</summary>
        public bool PublishOne(string topic, byte[] payload)
        {
            int n = Links.Count, start = (rr++ & 0x7fffffff) % Math.Max(n, 1);
            for (int i = 0; i < n; i++) if (Links[(start + i) % n].Publish(topic, payload, false)) return true;
            return false;
        }
        public void Start() { foreach (var l in Links) l.Start(); }
        public void Stop() { foreach (var l in Links) l.Stop(); }
        /// <summary>Self-healing: kick links that look dead (no traffic incl. ping replies for 35s).</summary>
        public void Heal() { foreach (var l in Links) if (l.Up && l.IdleMs > 35000) { Log.Write("healing stale relay " + l.Host); l.Kick(); } }
        public void KickAll() { foreach (var l in Links) l.Kick(); }
    }
}
