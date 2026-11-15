// Prüft den H.264-Decoder ohne Kamera: erzeugt einen Strom mit bekanntem Inhalt, dekodiert ihn und
// vergleicht Farben und Lage des wandernden Balkens.
//
// Aufruf: DecoderTest.exe [Bild.png]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QcKamera.Media;

static class DecoderTest
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + what);
        if (!ok) _fail++;
    }

    static bool Near(byte[] bgra, int w, int x, int y, int r, int g, int b)
    {
        int i = (y * w + x) * 4;
        return Math.Abs(bgra[i + 2] - r) < 40 && Math.Abs(bgra[i + 1] - g) < 40 && Math.Abs(bgra[i] - b) < 40;
    }

    [STAThread]
    static int Main(string[] a)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Prozess: " + (IntPtr.Size == 4 ? "32 Bit" : "64 Bit"));
        const int W = 640, H = 360, N = 60;
        Console.WriteLine("Testbild kodieren:");
        List<byte[]> aus = TestQuelle.Encode(W, H, N, 25);
        Check(aus.Count == N, aus.Count + " Bilder kodiert, erstes mit NAL-Typen " + (aus.Count > 0 ? TestQuelle.NalTypes(aus[0]) : "-"));
        if (aus.Count == 0) return 1;

        Console.WriteLine("Dekodieren:");
        byte[] bgra = null, half = null;
        int late = 0, wrongBar = 0, fed = 0;
        Stopwatch sw = Stopwatch.StartNew();
        using (VideoDecoder d = new VideoDecoder(false))
        {
            foreach (byte[] au in aus)
            {
                d.Decode(au, au.Length, delegate(VideoDecoder dec)
                {
                    dec.ToBgra(1, ref bgra);
                    int frame = dec.Frames - 1, x = TestQuelle.BarX(frame, W) + TestQuelle.BarWidth / 2;
                    if (!Near(bgra, dec.Width, x, H - 20, 255, 255, 255)) wrongBar++;
                });
                fed++;
                if (d.Frames < fed) late++;
            }
            sw.Stop();
            Check(d.Frames == N, d.Frames + " von " + N + " Bildern dekodiert, " + (sw.ElapsedMilliseconds / (double)N).ToString("0.0") + " ms je Bild einschließlich Umrechnung");
            Check(d.Width == W && d.Height == H, "Bildgröße " + d.Width + " × " + d.Height + " (kodiert " + d.Stride + " × " + d.CodedHeight + ")");
            Check(late == 0, "kein Bild verzögert (Bilder, die erst nach dem nächsten kamen: " + late + ")");
            Check(wrongBar == 0, "Balken in jedem Bild an der erwarteten Stelle (Abweichungen: " + wrongBar + ")");
            Check(Near(bgra, W, W / 4, H / 3, 255, 0, 0), "links rot");
            Check(Near(bgra, W, W * 3 / 4, H / 3, 0, 0, 255), "rechts blau");
            int hw, hh;
            d.ScaledSize(2, out hw, out hh);
            d.ToBgra(2, ref half);
            Check(hw == W / 2 && hh == H / 2 && Near(half, hw, hw / 4, hh / 3, 255, 0, 0) && Near(half, hw, hw * 3 / 4, hh / 3, 0, 0, 255), "verkleinert auf " + hw + " × " + hh);
            Check(d.DivisorFor(680, 237) == 1 && d.DivisorFor(200, 100) == 3, "Teiler für die Anzeigegröße");
        }
        if (a.Length > 0 && bgra != null)
        {
            BitmapSource bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, bgra, W * 4);
            PngBitmapEncoder png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream f = File.Create(a[0])) png.Save(f);
            Console.WriteLine("  geschrieben: " + a[0]);
        }
        Console.WriteLine(_fail == 0 ? "\nALLES OK" : "\n" + _fail + " FEHLER");
        return _fail == 0 ? 0 : 1;
    }
}
