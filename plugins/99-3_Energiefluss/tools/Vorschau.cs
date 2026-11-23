// Vorschau des Energiefluss-Plugins ohne QuadClient/HomeServer: lädt 99-3_client.dll, füttert Beispielwerte
// und speichert die Anzeige als PNG. Dient zum Prüfen der Darstellung und erzeugt die Vorschaubilder.
//
// Aufruf: Vorschau.exe <ausgabe.png> [dark|light] [sample|einzel|viele|ruhe|leer] [breite] [hoehe]
//   einzel = wie sample, mit eingeblendeten Einzelwerten der PV-Anlage
//   sample = Tag mit PV-Überschuss und sieben Verbrauchern, viele = alle 17 Verbraucher (zwei Reihen am Haus),
//   ruhe   = Nacht mit Netzbezug, Verbraucher ohne Leistung, leer = kein KO zugewiesen
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using hs_client_framework;
using QcEnergie;
using QcEnergie.Client;

static class Vorschau
{
    const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static;

    static void Pump(Dispatcher d)
    {
        d.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
    }

    static void Vb(EnergieSettings s, Dictionary<int, double> vals, string id, string name, string icon, string an, double w)
    {
        Verbraucher v = new Verbraucher();
        v.Id = id; v.Name = name; v.Icon = icon; v.An = an; v.Tag = 200 + s.Vbs.Count;
        s.Vbs.Add(v);
        if (!double.IsNaN(w)) vals[v.Tag] = w;
    }

    static void Part(EnergieSettings s, Dictionary<int, double> vals, string an, string name, double w)
    {
        Teil t = new Teil();
        t.An = an; t.Name = name; t.Tag = 300 + s.Teile.Count;
        s.Teile.Add(t);
        if (!double.IsNaN(w)) vals[t.Tag] = w;
    }

    [STAThread]
    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outFile = a.Length > 0 ? a[0] : "vorschau.png";
        bool light = a.Length > 1 && a[1] == "light";
        string mode = a.Length > 2 ? a[2] : "sample";
        bool open = mode == "einzel";
        int width = a.Length > 3 ? int.Parse(a[3]) : 680, height = a.Length > 4 ? int.Parse(a[4]) : 320;

        List<string> bad = Symbole.Check();
        foreach (string b in bad) Console.WriteLine("FEHLER Symbol " + b);

        EnergieSettings set = new EnergieSettings();
        Dictionary<int, double> vals = new Dictionary<int, double>();
        if (mode != "leer")
        {
            for (int i = 0; i < EnergieSettings.KoIds.Length; i++) set.Kos[EnergieSettings.KoIds[i]] = 101 + i;
            // pv, bat, haus, netz, soc, batstate, pvkwh, evkwh, evpct, autpct, einsp, bezug
            double[] day = { 6840, 1250, 4310, 1280, 78, 1, 23.4, 11.8, 64, 92, 9.6, 0.7 };
            double[] night = { 0, 620, 940, -320, 31, 2, 23.4, 14.9, 64, 88, 9.6, 1.3 };
            double[] v = mode == "ruhe" ? night : day;
            for (int i = 0; i < v.Length; i++) vals[101 + i] = v[i];
            bool idle = mode == "ruhe";
            if (mode == "einzel") mode = "sample";
            Vb(set, vals, "wb1", "Garage", "", "haus", idle ? 0 : 3680);
            Vb(set, vals, "wm", "", "", "haus", idle ? 0 : 420);
            Vb(set, vals, "sm", "", "", "haus", 0);
            Vb(set, vals, "tr", "", "", "haus", idle ? 0 : 890);
            Vb(set, vals, "s1", "Wärmepumpe", "wp", "haus", idle ? 640 : 1150);
            Vb(set, vals, "s2", "Heizstab", "boiler", "pv", idle ? 0 : 2000);
            Vb(set, vals, "s3", "Notstrom", "steckdose", "bat", idle ? 85 : double.NaN);
            // Einzelwerte hinter den Hauptkreisen: drei Wechselrichter, zwei Zähler
            Part(set, vals, "pv", "Wechselrichter Süd", idle ? 0 : 3120);
            Part(set, vals, "pv", "Wechselrichter Ost/West", idle ? 0 : 2480);
            Part(set, vals, "pv", "", idle ? 0 : 1240);
            Part(set, vals, "netz", "Zähler Haus", double.NaN);
            Part(set, vals, "netz", "Zähler Wärmepumpe", idle ? -320 : 1280);
            if (mode == "viele")
            {
                Vb(set, vals, "wb2", "Carport", "auto", "haus", 11000);
                Vb(set, vals, "wb3", "", "", "haus", 0);
                Vb(set, vals, "wb4", "", "", "netz", 0);
                Vb(set, vals, "s4", "Herd", "herd", "haus", 1800);
                Vb(set, vals, "s5", "Kühlschrank", "kuehl", "haus", 65);
                Vb(set, vals, "s6", "Klima", "klima", "haus", 0);
                Vb(set, vals, "s7", "Licht", "licht", "haus", 120);
                Vb(set, vals, "s8", "Pool", "pool", "pv", 750);
                Vb(set, vals, "s9", "Fernseher", "tv", "haus", 95);
                Vb(set, vals, "s10", "Büro", "pc", "haus", -40);
            }
        }

        Canvas display = new Canvas();
        display.Width = width; display.Height = height;
        display.Background = new SolidColorBrush(light ? Color.FromRgb(0xE4, 0xE6, 0xE8) : Color.FromRgb(0x0D, 0x0D, 0x0D));
        // Titelzeile, wie sie der QuadClient über dem Plugin zeigt (nur zur Orientierung)
        TextBlock head = new TextBlock();
        head.Text = "Energiefluss"; head.FontSize = 15; head.Margin = new Thickness(14, 10, 0, 0);
        head.Foreground = new SolidColorBrush(light ? Color.FromRgb(0x3C, 0x3C, 0x3C) : Color.FromRgb(0xE6, 0xE6, 0xE6));
        display.Children.Add(head);

        IQuadContent q = new EnergieQuad();
        q.init(1, "Energiefluss");
        q.onProjectData(set.ToBytes());
        q.onDesignData(Encoding.UTF8.GetBytes("<design/>"));
        q.run(display);
        if (light) ((IThemeable)q).OnThemeChanged(ThemeMode.light);
        q.show(1);
        MethodInfo onTag = typeof(EnergieQuad).GetMethod("OnTag", Priv);
        foreach (KeyValuePair<int, double> kv in vals) onTag.Invoke(q, new object[] { kv.Key, kv.Value, "" });
        Pump(display.Dispatcher);
        if (open)
        {
            // wie ein Klick auf den Kreis der PV-Anlage
            typeof(EnergieQuad).GetField("_open", Priv).SetValue(q, "pv");
            typeof(EnergieQuad).GetMethod("BuildPanel", Priv).Invoke(q, null);
            typeof(EnergieQuad).GetMethod("Update", Priv).Invoke(q, null);
        }
        // Designwechsel bei laufender Anzeige muss dieselbe Zeichnung ergeben
        ((IThemeable)q).OnThemeChanged(light ? ThemeMode.dark : ThemeMode.light);
        ((IThemeable)q).OnThemeChanged(light ? ThemeMode.light : ThemeMode.dark);
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

        int shown = ((System.Collections.ICollection)typeof(EnergieQuad).GetField("_vbViews", Priv).GetValue(q)).Count;
        q.clear();
        q.onHaltPlugin();
        Console.WriteLine("geschrieben: " + outFile + " (" + shown + " Verbraucher)");
        return bad.Count == 0 ? 0 : 1;
    }
}
