// QC-Plugin 99-3 "Energiefluss mit Verbrauchern" – Anzeige im QuadClient (99-3_client.dll).
// Klasse laut interface.xml: nsclient="QcEnergie.Client.EnergieQuad"
//
// Vier Hauptkreise (PV oben, Speicher links, Netz rechts, Haus unten) und bis zu 17 Verbraucher als kleine Kreise,
// jeder an einem Anschlusspunkt. Hinter jedem Hauptkreis können bis zu 8 Einzelwerte stehen (z. B. Wechselrichter):
// eine Scheibe am Kreis zeigt die Anzahl, ein Klick auf den Kreis blendet sie ein. Gleiche Geometrie wie efSvg()/efPlace() im QC-Client (qc.html), Fläche 507 × 273.
//
// Idee und Vorbild: Plugin "Energiefluss" (180-1) von johannesbonn (KNX-User-Forum). Aus dessen Paket ist nichts
// übernommen; Zeichnung, Symbole und Code sind eigene.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using hs_client_framework;

namespace QcEnergie.Client
{
    public sealed class EnergieQuad : IQuadContent, IThemeable
    {
        // Maße im Quadranten: Inhalt beginnt unter der Titelzeile des QuadClients.
        const double Top0 = 40, W = 680, H = 274;
        const double DrawW = 507, FootY = 245, OX = (W - DrawW) / 2;   // Zeichnung mittig, Fußzeile über die ganze Breite
        const double MainR = 39;
        const double Ascent = 1.08;                                    // Grundlinie von Segoe UI in Schrifthöhen

        /// <summary>Ein Platz für einen Verbraucher.</summary>
        sealed class Slot
        {
            public double X, Y, R;
            public bool Named;
        }

        /// <summary>Eine Flusslinie; State: 0 = aus, 1 = läuft vorwärts, -1 = läuft rückwärts.</summary>
        sealed class Flow
        {
            public Path Line;
            public Brush Color;
            public bool Idle;          // ohne Fluss als dünner Strich sichtbar (Verbraucher) statt unsichtbar
            public int State = 99;
        }

        sealed class VbView
        {
            public Verbraucher Vb;
            public Flow Flow;
            public TextBlock Value;
        }

        int _id;
        string _title = "";
        EnergieSettings _set = new EnergieSettings();
        Canvas _display, _root;
        bool _dark = true, _german = true, _halted, _updatePending;

        readonly Dictionary<int, double> _vals = new Dictionary<int, double>();
        readonly List<int> _tags = new List<int>();
        CQuadEnvironment.DelegateOnTagChanged _onTag;

        // Teile der aufgebauten Zeichnung, die Update() ändert
        readonly Dictionary<string, TextBlock> _txt = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, Flow> _flows = new Dictionary<string, Flow>();
        readonly List<VbView> _vbViews = new List<VbView>();
        FrameworkElement _live;

        // Einblendung der Einzelwerte eines Hauptkreises ("" = zu)
        string _open = "";
        Canvas _panel;
        readonly List<KeyValuePair<Teil, TextBlock>> _panelRows = new List<KeyValuePair<Teil, TextBlock>>();
        TextBlock _panelSum;
        readonly Dictionary<string, string> _titles = new Dictionary<string, string>();
        readonly Dictionary<string, Brush> _colors = new Dictionary<string, Brush>();

        // ---- IQuadContent ----

        int IQuadContent._id { get { return _id; } set { _id = value; } }
        string IQuadContent._title { get { return _title; } set { _title = value ?? ""; } }

        public void init(int _id, string _title)
        {
            this._id = _id;
            this._title = _title ?? "";
            _german = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en";
        }

        public void onProjectData(byte[] _data)
        {
            try { _set = EnergieSettings.Parse(_data); }
            catch (Exception ex) { Log("Einstellungen nicht lesbar: " + ex.Message); }
        }

        public void onUserDefData(byte[] _data) { }
        public void onDesignData(byte[] _data) { }
        public void onBroadcast(string _key, string _value) { }
        public void onAbortAction() { }
        public void onSetConfigState(bool _state) { }

        public void run(Canvas _display)
        {
            this._display = _display;
            _halted = false;
            RegisterTags();
        }

        public void show(int _pos)
        {
            if (_display == null) return;
            try
            {
                if (_root == null)
                {
                    _root = new Canvas();
                    _root.Width = W;
                    _root.Height = H;
                    _root.ClipToBounds = true;
                    Canvas.SetLeft(_root, 0);
                    Canvas.SetTop(_root, Top0);
                    Build();
                }
                if (!_display.Children.Contains(_root)) _display.Children.Add(_root);
                Update();
            }
            catch (Exception ex) { Log("show: " + ex); }
        }

        public void clear()
        {
            try
            {
                if (_display != null && _root != null) _display.Children.Remove(_root);
                Drop();
                _root = null;
            }
            catch (Exception ex) { Log("clear: " + ex.Message); }
        }

        public void onHaltPlugin()
        {
            _halted = true;
            ReleaseTags();
        }

        // ---- IThemeable ----

        public void OnThemeChanged(ThemeMode mode)
        {
            bool dark = mode != ThemeMode.light;
            if (dark == _dark) return;
            _dark = dark;
            if (_root == null) return;
            try { Build(); Update(); }
            catch (Exception ex) { Log("Design: " + ex.Message); }
        }

        // ---- KOs ----

        void RegisterTags()
        {
            if (_onTag == null) _onTag = OnTag;
            List<int> want = new List<int>();
            foreach (string k in EnergieSettings.KoIds) want.Add(_set.Kos[k]);
            foreach (Verbraucher v in _set.Vbs) want.Add(v.Tag);
            foreach (Teil q in _set.Teile) want.Add(q.Tag);
            foreach (int tag in want)
            {
                if (tag == 0 || _tags.Contains(tag)) continue;
                try
                {
                    CQuadEnvironment._item.registerTag(tag, _onTag);
                    _tags.Add(tag);
                }
                catch (Exception ex) { Log("KO " + tag + " nicht angemeldet: " + ex.Message); }
            }
        }

        void ReleaseTags()
        {
            foreach (int tag in _tags)
            {
                try { CQuadEnvironment._item.releaseTag(tag, _onTag); } catch { }
            }
            _tags.Clear();
        }

        void OnTag(int _tagId, double _numValue, string _textValue)
        {
            Dispatcher d = _display != null ? _display.Dispatcher : null;
            if (d != null && !d.CheckAccess())
            {
                d.BeginInvoke(new Action(delegate { OnTag(_tagId, _numValue, _textValue); }));
                return;
            }
            if (_halted) return;
            _vals[_tagId] = _numValue;
            if (_root == null || _updatePending) return;
            _updatePending = true;                         // viele Werte kurz nacheinander: nur einmal neu beschriften
            _root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                _updatePending = false;
                try { if (_root != null) Update(); }
                catch (Exception ex) { Log("Anzeige: " + ex.Message); }
            }));
        }

        bool Has(string key) { return _set.Kos[key] != 0; }

        double? Val(int tag)
        {
            double v;
            if (tag != 0 && _vals.TryGetValue(tag, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return v;
            return null;
        }

        double? Val(string key) { return Val(_set.Kos[key]); }

        // ---- Farben ----

        static Brush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static Brush Rgb(int rgb) { return Frozen(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)); }

        // Dunkles Design: Flächen wie im QuadClient (Kachel #1A1A1A, Leiste #1E1E1E, Fuge fast schwarz).
        static readonly Brush BInkD = Rgb(0xE6E6E6), BInkL = Rgb(0x3C3C3C);
        static readonly Brush BDimD = Rgb(0xA4A4A4), BDimL = Rgb(0x747474);
        static readonly Brush BRimD = Rgb(0x444648), BRimL = Rgb(0xB9BCC0);
        static readonly Brush BNodeD = Rgb(0x2A2C2E), BNodeL = Rgb(0xFAFAFA);
        static readonly Brush BPanelD = Rgb(0x1A1A1A), BPanelL = Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        static readonly Brush BFootD = Rgb(0x1E1E1E), BFootL = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        static readonly Brush BGapD = Rgb(0x010101);
        static readonly Brush BPv = Rgb(0xF5A623), BBat = Rgb(0x3FBF8F), BHaus = Rgb(0x4A9AF5);
        static readonly Brush BVbD = Rgb(0xB08AF0), BVbL = Rgb(0x7A4FD0);
        static readonly Brush BNetzD = Rgb(0xC8CCD0), BNetzL = Rgb(0x55595E);

        Brush Ink { get { return _dark ? BInkD : BInkL; } }
        Brush Dim { get { return _dark ? BDimD : BDimL; } }
        Brush Rim { get { return _dark ? BRimD : BRimL; } }
        Brush NodeFill { get { return _dark ? BNodeD : BNodeL; } }
        Brush VbColor { get { return _dark ? BVbD : BVbL; } }
        Brush NetzColor { get { return _dark ? BNetzD : BNetzL; } }

        string L(string de, string en) { return _german ? de : en; }

        static string Num(double? v, int dec)
        {
            return v == null ? "---" : v.Value.ToString("F" + dec, CultureInfo.InvariantCulture);
        }

        // ---- Zeichnung aufbauen ----

        void Drop()
        {
            foreach (Flow f in _flows.Values) Animate(f.Line, 0);
            foreach (VbView v in _vbViews) Animate(v.Flow.Line, 0);
            if (_live != null) _live.BeginAnimation(UIElement.OpacityProperty, null);
            _txt.Clear();
            _flows.Clear();
            _vbViews.Clear();
            _footRuns.Clear();
            _live = null;
            _panel = null;
            _panelRows.Clear();
            _panelSum = null;
            if (_root != null) _root.Children.Clear();
        }

        void Add(UIElement e, double x, double y, int z)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            Panel.SetZIndex(e, z);
            _root.Children.Add(e);
        }

        void Rect(double x, double y, double w, double h, Brush fill)
        {
            Rectangle r = new Rectangle();
            r.Width = w;
            r.Height = h;
            r.Fill = fill;
            r.IsHitTestVisible = false;
            Add(r, x, y, 0);
        }

        /// <summary>Text mit der Mitte bei x und der Grundlinie bei y (Koordinaten der Zeichnung).</summary>
        TextBlock Text(string s, double x, double y, double width, double size, Brush brush, bool bold, int z)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = brush;
            t.Width = width;
            t.TextAlignment = TextAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.IsHitTestVisible = false;
            Add(t, OX + x - width / 2, y - size * Ascent, z);
            return t;
        }

        void Circle(double x, double y, double r, int z)
        {
            Ellipse e = new Ellipse();
            e.Width = e.Height = 2 * r;
            e.Fill = NodeFill;
            e.Stroke = Rim;
            e.StrokeThickness = 1;
            e.IsHitTestVisible = false;
            Add(e, OX + x - r, y - r, z);
        }

        Flow Line(double x1, double y1, double x2, double y2, Brush color, bool idle, int z)
        {
            Path p = new Path();
            p.Data = new LineGeometry(new Point(OX + x1, y1), new Point(OX + x2, y2));
            p.IsHitTestVisible = false;
            Add(p, 0, 0, z);
            Flow f = new Flow();
            f.Line = p;
            f.Color = color;
            f.Idle = idle;
            return f;
        }

        /// <summary>Lässt die Striche einer Linie laufen (dir = 1 zum Ende, -1 zum Anfang, 0 = anhalten).</summary>
        static void Animate(Path p, int dir)
        {
            if (dir == 0) { p.BeginAnimation(Shape.StrokeDashOffsetProperty, null); return; }
            double period = 9 / 1.7;                           // Strich 5 + Lücke 4, in Einheiten der Strichstärke
            DoubleAnimation a = new DoubleAnimation(0, dir > 0 ? -period : period, new Duration(TimeSpan.FromSeconds(1)));
            a.RepeatBehavior = RepeatBehavior.Forever;
            p.BeginAnimation(Shape.StrokeDashOffsetProperty, a);
        }

        void SetFlow(Flow f, int state)
        {
            if (f.State == state) return;
            f.State = state;
            Path p = f.Line;
            if (state == 0)
            {
                Animate(p, 0);
                p.Visibility = f.Idle ? Visibility.Visible : Visibility.Collapsed;
                p.Stroke = Rim;
                p.StrokeThickness = 1;
                p.StrokeDashArray = null;
                Panel.SetZIndex(p, 1);                         // ruhende Striche zuunterst
                return;
            }
            p.Visibility = Visibility.Visible;
            p.Stroke = f.Color;
            p.StrokeThickness = 1.7;
            p.StrokeDashArray = new DoubleCollection(new double[] { 5 / 1.7, 4 / 1.7 });
            Panel.SetZIndex(p, 2);
            Animate(p, state);
        }

        static readonly Brush BHit = Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00));
        static readonly Brush BBadgeInk = Rgb(0x16181A);

        static void Touched()
        {
            try { CQuadEnvironment._item.resetMyTouchTimeout(); } catch { }
        }

        void MainNode(string key, double x, double y, Brush color, string title)
        {
            _titles[key] = title;
            _colors[key] = color;
            int parts = _set.ShownTeile(key).Count;
            if (parts > 0)
            {
                // Scheibe mit der Anzahl der Einzelwerte; ein Klick auf den Kreis blendet sie ein
                Ellipse badge = new Ellipse();
                badge.Width = badge.Height = 14;
                badge.Fill = color;
                badge.IsHitTestVisible = false;
                Add(badge, OX + x + 21, y - 35, 6);
                Text(parts.ToString(CultureInfo.InvariantCulture), x + 28, y - 24.6, 14, 9.5, BBadgeInk, true, 6);
                Ellipse hit = new Ellipse();
                hit.Width = hit.Height = 2 * MainR + 8;
                hit.Fill = BHit;
                hit.MouseLeftButtonDown += delegate(object s, System.Windows.Input.MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    Touched();
                    _open = key;
                    try { BuildPanel(); Update(); }
                    catch (Exception ex) { Log("Einzelwerte: " + ex.Message); }
                };
                Add(hit, OX + x - MainR - 4, y - MainR - 4, 7);
            }
            Circle(x, y, MainR, 4);
            Add(Symbole.Get(key, 15), OX + x - 7.5, y - 35, 5);
            Text(title.ToUpperInvariant(), x, y - 10, 70, 8.5, Dim, true, 5);
            _txt[key] = Text("---", x, y + 8, 74, 17, color, false, 5);
            _txt[key + ".unit"] = Text("W", x, y + 19, 70, 8.5, Dim, false, 5);
            _txt[key + ".sub"] = Text("", x, y + 30, 60, 8.5, Dim, false, 5);
        }

        static readonly Dictionary<string, Point> Anchor = new Dictionary<string, Point>
        {
            { "pv", new Point(253, 44) }, { "bat", new Point(95, 120) }, { "netz", new Point(412, 120) }, { "haus", new Point(253, 197) }
        };

        static List<Slot> Row(double y, double r, bool named, bool right)
        {
            List<Slot> l = new List<Slot>();
            for (int i = 0; i < 5; i++)
            {
                Slot s = new Slot();
                s.X = right ? 312 + 40 * i : 194 - 40 * i;
                s.Y = y; s.R = r; s.Named = named;
                l.Add(s);
            }
            return l;
        }

        static List<Slot> Zip(List<Slot> a, List<Slot> b)
        {
            List<Slot> l = new List<Slot>();
            for (int i = 0; i < a.Count; i++) { l.Add(a[i]); l.Add(b[i]); }
            return l;
        }

        /// <summary>Verteilt die Verbraucher auf feste Plätze (wie efPlace im QC-Client).</summary>
        static List<KeyValuePair<Verbraucher, Slot>> Place(List<Verbraucher> vbs)
        {
            int haus = 0;
            foreach (Verbraucher v in vbs) if (v.An == "haus") haus++;
            bool two = haus > 10;
            List<Slot> topL = Row(24, 13, true, false), topR = Row(24, 13, true, true);
            Dictionary<string, List<Slot>> order = new Dictionary<string, List<Slot>>();
            List<Slot> bottom = new List<Slot>();
            if (two)
            {
                bottom.AddRange(Zip(Row(183, 10, false, true), Row(183, 10, false, false)));
                bottom.AddRange(Zip(Row(218, 10, false, true), Row(218, 10, false, false)));
            }
            else bottom.AddRange(Zip(Row(199, 13, true, true), Row(199, 13, true, false)));
            order["haus"] = bottom;
            order["pv"] = Zip(topR, topL);                     // oben, von innen nach außen
            List<Slot> batOrder = new List<Slot>(topL), netzOrder = new List<Slot>(topR);
            batOrder.Reverse();                                // oben, von links außen
            netzOrder.Reverse();                               // oben, von rechts außen
            order["bat"] = batOrder;
            order["netz"] = netzOrder;
            List<Slot> all = new List<Slot>(order["haus"]);
            all.AddRange(order["pv"]);

            List<Slot> used = new List<Slot>();
            List<KeyValuePair<Verbraucher, Slot>> result = new List<KeyValuePair<Verbraucher, Slot>>();
            foreach (string an in new string[] { "bat", "netz", "pv", "haus" })
                foreach (Verbraucher v in vbs)
                {
                    if (v.An != an) continue;
                    Slot s = order[an].Find(delegate(Slot q) { return !used.Contains(q); });
                    if (s == null) s = all.Find(delegate(Slot q) { return !used.Contains(q); });
                    if (s == null) continue;
                    used.Add(s);
                    result.Add(new KeyValuePair<Verbraucher, Slot>(v, s));
                }
            return result;
        }

        void Build()
        {
            Drop();
            if (_root == null) return;
            if (_dark) Rect(0, 0, W, H, BGapD);
            Rect(0, 0, W, FootY - 2, _dark ? BPanelD : BPanelL);
            Rect(0, FootY, W, H - FootY, _dark ? BFootD : BFootL);
            if (!_dark) Rect(0, FootY, W, 1, Rim);

            if (!_set.HasAny)
            {
                Text(L("Warte auf Daten – dem Plugin ist kein KO zugewiesen.", "Waiting for data – no communication object assigned."),
                    DrawW / 2, 125, DrawW, 14, Ink, false, 5);
                return;
            }

            // Verbraucher: Linie vom Anschlusspunkt (liegt unter den Kreisen), Kreis mit Symbol, Leistung, Name
            foreach (KeyValuePair<Verbraucher, Slot> p in Place(_set.ShownVbs()))
            {
                Verbraucher v = p.Key;
                Slot s = p.Value;
                Point a = Anchor[v.An];
                VbView view = new VbView();
                view.Vb = v;
                view.Flow = Line(a.X, a.Y, s.X, s.Y, VbColor, true, 1);
                Circle(s.X, s.Y, s.R, 4);
                double size = 24 * s.R / 18.5;
                Add(Symbole.Get(v.ShownIcon, size), OX + s.X - size / 2, s.Y - size / 2, 5);
                view.Value = Text("--- W", s.X, s.Y + s.R + 10, 44, 9, VbColor, true, 5);
                if (s.Named) Text(v.ShownName(_german), s.X, s.Y + s.R + 19.5, 40, 7.5, Dim, false, 5);
                _vbViews.Add(view);
            }

            _flows["pvHaus"] = Line(253, 84, 253, 157, BPv, false, 3);
            _flows["batHaus"] = Line(132, 132, 216, 184, BBat, false, 3);
            _flows["netzHaus"] = Line(375, 132, 290, 184, NetzColor, false, 3);
            _flows["pvNetz"] = Line(288, 62, 377, 103, BPv, false, 3);

            MainNode("pv", 253, 44, BPv, L("PV-Anlage", "PV system"));
            MainNode("bat", 95, 120, BBat, L("Speicher", "Battery"));
            MainNode("netz", 412, 120, NetzColor, L("Netz", "Grid"));
            MainNode("haus", 253, 197, BHaus, L("Haus", "House"));

            BuildFoot();
            BuildPanel();
        }

        /// <summary>Feld mit den Einzelwerten des geöffneten Kreises; der Rest wird abgedunkelt, ein Klick schließt es.</summary>
        void BuildPanel()
        {
            if (_panel != null) _root.Children.Remove(_panel);
            _panel = null;
            _panelRows.Clear();
            _panelSum = null;
            List<Teil> parts = _open.Length > 0 && _titles.ContainsKey(_open) ? _set.ShownTeile(_open) : new List<Teil>();
            if (parts.Count == 0) { _open = ""; return; }

            const double pw = 211, row = 14;
            double ph = 40 + row * parts.Count, px = OX + (DrawW - pw) / 2, py = Math.Round((FootY - 2 - ph) / 2);
            Canvas c = new Canvas();
            c.Width = W;
            c.Height = FootY - 2;
            Rectangle shade = new Rectangle();
            shade.Width = W;
            shade.Height = FootY - 2;
            shade.Fill = Frozen(_dark ? Color.FromArgb(0x99, 0x10, 0x10, 0x10) : Color.FromArgb(0x99, 0xE4, 0xE6, 0xE8));
            c.Children.Add(shade);
            Rectangle box = new Rectangle();
            box.Width = pw;
            box.Height = ph;
            box.RadiusX = box.RadiusY = 6;
            box.Fill = NodeFill;
            box.Stroke = Rim;
            box.IsHitTestVisible = false;
            Canvas.SetLeft(box, px);
            Canvas.SetTop(box, py);
            c.Children.Add(box);
            c.Children.Add(PanelText(_titles[_open].ToUpperInvariant() + " – " + L("EINZELWERTE", "SINGLE VALUES"), px + 10, py + 13, pw - 36, 8.5, Dim, true, TextAlignment.Left));
            Path x = new Path();
            x.Data = Geometry.Parse("M0,0 L6,6 M6,0 L0,6");
            x.Stroke = Dim;
            x.StrokeThickness = 1;
            x.IsHitTestVisible = false;
            Canvas.SetLeft(x, px + pw - 16);
            Canvas.SetTop(x, py + 6);
            c.Children.Add(x);
            for (int i = 0; i < parts.Count; i++)
            {
                double by = py + 29 + row * i;
                string name = parts[i].Name.Length > 0 ? parts[i].Name : Teil.DefaultName(_open, i + 1, _german);
                c.Children.Add(PanelText(name, px + 10, by, pw - 80, 10, Ink, false, TextAlignment.Left));
                TextBlock v = PanelText("--- W", px + pw - 80, by, 70, 10, _colors[_open], true, TextAlignment.Right);
                c.Children.Add(v);
                _panelRows.Add(new KeyValuePair<Teil, TextBlock>(parts[i], v));
            }
            Rectangle sep = new Rectangle();
            sep.Width = pw - 20;
            sep.Height = 1;
            sep.Fill = Rim;
            sep.IsHitTestVisible = false;
            Canvas.SetLeft(sep, px + 10);
            Canvas.SetTop(sep, py + 20 + row * parts.Count);
            c.Children.Add(sep);
            double sy = py + 33 + row * parts.Count;
            c.Children.Add(PanelText(L("Summe", "Total"), px + 10, sy, pw - 80, 10, Ink, false, TextAlignment.Left));
            _panelSum = PanelText("--- W", px + pw - 80, sy, 70, 10, Ink, true, TextAlignment.Right);
            c.Children.Add(_panelSum);
            c.MouseLeftButtonDown += delegate(object s, System.Windows.Input.MouseButtonEventArgs e)
            {
                e.Handled = true;
                Touched();
                _open = "";
                BuildPanel();
            };
            _panel = c;
            Add(c, 0, 0, 20);
        }

        /// <summary>Text im Einblendfeld: linke Kante x, Grundlinie y (Koordinaten des Quadranten).</summary>
        static TextBlock PanelText(string s, double x, double y, double width, double size, Brush brush, bool bold, TextAlignment align)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = brush;
            t.Width = width;
            t.TextAlignment = align;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.IsHitTestVisible = false;
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y - size * Ascent);
            return t;
        }

        static string Watt(double? w)
        {
            return w == null ? "--- W"
                : Math.Abs(w.Value) >= 10000 ? (w.Value / 1000).ToString("F1", CultureInfo.InvariantCulture) + " kW"
                : Math.Round(w.Value).ToString("F0", CultureInfo.InvariantCulture) + " W";
        }

        void BuildFoot()
        {
            string[] keys = { "evpct", "autpct", "einsp", "bezug" };
            string[] caps = { L("Eigenver.", "Self-cons."), L("Autarkie", "Self-suff."), L("Einspeisung", "Feed-in"), L("Netzbezug", "Grid import") };
            string[] units = { "%", "%", "kWh", "kWh" };
            Brush[] cols = { BPv, BBat, BHaus, NetzColor };
            double[] xs = { 26, 130, 230, 357 };
            bool first = true;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!Has(keys[i])) continue;
                if (!first) Rect(OX + xs[i] - 9, 253, 1, 12, Rim);
                first = false;
                TextBlock t = new TextBlock();
                t.IsHitTestVisible = false;
                Run cap = new Run(caps[i].ToUpperInvariant() + "  ");
                cap.FontSize = 8; cap.Foreground = Dim;
                Run val = new Run("---");
                val.FontSize = 11; val.FontWeight = FontWeights.SemiBold; val.Foreground = cols[i];
                Run unit = new Run(" " + units[i]);
                unit.FontSize = 8; unit.Foreground = Dim;
                t.Inlines.Add(cap); t.Inlines.Add(val); t.Inlines.Add(unit);
                Add(t, OX + xs[i], 263 - 11 * Ascent, 5);
                _footRuns[keys[i]] = val;
            }

            StackPanel live = new StackPanel();
            live.Orientation = Orientation.Horizontal;
            live.IsHitTestVisible = false;
            Ellipse dot = new Ellipse();
            dot.Width = dot.Height = 5;
            dot.Fill = BBat;
            dot.Margin = new Thickness(0, 3.5, 4, 0);
            TextBlock lt = new TextBlock();
            lt.Text = "Live"; lt.FontSize = 8.5; lt.FontWeight = FontWeights.SemiBold; lt.Foreground = BBat;
            live.Children.Add(dot);
            live.Children.Add(lt);
            live.Visibility = Visibility.Collapsed;
            Add(live, OX + 468, 263 - 8.5 * Ascent, 5);
            _live = live;
        }

        readonly Dictionary<string, Run> _footRuns = new Dictionary<string, Run>();

        // ---- Werte eintragen ----

        static void Set(TextBlock t, string s)
        {
            if (t != null && t.Text != s) t.Text = s;
        }

        void Update()
        {
            if (_root == null || _flows.Count == 0) return;
            double? pv = Val("pv"), bat = Val("bat"), haus = Val("haus"), netz = Val("netz"), bsv = Val("batstate");
            int bs = bsv != null && Math.Round(bsv.Value) == 1 ? 1 : bsv != null && Math.Round(bsv.Value) == 2 ? 2 : 0;

            Set(_txt["pv"], Num(pv, 0));
            Set(_txt["pv.sub"], Has("pvkwh") ? Num(Val("pvkwh"), 1) + " kWh" : "");
            Set(_txt["bat"], Num(bat == null ? bat : Math.Abs(bat.Value), 0));
            Set(_txt["bat.unit"], bs == 1 ? L("W lädt", "W charging") : bs == 2 ? L("W entlädt", "W discharging") : "W");
            Set(_txt["bat.sub"], Has("soc") ? Num(Val("soc"), 0) + " %" : "");
            Set(_txt["netz"], Num(netz == null ? netz : Math.Abs(netz.Value), 0));
            Set(_txt["netz.sub"], netz == null || netz.Value == 0 ? "" : netz.Value > 0 ? L("Einspeisung", "Feed-in") : L("Bezug", "Import"));
            Set(_txt["haus"], Num(haus, 0));
            Set(_txt["haus.sub"], Has("evkwh") ? Num(Val("evkwh"), 1) + " kWh" : "");

            // Linien nur, wenn etwas fließt: PV → Haus, Netz → Haus bei Bezug, PV → Netz bei Einspeisung,
            // Speicher ↔ Haus beim Laden (zum Speicher) und Entladen (zum Haus).
            SetFlow(_flows["pvHaus"], pv != null && pv.Value > 0 ? 1 : 0);
            SetFlow(_flows["netzHaus"], netz != null && netz.Value < 0 ? 1 : 0);
            SetFlow(_flows["pvNetz"], netz != null && netz.Value > 0 ? 1 : 0);
            SetFlow(_flows["batHaus"], bs == 2 ? 1 : bs == 1 ? -1 : 0);

            foreach (VbView v in _vbViews)
            {
                double? w = Val(v.Vb.Tag);
                Set(v.Value, Watt(w));
                SetFlow(v.Flow, w == null || Math.Round(w.Value) == 0 ? 0 : w.Value > 0 ? 1 : -1);   // negativ: zurück zum Anschlusspunkt
            }

            if (_panel != null)
            {
                double sum = 0;
                bool any = false;
                foreach (KeyValuePair<Teil, TextBlock> r in _panelRows)
                {
                    double? w = Val(r.Key.Tag);
                    Set(r.Value, Watt(w));
                    if (w != null) { sum += w.Value; any = true; }
                }
                Set(_panelSum, Watt(any ? sum : (double?)null));
            }

            foreach (KeyValuePair<string, Run> f in _footRuns)
            {
                string s = Num(Val(f.Key), f.Key == "einsp" || f.Key == "bezug" ? 1 : 0);
                if (f.Value.Text != s) f.Value.Text = s;
            }

            if (_live != null && _vals.Count > 0 && _live.Visibility != Visibility.Visible)
            {
                _live.Visibility = Visibility.Visible;
                DoubleAnimation a = new DoubleAnimation(1, 0.35, new Duration(TimeSpan.FromSeconds(1)));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                _live.BeginAnimation(UIElement.OpacityProperty, a);
            }
        }

        static void Log(string msg)
        {
            try { CLog.logError("Energiefluss " + EnergieSettings.PluginId + ": " + msg); } catch { }
        }
    }
}
