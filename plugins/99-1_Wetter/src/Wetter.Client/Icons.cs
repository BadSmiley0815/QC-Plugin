// Eigene, im Code gezeichnete Symbole (keine Gira-Symbole): Wetter, Windpfeil, kleine Zeilensymbole.
// Die Wettersymbole entsprechen wxIcon() im QC-Client (qc.html), Zeichenfläche 64 × 64.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace QcWetter.Client
{
    public static class Icons
    {
        static readonly Brush SunBrush = Frozen(Color.FromRgb(0xF2, 0xB0, 0x1E));
        static readonly Brush MoonBrush = Frozen(Color.FromRgb(0xC9, 0xD3, 0xE0));
        static readonly Brush RainBrush = Frozen(Color.FromRgb(0x5A, 0xA9, 0xEE));
        static readonly Brush SnowBrush = Frozen(Color.FromRgb(0x8F, 0xB4, 0xD8));

        const string CloudPath = "M20,50 h26 a9,9 0 0 0 1.5,-17.9 A13,13 0 0 0 22.5,29.6 A10.2,10.2 0 0 0 20,50 z";
        const string MoonPath = "M3,-12 a12,12 0 1 0 9,19 A10,10 0 0 1 3,-12 z";

        public static Brush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static Path Stroke(string data, Brush brush, double thickness)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(data);
            p.Stroke = brush;
            p.StrokeThickness = thickness;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.StrokeLineJoin = PenLineJoin.Round;
            return p;
        }

        static Viewbox Box(Canvas c, double size)
        {
            Viewbox v = new Viewbox();
            v.Width = size;
            v.Height = size;
            v.Stretch = Stretch.Uniform;
            v.Child = c;
            v.IsHitTestVisible = false;
            return v;
        }

        static Canvas Sun(double x, double y, double r)
        {
            Canvas g = new Canvas();
            Ellipse e = new Ellipse();
            e.Width = e.Height = 2 * r;
            e.Stroke = SunBrush;
            e.StrokeThickness = 2.4;
            Canvas.SetLeft(e, x - r);
            Canvas.SetTop(e, y - r);
            g.Children.Add(e);
            for (int a = 0; a < 360; a += 45)
            {
                double rad = a * Math.PI / 180;
                Line l = new Line();
                l.X1 = x + Math.Cos(rad) * (r + 4); l.Y1 = y + Math.Sin(rad) * (r + 4);
                l.X2 = x + Math.Cos(rad) * (r + 8); l.Y2 = y + Math.Sin(rad) * (r + 8);
                l.Stroke = SunBrush;
                l.StrokeThickness = 2.4;
                l.StrokeStartLineCap = l.StrokeEndLineCap = PenLineCap.Round;
                g.Children.Add(l);
            }
            return g;
        }

        static Canvas Moon(double x, double y, double s)
        {
            Canvas g = new Canvas();
            Path p = Stroke(MoonPath, MoonBrush, 2.4 / s);
            TransformGroup t = new TransformGroup();
            t.Children.Add(new ScaleTransform(s, s));
            t.Children.Add(new TranslateTransform(x, y));
            p.RenderTransform = t;
            g.Children.Add(p);
            return g;
        }

        /// <summary>Wettersymbol für einen WMO-Code. ink = Strichfarbe der Wolke (Textfarbe).</summary>
        public static FrameworkElement Weather(double? code, bool day, Brush ink, double size)
        {
            Sky k = Forecast.SkyOf(code);
            Canvas c = new Canvas();
            c.Width = c.Height = 64;

            if (k == Sky.Sun)
            {
                c.Children.Add(day ? Sun(32, 32, 11) : Moon(32, 32, 1));
                return Box(c, size);
            }

            double dy = k == Sky.Part ? 4 : k == Sky.Cloud ? -2 : -8;
            Geometry cloud = Geometry.Parse(CloudPath).Clone();
            cloud.Transform = new TranslateTransform(0, dy);

            if (k == Sky.Part)
            {
                // Sonne/Mond hinter der Wolke: der von der Wolke verdeckte Teil wird ausgeschnitten.
                Canvas sky = day ? Sun(24, 22, 8) : Moon(24, 22, 8.0 / 11);
                sky.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, 64, 64)), cloud);
                c.Children.Add(sky);
            }

            Path cp = new Path();
            cp.Data = cloud;
            cp.Stroke = ink;
            cp.StrokeThickness = 2.4;
            cp.StrokeLineJoin = PenLineJoin.Round;
            c.Children.Add(cp);

            switch (k)
            {
                case Sky.Fog: c.Children.Add(Stroke("M14,50 h36 M18,56 h28", ink, 2.4)); break;
                case Sky.Drizzle: c.Children.Add(Stroke("M24,50 v2 M32,52 v2 M40,50 v2 M28,57 v2 M36,57 v2", RainBrush, 2.4)); break;
                case Sky.Rain: c.Children.Add(Stroke("M24,48 l-3,7 M33,48 l-3,7 M42,48 l-3,7", RainBrush, 2.4)); break;
                case Sky.Snow: c.Children.Add(Stroke("M24,52 h5 M26.5,49.5 v5 M36,52 h5 M38.5,49.5 v5 M30,58 h5 M32.5,55.5 v5", SnowBrush, 2.4)); break;
                case Sky.Storm: c.Children.Add(Stroke("M33,46 l-5,8 h7 l-5,8", SunBrush, 2.4)); break;
            }
            return Box(c, size);
        }

        /// <summary>Pfeil, der zeigt, wohin der Wind weht (deg = Richtung, aus der er kommt; 0 = Nord).</summary>
        public static FrameworkElement WindArrow(double deg, Brush ink, double size)
        {
            Canvas c = new Canvas();
            c.Width = c.Height = 24;
            Path p = new Path();
            p.Data = Geometry.Parse("M12,3 l5,15 -5,-3 -5,3 z");
            p.Fill = ink;
            p.RenderTransform = new RotateTransform((deg + 180) % 360, 12, 12);
            c.Children.Add(p);
            return Box(c, size);
        }

        public enum Glyph { Wind, Drop, Umbrella, Light, Gauge }

        /// <summary>Kleines Zeilensymbol, Zeichenfläche 16 × 16.</summary>
        public static FrameworkElement Small(Glyph g, Brush ink, double size)
        {
            Canvas c = new Canvas();
            c.Width = c.Height = 16;
            switch (g)
            {
                case Glyph.Wind: c.Children.Add(Stroke("M1.5,5 h7.5 a2,2 0 1 0 -2,-2 M1.5,8.5 h11 a2,2 0 1 1 -2,2 M1.5,12 h5", ink, 1.4)); break;
                case Glyph.Drop: c.Children.Add(Stroke("M8,1.8 C5.2,5.6 3.6,8 3.6,10.3 a4.4,4.4 0 0 0 8.8,0 C12.4,8 10.8,5.6 8,1.8 z", ink, 1.4)); break;
                case Glyph.Umbrella: c.Children.Add(Stroke("M1.5,8.5 a6.5,6.5 0 0 1 13,0 z M8,8.5 v4.3 a1.4,1.4 0 0 1 -2.8,0 M8,2 v-1", ink, 1.4)); break;
                case Glyph.Light: c.Children.Add(Stroke("M8,5.2 a2.8,2.8 0 1 0 0.01,0 z M8,1 v1.6 M8,13.4 v1.6 M1,8 h1.6 M13.4,8 h1.6 M3.1,3.1 l1.1,1.1 M11.8,11.8 l1.1,1.1 M3.1,12.9 l1.1,-1.1 M11.8,4.2 l1.1,-1.1", ink, 1.3)); break;
                case Glyph.Gauge: c.Children.Add(Stroke("M2.2,12.5 a6.3,6.3 0 1 1 11.6,0 M8,10 l2.8,-4.2", ink, 1.4)); break;
            }
            return Box(c, size);
        }
    }
}
