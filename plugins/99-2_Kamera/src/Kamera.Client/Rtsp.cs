// QC-Plugin 99-2 "Kamera" – RTSP-Empfang (RFC 2326) mit RTP über dieselbe TCP-Verbindung ("interleaved"),
// Anmeldung Digest oder Basic, Video H.264 (RFC 6184) und H.265 (RFC 7798).
// Ergebnis: je Bild die NAL-Einheiten mit Startcodes, fertig für den Decoder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace QcKamera.Media
{
    delegate void AccessUnitHandler(byte[] data, int length, bool key);

    /// <summary>Setzt aus RTP-Paketen ganze Bilder zusammen.</summary>
    sealed class NalAssembler
    {
        readonly bool _hevc;
        byte[] _au = new byte[256 * 1024];
        int _len, _lastSeq = -1, _skipped;
        uint _ts;
        bool _any, _key, _hasParams, _broken, _fuOpen, _gate = true;

        /// <summary>SPS/PPS (bei H.265 auch VPS) aus der Stream-Beschreibung, falls die Kamera sie nicht im Strom sendet.</summary>
        public byte[][] ParameterSets;

        public NalAssembler(bool hevc) { _hevc = hevc; }

        void Room(int n)
        {
            if (_len + n <= _au.Length) return;
            byte[] b = new byte[Math.Max(_au.Length * 2, _len + n)];
            Buffer.BlockCopy(_au, 0, b, 0, _len);
            _au = b;
        }

        void Raw(byte[] p, int from, int to)
        {
            if (to <= from) return;
            Room(to - from);
            Buffer.BlockCopy(p, from, _au, _len, to - from);
            _len += to - from;
        }

        void StartCode()
        {
            Room(4);
            _au[_len] = 0; _au[_len + 1] = 0; _au[_len + 2] = 0; _au[_len + 3] = 1;
            _len += 4;
        }

        void Note(int type)
        {
            if (_hevc) { if (type >= 16 && type <= 21) _key = true; if (type == 33) _hasParams = true; }
            else { if (type == 5) _key = true; if (type == 7) _hasParams = true; }
        }

        void Nal(byte[] p, int from, int to)
        {
            if (to <= from) return;
            Note(_hevc ? (p[from] >> 1) & 0x3F : p[from] & 0x1F);
            StartCode();
            Raw(p, from, to);
        }

        public void Push(byte[] p, int off, int end, bool marker, uint ts, int seq, AccessUnitHandler h)
        {
            if (_any && ts != _ts && _len > 0) Flush(h);          // Kamera setzt kein Endezeichen: neues Bild am Zeitstempel erkennen
            if (_lastSeq >= 0 && seq != ((_lastSeq + 1) & 0xFFFF)) _broken = true;
            _lastSeq = seq; _ts = ts; _any = true;
            if (end - off >= (_hevc ? 2 : 1))
            {
                if (_hevc) PushHevc(p, off, end); else PushAvc(p, off, end);
            }
            if (marker) Flush(h);
        }

        void PushAvc(byte[] p, int off, int end)
        {
            int type = p[off] & 0x1F;
            if (type >= 1 && type <= 23) Nal(p, off, end);
            else if (type == 24)                                   // STAP-A: mehrere kleine NAL-Einheiten in einem Paket
            {
                int i = off + 1;
                while (i + 2 <= end)
                {
                    int size = (p[i] << 8) | p[i + 1];
                    i += 2;
                    if (size == 0 || i + size > end) break;
                    Nal(p, i, i + size);
                    i += size;
                }
            }
            else if (type == 28 && end - off >= 2)                 // FU-A: eine große NAL-Einheit in Stücken
            {
                int fh = p[off + 1];
                if ((fh & 0x80) != 0)
                {
                    StartCode();
                    Room(1);
                    _au[_len++] = (byte)((p[off] & 0xE0) | (fh & 0x1F));
                    Note(fh & 0x1F);
                    _fuOpen = true;
                }
                if (!_fuOpen) { _broken = true; return; }
                Raw(p, off + 2, end);
                if ((fh & 0x40) != 0) _fuOpen = false;
            }
        }

        void PushHevc(byte[] p, int off, int end)
        {
            int type = (p[off] >> 1) & 0x3F;
            if (type < 48) Nal(p, off, end);
            else if (type == 48)                                   // AP
            {
                int i = off + 2;
                while (i + 2 <= end)
                {
                    int size = (p[i] << 8) | p[i + 1];
                    i += 2;
                    if (size == 0 || i + size > end) break;
                    Nal(p, i, i + size);
                    i += size;
                }
            }
            else if (type == 49 && end - off >= 3)                 // FU
            {
                int fh = p[off + 2], t = fh & 0x3F;
                if ((fh & 0x80) != 0)
                {
                    StartCode();
                    Room(2);
                    _au[_len++] = (byte)((p[off] & 0x81) | (t << 1));
                    _au[_len++] = p[off + 1];
                    Note(t);
                    _fuOpen = true;
                }
                if (!_fuOpen) { _broken = true; return; }
                Raw(p, off + 3, end);
                if ((fh & 0x40) != 0) _fuOpen = false;
            }
        }

        void Flush(AccessUnitHandler h)
        {
            int len = _len;
            bool key = _key, hasParams = _hasParams, broken = _broken || _fuOpen;
            _len = 0; _key = false; _hasParams = false; _broken = false; _fuOpen = false;
            if (len == 0) return;
            if (broken) { _gate = true; _skipped = 0; return; }   // unvollständig: bis zum nächsten Schlüsselbild warten
            if (_gate)
            {
                if (!key && ++_skipped < 250) return;             // erst mit einem Schlüsselbild beginnen
                _gate = false;
                if (!hasParams && ParameterSets != null)
                {
                    MemoryStream m = new MemoryStream();
                    foreach (byte[] ps in ParameterSets)
                    {
                        m.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);
                        m.Write(ps, 0, ps.Length);
                    }
                    m.Write(_au, 0, len);
                    byte[] all = m.ToArray();
                    h(all, all.Length, key);
                    return;
                }
            }
            h(_au, len, key);
        }
    }

    sealed class RtspClient : IDisposable
    {
        sealed class Response
        {
            public int Status;
            public string Reason = "";
            public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Challenges = new List<string>();
            public string Body = "";
        }

        readonly string _url, _host, _user, _pass;
        readonly int _port;
        TcpClient _tcp;
        Stream _s;
        readonly byte[] _buf = new byte[64 * 1024];
        int _pos, _end, _cseq, _nc;
        string _session, _realm, _nonce, _qop, _opaque, _keepAliveMethod = "OPTIONS";
        bool _basic;
        volatile bool _closed;
        int _rtpChannel;
        NalAssembler _asm;

        public int TimeoutMs = 8000;
        /// <summary>Abstand der Lebenszeichen an die Kamera; wird aus der Sitzungsdauer der Kamera berechnet.</summary>
        public int KeepAliveMs = 25000;
        /// <summary>"H264" oder "H265" (nach Open).</summary>
        public string Codec = "";
        public bool IsHevc { get { return Codec == "H265"; } }

        public RtspClient(string url, string user, string password)
        {
            Uri u = new Uri(url);
            if (!string.Equals(u.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Keine rtsp://-Adresse: " + u.Scheme);
            _host = u.Host;
            _port = u.Port > 0 ? u.Port : 554;
            _url = "rtsp://" + (u.HostNameType == UriHostNameType.IPv6 ? "[" + _host + "]" : _host) + (u.Port > 0 && u.Port != 554 ? ":" + u.Port : "") + u.PathAndQuery;
            _user = user ?? ""; _pass = password ?? "";
            if (_user.Length == 0 && u.UserInfo.Length > 0)
            {
                string[] up = u.UserInfo.Split(new char[] { ':' }, 2);
                _user = Uri.UnescapeDataString(up[0]);
                _pass = up.Length > 1 ? Uri.UnescapeDataString(up[1]) : "";
            }
        }

        // ---- Lesen ----

        void Fill()
        {
            _pos = 0;
            _end = _s.Read(_buf, 0, _buf.Length);
            if (_end <= 0) { _end = 0; throw new IOException("Die Kamera hat die Verbindung beendet"); }
        }

        int ReadByte()
        {
            if (_pos >= _end) Fill();
            return _buf[_pos++];
        }

        void ReadExact(byte[] dst, int n)
        {
            int got = 0;
            while (got < n)
            {
                if (_pos >= _end) Fill();
                int c = Math.Min(n - got, _end - _pos);
                Buffer.BlockCopy(_buf, _pos, dst, got, c);
                _pos += c; got += c;
            }
        }

        string ReadLine(int first)
        {
            StringBuilder b = new StringBuilder();
            int c = first >= 0 ? first : ReadByte();
            while (c != '\n')
            {
                if (c != '\r') b.Append((char)c);
                if (b.Length > 8192) throw new IOException("Antwort der Kamera nicht lesbar");
                c = ReadByte();
            }
            return b.ToString();
        }

        /// <summary>Liest eine Textnachricht (Antwort oder Anfrage der Kamera) ab dem schon gelesenen ersten Zeichen.</summary>
        Response ReadMessage(int first)
        {
            Response r = new Response();
            string line = ReadLine(first);
            while (line.Length == 0) line = ReadLine(-1);
            string[] st = line.Split(new char[] { ' ' }, 3);
            if (st[0].StartsWith("RTSP/") && st.Length > 1) int.TryParse(st[1], out r.Status);
            if (st.Length > 2) r.Reason = st[2];
            while ((line = ReadLine(-1)).Length > 0)
            {
                int i = line.IndexOf(':');
                if (i < 1) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                if (string.Equals(k, "WWW-Authenticate", StringComparison.OrdinalIgnoreCase)) r.Challenges.Add(v);
                else r.Headers[k] = v;
            }
            string cl;
            int n;
            if (r.Headers.TryGetValue("Content-Length", out cl) && int.TryParse(cl, out n) && n > 0 && n < 1024 * 1024)
            {
                byte[] body = new byte[n];
                ReadExact(body, n);
                r.Body = Encoding.UTF8.GetString(body);
            }
            return r;
        }

        void SkipPacket()
        {
            ReadByte();
            int len = (ReadByte() << 8) | ReadByte();
            ReadExact(_packet, len);
        }

        readonly byte[] _packet = new byte[65536];

        // ---- Anfragen ----

        static string Md5(string s)
        {
            using (MD5 m = MD5.Create())
            {
                StringBuilder b = new StringBuilder();
                foreach (byte x in m.ComputeHash(Encoding.UTF8.GetBytes(s))) b.Append(x.ToString("x2"));
                return b.ToString();
            }
        }

        string Authorization(string method, string url)
        {
            if (_nonce != null)
            {
                string ha1 = Md5(_user + ":" + _realm + ":" + _pass), ha2 = Md5(method + ":" + url);
                string h = "Digest username=\"" + _user + "\", realm=\"" + _realm + "\", nonce=\"" + _nonce + "\", uri=\"" + url + "\"";
                if (_qop != null)
                {
                    string nc = (++_nc).ToString("x8"), cnonce = Guid.NewGuid().ToString("N").Substring(0, 16);
                    h += ", qop=auth, nc=" + nc + ", cnonce=\"" + cnonce + "\", response=\"" + Md5(ha1 + ":" + _nonce + ":" + nc + ":" + cnonce + ":auth:" + ha2) + "\"";
                }
                else h += ", response=\"" + Md5(ha1 + ":" + _nonce + ":" + ha2) + "\"";
                if (_opaque != null) h += ", opaque=\"" + _opaque + "\"";
                return h;
            }
            if (_basic) return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(_user + ":" + _pass));
            return null;
        }

        static string Param(string challenge, string name)
        {
            Match m = Regex.Match(challenge, "(?:^|[\\s,])" + name + "\\s*=\\s*(?:\"([^\"]*)\"|([^\\s,]+))", RegexOptions.IgnoreCase);
            return !m.Success ? null : m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        bool TakeChallenge(Response r)
        {
            if (_user.Length == 0) return false;
            foreach (string c in r.Challenges)
            {
                if (!c.StartsWith("Digest", StringComparison.OrdinalIgnoreCase)) continue;
                _realm = Param(c, "realm") ?? "";
                _nonce = Param(c, "nonce") ?? "";
                _opaque = Param(c, "opaque");
                string qop = Param(c, "qop");
                _qop = qop != null && qop.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ? "auth" : null;
                _nc = 0; _basic = false;
                return true;
            }
            foreach (string c in r.Challenges)
                if (c.StartsWith("Basic", StringComparison.OrdinalIgnoreCase)) { _basic = true; _nonce = null; return true; }
            return false;
        }

        void Send(string method, string url, string extra)
        {
            StringBuilder b = new StringBuilder();
            b.Append(method + " " + url + " RTSP/1.0\r\nCSeq: " + (++_cseq) + "\r\nUser-Agent: QC-Plugin 99-2\r\n");
            string auth = Authorization(method, url);
            if (auth != null) b.Append("Authorization: " + auth + "\r\n");
            if (_session != null) b.Append("Session: " + _session + "\r\n");
            if (extra != null) b.Append(extra);
            b.Append("\r\n");
            byte[] d = Encoding.UTF8.GetBytes(b.ToString());
            _s.Write(d, 0, d.Length);
            _s.Flush();
        }

        Response Request(string method, string url, string extra)
        {
            for (int attempt = 0; ; attempt++)
            {
                Send(method, url, extra);
                Response r;
                for (;;)
                {
                    int c = ReadByte();
                    if (c == '$') { SkipPacket(); continue; }
                    r = ReadMessage(c);
                    if (r.Status != 0) break;                 // Anfragen der Kamera überspringen
                }
                if (r.Status == 401 && attempt == 0 && TakeChallenge(r)) continue;
                if (r.Status == 401) throw new IOException(_user.Length == 0 ? "Die Kamera verlangt eine Anmeldung" : "Anmeldung abgelehnt (Benutzer oder Kennwort falsch)");
                if (r.Status < 200 || r.Status > 299) throw new IOException(method + ": " + r.Status + " " + r.Reason);
                return r;
            }
        }

        // ---- Verbindungsaufbau ----

        public void Open()
        {
            _tcp = new TcpClient();
            IAsyncResult ar = _tcp.BeginConnect(_host, _port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TimeoutMs)) { _tcp.Close(); throw new IOException("Kamera nicht erreichbar (" + _host + ":" + _port + ")"); }
            try { _tcp.EndConnect(ar); }
            catch (SocketException) { _tcp.Close(); throw new IOException("Kamera nicht erreichbar (" + _host + ":" + _port + ")"); }
            _tcp.NoDelay = true;
            _tcp.ReceiveTimeout = TimeoutMs;
            _tcp.SendTimeout = TimeoutMs;
            _s = _tcp.GetStream();

            Response o = Request("OPTIONS", _url, null);
            string pub;
            if (o.Headers.TryGetValue("Public", out pub) && pub.IndexOf("GET_PARAMETER", StringComparison.OrdinalIgnoreCase) >= 0) _keepAliveMethod = "GET_PARAMETER";

            Response d = Request("DESCRIBE", _url, "Accept: application/sdp\r\n");
            string baseUrl;
            if (!d.Headers.TryGetValue("Content-Base", out baseUrl) && !d.Headers.TryGetValue("Content-Location", out baseUrl)) baseUrl = _url;
            string control;
            byte[][] sets;
            int pt;
            ParseSdp(d.Body, out control, out pt, out sets);
            if (Codec.Length == 0) throw new IOException("Der Stream enthält kein H.264- oder H.265-Video (in der Kamera den Codec umstellen)");
            _asm = new NalAssembler(IsHevc);
            _asm.ParameterSets = sets;
            _payloadType = pt;

            string track = control == null || control == "*" ? baseUrl
                         : control.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ? control
                         : baseUrl.TrimEnd('/') + "/" + control;
            Response s = Request("SETUP", track, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n");
            string sess, tr;
            if (!s.Headers.TryGetValue("Session", out sess)) throw new IOException("SETUP: Die Kamera nennt keine Sitzung");
            string[] sp = sess.Split(';');
            _session = sp[0].Trim();
            for (int i = 1; i < sp.Length; i++)
            {
                string t = sp[i].Trim();
                int sec;
                if (t.StartsWith("timeout=", StringComparison.OrdinalIgnoreCase) && int.TryParse(t.Substring(8), out sec) && sec >= 4)
                    KeepAliveMs = Math.Min(KeepAliveMs, sec * 1000 / 2);
            }
            _rtpChannel = 0;
            if (s.Headers.TryGetValue("Transport", out tr))
            {
                Match m = Regex.Match(tr, "interleaved=(\\d+)", RegexOptions.IgnoreCase);
                if (m.Success) _rtpChannel = int.Parse(m.Groups[1].Value);
                else if (tr.IndexOf("TCP", StringComparison.OrdinalIgnoreCase) < 0) throw new IOException("Die Kamera liefert RTP nicht über TCP");
            }
            Request("PLAY", baseUrl, "Range: npt=0.000-\r\n");
        }

        int _payloadType = -1;

        void ParseSdp(string sdp, out string control, out int payloadType, out byte[][] sets)
        {
            control = null; payloadType = -1; sets = null;
            bool inVideo = false, done = false;
            List<int> pts = new List<int>();
            Dictionary<int, string> fmtp = new Dictionary<int, string>();
            string ctl = null;
            foreach (string raw in sdp.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("m="))
                {
                    if (inVideo && Codec.Length > 0) done = true;
                    inVideo = !done && l.StartsWith("m=video");
                    if (inVideo)
                    {
                        pts.Clear(); ctl = null;
                        string[] f = l.Split(' ');
                        int p;
                        for (int i = 3; i < f.Length; i++) if (int.TryParse(f[i], out p)) pts.Add(p);
                    }
                    continue;
                }
                if (!inVideo) continue;
                if (l.StartsWith("a=control:")) { ctl = l.Substring(10).Trim(); if (Codec.Length > 0) control = ctl; }
                else if (l.StartsWith("a=rtpmap:"))
                {
                    Match m = Regex.Match(l, "^a=rtpmap:(\\d+)\\s+([A-Za-z0-9.]+)/");
                    if (!m.Success || Codec.Length > 0) continue;
                    string enc = m.Groups[2].Value.ToUpperInvariant();
                    if (enc == "H264" || enc == "H265" || enc == "HEVC")
                    {
                        Codec = enc == "H264" ? "H264" : "H265";
                        payloadType = int.Parse(m.Groups[1].Value);
                        if (ctl != null) control = ctl;
                    }
                }
                else if (l.StartsWith("a=fmtp:"))
                {
                    Match m = Regex.Match(l, "^a=fmtp:(\\d+)\\s+(.*)$");
                    if (m.Success) fmtp[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                }
            }
            string fm;
            if (payloadType < 0 || !fmtp.TryGetValue(payloadType, out fm)) return;
            List<byte[]> list = new List<byte[]>();
            string[] names = IsHevc ? new string[] { "sprop-vps", "sprop-sps", "sprop-pps" } : new string[] { "sprop-parameter-sets" };
            foreach (string name in names)
            {
                Match m = Regex.Match(fm, "(?:^|;)\\s*" + name + "\\s*=\\s*([^;\\s]+)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                foreach (string part in m.Groups[1].Value.Split(','))
                {
                    try { byte[] b = Convert.FromBase64String(part.Trim()); if (b.Length > 0) list.Add(b); } catch { }
                }
            }
            if (list.Count > 0) sets = list.ToArray();
        }

        // ---- Empfang ----

        /// <summary>Empfängt, bis Close() gerufen wird; Verbindungsfehler kommen als Ausnahme.</summary>
        public void Receive(AccessUnitHandler handler)
        {
            Stopwatch alive = Stopwatch.StartNew();
            while (!_closed)
            {
                int c = ReadByte();
                if (c == '$')
                {
                    int ch = ReadByte(), len = (ReadByte() << 8) | ReadByte();
                    ReadExact(_packet, len);
                    if (ch == _rtpChannel) Rtp(_packet, len, handler);
                }
                else ReadMessage(c);                               // Antwort auf ein Lebenszeichen oder Anfrage der Kamera
                if (alive.ElapsedMilliseconds >= KeepAliveMs)
                {
                    alive.Restart();
                    Send(_keepAliveMethod, _url, null);
                }
            }
        }

        void Rtp(byte[] p, int len, AccessUnitHandler handler)
        {
            if (len < 12 || (p[0] >> 6) != 2) return;
            if ((p[1] & 0x7F) != _payloadType && _payloadType >= 0) return;
            int off = 12 + (p[0] & 0x0F) * 4, end = len;
            if ((p[0] & 0x10) != 0)
            {
                if (off + 4 > len) return;
                off += 4 + ((p[off + 2] << 8) | p[off + 3]) * 4;
            }
            if ((p[0] & 0x20) != 0 && len > 0) end -= p[len - 1];
            if (off >= end) return;
            bool marker = (p[1] & 0x80) != 0;
            int seq = (p[2] << 8) | p[3];
            uint ts = ((uint)p[4] << 24) | ((uint)p[5] << 16) | ((uint)p[6] << 8) | p[7];
            _asm.Push(p, off, end, marker, ts, seq, handler);
        }

        public void Close()
        {
            _closed = true;
            try { if (_s != null && _session != null) Send("TEARDOWN", _url, null); } catch { }
            try { if (_tcp != null) _tcp.Close(); } catch { }
        }

        public void Dispose() { Close(); }
    }
}
