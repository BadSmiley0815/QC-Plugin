// Prüft eine ECHTE Kamera mit denselben Klassen wie das Plugin: ONVIF-Abfrage, RTSP-Empfang, Decoder, Einzelbild.
// Das Kennwort wird verdeckt abgefragt und nirgends ausgegeben oder gespeichert.
//
// Aufruf: KameraTest.exe <Adresse> <Benutzer> [ONVIF-Port] [Sekunden]
//    oder KameraTest.exe rtsp://... <Benutzer>            (nur RTSP, ohne ONVIF)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QcKamera;
using QcKamera.Media;

static class KameraTest
{
    sealed class Enough : Exception { }

    static string ReadPassword()
    {
        Console.Write("Kennwort (wird nicht angezeigt): ");
        StringBuilder b = new StringBuilder();
        if (Console.IsInputRedirected) { string l = Console.ReadLine(); Console.WriteLine(); return l ?? ""; }
        for (;;)
        {
            ConsoleKeyInfo k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (b.Length > 0) b.Length--; }
            else if (k.KeyChar != '\0') b.Append(k.KeyChar);
        }
        Console.WriteLine();
        return b.ToString();
    }

    /// <summary>Adresse ohne Benutzer und Kennwort, falls jemand sie in die Adresse geschrieben hat.</summary>
    static string Safe(string url)
    {
        int at = url.IndexOf('@'), sl = url.IndexOf("://");
        return at > 0 && sl > 0 && at > sl ? url.Substring(0, sl + 3) + "…@" + url.Substring(at + 1) : url;
    }

    static bool Rtsp(string label, string url, string user, string pass, int seconds, string pngFile)
    {
        Console.WriteLine(label + ": " + Safe(url));
        try
        {
            using (RtspClient c = new RtspClient(url, user, pass))
            {
                c.TimeoutMs = 8000;
                Stopwatch sw = Stopwatch.StartNew();
                c.Open();
                Console.WriteLine("  ok     verbunden nach " + sw.ElapsedMilliseconds + " ms, Codec " + c.Codec);
                using (VideoDecoder dec = new VideoDecoder(c.IsHevc))
                {
                    int units = 0, keys = 0, bytes = 0;
                    long first = -1, decodeMs = 0;
                    byte[] bgra = null;
                    int pw = 0, ph = 0;
                    try
                    {
                        c.Receive(delegate(byte[] data, int len, bool key)
                        {
                            units++; bytes += len;
                            if (key) keys++;
                            long t0 = sw.ElapsedMilliseconds;
                            dec.Decode(data, len, delegate(VideoDecoder d)
                            {
                                if (first < 0) first = sw.ElapsedMilliseconds;
                                if (bgra == null || d.Frames % 25 == 0) { d.ToBgra(1, ref bgra); pw = d.Width; ph = d.Height; }
                            });
                            decodeMs += sw.ElapsedMilliseconds - t0;
                            if (sw.ElapsedMilliseconds > seconds * 1000) throw new Enough();
                        });
                    }
                    catch (Enough) { }
                    double secs = Math.Max(0.001, (sw.ElapsedMilliseconds - Math.Max(0, first)) / 1000.0);
                    bool ok = dec.Frames > 0;
                    Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + dec.Frames + " Bilder dekodiert (" + units + " empfangen, " + keys + " Schlüsselbilder), "
                        + dec.Width + " × " + dec.Height + ", erstes Bild nach " + first + " ms");
                    if (ok)
                    {
                        Console.WriteLine("         " + (dec.Frames / secs).ToString("0.0") + " Bilder/s, " + (bytes * 8 / 1000.0 / (sw.ElapsedMilliseconds / 1000.0)).ToString("0")
                            + " kbit/s, Dekodieren " + (decodeMs / (double)dec.Frames).ToString("0.0") + " ms je Bild");
                        if (bgra != null && pngFile != null)
                        {
                            PngBitmapEncoder enc = new PngBitmapEncoder();
                            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgr32, null, bgra, pw * 4)));
                            using (FileStream fs = File.Create(pngFile)) enc.Save(fs);
                            Console.WriteLine("         Bild gespeichert: " + pngFile);
                        }
                    }
                    return ok;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FEHLER " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    static bool Snapshot(string url, string user, string pass)
    {
        Console.WriteLine("Einzelbild: " + Safe(url));
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Timeout = 8000; rq.Proxy = null;
            rq.ServerCertificateValidationCallback = delegate { return true; };
            if (user.Length > 0) rq.Credentials = new NetworkCredential(user, pass);
            using (WebResponse rs = rq.GetResponse())
            using (MemoryStream ms = new MemoryStream())
            {
                rs.GetResponseStream().CopyTo(ms);
                ms.Position = 0;
                BitmapDecoder d = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Console.WriteLine("  ok     " + rs.ContentType + ", " + ms.Length + " Bytes, " + d.Frames[0].PixelWidth + " × " + d.Frames[0].PixelHeight);
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FEHLER " + ex.Message);
            return false;
        }
    }

    [STAThread]
    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (a.Length < 1)
        {
            Console.WriteLine("Aufruf: KameraTest.exe <Adresse> <Benutzer> [ONVIF-Port] [Sekunden]");
            Console.WriteLine("   oder KameraTest.exe rtsp://... <Benutzer>");
            return 2;
        }
        string host = a[0], user = a.Length > 1 ? a[1] : "";
        int port = a.Length > 2 ? int.Parse(a[2]) : 80, seconds = a.Length > 3 ? int.Parse(a[3]) : 6;
        string pass = user.Length > 0 ? ReadPassword() : "";
        string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        int fail = 0;

        if (host.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            return Rtsp("RTSP", host, user, pass, seconds, Path.Combine(dir, "kameratest.png")) ? 0 : 1;

        Console.WriteLine("ONVIF-Abfrage: " + host + ", Port " + port + ", Benutzer " + (user.Length > 0 ? user : "(keiner)"));
        List<OnvifProfile> list;
        OnvifClient oc = new OnvifClient(host, port, user, pass);
        try { list = oc.Query(); }
        catch (Exception ex)
        {
            Console.WriteLine("  FEHLER " + ex.Message);
            return 1;
        }
        Console.WriteLine("  ok     Antwort über " + oc.DeviceUrl + ", " + list.Count + " Profile:");
        foreach (OnvifProfile p in list)
        {
            Console.WriteLine("         " + p);
            Console.WriteLine("           RTSP: " + (p.StreamUri.Length > 0 ? Safe(p.StreamUri) : "(keine)"));
            Console.WriteLine("           Bild: " + (p.SnapshotUri.Length > 0 ? Safe(p.SnapshotUri) : "(keine)"));
        }
        OnvifProfile small = OnvifClient.PickSmall(list), large = OnvifClient.PickLarge(list);
        if (small == null) { Console.WriteLine("  FEHLER kein Profil mit H.264 oder H.265"); return 1; }
        string name = host.Replace(':', '_').Replace('/', '_');
        if (!Rtsp("Quadrant (" + small + ")", small.StreamUri, user, pass, seconds, Path.Combine(dir, "kameratest_" + name + "_quadrant.png"))) fail++;
        if (large != null && large != small
            && !Rtsp("Vollbild (" + large + ")", large.StreamUri, user, pass, seconds, Path.Combine(dir, "kameratest_" + name + "_vollbild.png"))) fail++;
        string snap = small.SnapshotUri.Length > 0 ? small.SnapshotUri : (large != null ? large.SnapshotUri : "");
        if (snap.Length > 0 && !Snapshot(snap, user, pass)) fail++;

        Console.WriteLine(fail == 0 ? "\nALLES OK" : "\n" + fail + " FEHLER");
        return fail == 0 ? 0 : 1;
    }
}
