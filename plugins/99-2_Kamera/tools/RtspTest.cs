// Prüft RTSP-Empfang und Decoder zusammen gegen die nachgebaute Kamera (TestKamera.cs), ohne echte Kamera.
//
// Aufruf: RtspTest.exe
using System;
using System.Collections.Generic;
using System.IO;
using QcKamera.Media;

static class RtspTest
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + what);
        if (!ok) _fail++;
    }

    static bool White(byte[] bgra, int w, int x, int y)
    {
        int i = (y * w + x) * 4;
        return bgra[i] > 200 && bgra[i + 1] > 200 && bgra[i + 2] > 200;
    }

    static void Run(List<byte[]> aus, bool spropOnly, int w, int h)
    {
        Console.WriteLine(spropOnly ? "SPS/PPS nur in der Stream-Beschreibung:" : "SPS/PPS im Strom:");
        using (TestRtspServer cam = new TestRtspServer(aus))
        using (VideoDecoder dec = new VideoDecoder(false))
        using (RtspClient c = new RtspClient(cam.Url, TestRtspServer.User, TestRtspServer.Password))
        {
            cam.SpropOnly = spropOnly;
            c.KeepAliveMs = 30;
            c.TimeoutMs = 3000;
            c.Open();
            Check(c.Codec == "H264", "Verbindung aufgebaut, Codec " + c.Codec);
            byte[] bgra = null;
            int units = 0, keys = 0, wrongBar = 0;
            string end = "";
            try
            {
                c.Receive(delegate(byte[] data, int len, bool key)
                {
                    units++;
                    if (key) keys++;
                    dec.Decode(data, len, delegate(VideoDecoder d)
                    {
                        d.ToBgra(1, ref bgra);
                        if (!White(bgra, d.Width, TestQuelle.BarX(d.Frames - 1, w) + TestQuelle.BarWidth / 2, h - 20)) wrongBar++;
                    });
                });
            }
            catch (IOException ex) { end = ex.Message; }
            Check(units == aus.Count && keys >= 1, units + " von " + aus.Count + " Bildern empfangen, davon " + keys + " Schlüsselbild(er)");
            Check(dec.Frames == aus.Count && dec.Width == w && dec.Height == h, dec.Frames + " Bilder dekodiert, " + dec.Width + " × " + dec.Height);
            Check(wrongBar == 0, "Balken in jedem Bild an der erwarteten Stelle (Abweichungen: " + wrongBar + ")");
            Check(cam.KeepAlives > 0, "Lebenszeichen an die Kamera gesendet (" + cam.KeepAlives + ")");
            Check(end.Length > 0 && cam.LastError.Length == 0, "Ende des Stroms erkannt: " + end + (cam.LastError.Length > 0 ? " / Kamera: " + cam.LastError : ""));
        }
    }

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        const int W = 640, H = 360, N = 60;
        List<byte[]> aus = TestQuelle.Encode(W, H, N, 25);
        Run(aus, false, W, H);
        Run(aus, true, W, H);

        Console.WriteLine("Anmeldung:");
        using (TestRtspServer cam = new TestRtspServer(aus))
        {
            string msg = "";
            try { using (RtspClient c = new RtspClient(cam.Url, TestRtspServer.User, "falsch")) c.Open(); }
            catch (IOException ex) { msg = ex.Message; }
            Check(msg.StartsWith("Anmeldung abgelehnt"), "falsches Kennwort: " + msg);
            msg = "";
            try { using (RtspClient c = new RtspClient(cam.Url, "", "")) c.Open(); }
            catch (IOException ex) { msg = ex.Message; }
            Check(msg.StartsWith("Die Kamera verlangt"), "ohne Anmeldung: " + msg);
            msg = "";
            string withUser = cam.Url.Replace("rtsp://", "rtsp://" + TestRtspServer.User + ":" + TestRtspServer.Password + "@");
            try { using (RtspClient c = new RtspClient(withUser, "", "")) { c.Open(); msg = c.Codec; } }
            catch (IOException ex) { msg = ex.Message; }
            Check(msg == "H264", "Benutzer und Kennwort in der Adresse: " + msg);
        }
        string unreachable = "";
        try { using (RtspClient c = new RtspClient("rtsp://127.0.0.1:9/x", "", "")) { c.TimeoutMs = 1500; c.Open(); } }
        catch (Exception ex) { unreachable = ex.Message; }
        Check(unreachable.Length > 0, "Kamera nicht erreichbar: " + unreachable);

        Console.WriteLine(_fail == 0 ? "\nALLES OK" : "\n" + _fail + " FEHLER");
        return _fail == 0 ? 0 : 1;
    }
}
