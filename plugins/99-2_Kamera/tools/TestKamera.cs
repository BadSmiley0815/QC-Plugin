// Nachgebaute Kamera für die Testprogramme: RTSP-Server auf 127.0.0.1 mit Digest-Anmeldung, der einen
// vorbereiteten H.264-Strom als RTP über die RTSP-Verbindung schickt (wie Axis, Hikvision und Dahua bei "RTP über TCP").
#pragma warning disable 0649   // nicht jedes Testprogramm setzt jede Einstellung
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

sealed class TestRtspServer : IDisposable
{
    public const string User = "viewer", Password = "geheim", Realm = "QC Test", Nonce = "5f2a9c0d17";

    readonly TcpListener _listener;
    readonly List<byte[]> _aus;
    volatile bool _stop;

    /// <summary>SPS/PPS nur in der Stream-Beschreibung nennen, nicht im Strom senden.</summary>
    public bool SpropOnly;
    /// <summary>Klein gewählt, damit auch kleine Testbilder in Stücken (FU-A) übertragen werden.</summary>
    public int Mtu = 200;
    public int PaceMs = 2;
    /// <summary>Strom endlos wiederholen (für die Vorschau).</summary>
    public bool Loop;
    public int KeepAlives, Requests;
    public string LastError = "";

    public int Port { get { return ((IPEndPoint)_listener.LocalEndpoint).Port; } }
    public string Url { get { return "rtsp://127.0.0.1:" + Port + "/stream"; } }

    public TestRtspServer(List<byte[]> accessUnits)
    {
        _aus = accessUnits;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Thread t = new Thread(AcceptLoop);
        t.IsBackground = true;
        t.Start();
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener.Stop(); } catch { }
    }

    void AcceptLoop()
    {
        while (!_stop)
        {
            TcpClient c;
            try { c = _listener.AcceptTcpClient(); } catch { return; }
            // Fehler beim Lesen, nachdem der Strom zu Ende ist, kommen nur vom eigenen Schliessen der Verbindung.
            Thread t = new Thread(delegate() { try { Serve(c); } catch (Exception ex) { if (!_sent) LastError = ex.Message; } finally { c.Close(); } });
            t.IsBackground = true;
            t.Start();
        }
    }

    public static List<byte[]> SplitNals(byte[] au)
    {
        List<byte[]> nals = new List<byte[]>();
        int start = -1;
        for (int i = 0; i + 2 < au.Length; i++)
        {
            if (au[i] != 0 || au[i + 1] != 0 || au[i + 2] != 1) continue;
            if (start >= 0)
            {
                int end = i > 0 && au[i - 1] == 0 ? i - 1 : i;
                nals.Add(Slice(au, start, end));
            }
            start = i + 3;
            i += 2;
        }
        if (start >= 0) nals.Add(Slice(au, start, au.Length));
        return nals;
    }

    static byte[] Slice(byte[] a, int from, int to)
    {
        byte[] r = new byte[to - from];
        Buffer.BlockCopy(a, from, r, 0, r.Length);
        return r;
    }

    static string Md5(string s)
    {
        using (MD5 m = MD5.Create())
        {
            StringBuilder b = new StringBuilder();
            foreach (byte x in m.ComputeHash(Encoding.UTF8.GetBytes(s))) b.Append(x.ToString("x2"));
            return b.ToString();
        }
    }

    static bool Authorized(string method, string url, string header)
    {
        if (header == null || !header.StartsWith("Digest ")) return false;
        Match user = Regex.Match(header, "username=\"([^\"]*)\""), uri = Regex.Match(header, "uri=\"([^\"]*)\""), resp = Regex.Match(header, "response=\"([^\"]*)\"");
        if (!user.Success || !uri.Success || !resp.Success || user.Groups[1].Value != User || uri.Groups[1].Value != url) return false;
        string expected = Md5(Md5(User + ":" + Realm + ":" + Password) + ":" + Nonce + ":" + Md5(method + ":" + url));
        return resp.Groups[1].Value == expected;
    }

    void Serve(TcpClient c)
    {
        NetworkStream s = c.GetStream();
        StreamReader r = new StreamReader(s, Encoding.ASCII);
        object writeLock = new object();
        Thread sender = null;
        string baseUrl = Url + "/";
        List<byte[]> first = SplitNals(_aus[0]);
        byte[] sps = first.Find(delegate(byte[] n) { return (n[0] & 0x1F) == 7; }), pps = first.Find(delegate(byte[] n) { return (n[0] & 0x1F) == 8; });

        for (;;)
        {
            string line = r.ReadLine();
            if (line == null) return;
            if (line.Length == 0) continue;
            string[] rq = line.Split(' ');
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while ((line = r.ReadLine()) != null && line.Length > 0)
            {
                int i = line.IndexOf(':');
                if (i > 0) h[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
            }
            string method = rq[0], url = rq.Length > 1 ? rq[1] : "", cseq, auth;
            h.TryGetValue("CSeq", out cseq);
            h.TryGetValue("Authorization", out auth);
            Interlocked.Increment(ref Requests);
            string head = "RTSP/1.0 200 OK\r\nCSeq: " + cseq + "\r\n", body = "";

            if (method == "OPTIONS" && auth == null && sender == null) head += "Public: OPTIONS, DESCRIBE, SETUP, PLAY, TEARDOWN, GET_PARAMETER\r\n";
            else if (!Authorized(method, url, auth))
                head = "RTSP/1.0 401 Unauthorized\r\nCSeq: " + cseq + "\r\nWWW-Authenticate: Digest realm=\"" + Realm + "\", nonce=\"" + Nonce + "\"\r\nWWW-Authenticate: Basic realm=\"" + Realm + "\"\r\n";
            else if (method == "DESCRIBE")
            {
                body = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=QC Test\r\nt=0 0\r\na=control:*\r\n"
                     + "m=audio 0 RTP/AVP 8\r\na=control:trackID=2\r\n"
                     + "m=video 0 RTP/AVP 96\r\na=control:trackID=1\r\na=rtpmap:96 H264/90000\r\n"
                     + "a=fmtp:96 packetization-mode=1; profile-level-id=42001F; sprop-parameter-sets=" + Convert.ToBase64String(sps) + "," + Convert.ToBase64String(pps) + "\r\n";
                head += "Content-Base: " + baseUrl + "\r\nContent-Type: application/sdp\r\nContent-Length: " + body.Length + "\r\n";
            }
            else if (method == "SETUP")
            {
                string tr;
                h.TryGetValue("Transport", out tr);
                if (url != baseUrl + "trackID=1" || tr == null || tr.IndexOf("RTP/AVP/TCP") < 0) head = "RTSP/1.0 461 Unsupported Transport\r\nCSeq: " + cseq + "\r\n";
                else head += "Transport: RTP/AVP/TCP;unicast;interleaved=0-1;ssrc=1234ABCD\r\nSession: 4711;timeout=60\r\n";
            }
            else if (method == "GET_PARAMETER" || method == "OPTIONS") Interlocked.Increment(ref KeepAlives);

            byte[] reply = Encoding.ASCII.GetBytes(head + "\r\n" + body);
            lock (writeLock) s.Write(reply, 0, reply.Length);
            if (method == "TEARDOWN") return;
            if (method == "PLAY" && head.StartsWith("RTSP/1.0 200") && sender == null)
            {
                sender = new Thread(delegate() { try { Stream(s, writeLock); } catch (Exception ex) { LastError = ex.Message; } finally { c.Close(); } });
                sender.IsBackground = true;
                sender.Start();
            }
        }
    }

    int _seq = 65530;                // läuft im Test über 65535 hinweg

    void Packet(NetworkStream s, object writeLock, byte[] payload, bool marker, uint ts)
    {
        bool ext = _seq % 7 == 0, pad = _seq % 5 == 0;
        int head = 12 + (ext ? 8 : 0), len = head + payload.Length + (pad ? 4 : 0);
        byte[] p = new byte[4 + len];
        p[0] = (byte)'$'; p[1] = 0; p[2] = (byte)(len >> 8); p[3] = (byte)len;
        p[4] = (byte)(0x80 | (ext ? 0x10 : 0) | (pad ? 0x20 : 0));
        p[5] = (byte)(96 | (marker ? 0x80 : 0));
        p[6] = (byte)(_seq >> 8); p[7] = (byte)_seq;
        p[8] = (byte)(ts >> 24); p[9] = (byte)(ts >> 16); p[10] = (byte)(ts >> 8); p[11] = (byte)ts;
        p[12] = 0x12; p[13] = 0x34; p[14] = 0xAB; p[15] = 0xCD;
        if (ext) { p[16] = 0xAB; p[17] = 0xAC; p[18] = 0; p[19] = 1; }       // Erweiterung mit einem 32-Bit-Wort (wie ONVIF-Wiedergabe)
        Buffer.BlockCopy(payload, 0, p, 4 + head, payload.Length);
        if (pad) p[p.Length - 1] = 4;
        _seq = (_seq + 1) & 0xFFFF;
        lock (writeLock) s.Write(p, 0, p.Length);
    }

    void Stream(NetworkStream s, object writeLock)
    {
        uint ts = 90000;
        for (int n = 0; n < _aus.Count && !_stop; n++)
        {
            List<byte[]> nals = SplitNals(_aus[n]);
            if (SpropOnly) nals.RemoveAll(delegate(byte[] x) { int t = x[0] & 0x1F; return t == 7 || t == 8; });
            else
            {
                // SPS und PPS zusammen in einem Paket (STAP-A), wie es Kameras tun
                int i = nals.FindIndex(delegate(byte[] x) { return (x[0] & 0x1F) == 7; });
                if (i >= 0 && i + 1 < nals.Count && (nals[i + 1][0] & 0x1F) == 8)
                {
                    byte[] a = nals[i], b = nals[i + 1], stap = new byte[1 + 2 + a.Length + 2 + b.Length];
                    stap[0] = 24;
                    stap[1] = (byte)(a.Length >> 8); stap[2] = (byte)a.Length;
                    Buffer.BlockCopy(a, 0, stap, 3, a.Length);
                    stap[3 + a.Length] = (byte)(b.Length >> 8); stap[4 + a.Length] = (byte)b.Length;
                    Buffer.BlockCopy(b, 0, stap, 5 + a.Length, b.Length);
                    nals[i] = stap;
                    nals.RemoveAt(i + 1);
                    Packet(s, writeLock, nals[i], false, ts);
                    nals.RemoveAt(i);
                }
            }
            for (int k = 0; k < nals.Count; k++)
            {
                byte[] nal = nals[k];
                bool last = k == nals.Count - 1;
                if (nal.Length <= Mtu) { Packet(s, writeLock, nal, last, ts); continue; }
                for (int off = 1; off < nal.Length; off += Mtu)
                {
                    int size = Math.Min(Mtu, nal.Length - off);
                    byte[] fu = new byte[2 + size];
                    fu[0] = (byte)((nal[0] & 0xE0) | 28);
                    fu[1] = (byte)((nal[0] & 0x1F) | (off == 1 ? 0x80 : 0) | (off + size >= nal.Length ? 0x40 : 0));
                    Buffer.BlockCopy(nal, off, fu, 2, size);
                    Packet(s, writeLock, fu, last && off + size >= nal.Length, ts);
                }
            }
            if (n % 10 == 5)
            {
                byte[] rtcp = { (byte)'$', 1, 0, 8, 0x80, 200, 0, 1, 0x12, 0x34, 0xAB, 0xCD };     // RTCP auf Kanal 1: muss überlesen werden
                lock (writeLock) s.Write(rtcp, 0, rtcp.Length);
            }
            ts += 3600;
            Thread.Sleep(PaceMs);
            if (Loop && n == _aus.Count - 1) n = -1;        // das erste Bild ist ein Schluesselbild
        }
        for (int i = 0; i < 100 && KeepAlives == 0 && !_stop; i++) Thread.Sleep(10);
        _sent = true;
    }

    volatile bool _sent;
}

/// <summary>HTTP-Seite der nachgebauten Kamera: ONVIF (Profile, Stream- und Einzelbild-Adresse), Einzelbild und MJPEG.</summary>
sealed class TestHttpServer : IDisposable
{
    readonly TcpListener _listener;
    readonly List<byte[]> _jpegs;
    readonly string _rtspUrl;
    volatile bool _stop;
    public int OnvifCalls, Snapshots;
    /// <summary>Uhr der Kamera geht falsch: der Kennwort-Digest muss mit der Zeit der Kamera gebildet werden.</summary>
    public TimeSpan ClockOffset = TimeSpan.FromHours(-7);

    public int Port { get { return ((IPEndPoint)_listener.LocalEndpoint).Port; } }
    public string Base { get { return "http://127.0.0.1:" + Port; } }

    public TestHttpServer(List<byte[]> jpegs, string rtspUrl)
    {
        _jpegs = jpegs; _rtspUrl = rtspUrl;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Thread t = new Thread(delegate()
        {
            while (!_stop)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); } catch { return; }
                Thread w = new Thread(delegate() { try { Serve(c); } catch { } finally { c.Close(); } });
                w.IsBackground = true;
                w.Start();
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public void Dispose()
    {
        _stop = true;
        try { _listener.Stop(); } catch { }
    }

    static string ReadLine(Stream s)
    {
        StringBuilder b = new StringBuilder();
        for (;;)
        {
            int c = s.ReadByte();
            if (c < 0) return b.Length > 0 ? b.ToString() : null;
            if (c == '\n') return b.ToString();
            if (c != '\r') b.Append((char)c);
        }
    }

    static void Reply(Stream s, string status, string type, byte[] body, string extra)
    {
        byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n" + extra + "\r\n");
        s.Write(head, 0, head.Length);
        s.Write(body, 0, body.Length);
    }

    static string Tag(string xml, string name)
    {
        Match m = Regex.Match(xml, "<(?:\\w+:)?" + name + "[^>]*>([^<]*)</");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    bool TokenOk(string xml)
    {
        string user = Tag(xml, "Username"), digest = Tag(xml, "Password"), nonce = Tag(xml, "Nonce"), created = Tag(xml, "Created");
        if (user != TestRtspServer.User || nonce.Length == 0) return false;
        DateTime t;
        if (!DateTime.TryParse(created, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) return false;
        if (Math.Abs((t - (DateTime.UtcNow + ClockOffset)).TotalSeconds) > 10) return false;        // wie Hikvision: Zeit muss zur Kamera passen
        byte[] n = Convert.FromBase64String(nonce), c = Encoding.UTF8.GetBytes(created), p = Encoding.UTF8.GetBytes(TestRtspServer.Password), all = new byte[n.Length + c.Length + p.Length];
        Buffer.BlockCopy(n, 0, all, 0, n.Length);
        Buffer.BlockCopy(c, 0, all, n.Length, c.Length);
        Buffer.BlockCopy(p, 0, all, n.Length + c.Length, p.Length);
        using (SHA1 sha = SHA1.Create()) return Convert.ToBase64String(sha.ComputeHash(all)) == digest;
    }

    string Onvif(string xml, out string status)
    {
        status = "200 OK";
        const string open = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><SOAP-ENV:Body>";
        const string close = "</SOAP-ENV:Body></SOAP-ENV:Envelope>";
        Interlocked.Increment(ref OnvifCalls);
        if (xml.Contains("GetSystemDateAndTime"))
        {
            DateTime t = DateTime.UtcNow + ClockOffset;
            return open + "<tds:GetSystemDateAndTimeResponse><tds:SystemDateAndTime><tt:DateTimeType>NTP</tt:DateTimeType><tt:UTCDateTime><tt:Time><tt:Hour>" + t.Hour + "</tt:Hour><tt:Minute>" + t.Minute
                 + "</tt:Minute><tt:Second>" + t.Second + "</tt:Second></tt:Time><tt:Date><tt:Year>" + t.Year + "</tt:Year><tt:Month>" + t.Month + "</tt:Month><tt:Day>" + t.Day
                 + "</tt:Day></tt:Date></tt:UTCDateTime></tds:SystemDateAndTime></tds:GetSystemDateAndTimeResponse>" + close;
        }
        if (!TokenOk(xml))
        {
            status = "400 Bad Request";
            return open + "<SOAP-ENV:Fault><SOAP-ENV:Code><SOAP-ENV:Value>SOAP-ENV:Sender</SOAP-ENV:Value><SOAP-ENV:Subcode><SOAP-ENV:Value>ter:NotAuthorized</SOAP-ENV:Value></SOAP-ENV:Subcode></SOAP-ENV:Code>"
                 + "<SOAP-ENV:Reason><SOAP-ENV:Text xml:lang=\"en\">Sender not Authorized</SOAP-ENV:Text></SOAP-ENV:Reason></SOAP-ENV:Fault>" + close;
        }
        if (xml.Contains("GetCapabilities"))
            return open + "<tds:GetCapabilitiesResponse><tds:Capabilities><tt:Device><tt:XAddr>" + Base + "/onvif/device_service</tt:XAddr></tt:Device><tt:Media><tt:XAddr>" + Base
                 + "/onvif/media</tt:XAddr></tt:Media></tds:Capabilities></tds:GetCapabilitiesResponse>" + close;
        if (xml.Contains("GetProfiles"))
        {
            string[][] p = { new string[] { "p1", "mainStream", "H264", "1920", "1080" }, new string[] { "p2", "subStream", "H264", "640", "360" }, new string[] { "p3", "thirdStream", "H265", "2560", "1440" } };
            StringBuilder b = new StringBuilder(open + "<trt:GetProfilesResponse>");
            foreach (string[] x in p)
                b.Append("<trt:Profiles token=\"" + x[0] + "\" fixed=\"true\"><tt:Name>" + x[1] + "</tt:Name><tt:VideoSourceConfiguration token=\"vs\"><tt:Name>vs</tt:Name></tt:VideoSourceConfiguration>"
                       + "<tt:VideoEncoderConfiguration token=\"ve" + x[0] + "\"><tt:Name>enc</tt:Name><tt:Encoding>" + x[2] + "</tt:Encoding><tt:Resolution><tt:Width>" + x[3] + "</tt:Width><tt:Height>" + x[4]
                       + "</tt:Height></tt:Resolution></tt:VideoEncoderConfiguration></trt:Profiles>");
            return b + "</trt:GetProfilesResponse>" + close;
        }
        string token = Tag(xml, "ProfileToken");
        if (xml.Contains("GetStreamUri"))
            return open + "<trt:GetStreamUriResponse><trt:MediaUri><tt:Uri>" + _rtspUrl + (token == "p2" ? "" : "?profile=" + token) + "</tt:Uri><tt:InvalidAfterConnect>false</tt:InvalidAfterConnect></trt:MediaUri></trt:GetStreamUriResponse>" + close;
        if (xml.Contains("GetSnapshotUri"))
            return open + "<trt:GetSnapshotUriResponse><trt:MediaUri><tt:Uri>" + Base + "/snap.jpg</tt:Uri></trt:MediaUri></trt:GetSnapshotUriResponse>" + close;
        status = "400 Bad Request";
        return open + "<SOAP-ENV:Fault><SOAP-ENV:Reason><SOAP-ENV:Text>Unknown action</SOAP-ENV:Text></SOAP-ENV:Reason></SOAP-ENV:Fault>" + close;
    }

    void Serve(TcpClient c)
    {
        NetworkStream s = c.GetStream();
        string line = ReadLine(s);
        if (line == null) return;
        string[] rq = line.Split(' ');
        int length = 0;
        string auth = null;
        while ((line = ReadLine(s)) != null && line.Length > 0)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) auth = line.Substring(14).Trim();
        }
        byte[] body = new byte[length];
        for (int n = 0; n < length; )
        {
            int k = s.Read(body, n, length - n);
            if (k <= 0) return;
            n += k;
        }
        string path = rq.Length > 1 ? rq[1] : "";
        if (rq[0] == "POST" && path.StartsWith("/onvif/"))
        {
            string status, xml = Onvif(Encoding.UTF8.GetString(body), out status);
            Reply(s, status, "application/soap+xml; charset=utf-8", Encoding.UTF8.GetBytes(xml), "");
            return;
        }
        string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(TestRtspServer.User + ":" + TestRtspServer.Password));
        if (auth != expected)
        {
            Reply(s, "401 Unauthorized", "text/plain", new byte[0], "WWW-Authenticate: Basic realm=\"" + TestRtspServer.Realm + "\"\r\n");
            return;
        }
        if (path.StartsWith("/snap.jpg"))
        {
            int i = Interlocked.Increment(ref Snapshots);
            Reply(s, "200 OK", "image/jpeg", _jpegs[i % _jpegs.Count], "");
        }
        else if (path.StartsWith("/mjpeg"))
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: multipart/x-mixed-replace; boundary=qcframe\r\nConnection: close\r\n\r\n");
            s.Write(head, 0, head.Length);
            for (int i = 0; !_stop; i++)
            {
                byte[] j = _jpegs[i % _jpegs.Count];
                // abwechselnd mit und ohne Längenangabe, beides kommt bei Kameras vor
                byte[] part = Encoding.ASCII.GetBytes("--qcframe\r\nContent-Type: image/jpeg\r\n" + (i % 2 == 0 ? "Content-Length: " + j.Length + "\r\n" : "") + "\r\n");
                s.Write(part, 0, part.Length);
                s.Write(j, 0, j.Length);
                s.Write(new byte[] { 13, 10 }, 0, 2);
                Thread.Sleep(40);
            }
        }
        else Reply(s, "404 Not Found", "text/plain", new byte[0], "");
    }
}
