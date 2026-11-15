// QC-Plugin 99-2 "Kamera" – Anzeige im QuadClient (99-2_client.dll).
// Klasse laut interface.xml: nsclient="QcKamera.Client.KameraQuad"
//
// Zeigt eine Kamera der Instanz; Unterleiste "◀ Name ▶" blättert, ein Klick ins Bild schaltet das Vollbild.
// Es läuft immer nur der Abspieler der sichtbaren Kamera.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using hs_client_framework;
using Path = System.Windows.Shapes.Path;

namespace QcKamera.Client
{
    public sealed class KameraQuad : IQuadContent, IThemeable
    {
        // Maße im Quadranten: Inhalt beginnt unter der Titelzeile des QuadClients.
        const double Top0 = 40, W = 680, ContentH = 237, BarH = 37;

        int _id;
        string _title = "";
        KameraSettings _set = new KameraSettings();
        Canvas _display, _root, _full;
        Image _img, _imgFull;
        TextBlock _status, _statusFull, _name, _info;
        Rectangle _bar;
        Path _prev, _next;
        bool _dark = true, _german = true, _halted, _visible;
        int _index;
        CamPlayer _player;
        string _statusText = "";
        DispatcherTimer _timer;

        readonly List<int> _tags = new List<int>();
        CQuadEnvironment.DelegateOnTagChanged _onTag;

        static Brush Frozen(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static readonly Brush BBack = Frozen(Color.FromRgb(0x01, 0x01, 0x01)), BVideo = Frozen(Color.FromRgb(0x0A, 0x0A, 0x0A));
        static readonly Brush BInkD = Frozen(Color.FromRgb(0xE6, 0xE6, 0xE6)), BInkL = Frozen(Color.FromRgb(0x3C, 0x3C, 0x3C));
        static readonly Brush BDimD = Frozen(Color.FromRgb(0xA4, 0xA4, 0xA4)), BDimL = Frozen(Color.FromRgb(0x74, 0x74, 0x74));
        static readonly Brush BBarD = Frozen(Color.FromRgb(0x1E, 0x1E, 0x1E)), BBarL = Frozen(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        static readonly Brush BOffD = Frozen(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), BOffL = Frozen(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
        static readonly Brush BHit = Frozen(Color.FromArgb(0x01, 0x00, 0x00, 0x00)), BStatusBack = Frozen(Color.FromArgb(0xB0, 0x00, 0x00, 0x00));

        Brush Ink { get { return _dark ? BInkD : BInkL; } }
        Brush Dim { get { return _dark ? BDimD : BDimL; } }

        string L(string de, string en) { return _german ? de : en; }

        CamSettings Current { get { return _index >= 0 && _index < _set.Cams.Count ? _set.Cams[_index] : null; } }

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
            try { _set = KameraSettings.Parse(_data); }
            catch (Exception ex) { Log("Einstellungen nicht lesbar: " + ex.Message); }
        }

        public void onUserDefData(byte[] _data) { }
        public void onDesignData(byte[] _data) { }
        public void onBroadcast(string _key, string _value) { }
        public void onAbortAction() { CloseFull(); }
        public void onSetConfigState(bool _state) { }

        public void run(Canvas _display)
        {
            this._display = _display;
            _halted = false;
            _tagsFrom = DateTime.UtcNow.AddSeconds(5);
            RegisterTags();
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Background, _display.Dispatcher);
                _timer.Interval = TimeSpan.FromSeconds(1);
                _timer.Tick += delegate { UpdateInfo(); };
            }
        }

        public void show(int _pos)
        {
            if (_display == null) return;
            try
            {
                if (_root == null) Build();
                if (!_display.Children.Contains(_root)) _display.Children.Add(_root);
                _visible = true;
                UpdateBar();
                StartPlayer();
                _timer.Start();
            }
            catch (Exception ex) { Log("show: " + ex); }
        }

        public void clear()
        {
            try
            {
                _visible = false;
                CloseFull();
                StopPlayer();
                if (_timer != null) _timer.Stop();
                if (_display != null && _root != null) _display.Children.Remove(_root);
                _root = null;
            }
            catch (Exception ex) { Log("clear: " + ex.Message); }
        }

        public void onHaltPlugin()
        {
            _halted = true;
            _visible = false;
            try { CloseFull(); } catch { }
            StopPlayer();
            if (_timer != null) _timer.Stop();
            ReleaseTags();
        }

        // ---- IThemeable ----

        public void OnThemeChanged(ThemeMode mode)
        {
            _dark = mode != ThemeMode.light;
            if (_root != null) UpdateBar();
        }

        // ---- KO "Kamera anzeigen" ----

        void RegisterTags()
        {
            if (_onTag == null) _onTag = OnTag;
            foreach (CamSettings c in _set.Cams)
            {
                if (c.KoShow == 0 || _tags.Contains(c.KoShow)) continue;
                try
                {
                    CQuadEnvironment._item.registerTag(c.KoShow, _onTag);
                    _tags.Add(c.KoShow);
                }
                catch (Exception ex) { Log("KO " + c.KoShow + " nicht angemeldet: " + ex.Message); }
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

        /// <summary>Bis dahin gemeldete Werte sind die Anfangswerte der KOs und schalten nicht um.</summary>
        internal DateTime _tagsFrom = DateTime.MaxValue;

        void OnTag(int _tagId, double _numValue, string _textValue)
        {
            Dispatcher d = _display != null ? _display.Dispatcher : null;
            if (d != null && !d.CheckAccess())
            {
                d.BeginInvoke(new Action(delegate { OnTag(_tagId, _numValue, _textValue); }));
                return;
            }
            if (_numValue == 0 || double.IsNaN(_numValue) || DateTime.UtcNow < _tagsFrom) return;
            for (int i = 0; i < _set.Cams.Count; i++)
            {
                if (_set.Cams[i].KoShow != _tagId) continue;
                Select(i);
                try { CQuadEnvironment._item.onTopAndClearPopups(this); } catch { }   // Quadrant nach vorn holen
                break;
            }
        }

        // ---- Aufbau ----

        TextBlock Label(double x, double y, double width, double size, TextAlignment align)
        {
            TextBlock t = new TextBlock();
            t.FontSize = size;
            t.Width = width;
            t.TextAlignment = align;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.IsHitTestVisible = false;
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            return t;
        }

        static TextBlock StatusBlock(double width)
        {
            TextBlock t = new TextBlock();
            t.FontSize = 13;
            t.Foreground = BInkD;
            t.Background = BStatusBack;
            t.Padding = new Thickness(10, 5, 10, 6);
            t.MaxWidth = width;
            t.TextWrapping = TextWrapping.Wrap;
            t.TextAlignment = TextAlignment.Center;
            t.IsHitTestVisible = false;
            t.Visibility = Visibility.Collapsed;
            return t;
        }

        void Build()
        {
            _root = new Canvas();
            _root.Width = W;
            _root.Height = ContentH + BarH;
            _root.ClipToBounds = true;
            Canvas.SetLeft(_root, 0);
            Canvas.SetTop(_root, Top0);

            Rectangle back = new Rectangle();
            back.Width = W; back.Height = ContentH + BarH; back.Fill = BBack;
            _root.Children.Add(back);
            Rectangle video = new Rectangle();
            video.Width = W; video.Height = ContentH - 2; video.Fill = BVideo;
            video.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Touch(); OpenFull(); };
            _root.Children.Add(video);

            _img = new Image();
            _img.Width = W; _img.Height = ContentH - 2;
            _img.Stretch = Stretch.Uniform;
            _img.IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.Linear);
            _root.Children.Add(_img);

            // Meldung mittig über dem Bild
            Grid g = new Grid();
            g.Width = W; g.Height = ContentH - 2; g.IsHitTestVisible = false;
            _status = StatusBlock(W - 80);
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(_status);
            _root.Children.Add(g);

            _bar = new Rectangle();
            _bar.Width = W; _bar.Height = BarH;
            Canvas.SetTop(_bar, ContentH);
            _root.Children.Add(_bar);
            _name = Label(70, ContentH + 9, W - 140, 14, TextAlignment.Center);
            _root.Children.Add(_name);
            _info = Label(W - 250, ContentH + 12, 185, 10.5, TextAlignment.Right);
            _root.Children.Add(_info);
            _prev = PageButton(0, -1);
            _next = PageButton(W - 60, 1);
            ShowStatus(_statusText);
        }

        Path PageButton(double x, int step)
        {
            Path p = new Path();
            p.Data = Geometry.Parse(step < 0 ? "M14,0 L0,9 L14,18 z" : "M0,0 L14,9 L0,18 z");
            p.IsHitTestVisible = false;
            Canvas.SetLeft(p, x + 23);
            Canvas.SetTop(p, ContentH + 10);
            _root.Children.Add(p);
            Rectangle hit = new Rectangle();
            hit.Width = 60; hit.Height = BarH; hit.Fill = BHit;
            hit.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Touch();
                if (_set.Cams.Count > 1) Select((_index + step + _set.Cams.Count) % _set.Cams.Count);
            };
            Canvas.SetLeft(hit, x);
            Canvas.SetTop(hit, ContentH);
            _root.Children.Add(hit);
            return p;
        }

        static void Touch()
        {
            try { CQuadEnvironment._item.resetMyTouchTimeout(); } catch { }
        }

        void UpdateBar()
        {
            if (_root == null) return;
            _bar.Fill = _dark ? BBarD : BBarL;
            CamSettings c = Current;
            string name = c != null && c.Name.Length > 0 ? c.Name : (_title.Length > 0 ? _title : L("Kamera", "Camera"));
            if (_set.Cams.Count > 1) name += "   " + (_index + 1) + " / " + _set.Cams.Count;
            _name.Text = name;
            _name.Foreground = Ink;
            _info.Foreground = Dim;
            Brush arrow = _set.Cams.Count > 1 ? Ink : (_dark ? BOffD : BOffL);
            _prev.Fill = arrow;
            _next.Fill = arrow;
            UpdateInfo();
        }

        void UpdateInfo()
        {
            if (_info != null) _info.Text = _player != null ? _player.Info : "";
        }

        // ---- Abspieler ----

        void Select(int index)
        {
            if (index < 0 || index >= _set.Cams.Count) return;
            if (index == _index && _player != null) return;
            _index = index;
            UpdateBar();
            if (_visible) StartPlayer();
        }

        void StartPlayer()
        {
            StopPlayer();
            if (_halted || _display == null) return;
            SetImage(null);
            CamSettings c = Current;
            if (c == null) { ShowStatus(L("Keine Kamera eingerichtet – bitte im Experten anlegen.", "No camera configured – please add one in the Experte.")); return; }
            bool full = _full != null;
            string src = c.Source;
            CamPlayer p;
            if (src == "rtsp") p = new RtspPlayer(_display.Dispatcher, c, _german, c.RtspFor(full));
            else if (src == "mjpeg") p = new MjpegPlayer(_display.Dispatcher, c, _german);
            else if (src == "jpeg") p = new JpegPlayer(_display.Dispatcher, c, _german);
            else { ShowStatus(L("Für diese Kamera ist keine Adresse eingetragen.", "No address is set for this camera.")); return; }
            p.OnImage = delegate(ImageSource img) { if (p == _player) { SetImage(img); ShowStatus(""); } };
            p.OnStatus = delegate(string s) { if (p == _player) ShowStatus(s); };
            SetTarget(p);
            _player = p;
            p.Start();
        }

        void StopPlayer()
        {
            CamPlayer p = _player;
            _player = null;
            if (p != null) p.Stop();
        }

        void SetTarget(CamPlayer p)
        {
            if (_full != null && _full.Width > 0) { p.TargetW = (int)_full.Width; p.TargetH = (int)_full.Height; }
            else { p.TargetW = (int)W; p.TargetH = (int)ContentH; }
        }

        void SetImage(ImageSource img)
        {
            if (_img != null && _img.Source != img) _img.Source = img;
            if (_imgFull != null && _imgFull.Source != img) _imgFull.Source = img;
        }

        /// <summary>Meldung über dem Bild; solange sie steht, wird ein altes Bild abgedunkelt.</summary>
        void ShowStatus(string text)
        {
            _statusText = text ?? "";
            bool on = _statusText.Length > 0;
            foreach (TextBlock t in new TextBlock[] { _status, _statusFull })
            {
                if (t == null) continue;
                t.Text = _statusText;
                t.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_img != null) _img.Opacity = on ? 0.35 : 1;
            if (_imgFull != null) _imgFull.Opacity = on ? 0.35 : 1;
        }

        // ---- Vollbild ----

        void OpenFull()
        {
            if (_full != null || Current == null) return;
            Canvas host = null;
            try { host = CQuadEnvironment._item.mainCanvas; } catch { }
            if (host == null) return;
            double w = host.ActualWidth > 0 ? host.ActualWidth : host.Width, h = host.ActualHeight > 0 ? host.ActualHeight : host.Height;
            if (double.IsNaN(w) || double.IsNaN(h) || w < 100 || h < 100) return;
            try
            {
                Canvas f = new Canvas();
                f.Width = w; f.Height = h;
                f.Background = Brushes.Black;
                Panel.SetZIndex(f, 30000);
                f.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; Touch(); CloseFull(); };
                _imgFull = new Image();
                _imgFull.Width = w; _imgFull.Height = h;
                _imgFull.Stretch = Stretch.Uniform;
                _imgFull.IsHitTestVisible = false;
                _imgFull.Source = _img != null ? _img.Source : null;
                f.Children.Add(_imgFull);
                Grid g = new Grid();
                g.Width = w; g.Height = h; g.IsHitTestVisible = false;
                _statusFull = StatusBlock(w - 80);
                _statusFull.HorizontalAlignment = HorizontalAlignment.Center;
                _statusFull.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(_statusFull);
                TextBlock name = new TextBlock();
                name.Text = Current.Name;
                name.FontSize = 15;
                name.Foreground = BInkD;
                name.Background = BStatusBack;
                name.Padding = new Thickness(10, 4, 10, 5);
                name.HorizontalAlignment = HorizontalAlignment.Left;
                name.VerticalAlignment = VerticalAlignment.Top;
                g.Children.Add(name);
                f.Children.Add(g);
                host.Children.Add(f);
                _full = f;
                ShowStatus(_statusText);
                CamSettings c = Current;
                // Nur neu verbinden, wenn es für das Vollbild einen eigenen (größeren) Stream gibt.
                if (c.Source == "rtsp" && c.RtspFor(true) != c.RtspFor(false)) StartPlayer();
                else if (_player != null) SetTarget(_player);
            }
            catch (Exception ex) { Log("Vollbild: " + ex.Message); _full = null; _imgFull = null; _statusFull = null; }
        }

        void CloseFull()
        {
            Canvas f = _full;
            if (f == null) return;
            _full = null; _imgFull = null; _statusFull = null;
            try
            {
                Panel parent = f.Parent as Panel;
                if (parent != null) parent.Children.Remove(f);
            }
            catch { }
            CamSettings c = Current;
            if (c != null && _visible && c.Source == "rtsp" && c.RtspFor(true) != c.RtspFor(false)) StartPlayer();
            else if (_player != null) SetTarget(_player);
        }

        internal static void Log(string msg)
        {
            try { CLog.logError("Kamera " + KameraSettings.PluginId + ": " + msg); } catch { }
        }
    }
}
