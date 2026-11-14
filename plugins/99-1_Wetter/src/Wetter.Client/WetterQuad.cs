// QC-Plugin 99-1 "Wetter" – Anzeige im QuadClient (99-1_client.dll).
// Klasse laut interface.xml: nsclient="QcWetter.Client.WetterQuad"
//
// Vorhersage: Open-Meteo. "Jetzt": Werte der eigenen Wetterstation über KOs; fehlt ein KO, gilt Open-Meteo.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using hs_client_framework;
using Path = System.Windows.Shapes.Path;

namespace QcWetter.Client
{
    public sealed class WetterQuad : IQuadContent, IThemeable
    {
        // Maße im Quadranten: Inhalt beginnt unter der Titelzeile des QuadClients.
        const double Top0 = 40, W = 680, ContentH = 237, BarH = 37;
        const double NowW = 200, DayW = 120;
        const int DaysShown = 4;

        /// <summary>Nur für die Vorschau/Tests: keine Abfragen ins Internet.</summary>
        internal static bool DisableNetwork = false;

        int _id;
        string _title = "";
        WetterSettings _set = new WetterSettings();
        Canvas _display, _root;
        bool _dark = true, _german = true;
        CultureInfo _culture = CultureInfo.GetCultureInfo("de-DE");

        Forecast _fc;
        DateTime _at = DateTime.MinValue;      // Stand der angezeigten Vorhersage
        DateTime _lastTry = DateTime.MinValue;
        bool _fetching, _offline, _renderPending, _halted;
        string _error = "";
        int _off;                              // erster angezeigter Tag
        DateTime _renderedDay = DateTime.MinValue;

        readonly Dictionary<string, double> _station = new Dictionary<string, double>();
        readonly List<int> _tags = new List<int>();
        CQuadEnvironment.DelegateOnTagChanged _onTag;
        DispatcherTimer _timer;

        // ---- IQuadContent ----

        int IQuadContent._id { get { return _id; } set { _id = value; } }
        string IQuadContent._title { get { return _title; } set { _title = value ?? ""; } }

        public void init(int _id, string _title)
        {
            this._id = _id;
            this._title = _title ?? "";
            _german = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en";
            _culture = _german ? CultureInfo.GetCultureInfo("de-DE") : CultureInfo.GetCultureInfo("en-GB");
        }

        public void onProjectData(byte[] _data)
        {
            try { _set = WetterSettings.Parse(_data); }
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
            LoadCache();
            RegisterTags();
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background, _display.Dispatcher);
                _timer.Interval = TimeSpan.FromSeconds(30);
                _timer.Tick += delegate { Tick(); };
            }
            _timer.Start();
            CheckFetch();
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
                    _root.Height = ContentH + BarH;
                    _root.ClipToBounds = true;
                    Canvas.SetLeft(_root, 0);
                    Canvas.SetTop(_root, Top0);
                }
                if (!_display.Children.Contains(_root)) _display.Children.Add(_root);
                Render();
                CheckFetch();
            }
            catch (Exception ex) { Log("show: " + ex); }
        }

        public void clear()
        {
            try
            {
                if (_display != null && _root != null) _display.Children.Remove(_root);
                if (_root != null) _root.Children.Clear();
                _root = null;
            }
            catch (Exception ex) { Log("clear: " + ex.Message); }
        }

        public void onHaltPlugin()
        {
            _halted = true;
            if (_timer != null) _timer.Stop();
            ReleaseTags();
        }

        // ---- IThemeable ----

        public void OnThemeChanged(ThemeMode mode)
        {
            _dark = mode != ThemeMode.light;
            Invalidate();
        }

        // ---- KOs der Wetterstation ----

        void RegisterTags()
        {
            if (_onTag == null) _onTag = OnTag;
            foreach (string k in WetterSettings.KoIds)
            {
                int tag = _set.Kos[k];
                if (tag == 0 || _tags.Contains(tag)) continue;
                try
                {
                    CQuadEnvironment._item.registerTag(tag, _onTag);
                    _tags.Add(tag);
                }
                catch (Exception ex) { Log("KO " + k + " (" + tag + ") nicht angemeldet: " + ex.Message); }
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
            foreach (string k in WetterSettings.KoIds)
                if (_set.Kos[k] == _tagId) _station[k] = _numValue;
            Invalidate();
        }

        double? St(string key)
        {
            double v;
            if (_station.TryGetValue(key, out v) && !double.IsNaN(v)) return v;
            return null;
        }

        // ---- Vorhersage laden ----

        string CacheFile()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QC-Plugin", WetterSettings.PluginId);
            string name = "vorhersage_" + WetterSettings.Num(_set.Lat) + "_" + WetterSettings.Num(_set.Lon) + "_" + _set.Tu + "_" + _set.Wu + ".json";
            return System.IO.Path.Combine(dir, name);
        }

        void LoadCache()
        {
            if (!_set.HasLocation || _fc != null) return;
            try
            {
                string f = CacheFile();
                if (!File.Exists(f)) return;
                _fc = Forecast.Parse(File.ReadAllText(f));
                _at = File.GetLastWriteTime(f);
            }
            catch (Exception ex) { Log("Zwischenspeicher nicht lesbar: " + ex.Message); }
        }

        void Tick()
        {
            if (_halted) return;
            CheckFetch();
            if (_root != null && _renderedDay != DateTime.Today) Render();   // Tageswechsel
        }

        void CheckFetch()
        {
            if (DisableNetwork || _halted || _fetching || !_set.HasLocation || _display == null) return;
            DateTime now = DateTime.Now;
            if (_fc != null && now - _at < TimeSpan.FromMinutes(_set.Interval)) return;
            if (now - _lastTry < TimeSpan.FromMinutes(Math.Min(5, _set.Interval))) return;   // nach Fehlern nicht drängeln
            _lastTry = now;
            _fetching = true;
            string url = OpenMeteo.ForecastUrl(_set), file = CacheFile();
            Dispatcher disp = _display.Dispatcher;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string json = null, err = null;
                Forecast fc = null;
                try
                {
                    json = OpenMeteo.HttpGet(url, 15000);
                    fc = Forecast.Parse(json);
                    try
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                        File.WriteAllText(file, json);
                    }
                    catch { }
                }
                catch (Exception ex) { err = ex.Message; }
                disp.BeginInvoke(new Action(delegate
                {
                    _fetching = false;
                    if (fc != null) { _fc = fc; _at = DateTime.Now; _offline = false; _error = ""; }
                    else { _offline = true; _error = err ?? ""; Log("Vorhersage nicht geladen: " + err); }
                    Invalidate();
                }));
            });
        }

        // ---- Anzeige ----

        void Invalidate()
        {
            if (_root == null || _renderPending) return;
            _renderPending = true;
            _root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                _renderPending = false;
                if (_root != null) Render();
            }));
        }

        Brush Ink { get { return _dark ? BInkD : BInkL; } }
        Brush Dim { get { return _dark ? BDimD : BDimL; } }
        Brush Line { get { return _dark ? BLineD : BLineL; } }
        Brush Panel { get { return _dark ? BPanelD : BPanelL; } }
        Brush Bar { get { return _dark ? BBarD : BBarL; } }

        static readonly Brush BInkD = Icons.Frozen(Color.FromRgb(0xE6, 0xE6, 0xE6)), BInkL = Icons.Frozen(Color.FromRgb(0x3C, 0x3C, 0x3C));
        static readonly Brush BDimD = Icons.Frozen(Color.FromRgb(0xA4, 0xA4, 0xA4)), BDimL = Icons.Frozen(Color.FromRgb(0x74, 0x74, 0x74));
        static readonly Brush BLineD = Icons.Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), BLineL = Icons.Frozen(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        // Dunkles Design: Farben am QuadClient gemessen (Kacheln #1A1A1A, Fugen fast schwarz, Leisten #1E1E1E).
        const double Gap = 2;
        static readonly Brush BGapD = Icons.Frozen(Color.FromRgb(0x01, 0x01, 0x01));
        static readonly Brush BPanelD = Icons.Frozen(Color.FromRgb(0x1A, 0x1A, 0x1A)), BPanelL = Icons.Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        static readonly Brush BBarD = Icons.Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E)), BBarL = Icons.Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        static readonly Brush BMax = Icons.Frozen(Color.FromRgb(0xEE, 0x4B, 0x3F)), BMin = Icons.Frozen(Color.FromRgb(0x4D, 0x9F, 0xEC));
        static readonly Brush BHit = Icons.Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00));

        string L(string de, string en) { return _german ? de : en; }
        string N(double v, int dec) { return v.ToString("N" + dec, _culture); }

        TextBlock Text(string s, double x, double y, double width, double size, Brush brush, TextAlignment align)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = brush;
            t.Width = width;
            t.TextAlignment = align;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.IsHitTestVisible = false;
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            _root.Children.Add(t);
            return t;
        }

        void Put(FrameworkElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            _root.Children.Add(e);
        }

        void Rect(double x, double y, double w, double h, Brush fill)
        {
            Rectangle r = new Rectangle();
            r.Width = w;
            r.Height = h;
            r.Fill = fill;
            r.IsHitTestVisible = false;
            Put(r, x, y);
        }

        /// <summary>Zeile "Symbol + Text (+ Windpfeil + Richtung)".</summary>
        void Row(Icons.Glyph g, string text, double x, double y, double width, double size, double? arrowDeg)
        {
            StackPanel p = new StackPanel();
            p.Orientation = Orientation.Horizontal;
            p.Width = width;
            p.IsHitTestVisible = false;
            FrameworkElement ico = Icons.Small(g, Dim, size + 3);
            ico.Margin = new Thickness(0, 1, 6, 0);
            p.Children.Add(ico);
            p.Children.Add(RowText(text, size));
            if (arrowDeg != null)
            {
                FrameworkElement arrow = Icons.WindArrow(arrowDeg.Value, Ink, size + 2);
                arrow.Margin = new Thickness(5, 1, 2, 0);
                p.Children.Add(arrow);
                p.Children.Add(RowText(Forecast.DirText(arrowDeg, _german), size));
            }
            Put(p, x, y);
        }

        TextBlock RowText(string s, double size)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = Ink;
            return t;
        }

        void Render()
        {
            if (_root == null) return;
            _root.Children.Clear();
            _renderedDay = DateTime.Today;

            List<DayForecast> days = _fc != null ? _fc.FromToday(DateTime.Today) : new List<DayForecast>();
            int maxOff = Math.Max(0, days.Count - DaysShown);
            if (_off > maxOff) _off = maxOff;
            if (_off < 0) _off = 0;

            // Kacheln wie im QuadClient: Fläche "Jetzt" und vier Tagesspalten, dazwischen Fugen.
            if (_dark) Rect(0, 0, W, ContentH + BarH, BGapD);
            Rect(0, 0, NowW - Gap, ContentH - Gap, Panel);
            for (int i = 0; i < DaysShown; i++)
                Rect(NowW + i * DayW, 0, DayW - (i == DaysShown - 1 ? 0 : Gap), ContentH - Gap, Panel);
            RenderBar(days.Count, maxOff);

            if (!_set.HasLocation)
            {
                Text(L("Kein Ort eingestellt – bitte im Experten Ort oder Koordinaten eintragen.", "No location set – please enter a location in the Experte."),
                    20, ContentH / 2 - 10, W - 40, 14, Ink, TextAlignment.Center);
                return;
            }
            RenderNow();
            if (days.Count == 0)
            {
                string msg = _offline ? L("Vorhersage nicht erreichbar", "Forecast not available") + (_error.Length > 0 ? ": " + _error : "")
                                      : L("Lade Vorhersage …", "Loading forecast …");
                Text(msg, NowW + 20, ContentH / 2 - 10, W - NowW - 40, 14, Ink, TextAlignment.Center);
                return;
            }
            for (int i = 0; i < DaysShown && _off + i < days.Count; i++) RenderDay(days[_off + i], NowW + i * DayW);
        }

        void RenderBar(int dayCount, int maxOff)
        {
            Rect(0, ContentH, W, BarH, Bar);
            if (!_dark) Rect(0, ContentH, W, 1, Line);
            string title = _set.Name.Length > 0 ? _set.Name : (_title.Length > 0 ? _title : L("Wetter", "Weather"));
            if (_at != DateTime.MinValue && _fc != null)
                title += "   [" + _at.ToString("d", _culture) + " " + _at.ToString("HH:mm", _culture) + "]";
            if (_offline) title += " · offline";
            Text(title, 70, ContentH + 9, W - 140, 14, Ink, TextAlignment.Center);
            PageButton(0, -1, _off > 0);
            PageButton(W - 60, 1, _off < maxOff);
        }

        void PageButton(double x, int step, bool active)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(step < 0 ? "M14,0 L0,9 L14,18 z" : "M0,0 L14,9 L0,18 z");
            p.Fill = active ? Ink : Line;
            p.IsHitTestVisible = false;
            Put(p, x + 23, ContentH + 10);
            if (!active) return;
            Rectangle hit = new Rectangle();
            hit.Width = 60;
            hit.Height = BarH;
            hit.Fill = BHit;
            hit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _off += step;
                try { CQuadEnvironment._item.resetMyTouchTimeout(); } catch { }
                Render();
            };
            Put(hit, x, ContentH);
        }

        void RenderNow()
        {
            Forecast f = _fc ?? new Forecast();
            double? stTemp = St("temp"), temp = f.Temp;
            if (stTemp != null) temp = _set.Tu == "fahrenheit" ? stTemp.Value * 9 / 5 + 32 : stTemp.Value;
            double? stFeel = St("feel"), feel = f.Feel;
            if (stFeel != null) feel = _set.Tu == "fahrenheit" ? stFeel.Value * 9 / 5 + 32 : stFeel.Value;
            double? wind = f.Wind, stWind = St("wind");
            if (stWind != null)
            {
                double ms = _set.KoWu == "kmh" ? stWind.Value / 3.6 : stWind.Value;
                wind = _set.Wu == "ms" ? ms : ms * 3.6;
            }
            double? dir = St("dir") ?? f.WindDir, hum = St("hum") ?? f.Hum, rainBit = St("rain"), lux = St("lux"), press = St("press");

            if (press != null && press.Value > 2000) press = press.Value / 100;   // DPT 9.006 sendet Pa, angezeigt wird hPa

            if (!_dark) Rect(NowW - 1, 8, 1, ContentH - 16, Line);
            Text(L("Jetzt", "Now"), 14, 7, 60, 13, Ink, TextAlignment.Left);
            if (stTemp != null) Text(L("gemessen", "measured"), 74, 9, NowW - 88, 11, Dim, TextAlignment.Right);

            if (_fc != null) Put(Icons.Weather(f.Code, f.IsDay, Ink, 62), 10, 26);
            Text(temp == null ? "–" : N(temp.Value, 1), 70, 30, NowW - 110, 32, Ink, TextAlignment.Right);
            Text(_set.TempUnitText, NowW - 38, 36, 30, 15, Dim, TextAlignment.Left);

            if (feel != null) Text(L("gefühlt ", "feels like ") + N(feel.Value, 1) + " " + _set.TempUnitText, 70, 73, NowW - 80, 12, Dim, TextAlignment.Right);

            double y = 98;
            const double step = 22;
            if (wind != null) { Row(Icons.Glyph.Wind, N(wind.Value, 0) + " " + _set.WindUnitText, 14, y, NowW - 24, 13, dir); y += step; }
            if (hum != null) { Row(Icons.Glyph.Drop, N(hum.Value, 0) + " %", 14, y, NowW - 24, 13, null); y += step; }
            if (rainBit != null) { Row(Icons.Glyph.Umbrella, rainBit.Value != 0 ? L("Regen", "Rain") : L("trocken", "dry"), 14, y, NowW - 24, 13, null); y += step; }
            else if (f.Precip != null) { Row(Icons.Glyph.Umbrella, N(f.Precip.Value, 1) + " mm", 14, y, NowW - 24, 13, null); y += step; }
            string luxDirs = LuxByDirection();
            if (luxDirs != null) { Row(Icons.Glyph.Light, luxDirs, 14, y, NowW - 24, luxDirs.Length > 24 ? 11.5 : 13, null); y += step; }
            else if (lux != null) { Row(Icons.Glyph.Light, N(lux.Value, 0) + " lx", 14, y, NowW - 24, 13, null); y += step; }
            if (press != null) { Row(Icons.Glyph.Gauge, N(press.Value, 0) + " hPa", 14, y, NowW - 24, 13, null); y += step; }

            Text(OpenMeteo.SourceText, 14, ContentH - 18, NowW - 24, 9.5, Dim, TextAlignment.Left);
        }

        /// <summary>Helligkeit je Himmelsrichtung in klx, z. B. "O 8 · S 45 · W 12 klx"; null, wenn kein Richtungs-KO einen Wert hat.</summary>
        string LuxByDirection()
        {
            string[] ids = { "lux_n", "lux_e", "lux_s", "lux_w" };
            string[] names = { "N", _german ? "O" : "E", "S", "W" };
            List<string> parts = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                double? v = St(ids[i]);
                if (v == null) continue;
                double klx = v.Value / 1000;
                parts.Add(names[i] + " " + N(klx, klx < 10 ? 1 : 0));
            }
            return parts.Count == 0 ? null : string.Join(" · ", parts.ToArray()) + " klx";
        }

        void RenderDay(DayForecast d, double x)
        {
            int diff = (int)Math.Round((d.Date - DateTime.Today).TotalDays);
            string wd = diff == 0 ? L("Heute", "Today") : diff == 1 ? L("Morgen", "Tomorrow") : _culture.DateTimeFormat.GetDayName(d.Date.DayOfWeek);
            if (!_dark && x > NowW) Rect(x, 8, 1, ContentH - 16, Line);
            Text(wd, x + 6, 7, DayW - 12, 13, Ink, TextAlignment.Center);
            Text(d.Date.ToString(_german ? "dd.MM." : "dd/MM", CultureInfo.InvariantCulture), x + 6, 26, DayW - 12, 11, Dim, TextAlignment.Center);
            Put(Icons.Weather(d.Code, true, Ink, 56), x + (DayW - 56) / 2, 42);
            string u = _set.Tu == "fahrenheit" ? "°F" : "°C";
            Text(d.Max == null ? "–" : N(d.Max.Value, 0) + u, x + 4, 103, DayW / 2 - 6, 17, BMax, TextAlignment.Right);
            Text(d.Min == null ? "–" : N(d.Min.Value, 0) + u, x + DayW / 2 + 4, 103, DayW / 2 - 6, 17, BMin, TextAlignment.Left);
            Rect(x + 12, 136, DayW - 24, 1, Line);
            string rain = (d.RainSum == null ? "–" : N(d.RainSum.Value, 1)) + " l/m²" + (d.RainProb == null ? "" : " · " + N(d.RainProb.Value, 0) + " %");
            Row(Icons.Glyph.Umbrella, rain, x + 8, 148, DayW - 12, 11, null);
            if (d.WindMax != null) Row(Icons.Glyph.Wind, N(d.WindMax.Value, 0) + " " + _set.WindUnitText, x + 8, 174, DayW - 12, 11, d.WindDir);
        }

        static void Log(string msg)
        {
            try { CLog.logError("Wetter " + WetterSettings.PluginId + ": " + msg); } catch { }
        }
    }
}
