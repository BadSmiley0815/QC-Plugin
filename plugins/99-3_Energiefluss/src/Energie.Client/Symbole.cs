// Eigene Symbole des Energieflusses (keine Gira-Symbole, nichts aus dem Plugin 180-1): dieselben Zeichnungen wie
// EF_ICO / EF_VICO im QC-Client (qc.html), Zeichenfläche 24 × 24. Sie stehen hier als kleine SVG-Stücke
// (nur path, rect, circle) und werden beim ersten Gebrauch in WPF-Zeichnungen übersetzt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;

namespace QcEnergie.Client
{
    public static class Symbole
    {
        static readonly Dictionary<string, string> Svg = new Dictionary<string, string>
        {
            // Hauptkreise
            { "pv", "<circle cx='12' cy='12' r='5.2' fill='#f5a623'/><path d='M12 1.8v3M12 19.2v3M1.8 12h3M19.2 12h3M4.8 4.8l2.1 2.1M17.1 17.1l2.1 2.1M4.8 19.2l2.1-2.1M17.1 6.9l2.1-2.1' stroke='#f5a623' stroke-width='2.2' stroke-linecap='round'/>" },
            { "bat", "<rect x='9.5' y='1.8' width='5' height='2.6' rx='.8' fill='#9aa0a6'/><rect x='6.5' y='4' width='11' height='18' rx='2' fill='#57c26a'/><path d='M9 9.5h6M9 13h6M9 16.5h6' stroke='#1e3a24' stroke-width='1.3'/>" },
            { "haus", "<path d='M5 11.5h14V21H5z' fill='#e9e3d2'/><path d='M2.5 12.2L12 3.5l9.5 8.7z' fill='#e0563b'/><path d='M10.2 21v-5.5h3.6V21z' fill='#4a9af5'/>" },
            { "netz", "<path d='M13.5 1.5L5 13.3h5.6L9.4 22.5 19 10.4h-5.8z' fill='#f0803c'/>" },
            // Verbraucher
            { "wallbox", "<rect x='6' y='2.5' width='12' height='15' rx='2.5' fill='#4a9af5'/><path d='M12.8 5.5l-3 5h2.2l-.8 4 3.2-5.2h-2.2z' fill='#fff'/><path d='M9 17.5v2.2a2 2 0 0 0 2 2h4' stroke='#9aa0a6' stroke-width='1.6' fill='none' stroke-linecap='round'/>" },
            { "auto", "<path d='M3 16v-3.2l2.2-.8 1.8-4.2a2 2 0 0 1 1.8-1.2h6.4a2 2 0 0 1 1.8 1.2l1.8 4.2 2.2.8V16a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z' fill='#4a9af5'/><path d='M7.6 11.6l1.3-3h6.2l1.3 3z' fill='#cfe3fb'/><circle cx='7.5' cy='17' r='2.2' fill='#30343a'/><circle cx='16.5' cy='17' r='2.2' fill='#30343a'/>" },
            { "wasch", "<rect x='4' y='2.5' width='16' height='19' rx='2' fill='#c8ccd0'/><circle cx='12' cy='13.5' r='5' fill='#4a9af5' stroke='#6b7078' stroke-width='1.2'/><circle cx='7' cy='5.5' r='1' fill='#6b7078'/><path d='M10 5.5h7' stroke='#6b7078' stroke-width='1.2'/>" },
            { "spuel", "<rect x='4' y='2.5' width='16' height='19' rx='2' fill='#c8ccd0'/><path d='M4 7.5h16M8 5h3' stroke='#6b7078' stroke-width='1.2'/><circle cx='12' cy='14.5' r='4' fill='#fff' stroke='#4a9af5' stroke-width='1.4'/><circle cx='12' cy='14.5' r='1.6' fill='#4a9af5'/>" },
            { "trockner", "<rect x='4' y='2.5' width='16' height='19' rx='2' fill='#c8ccd0'/><circle cx='12' cy='13.5' r='5' fill='#f0803c' stroke='#6b7078' stroke-width='1.2'/><path d='M9.5 13.5c1-1.6 1.6 1.6 2.5 0s1.6 1.6 2.5 0' stroke='#fff' stroke-width='1.1' fill='none'/><circle cx='17' cy='5.5' r='1' fill='#6b7078'/><path d='M7 5.5h7' stroke='#6b7078' stroke-width='1.2'/>" },
            { "herd", "<rect x='4' y='3' width='16' height='18' rx='2' fill='#6b7078'/><circle cx='9' cy='8' r='2' fill='#e0563b'/><circle cx='15' cy='8' r='2' fill='#e0563b'/><rect x='7' y='13' width='10' height='6' rx='1' fill='#30343a'/>" },
            { "kuehl", "<rect x='6' y='2' width='12' height='20' rx='2' fill='#cfe3fb'/><path d='M6 9h12' stroke='#4a9af5' stroke-width='1.2'/><path d='M9 5v2M9 12v4' stroke='#4a9af5' stroke-width='1.4' stroke-linecap='round'/>" },
            { "wp", "<rect x='2.5' y='6' width='19' height='13' rx='2' fill='#c8ccd0'/><circle cx='9' cy='12.5' r='4.2' fill='#30343a'/><path d='M9 8.6v7.8M5.1 12.5h7.8' stroke='#9aa0a6' stroke-width='1.2'/><path d='M16 10h3M16 12.5h3M16 15h3' stroke='#6b7078' stroke-width='1.1'/>" },
            { "boiler", "<rect x='7' y='2' width='10' height='20' rx='4' fill='#e9e3d2'/><path d='M12 8c2 2.4 2.8 3.6 2.8 5a2.8 2.8 0 0 1 -5.6 0c0-1.4 .8-2.6 2.8-5z' fill='#e0563b'/>" },
            { "klima", "<rect x='2.5' y='5' width='19' height='8' rx='2' fill='#c8ccd0'/><path d='M5 10.5h14' stroke='#6b7078' stroke-width='1.1'/><path d='M8 16v3M12 16v4M16 16v3' stroke='#4a9af5' stroke-width='1.5' stroke-linecap='round'/>" },
            { "licht", "<path d='M12 2.5a6 6 0 0 0 -3.4 10.9c.6 .5 .9 1.1 .9 1.8V16h5v-.8c0-.7 .3-1.3 .9-1.8A6 6 0 0 0 12 2.5z' fill='#f5c542'/><path d='M9.8 18h4.4M10.4 20.5h3.2' stroke='#9aa0a6' stroke-width='1.5' stroke-linecap='round'/>" },
            { "steckdose", "<circle cx='12' cy='12' r='9' fill='#c8ccd0'/><circle cx='8.6' cy='12' r='1.5' fill='#30343a'/><circle cx='15.4' cy='12' r='1.5' fill='#30343a'/><path d='M12 3.6v2M12 18.4v2' stroke='#6b7078' stroke-width='1.6'/>" },
            { "pool", "<path d='M2.5 14c1.6-1.6 3.2-1.6 4.8 0s3.2 1.6 4.7 0 3.2-1.6 4.8 0 3.1 1.6 4.7 0M2.5 19c1.6-1.6 3.2-1.6 4.8 0s3.2 1.6 4.7 0 3.2-1.6 4.8 0 3.1 1.6 4.7 0' stroke='#4a9af5' stroke-width='1.8' fill='none' stroke-linecap='round'/><path d='M8 11.5V5a2 2 0 0 1 4 0M14 11.5V5a2 2 0 0 1 4 0' stroke='#9aa0a6' stroke-width='1.5' fill='none'/>" },
            { "tv", "<rect x='2.5' y='4.5' width='19' height='12.5' rx='1.5' fill='#30343a'/><rect x='4.2' y='6.2' width='15.6' height='9.1' fill='#4a9af5'/><path d='M8 20.5h8M12 17v3.5' stroke='#9aa0a6' stroke-width='1.6' stroke-linecap='round'/>" },
            { "pc", "<rect x='3' y='4' width='18' height='12' rx='1.5' fill='#6b7078'/><rect x='4.6' y='5.6' width='14.8' height='8.8' fill='#cfe3fb'/><path d='M1.5 19.5h21' stroke='#9aa0a6' stroke-width='2' stroke-linecap='round'/>" },
            { "sonst", "<path d='M9 2.5v5M15 2.5v5M12 17v4.5' stroke='#9aa0a6' stroke-width='2' stroke-linecap='round'/><path d='M6 7.5h12V11a6 6 0 0 1 -12 0z' fill='#57c26a'/>" }
        };

        static readonly Dictionary<string, Drawing> Cache = new Dictionary<string, Drawing>();

        public static IEnumerable<string> Names { get { return Svg.Keys; } }

        static double Num(XmlReader x, string name, double fallback)
        {
            string s = x.GetAttribute(name);
            double v;
            return s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        static Brush Paint(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "none") return null;
            SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
            b.Freeze();
            return b;
        }

        /// <summary>Übersetzt ein Symbol; wirft bei einem Fehler in der Zeichnung.</summary>
        public static Drawing Build(string name)
        {
            DrawingGroup g = new DrawingGroup();
            // unsichtbare Fläche 24 × 24, damit jedes Symbol dieselben Maße hat
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            using (XmlReader x = XmlReader.Create(new StringReader("<g>" + Svg[name] + "</g>")))
            {
                while (x.Read())
                {
                    if (x.NodeType != XmlNodeType.Element || x.Name == "g") continue;
                    Geometry geo;
                    if (x.Name == "path") geo = Geometry.Parse(x.GetAttribute("d"));
                    else if (x.Name == "rect")
                    {
                        double rx = Num(x, "rx", 0);
                        geo = new RectangleGeometry(new Rect(Num(x, "x", 0), Num(x, "y", 0), Num(x, "width", 0), Num(x, "height", 0)), rx, rx);
                    }
                    else if (x.Name == "circle")
                    {
                        double r = Num(x, "r", 0);
                        geo = new EllipseGeometry(new Point(Num(x, "cx", 0), Num(x, "cy", 0)), r, r);
                    }
                    else throw new FormatException("unbekanntes Element <" + x.Name + ">");

                    string stroke = x.GetAttribute("stroke"), fill = x.GetAttribute("fill");
                    Pen pen = null;
                    Brush sb = Paint(stroke);
                    if (sb != null)
                    {
                        pen = new Pen(sb, Num(x, "stroke-width", 1));
                        if (x.GetAttribute("stroke-linecap") == "round") pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
                        pen.Freeze();
                    }
                    Brush fb = fill == null ? (pen == null ? Brushes.Black : null) : Paint(fill);   // ohne Angabe: Linien bleiben ungefüllt
                    geo.Freeze();
                    g.Children.Add(new GeometryDrawing(fb, pen, geo));
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>Symbol als Bild der Kantenlänge size; unbekannte Namen zeigen die Steckdose.</summary>
        public static FrameworkElement Get(string name, double size)
        {
            if (name == null || !Svg.ContainsKey(name)) name = "steckdose";
            Drawing d;
            if (!Cache.TryGetValue(name, out d))
            {
                try { d = Build(name); }
                catch { d = null; }
                Cache[name] = d;
            }
            Image img = new Image();
            img.Width = size;
            img.Height = size;
            img.Stretch = Stretch.Fill;
            img.IsHitTestVisible = false;
            if (d != null)
            {
                DrawingImage di = new DrawingImage(d);
                di.Freeze();
                img.Source = di;
            }
            return img;
        }

        /// <summary>Für die Tests: Namen der Symbole, die sich nicht übersetzen lassen.</summary>
        public static List<string> Check()
        {
            List<string> bad = new List<string>();
            foreach (string n in Svg.Keys)
            {
                try { Build(n); }
                catch (Exception ex) { bad.Add(n + ": " + ex.Message); }
            }
            return bad;
        }
    }
}
