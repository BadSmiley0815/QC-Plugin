// QC-Plugin 99-2 "Kamera" – Abspieler: holen die Bilder einer Kamera in einem eigenen Thread und geben sie
// an die Anzeige weiter. RTSP (H.264/H.265), MJPEG über HTTP und Einzelbilder (JPEG).
// Jeder Abspieler verbindet sich nach Fehlern von selbst neu.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QcKamera.Media;

namespace QcKamera.Client
{
    abstract class CamPlayer
    {
        protected readonly Dispatcher Ui;
        protected readonly CamSettings Cam;
        protected readonly bool German;
        protected volatile bool Stopped;
        protected volatile bool Shown;
        readonly ManualResetEvent _wake = new ManualResetEvent(false);

        /// <summary>Größe der Anzeige in Bildpunkten; größere Bilder werden beim Umrechnen verkleinert.</summary>
        public volatile int TargetW = 680, TargetH = 237;
        /// <summary>Beide werden im Thread der Oberfläche gerufen.</summary>
        public Action<ImageSource> OnImage;
        public Action<string> OnStatus;
        /// <summary>Kurzbezeichnung der Quelle für die Unterleiste, z. B. "RTSP H.264 1280×720".</summary>
        public volatile string Info = "";

        protected CamPlayer(Dispatcher ui, CamSettings cam, bool german) { Ui = ui; Cam = cam; German = german; }

        protected string L(string de, string en) { return German ? de : en; }

        public void Start()
        {
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Name = "QC-Kamera " + Cam.Name;
            t.Start();
        }

        public void Stop()
        {
            Stopped = true;
            _wake.Set();
            try { Abort(); } catch { }
        }

        protected virtual void Abort() { }
        protected abstract void Run();

        /// <summary>Wartet; true = der Abspieler wurde angehalten.</summary>
        protected bool Wait(int ms) { return _wake.WaitOne(ms) || Stopped; }

        void Loop()
        {
            int fails = 0;
            while (!Stopped)
            {
                try
                {
                    Shown = false;
                    Status(L("Verbinde …", "Connecting …"));
                    Run();
                    if (!Stopped) Status(L("Verbindung beendet – neuer Versuch …", "Connection closed – retrying …"));
                }
                catch (Exception ex)
                {
                    if (Stopped) break;
                    Status(ex.Message + L(" – neuer Versuch …", " – retrying …"));
                    KameraQuad.Log(Cam.Name + ": " + ex.Message);
                }
                fails = Shown ? 0 : fails + 1;
                if (Wait(Math.Min(15000, 2000 + 3000 * fails))) break;
            }
        }

        protected void Status(string text)
        {
            Ui.BeginInvoke(new Action(delegate { if (!Stopped && OnStatus != null) OnStatus(text); }));
        }

        protected HttpWebRequest Http(string url, int timeoutMs)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            Uri u = new Uri(url);
            string user = Cam.User, pass = Cam.Password;
            if (user.Length == 0 && u.UserInfo.Length > 0)
            {
                string[] up = u.UserInfo.Split(new char[] { ':' }, 2);
                user = Uri.UnescapeDataString(up[0]);
                pass = up.Length > 1 ? Uri.UnescapeDataString(up[1]) : "";
                url = new UriBuilder(u) { UserName = "", Password = "" }.Uri.AbsoluteUri;
            }
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "GET";
            rq.Timeout = timeoutMs;
            rq.ReadWriteTimeout = timeoutMs;
            rq.Proxy = null;
            rq.UserAgent = "QC-Plugin 99-2";
            rq.ServerCertificateValidationCallback = delegate { return true; };   // Kameras haben selbst ausgestellte Zertifikate
            if (user.Length > 0) rq.Credentials = new NetworkCredential(user, pass);   // Basic und Digest
            return rq;
        }

        protected static string HttpError(WebException ex)
        {
            HttpWebResponse rs = ex.Response as HttpWebResponse;
            if (rs == null) return ex.Message;
            if (rs.StatusCode == HttpStatusCode.Unauthorized) return "Anmeldung abgelehnt (Benutzer oder Kennwort falsch)";
            return "HTTP " + (int)rs.StatusCode + " " + rs.StatusDescription;
        }

        bool _jpegPosted;

        /// <summary>Dekodiert ein JPEG und zeigt es an (überspringt Bilder, solange die Oberfläche beschäftigt ist).</summary>
        protected void ShowJpeg(byte[] jpg, int length)
        {
            if (_jpegPosted) return;
            BitmapImage bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(jpg, 0, length, false);
            bi.EndInit();
            bi.Freeze();
            Info = Kind + " " + bi.PixelWidth + "×" + bi.PixelHeight;
            _jpegPosted = true;
            Shown = true;
            Ui.BeginInvoke(DispatcherPriority.Render, new Action(delegate
            {
                _jpegPosted = false;
                if (!Stopped && OnImage != null) OnImage(bi);
            }));
        }

        protected virtual string Kind { get { return ""; } }
    }

    sealed class RtspPlayer : CamPlayer
    {
        readonly string _url;
        volatile RtspClient _client;
        readonly object _lock = new object();
        byte[] _work, _ready;
        int _readyW, _readyH;
        bool _posted;
        WriteableBitmap _bmp;

        public RtspPlayer(Dispatcher ui, CamSettings cam, bool german, string url) : base(ui, cam, german) { _url = url; }

        protected override void Abort()
        {
            RtspClient c = _client;
            if (c != null) c.Close();
        }

        protected override void Run()
        {
            using (RtspClient c = new RtspClient(_url, Cam.User, Cam.Password))
            {
                _client = c;
                if (Stopped) return;
                c.Open();
                using (VideoDecoder dec = new VideoDecoder(c.IsHevc))
                {
                    string codec = c.IsHevc ? "H.265" : "H.264";
                    Info = "RTSP " + codec;
                    c.Receive(delegate(byte[] data, int len, bool key)
                    {
                        if (Stopped) return;
                        dec.Decode(data, len, delegate(VideoDecoder d)
                        {
                            Info = "RTSP " + codec + " " + d.Width + "×" + d.Height;
                            Frame(d);
                        });
                    });
                }
            }
        }

        void Frame(VideoDecoder d)
        {
            lock (_lock) { if (_posted) return; }          // Oberfläche hat das letzte Bild noch nicht gezeigt: dieses auslassen
            int div = d.DivisorFor(TargetW, TargetH), w, h;
            d.ScaledSize(div, out w, out h);
            d.ToBgra(div, ref _work);
            lock (_lock)
            {
                byte[] t = _ready; _ready = _work; _work = t;
                _readyW = w; _readyH = h;
                _posted = true;
            }
            Shown = true;
            Ui.BeginInvoke(DispatcherPriority.Render, new Action(Present));
        }

        void Present()
        {
            lock (_lock)
            {
                _posted = false;
                if (Stopped || _ready == null || _ready.Length != _readyW * _readyH * 4) return;
                if (_bmp == null || _bmp.PixelWidth != _readyW || _bmp.PixelHeight != _readyH)
                    _bmp = new WriteableBitmap(_readyW, _readyH, 96, 96, PixelFormats.Bgr32, null);
                _bmp.WritePixels(new Int32Rect(0, 0, _readyW, _readyH), _ready, _readyW * 4, 0);
            }
            if (OnImage != null) OnImage(_bmp);
        }
    }

    sealed class JpegPlayer : CamPlayer
    {
        public JpegPlayer(Dispatcher ui, CamSettings cam, bool german) : base(ui, cam, german) { }

        protected override string Kind { get { return "JPEG"; } }

        protected override void Run()
        {
            MemoryStream ms = new MemoryStream();
            byte[] chunk = new byte[32 * 1024];
            while (!Stopped)
            {
                DateTime start = DateTime.UtcNow;
                try
                {
                    HttpWebRequest rq = Http(Cam.Jpeg, 8000);
                    using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    using (Stream s = rs.GetResponseStream())
                    {
                        ms.SetLength(0);
                        int n;
                        while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            ms.Write(chunk, 0, n);
                            if (ms.Length > 16 * 1024 * 1024) throw new IOException("Bild zu groß");
                        }
                    }
                }
                catch (WebException ex) { throw new IOException(HttpError(ex)); }
                try { ShowJpeg(ms.GetBuffer(), (int)ms.Length); }
                catch (NotSupportedException) { throw new IOException(L("Die Adresse liefert kein JPEG-Bild", "The address does not return a JPEG image")); }
                int rest = Cam.Refresh - (int)(DateTime.UtcNow - start).TotalMilliseconds;
                if (Wait(Math.Max(20, rest))) return;
            }
        }
    }

    sealed class MjpegPlayer : CamPlayer
    {
        volatile HttpWebRequest _rq;
        Stream _s;
        readonly byte[] _buf = new byte[64 * 1024];
        int _pos, _end;

        public MjpegPlayer(Dispatcher ui, CamSettings cam, bool german) : base(ui, cam, german) { }

        protected override string Kind { get { return "MJPEG"; } }

        protected override void Abort()
        {
            HttpWebRequest r = _rq;
            if (r != null) r.Abort();
        }

        int ReadByte()
        {
            if (_pos >= _end)
            {
                _pos = 0;
                _end = _s.Read(_buf, 0, _buf.Length);
                if (_end <= 0) { _end = 0; throw new IOException(L("Die Kamera hat den Strom beendet", "The camera closed the stream")); }
            }
            return _buf[_pos++];
        }

        string ReadLine()
        {
            StringBuilder b = new StringBuilder();
            for (int c = ReadByte(); c != '\n'; c = ReadByte())
            {
                if (c != '\r' && b.Length < 2048) b.Append((char)c);
            }
            return b.ToString();
        }

        protected override void Run()
        {
            _pos = _end = 0;
            HttpWebRequest rq = Http(Cam.Mjpeg, 8000);
            _rq = rq;
            if (Stopped) return;
            try
            {
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (_s = rs.GetResponseStream())
                {
                    string type = rs.ContentType ?? "";
                    if (type.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase))
                        throw new IOException(L("Die Adresse liefert ein Einzelbild, keinen MJPEG-Strom", "The address returns a single image, not an MJPEG stream"));
                    byte[] jpg = new byte[256 * 1024];
                    while (!Stopped)
                    {
                        string line = ReadLine();
                        if (!line.StartsWith("--")) continue;                 // Trennzeile zwischen den Bildern
                        int length = -1;
                        while ((line = ReadLine()).Length > 0)
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out length);
                        }
                        int n = 0;
                        if (length > 0)
                        {
                            if (length > 16 * 1024 * 1024) throw new IOException("Bild zu groß");
                            if (jpg.Length < length) jpg = new byte[length];
                            while (n < length)
                            {
                                if (_pos >= _end) { ReadByte(); _pos--; }
                                int c = Math.Min(length - n, _end - _pos);
                                Buffer.BlockCopy(_buf, _pos, jpg, n, c);
                                _pos += c; n += c;
                            }
                        }
                        else
                        {
                            int prev = 0;                                       // ohne Längenangabe: bis zum JPEG-Endezeichen FF D9 lesen
                            for (;;)
                            {
                                int c = ReadByte();
                                if (n == jpg.Length)
                                {
                                    if (n > 16 * 1024 * 1024) throw new IOException("Bild zu groß");
                                    Array.Resize(ref jpg, n * 2);
                                }
                                jpg[n++] = (byte)c;
                                if (prev == 0xFF && c == 0xD9) break;
                                prev = c;
                            }
                        }
                        try { ShowJpeg(jpg, n); } catch (NotSupportedException) { } catch (FileFormatException) { }
                    }
                }
            }
            catch (WebException ex)
            {
                if (!Stopped) throw new IOException(HttpError(ex));
            }
            finally { _rq = null; }
        }
    }
}
