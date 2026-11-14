// Vorschau des Wetter-Plugins ohne QuadClient/HomeServer: lädt 99-1_client.dll, füttert Beispielwerte
// und speichert die Anzeige als PNG. Dient zum Prüfen der Darstellung und erzeugt die Vorschaubilder.
//
// Aufruf: Vorschau.exe <ausgabe.png> [dark|light] [sample|live|empty] [breite] [hoehe] [blaettern]
//   sample = feste Beispielvorhersage (ohne Internet), live = echte Abfrage bei Open-Meteo,
//   empty  = keine Einstellungen (Hinweis "Kein Ort eingestellt")
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using hs_client_framework;
using QcWetter;
using QcWetter.Client;

static class Vorschau
{
    const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    static void Pump(Dispatcher d)
    {
        d.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
    }

    static string SampleJson()
    {
        int[] codes = { 2, 61, 3, 0, 95, 71, 45 };
        double[] max = { 9.4, 7.1, 6.3, 8.8, 11.2, 1.4, 4.0 }, min = { 2.1, 3.3, 0.4, -1.6, 4.9, -3.2, -0.5 };
        double[] sum = { 0.0, 6.4, 0.3, 0.0, 12.8, 3.1, 0.0 }, prob = { 10, 85, 30, 0, 90, 70, 15 };
        double[] wind = { 14, 26, 18, 9, 41, 22, 6 }, dir = { 250, 230, 300, 80, 200, 350, 120 };
        CultureInfo inv = CultureInfo.InvariantCulture;
        StringBuilder t = new StringBuilder(), c = new StringBuilder(), mx = new StringBuilder(), mn = new StringBuilder(),
            s = new StringBuilder(), p = new StringBuilder(), w = new StringBuilder(), d = new StringBuilder();
        for (int i = 0; i < 7; i++)
        {
            string sep = i == 0 ? "" : ",";
            t.Append(sep + "\"" + DateTime.Today.AddDays(i).ToString("yyyy-MM-dd", inv) + "\"");
            c.Append(sep + codes[i]); mx.Append(sep + max[i].ToString(inv)); mn.Append(sep + min[i].ToString(inv));
            s.Append(sep + sum[i].ToString(inv)); p.Append(sep + prob[i].ToString(inv));
            w.Append(sep + wind[i].ToString(inv)); d.Append(sep + dir[i].ToString(inv));
        }
        return "{\"current\":{\"temperature_2m\":7.3,\"apparent_temperature\":5.6,\"relative_humidity_2m\":81,\"weather_code\":2,\"wind_speed_10m\":12.6,"
            + "\"wind_direction_10m\":248,\"precipitation\":0.0,\"is_day\":1},"
            + "\"daily\":{\"time\":[" + t + "],\"weather_code\":[" + c + "],\"temperature_2m_max\":[" + mx + "],\"temperature_2m_min\":[" + mn
            + "],\"precipitation_sum\":[" + s + "],\"precipitation_probability_max\":[" + p + "],\"wind_speed_10m_max\":[" + w
            + "],\"wind_direction_10m_dominant\":[" + d + "]}}";
    }

    [STAThread]
    static int Main(string[] a)
    {
        string outFile = a.Length > 0 ? a[0] : "vorschau.png";
        bool light = a.Length > 1 && a[1] == "light";
        string mode = a.Length > 2 ? a[2] : "sample";
        int width = a.Length > 3 ? int.Parse(a[3]) : 680, height = a.Length > 4 ? int.Parse(a[4]) : 320;
        int page = a.Length > 5 ? int.Parse(a[5]) : 0;

        Type tq = typeof(WetterQuad);
        tq.GetField("DisableNetwork", Priv).SetValue(null, mode != "live");

        WetterSettings set = new WetterSettings();
        if (mode != "empty")
        {
            set.Name = "Nürnberg"; set.Lat = 49.45; set.Lon = 11.08;
            set.Kos["temp"] = 101; set.Kos["hum"] = 102; set.Kos["wind"] = 103; set.Kos["dir"] = 104;
            set.Kos["rain"] = 105; set.Kos["lux"] = 106; set.Kos["press"] = 107; set.Kos["feel"] = 108; set.Kos["lux_e"] = 109; set.Kos["lux_s"] = 110; set.Kos["lux_w"] = 111;
        }

        Canvas display = new Canvas();
        display.Width = width; display.Height = height;
        display.Background = new SolidColorBrush(light ? Color.FromRgb(0xE4, 0xE6, 0xE8) : Color.FromRgb(0x0D, 0x0D, 0x0D));
        // Titelzeile, wie sie der QuadClient über dem Plugin zeigt (nur zur Orientierung)
        TextBlock head = new TextBlock();
        head.Text = "Wetter"; head.FontSize = 15; head.Margin = new Thickness(14, 10, 0, 0);
        head.Foreground = new SolidColorBrush(light ? Color.FromRgb(0x3C, 0x3C, 0x3C) : Color.FromRgb(0xE6, 0xE6, 0xE6));
        display.Children.Add(head);

        IQuadContent q = new WetterQuad();
        q.init(1, "Wetter");
        q.onProjectData(set.ToBytes());
        q.onDesignData(Encoding.UTF8.GetBytes("<design/>"));
        q.run(display);
        if (light) ((IThemeable)q).OnThemeChanged(ThemeMode.light);

        if (mode == "sample")
        {
            tq.GetField("_fc", Priv).SetValue(q, Forecast.Parse(SampleJson()));
            tq.GetField("_at", Priv).SetValue(q, DateTime.Now);
            MethodInfo onTag = tq.GetMethod("OnTag", Priv);
            double[] vals = { 6.8, 78, 3.2, 255, 0, 12300, 101600, 4.9, 8200, 45300, 12300 };   // Luftdruck in Pa (DPT 9.006)      // Wind in m/s (Einheit des Wind-KO)
            for (int i = 0; i < vals.Length; i++) onTag.Invoke(q, new object[] { 101 + i, vals[i], "" });
        }
        tq.GetField("_off", Priv).SetValue(q, page);
        q.show(1);

        if (mode == "live")
        {
            DateTime end = DateTime.Now.AddSeconds(20);
            while (DateTime.Now < end && tq.GetField("_fc", Priv).GetValue(q) == null)
            {
                Pump(display.Dispatcher);
                System.Threading.Thread.Sleep(100);
            }
            Console.WriteLine(tq.GetField("_fc", Priv).GetValue(q) == null
                ? "live: KEINE Vorhersage – " + tq.GetField("_error", Priv).GetValue(q)
                : "live: Vorhersage geladen");
        }
        Pump(display.Dispatcher);

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

        q.clear();
        q.onHaltPlugin();
        Console.WriteLine("geschrieben: " + outFile);
        return 0;
    }
}
