// Zeichnet die Anzeige von 99-2 ohne QuadClient als PNG und prüft dabei, dass wirklich ein Bild ankommt.
// Die Kamera ist nachgebaut (TestKamera.cs): RTSP mit H.264, MJPEG oder Einzelbilder, jeweils mit Anmeldung.
//
// Aufruf: Vorschau.exe <Bild.png> [dark|light] [rtsp|mjpeg|jpeg|szene|leer|aus] [Breite] [Höhe]
// "szene" zeigt statt des Testbilds eine gezeichnete Beispielszene (für Vorschaubild, Hilfe und README).
#pragma warning disable 0618   // FormattedText ohne PixelsPerDip: für .NET 4.5.2 gibt es keine andere Überladung
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using hs_client_framework;
using QcKamera;
using QcKamera.Client;

static class Vorschau
{
    const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    static void Pump(Dispatcher d)
    {
        DispatcherFrame f = new DispatcherFrame();
        d.BeginInvoke(DispatcherPriority.Background, new Action(delegate { f.Continue = false; }));
        Dispatcher.PushFrame(f);
    }

    static byte[] Jpeg(int frame, int w, int h)
    {
        byte[] nv = TestQuelle.Frame(frame, w, h), bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int c = 298 * (nv[y * w + x] - 16) + 128, o = w * h + (y / 2) * w + (x & ~1), d = nv[o] - 128, e = nv[o + 1] - 128, i = (y * w + x) * 4;
                bgra[i] = (byte)Math.Max(0, Math.Min(255, (c + 516 * d) >> 8));
                bgra[i + 1] = (byte)Math.Max(0, Math.Min(255, (c - 100 * d - 208 * e) >> 8));
                bgra[i + 2] = (byte)Math.Max(0, Math.Min(255, (c + 409 * e) >> 8));
                bgra[i + 3] = 255;
            }
        JpegBitmapEncoder enc = new JpegBitmapEncoder();
        enc.QualityLevel = 85;
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4)));
        using (MemoryStream ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
    }

    static Brush Rgb(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }

    static Geometry Poly(params double[] p)
    {
        StreamGeometry g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(p[0], p[1]), true, true);
            for (int i = 2; i < p.Length; i += 2) c.LineTo(new Point(p[i], p[i + 1]), true, false);
        }
        return g;
    }

    /// <summary>Gezeichnete Beispielszene (Einfahrt am Abend) für Vorschaubild, Hilfe und README – kein echtes Kamerabild.</summary>
    static byte[] Scene(int w, int h)
    {
        DrawingVisual v = new DrawingVisual();
        using (DrawingContext d = v.RenderOpen())
        {
            double hor = h * 0.52;
            d.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1B, 0x2B, 0x4A), Color.FromRgb(0xE8, 0x9A, 0x5B), 90), null, new Rect(0, 0, w, hor));
            d.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xFF, 0xFF, 0xE2, 0xA8), Color.FromArgb(0, 0xFF, 0xC0, 0x70)), null, new Point(w * 0.63, hor), w * 0.16, h * 0.34);
            // Bäume und Hecke am Horizont
            Brush tree = Rgb(0x16, 0x2A, 0x22);
            for (int i = 0; i < 9; i++)
                d.DrawEllipse(tree, null, new Point(w * (0.36 + i * 0.075), hor - h * (0.03 + (i * 7 % 5) * 0.012)), w * 0.05, h * (0.09 + (i * 3 % 4) * 0.015));
            d.DrawRectangle(Rgb(0x1E, 0x3A, 0x2A), null, new Rect(w * 0.30, hor - h * 0.035, w * 0.70, h * 0.07));
            // Rasen und gepflasterte Einfahrt
            d.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2C, 0x4A, 0x30), Color.FromRgb(0x1C, 0x30, 0x20), 90), null, new Rect(0, hor, w, h - hor));
            d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(0x8A, 0x86, 0x80), Color.FromRgb(0x55, 0x52, 0x4E), 90), null,
                Poly(w * 0.50, hor, w * 0.66, hor, w * 0.98, h, w * 0.22, h));
            Pen joint = new Pen(Rgb(0x48, 0x45, 0x42), Math.Max(1, h / 240.0));
            for (int i = 1; i < 7; i++)
            {
                double t = i / 7.0, y = hor + (h - hor) * t * t;
                double k = (y - hor) / (h - hor);
                d.DrawLine(joint, new Point(w * (0.50 - 0.28 * k), y), new Point(w * (0.66 + 0.32 * k), y));
            }
            // Hauswand mit Tür, Fenster und Lampe
            d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(0xC9, 0xC2, 0xB6), Color.FromRgb(0x8F, 0x89, 0x80), 0), null,
                Poly(0, 0, w * 0.27, h * 0.10, w * 0.27, h * 0.80, 0, h));
            d.DrawGeometry(Rgb(0x3A, 0x2E, 0x28), null, Poly(w * 0.05, h * 0.34, w * 0.115, h * 0.36, w * 0.115, h * 0.90, w * 0.05, h * 0.955));
            d.DrawGeometry(new LinearGradientBrush(Color.FromRgb(0xFF, 0xD9, 0x8C), Color.FromRgb(0xE0, 0xA0, 0x50), 90), null,
                Poly(w * 0.16, h * 0.30, w * 0.235, h * 0.315, w * 0.235, h * 0.55, w * 0.16, h * 0.56));
            d.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xD0, 0xFF, 0xE8, 0xB0), Color.FromArgb(0, 0xFF, 0xD0, 0x80)), null, new Point(w * 0.135, h * 0.27), w * 0.07, h * 0.20);
            d.DrawEllipse(Rgb(0xFF, 0xF4, 0xD0), null, new Point(w * 0.135, h * 0.27), w * 0.006, w * 0.006);
            // Auto in der Einfahrt
            double cx = w * 0.60, cy = h * 0.70, s = h / 360.0;
            d.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), null, new Point(cx, cy + 38 * s), 105 * s, 12 * s);
            d.DrawRoundedRectangle(Rgb(0x2F, 0x5D, 0x8C), null, new Rect(cx - 95 * s, cy - 12 * s, 190 * s, 46 * s), 12 * s, 12 * s);
            d.DrawGeometry(Rgb(0x27, 0x4E, 0x78), null, Poly(cx - 62 * s, cy - 10 * s, cx - 42 * s, cy - 48 * s, cx + 42 * s, cy - 48 * s, cx + 62 * s, cy - 10 * s));
            d.DrawGeometry(Rgb(0x9F, 0xC4, 0xE0), null, Poly(cx - 50 * s, cy - 13 * s, cx - 36 * s, cy - 41 * s, cx + 36 * s, cy - 41 * s, cx + 50 * s, cy - 13 * s));
            d.DrawRectangle(Rgb(0xC8, 0x30, 0x30), null, new Rect(cx - 90 * s, cy - 2 * s, 28 * s, 10 * s));
            d.DrawRectangle(Rgb(0xC8, 0x30, 0x30), null, new Rect(cx + 62 * s, cy - 2 * s, 28 * s, 10 * s));
            d.DrawRectangle(Rgb(0xE8, 0xE8, 0xE0), null, new Rect(cx - 22 * s, cy + 12 * s, 44 * s, 11 * s));
            d.DrawEllipse(Rgb(0x15, 0x15, 0x15), null, new Point(cx - 68 * s, cy + 34 * s), 15 * s, 9 * s);
            d.DrawEllipse(Rgb(0x15, 0x15, 0x15), null, new Point(cx + 68 * s, cy + 34 * s), 15 * s, 9 * s);
            // Einblendung der Kamera
            FormattedText osd = new FormattedText("Einfahrt   15.11.2026  18:30:12", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 15 * s, Brushes.White);
            d.DrawText(osd, new Point(w - osd.Width - 12 * s, 9 * s));
        }
        RenderTargetBitmap bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        JpegBitmapEncoder enc = new JpegBitmapEncoder();
        enc.QualityLevel = 92;
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (MemoryStream ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
    }

    [STAThread]
    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outFile = a.Length > 0 ? a[0] : "vorschau.png";
        bool light = a.Length > 1 && a[1] == "light";
        string mode = a.Length > 2 ? a[2] : "rtsp";
        int width = a.Length > 3 ? int.Parse(a[3]) : 680, height = a.Length > 4 ? int.Parse(a[4]) : 320;
        const int W = 640, H = 360;

        List<byte[]> jpegs = new List<byte[]>();
        bool scene = mode == "szene";                           // Beispielszene im Seitenverhältnis der Anzeige, als Einzelbild
        if (scene) jpegs.Add(Scene(width * 2, (height - 83) * 2));
        else for (int i = 0; i < 20; i++) jpegs.Add(Jpeg(i * 3, W, H));
        TestRtspServer rtsp = new TestRtspServer(TestQuelle.Encode(W, H, 60, 25));
        rtsp.Loop = true; rtsp.PaceMs = 40; rtsp.Mtu = 1400;
        TestHttpServer http = new TestHttpServer(jpegs, rtsp.Url);

        KameraSettings set = new KameraSettings();
        if (mode != "leer")
        {
            CamSettings c = new CamSettings();
            c.Name = scene ? "Einfahrt" : "Einfahrt (Testbild)"; c.User = TestRtspServer.User; c.Password = TestRtspServer.Password; c.Refresh = 200;
            if (mode == "rtsp") c.Rtsp = rtsp.Url;
            else if (mode == "mjpeg") c.Mjpeg = http.Base + "/mjpeg";
            else if (mode == "jpeg" || scene) c.Jpeg = http.Base + "/snap.jpg";
            else if (mode == "aus") c.Rtsp = "rtsp://127.0.0.1:9/stream";
            set.Cams.Add(c);
            CamSettings c2 = new CamSettings();
            c2.Name = "Garten";
            set.Cams.Add(c2);
        }

        Canvas display = new Canvas();
        display.Width = width; display.Height = height;
        display.Background = new SolidColorBrush(light ? Color.FromRgb(0xE4, 0xE6, 0xE8) : Color.FromRgb(0x0D, 0x0D, 0x0D));
        TextBlock head = new TextBlock();                      // Titelzeile, wie sie der QuadClient über dem Plugin zeigt
        head.Text = "Kamera"; head.FontSize = 15; head.Margin = new Thickness(14, 10, 0, 0);
        head.Foreground = new SolidColorBrush(light ? Color.FromRgb(0x3C, 0x3C, 0x3C) : Color.FromRgb(0xE6, 0xE6, 0xE6));
        display.Children.Add(head);

        KameraQuad quad = new KameraQuad();
        IQuadContent q = quad;
        q.init(1, "Kamera");
        q.onProjectData(set.ToBytes());
        q.onDesignData(Encoding.UTF8.GetBytes("<design/>"));
        q.run(display);
        if (light) ((IThemeable)q).OnThemeChanged(ThemeMode.light);
        q.show(1);

        Type tq = typeof(KameraQuad);
        Func<bool> hasImage = delegate { Image img = (Image)tq.GetField("_img", Priv).GetValue(quad); return img != null && img.Source != null; };
        Func<string> status = delegate { return (string)tq.GetField("_statusText", Priv).GetValue(quad); };
        bool expectImage = mode == "rtsp" || mode == "mjpeg" || mode == "jpeg" || scene;
        DateTime end = DateTime.Now.AddSeconds(expectImage ? 15 : 4);
        while (DateTime.Now < end)
        {
            Pump(display.Dispatcher);
            Thread.Sleep(30);
            if (expectImage ? hasImage() && status().Length == 0 : status().Length > 0 && !status().StartsWith("Verbinde")) break;
        }
        Thread.Sleep(300);                                      // ein paar Bilder laufen lassen (Unterleiste zeigt dann die Auflösung)
        Pump(display.Dispatcher);
        tq.GetMethod("UpdateInfo", Priv).Invoke(quad, null);

        display.Measure(new Size(width, height));
        display.Arrange(new Rect(0, 0, width, height));
        display.UpdateLayout();
        Pump(display.Dispatcher);
        RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(display);
        PngBitmapEncoder enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
        using (FileStream fs = File.Create(outFile)) enc.Save(fs);

        // Bildinhalt prüfen: links rot, rechts blau (das Testbild ist auf die Anzeigehöhe eingepasst)
        bool ok;
        string info = (string)((TextBlock)tq.GetField("_info", Priv).GetValue(quad)).Text;
        if (expectImage)
        {
            byte[] px = new byte[width * height * 4];
            bmp.CopyPixels(px, width * 4, 0);
            int y = 40 + 100, l = (y * width + width / 2 - 100) * 4, r = (y * width + width / 2 + 100) * 4;
            ok = hasImage() && (scene || (px[l + 2] > 180 && px[l] < 80 && px[r] > 180 && px[r + 2] < 80));
            Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + mode + ": Bild angezeigt, Unterleiste „" + info + "“" + (status().Length > 0 ? ", Meldung: " + status() : ""));
        }
        else
        {
            ok = !hasImage() && status().Length > 0;
            Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + mode + ": Meldung „" + status() + "“");
        }

        q.clear();
        q.onHaltPlugin();
        rtsp.Dispose(); http.Dispose();
        Console.WriteLine("geschrieben: " + outFile);
        return ok ? 0 : 1;
    }
}
