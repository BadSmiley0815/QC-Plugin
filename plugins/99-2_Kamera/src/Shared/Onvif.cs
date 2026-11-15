// QC-Plugin 99-2 "Kamera" – ONVIF-Abfrage (Profile, Stream- und Schnappschuss-Adressen) über SOAP/HTTP.
// Anmeldung: WS-Security UsernameToken mit Kennwort-Digest (Zeit der Kamera) und zusätzlich HTTP-Digest,
// damit Axis, Hikvision und Dahua in ihren üblichen Einstellungen antworten.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace QcKamera
{
    sealed class OnvifProfile
    {
        public string Token = "", Name = "", Encoding = "", StreamUri = "", SnapshotUri = "";
        public int Width, Height;

        public bool IsH264 { get { return Encoding.ToUpperInvariant().Replace(".", "") == "H264"; } }
        public bool IsH265 { get { string e = Encoding.ToUpperInvariant().Replace(".", ""); return e == "H265" || e == "HEVC"; } }

        public override string ToString()
        {
            return (Name.Length > 0 ? Name : Token) + " " + Encoding + " " + Width + "×" + Height;
        }
    }

    sealed class OnvifClient
    {
        const string NsDevice = "http://www.onvif.org/ver10/device/wsdl", NsMedia = "http://www.onvif.org/ver10/media/wsdl", NsSchema = "http://www.onvif.org/ver10/schema";

        readonly string _user, _pass;
        string _deviceUrl, _altUrl = "";                       // _altUrl: dieselbe Kamera über HTTPS, falls HTTP nicht antwortet
        TimeSpan _clockOffset = TimeSpan.Zero;
        public int TimeoutMs = 6000;

        public OnvifClient(string host, int port, string user, string password)
        {
            string h = (host ?? "").Trim();
            if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || h.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                _deviceUrl = h.TrimEnd('/').IndexOf('/', 8) > 0 ? h : h.TrimEnd('/') + "/onvif/device_service";
            else if (port == 443) _deviceUrl = "https://" + h + "/onvif/device_service";
            else
            {
                // Neuere Kameras (z. B. Axis) lassen oft nur noch HTTPS zu: Port 80 ist dann zu oder leitet um.
                _deviceUrl = "http://" + h + (port > 0 && port != 80 ? ":" + port : "") + "/onvif/device_service";
                _altUrl = "https://" + h + (port > 0 && port != 80 ? ":" + port : "") + "/onvif/device_service";
            }
            _user = user ?? ""; _pass = password ?? "";
        }

        string Security()
        {
            if (_user.Length == 0) return "";
            byte[] nonce = new byte[16];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(nonce);
            string created = (DateTime.UtcNow + _clockOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            byte[] c = Encoding.UTF8.GetBytes(created), p = Encoding.UTF8.GetBytes(_pass), all = new byte[nonce.Length + c.Length + p.Length];
            Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
            Buffer.BlockCopy(c, 0, all, nonce.Length, c.Length);
            Buffer.BlockCopy(p, 0, all, nonce.Length + c.Length, p.Length);
            string digest;
            using (SHA1 sha = SHA1.Create()) digest = Convert.ToBase64String(sha.ComputeHash(all));
            return "<s:Header><wsse:Security s:mustUnderstand=\"1\" xmlns:wsse=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd\""
                 + " xmlns:wsu=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd\"><wsse:UsernameToken>"
                 + "<wsse:Username>" + SecurityElementEscape(_user) + "</wsse:Username>"
                 + "<wsse:Password Type=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest\">" + digest + "</wsse:Password>"
                 + "<wsse:Nonce EncodingType=\"http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary\">" + Convert.ToBase64String(nonce) + "</wsse:Nonce>"
                 + "<wsu:Created>" + created + "</wsu:Created></wsse:UsernameToken></wsse:Security></s:Header>";
        }

        static string SecurityElementEscape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        XmlDocument Call(string url, string body, bool auth)
        {
            string env = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\">"
                       + (auth ? Security() : "") + "<s:Body>" + body + "</s:Body></s:Envelope>";
            byte[] data = Encoding.UTF8.GetBytes(env);
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "POST";
            rq.ContentType = "application/soap+xml; charset=utf-8";
            rq.Timeout = TimeoutMs;
            rq.ReadWriteTimeout = TimeoutMs;
            rq.Proxy = null;
            rq.KeepAlive = false;
            rq.ServerCertificateValidationCallback = delegate { return true; };   // Kameras haben selbst ausgestellte Zertifikate
            if (auth && _user.Length > 0) rq.Credentials = new NetworkCredential(_user, _pass);
            string text;
            try
            {
                using (Stream s = rq.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader rd = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) text = rd.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse rs = ex.Response as HttpWebResponse;
                if (rs == null) throw new IOException("Kamera nicht erreichbar (" + new Uri(url).Authority + "): " + ex.Message);
                string fault = "";
                try { using (StreamReader rd = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) fault = rd.ReadToEnd(); } catch { }
                if (rs.StatusCode == HttpStatusCode.Unauthorized || fault.IndexOf("NotAuthorized", StringComparison.OrdinalIgnoreCase) >= 0
                    || fault.IndexOf("FailedAuthentication", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new IOException(_user.Length == 0 ? "Die Kamera verlangt eine Anmeldung" : "Anmeldung abgelehnt (Benutzer oder Kennwort falsch, oder ONVIF-Benutzer in der Kamera nicht angelegt)");
                string reason = "";
                try
                {
                    XmlDocument fd = new XmlDocument();
                    fd.LoadXml(fault);
                    XmlNode t = fd.SelectSingleNode("//*[local-name()='Reason']/*[local-name()='Text']");
                    if (t != null) reason = t.InnerText.Trim();
                }
                catch { }
                throw new IOException("ONVIF: " + (reason.Length > 0 ? reason : (int)rs.StatusCode + " " + rs.StatusDescription));
            }
            XmlDocument d = new XmlDocument();
            try { d.LoadXml(text); }
            catch (XmlException) { throw new IOException("Unter dieser Adresse antwortet kein ONVIF-Dienst (ONVIF in der Kamera einschalten, Port prüfen)"); }
            return d;
        }

        static string Text(XmlNode n, string path)
        {
            XmlNode t = n.SelectSingleNode(path);
            return t == null ? "" : t.InnerText.Trim();
        }

        static string L(params string[] names)
        {
            StringBuilder b = new StringBuilder();
            foreach (string n in names) b.Append((b.Length > 0 ? "/" : ".//") + "*[local-name()='" + n + "']");
            return b.ToString();
        }

        /// <summary>Uhrzeit der Kamera lesen (ohne Anmeldung), weil der Kennwort-Digest sie enthält.</summary>
        void SyncClock()
        {
            try
            {
                XmlDocument d = Call(_deviceUrl, "<GetSystemDateAndTime xmlns=\"" + NsDevice + "\"/>", false);
                XmlNode u = d.SelectSingleNode(L("UTCDateTime"));
                if (u == null) return;
                int y, mo, da, h, mi, s;
                if (int.TryParse(Text(u, L("Date", "Year")), out y) && int.TryParse(Text(u, L("Date", "Month")), out mo) && int.TryParse(Text(u, L("Date", "Day")), out da)
                    && int.TryParse(Text(u, L("Time", "Hour")), out h) && int.TryParse(Text(u, L("Time", "Minute")), out mi) && int.TryParse(Text(u, L("Time", "Second")), out s))
                    _clockOffset = new DateTime(y, mo, da, h, mi, Math.Min(s, 59), DateTimeKind.Utc) - DateTime.UtcNow;
            }
            catch (IOException ex)
            {
                if (ex.Message.StartsWith("Kamera nicht erreichbar") || ex.Message.StartsWith("Unter dieser Adresse")) throw;
            }
        }

        /// <summary>Adresse des ONVIF-Dienstes, über die die Kamera geantwortet hat.</summary>
        public string DeviceUrl { get { return _deviceUrl; } }

        /// <summary>HTTP-Adressen, die die Kamera nennt, auf den Weg umstellen, über den sie wirklich erreichbar war
        /// (Kameras nennen oft http:// oder eine andere Adresse, obwohl nur HTTPS offen ist).</summary>
        string Reachable(string uri)
        {
            Uri u, d = new Uri(_deviceUrl);
            if (!Uri.TryCreate(uri, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")) return uri;
            return d.Scheme + "://" + d.Authority + u.PathAndQuery;
        }

        /// <summary>Liefert alle Videoprofile der Kamera mit ihren Adressen.</summary>
        public List<OnvifProfile> Query()
        {
            try { SyncClock(); }
            catch (IOException ex)
            {
                // HTTP antwortet nicht oder ist kein ONVIF (Umleitung auf HTTPS): einmal über HTTPS versuchen
                if (_altUrl.Length == 0) throw;
                string first = _deviceUrl;
                _deviceUrl = _altUrl; _altUrl = "";
                try { SyncClock(); }
                catch (IOException) { _deviceUrl = first; throw new IOException(ex.Message + " – auch über HTTPS nicht"); }
            }
            string mediaUrl = _deviceUrl;
            XmlDocument caps = Call(_deviceUrl, "<GetCapabilities xmlns=\"" + NsDevice + "\"><Category>All</Category></GetCapabilities>", true);
            string x = Text(caps, L("Capabilities", "Media", "XAddr"));
            if (x.Length > 0) mediaUrl = Reachable(x);

            XmlDocument pd = Call(mediaUrl, "<GetProfiles xmlns=\"" + NsMedia + "\"/>", true);
            List<OnvifProfile> list = new List<OnvifProfile>();
            foreach (XmlNode n in pd.SelectNodes(L("GetProfilesResponse", "Profiles")))
            {
                XmlAttribute tok = n.Attributes["token"];
                XmlNode enc = n.SelectSingleNode(L("VideoEncoderConfiguration"));
                if (tok == null || enc == null) continue;
                OnvifProfile p = new OnvifProfile();
                p.Token = tok.Value;
                p.Name = Text(n, "*[local-name()='Name']");
                p.Encoding = Text(enc, "*[local-name()='Encoding']");
                int.TryParse(Text(enc, "*[local-name()='Resolution']/*[local-name()='Width']"), out p.Width);
                int.TryParse(Text(enc, "*[local-name()='Resolution']/*[local-name()='Height']"), out p.Height);
                list.Add(p);
            }
            foreach (OnvifProfile p in list)
            {
                string tok = SecurityElementEscape(p.Token);
                try
                {
                    XmlDocument sd = Call(mediaUrl, "<GetStreamUri xmlns=\"" + NsMedia + "\"><StreamSetup><Stream xmlns=\"" + NsSchema + "\">RTP-Unicast</Stream>"
                        + "<Transport xmlns=\"" + NsSchema + "\"><Protocol>RTSP</Protocol></Transport></StreamSetup><ProfileToken>" + tok + "</ProfileToken></GetStreamUri>", true);
                    p.StreamUri = Text(sd, L("MediaUri", "Uri"));
                }
                catch (IOException) { }
                try
                {
                    XmlDocument sn = Call(mediaUrl, "<GetSnapshotUri xmlns=\"" + NsMedia + "\"><ProfileToken>" + tok + "</ProfileToken></GetSnapshotUri>", true);
                    p.SnapshotUri = Reachable(Text(sn, L("MediaUri", "Uri")));
                }
                catch (IOException) { }
            }
            return list;
        }

        /// <summary>Profil für den Quadranten: das kleinste H.264-Profil ab 640 Punkten Breite (sonst das kleinste überhaupt).</summary>
        public static OnvifProfile PickSmall(List<OnvifProfile> list)
        {
            OnvifProfile best = null;
            foreach (bool needH264 in new bool[] { true, false })
            {
                foreach (OnvifProfile p in list)
                {
                    if (p.StreamUri.Length == 0 || (needH264 ? !p.IsH264 : !p.IsH265)) continue;
                    if (best == null) { best = p; continue; }
                    bool pOk = p.Width >= 640, bOk = best.Width >= 640;
                    if (pOk != bOk ? pOk : (pOk ? p.Width < best.Width : p.Width > best.Width)) best = p;
                }
                if (best != null) return best;
            }
            return null;
        }

        /// <summary>Profil für das Vollbild: das größte, H.264 bevorzugt.</summary>
        public static OnvifProfile PickLarge(List<OnvifProfile> list)
        {
            OnvifProfile best = null;
            foreach (bool needH264 in new bool[] { true, false })
            {
                foreach (OnvifProfile p in list)
                {
                    if (p.StreamUri.Length == 0 || (needH264 ? !p.IsH264 : !p.IsH265)) continue;
                    if (best == null || p.Width * p.Height > best.Width * best.Height) best = p;
                }
                if (best != null) return best;
            }
            return null;
        }
    }
}
