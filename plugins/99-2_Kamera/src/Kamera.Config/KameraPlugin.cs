// QC-Plugin 99-2 "Kamera" – Einstellungen im Experten/QuadConfig (plugin_config.dll).
// Klasse laut interface.xml: nsconfig="QcKamera.Config.KameraPlugin"
// Jede Kamera ist ein Unterelement der Plugin-Instanz (Liste im Experten).
using System;
using System.Collections.Generic;
using System.Globalization;
using de.dacom_ha.config_framework;

namespace QcKamera.Config
{
    /// <summary>Eintrag einer Auswahlliste im Eigenschaftsraster.</summary>
    public sealed class Choice
    {
        public readonly string Key;
        readonly string _text;

        public Choice(string key, string text) { Key = key; _text = text; }

        public override string ToString()
        {
            return _text.StartsWith("#") ? KameraPlugin.T(_text) : _text;
        }
    }

    /// <summary>Eine Kamera, wie sie das Raster bearbeitet (Name der Eigenschaft = Parameter "member" der Felder).</summary>
    public sealed class CamNode
    {
        internal static readonly Choice[] YesNo = { new Choice("0", "#99-2_no"), new Choice("1", "#99-2_yes") };
        internal static readonly Choice[] Modes =
        {
            new Choice("auto", "#99-2_mode_auto"), new Choice("rtsp", "#99-2_mode_rtsp"),
            new Choice("mjpeg", "#99-2_mode_mjpeg"), new Choice("jpeg", "#99-2_mode_jpeg")
        };

        string _name = "", _host = "", _port = "80", _user = "", _pass = "", _result = "";
        string _rtsp = "", _rtspFull = "", _mjpeg = "", _jpeg = "", _refresh = "1000";
        Choice _query = YesNo[0], _mode = Modes[0];
        CPluginLinkCObject _koShow = new CPluginLinkCObject();

        public string Name { get { return _name; } set { _name = (value ?? "").Trim(); } }
        public string Host { get { return _host; } set { _host = (value ?? "").Trim(); } }
        public string Port { get { return _port; } set { _port = (value ?? "").Trim(); } }
        public string User { get { return _user; } set { _user = (value ?? "").Trim(); } }
        public string Password { get { return _pass; } set { _pass = value ?? ""; } }
        public Choice Query { get { return _query; } set { if (value != null) _query = value; } }
        public string Result { get { return _result; } set { } }
        public Choice Mode { get { return _mode; } set { if (value != null) _mode = value; } }
        public string Rtsp { get { return _rtsp; } set { _rtsp = (value ?? "").Trim(); } }
        public string RtspFull { get { return _rtspFull; } set { _rtspFull = (value ?? "").Trim(); } }
        public string Mjpeg { get { return _mjpeg; } set { _mjpeg = (value ?? "").Trim(); } }
        public string Jpeg { get { return _jpeg; } set { _jpeg = (value ?? "").Trim(); } }
        public string Refresh { get { return _refresh; } set { _refresh = (value ?? "").Trim(); } }
        public CPluginLinkCObject KoShow { get { return _koShow; } set { if (value != null) _koShow = value; } }

        internal void SetResult(string s) { _result = s ?? ""; }
        internal void ResetQuery() { _query = YesNo[0]; }

        internal int PortNumber
        {
            get { int p; return int.TryParse(_port, out p) && p > 0 && p < 65536 ? p : 80; }
        }

        internal CamSettings ToSettings()
        {
            CamSettings c = new CamSettings();
            c.Name = _name; c.Host = _host; c.Port = PortNumber; c.User = _user; c.Password = _pass;
            c.Mode = _mode.Key; c.Rtsp = _rtsp; c.RtspFull = _rtspFull; c.Mjpeg = _mjpeg; c.Jpeg = _jpeg;
            int r;
            c.Refresh = int.TryParse(_refresh, out r) ? Math.Max(100, Math.Min(60000, r)) : 1000;
            c.KoShow = _koShow.ID;
            return c;
        }

        internal void FromSettings(CamSettings c)
        {
            _name = c.Name; _host = c.Host; _port = c.Port.ToString(CultureInfo.InvariantCulture); _user = c.User; _pass = c.Password;
            _mode = Modes[0];
            foreach (Choice m in Modes) if (m.Key == c.Mode) _mode = m;
            _rtsp = c.Rtsp; _rtspFull = c.RtspFull; _mjpeg = c.Mjpeg; _jpeg = c.Jpeg;
            _refresh = c.Refresh.ToString(CultureInfo.InvariantCulture);
            if (c.KoShow == 0) _koShow.Reset();
            else
            {
                try { _koShow.SetID(c.KoShow); }                  // löst die Bezeichnung des KOs auf
                catch { _koShow.ID = c.KoShow; _koShow.Text = "?"; }
            }
        }
    }

    /// <summary>Listeneintrag im Experten; zeigt den Namen der Kamera.</summary>
    public sealed class CamItem : CPluginListItem
    {
        public CamItem(int id, CamNode cam) : base(id, cam) { }
        public CamNode Cam { get { return (CamNode)Tag; } }
        public override string ToString() { return Cam.Name.Length > 0 ? Cam.Name : KameraPlugin.T("#99-2_new_cam"); }
    }

    public sealed class KameraPlugin : IPlugin
    {
        const int FieldHint = 1;
        const int FieldName = 1, FieldHost = 2, FieldPort = 3, FieldUser = 4, FieldPass = 5, FieldQuery = 6, FieldResult = 7;
        const int FieldMode = 10, FieldRtsp = 11, FieldRtspFull = 12, FieldMjpeg = 13, FieldJpeg = 14, FieldRefresh = 15;
        const int FieldKoShow = 101;

        string _lang = "de";
        int _nextId = 1;
        readonly List<CamItem> _cams = new List<CamItem>();
        CamNode _current;
        readonly List<CGridFieldBase> _shown = new List<CGridFieldBase>();

        public override string ToString() { return T("#99-2_title"); }

        /// <summary>Hinweistext der Instanz (nur Anzeige).</summary>
        public string Hint { get { return T("#99-2_hint_text"); } set { } }

        // ---- Hilfen ----

        /// <summary>Text aus config_lang.xml; bei Fehlern der Schlüssel selbst.</summary>
        internal static string T(string key)
        {
            try
            {
                string s = CQConfig.FW.Translate(key);
                if (!string.IsNullOrEmpty(s) && s != key && !s.StartsWith("#")) return s;
            }
            catch { }
            return Builtin(key);
        }

        static Dictionary<string, string> _builtin;
        static string _builtinLang = "de";

        /// <summary>Rückfall: Texte aus der eingebetteten config_lang.xml (das Raster übersetzt Feldnamen nicht selbst).</summary>
        static string Builtin(string key)
        {
            try
            {
                if (_builtin == null)
                {
                    Dictionary<string, string> d = new Dictionary<string, string>();
                    using (System.IO.Stream st = typeof(KameraPlugin).Assembly.GetManifestResourceStream("QcKamera.config_lang.xml"))
                    using (System.Xml.XmlReader x = System.Xml.XmlReader.Create(st))
                    {
                        string cur = null;
                        while (x.Read())
                        {
                            if (x.NodeType != System.Xml.XmlNodeType.Element) continue;
                            if (x.Name == "text") { cur = x.GetAttribute("key"); d[cur] = x.GetAttribute("default") ?? cur; }
                            else if (x.Name == "lang" && cur != null && x.GetAttribute("id") == _builtinLang) d[cur] = x.GetAttribute("value") ?? d[cur];
                        }
                    }
                    _builtin = d;
                }
                string s;
                return _builtin.TryGetValue(key, out s) ? s : key;
            }
            catch { return key; }
        }

        static void Log(string msg)
        {
            try { CQConfig.FW.LogError("Kamera " + KameraSettings.PluginId + ": " + msg); } catch { }
        }

        /// <summary>Fragt die Kamera über ONVIF ab und trägt die Adressen ein.</summary>
        void RunQuery(CamNode c)
        {
            c.ResetQuery();
            if (c.Host.Length == 0) { c.SetResult(T("#99-2_query_nohost")); return; }
            try
            {
                List<OnvifProfile> list = new OnvifClient(c.Host, c.PortNumber, c.User, c.Password).Query();
                OnvifProfile small = OnvifClient.PickSmall(list), large = OnvifClient.PickLarge(list);
                if (small == null) { c.SetResult(T("#99-2_query_none")); return; }
                c.Rtsp = small.StreamUri;
                c.RtspFull = large != null && large != small ? large.StreamUri : "";
                if (small.SnapshotUri.Length > 0) c.Jpeg = small.SnapshotUri;
                else if (large != null && large.SnapshotUri.Length > 0) c.Jpeg = large.SnapshotUri;
                string r = T("#99-2_res_small") + ": " + small;
                if (large != null && large != small) r += " – " + T("#99-2_res_full") + ": " + large;
                c.SetResult(r + " (" + list.Count + " " + T("#99-2_res_profiles") + ")");
            }
            catch (Exception ex)
            {
                c.SetResult(T("#99-2_query_error") + ": " + ex.Message);
                Log("ONVIF " + c.Host + ": " + ex.Message);
            }
        }

        /// <summary>Raster anstoßen, damit geänderte Werte neu gelesen werden.</summary>
        void Poke()
        {
            foreach (CGridFieldBase f in _shown)
            {
                try { f.Enabled = f.Enabled; } catch { }
            }
        }

        // ---- IPlugin ----

        public void Init(string _defaultLang, System.Xml.XmlReader _configXml, CPluginImageManager _images)
        {
            if (!string.IsNullOrEmpty(_defaultLang)) _lang = _defaultLang.ToLowerInvariant();
            if (_lang != _builtinLang) { _builtinLang = _lang == "de" ? "de" : "en"; _builtin = null; }
        }

        static CGridFieldBase Std(int id, string key, object obj, string member)
        {
            return new CGridFieldStandard(id, T(key), T(key + "_info"), obj, member);
        }

        static CGridFieldBase Lst(int id, string key, object[] items, object obj, string member)
        {
            return new CGridFieldList(id, T(key), T(key + "_info"), items, obj, member);
        }

        public void OnSelectNode(CGridFields _fields)
        {
            _current = null;
            _shown.Clear();
            _fields.Title = T("#99-2_title");
            CGridFieldBase hint = Std(FieldHint, "#99-2_hint", this, "Hint");
            hint.Enabled = false;
            _fields.Fields.Add(hint);
            _shown.AddRange(_fields.Fields);
        }

        public void OnSelectChild(CPluginListItem _child, CGridFields _fields)
        {
            CamItem item = _child as CamItem;
            _shown.Clear();
            _current = item != null ? item.Cam : null;
            if (_current == null) return;
            CamNode c = _current;
            List<CGridFieldBase> f = _fields.Fields;
            _fields.Title = T("#99-2_cam_title");
            f.Add(Std(FieldName, "#99-2_name", c, "Name"));
            f.Add(Std(FieldHost, "#99-2_host", c, "Host"));
            f.Add(Std(FieldPort, "#99-2_port", c, "Port"));
            f.Add(Std(FieldUser, "#99-2_user", c, "User"));
            f.Add(Std(FieldPass, "#99-2_pass", c, "Password"));
            f.Add(Lst(FieldQuery, "#99-2_query", CamNode.YesNo, c, "Query"));
            CGridFieldBase res = Std(FieldResult, "#99-2_result", c, "Result");
            res.Enabled = false;
            f.Add(res);
            f.Add(Lst(FieldMode, "#99-2_mode", CamNode.Modes, c, "Mode"));
            f.Add(Std(FieldRtsp, "#99-2_rtsp", c, "Rtsp"));
            f.Add(Std(FieldRtspFull, "#99-2_rtsp_full", c, "RtspFull"));
            f.Add(Std(FieldMjpeg, "#99-2_mjpeg", c, "Mjpeg"));
            f.Add(Std(FieldJpeg, "#99-2_jpeg", c, "Jpeg"));
            f.Add(Std(FieldRefresh, "#99-2_refresh", c, "Refresh"));
            f.Add(new CGridFieldCobject(FieldKoShow, T("#99-2_ko_show"), T("#99-2_ko_show_info"), c, "KoShow"));
            _shown.AddRange(f);
        }

        public void OnGridChanged()
        {
            bool ran = false;
            foreach (CamItem i in _cams)
                if (i.Cam.Query.Key == "1") { RunQuery(i.Cam); ran = true; }
            if (ran) Poke();
        }

        public CPluginListItems GetChilds()
        {
            CPluginListItems l = new CPluginListItems();
            l.Sort = false;
            foreach (CamItem i in _cams) l.Items.Add(i);
            return l;
        }

        public CPluginListItem CreateChild()
        {
            CamNode c = new CamNode();
            c.Name = T("#99-2_new_cam") + " " + (_cams.Count + 1);
            CamItem i = new CamItem(_nextId++, c);
            _cams.Add(i);
            return i;
        }

        public bool DeleteChild(CPluginListItem _child)
        {
            CamItem i = _child as CamItem;
            if (i == null || !_cams.Remove(i)) return false;
            if (_current == i.Cam) _current = null;
            return true;
        }

        void Move(CPluginListItem child, int step)
        {
            int i = _cams.IndexOf(child as CamItem), j = i + step;
            if (i < 0 || j < 0 || j >= _cams.Count) return;
            CamItem t = _cams[i];
            _cams[i] = _cams[j];
            _cams[j] = t;
        }

        public void MoveChildUp(CPluginListItem _child) { Move(_child, -1); }
        public void MoveChildDown(CPluginListItem _child) { Move(_child, 1); }
        public bool CanAddChilds() { return true; }
        public bool CanDeleteChilds() { return true; }
        public bool CanMoveChilds() { return true; }

        public void LoadFromArray(byte[] _data)
        {
            try
            {
                _cams.Clear();
                _current = null;
                foreach (CamSettings c in KameraSettings.Parse(_data).Cams)
                {
                    CamNode n = new CamNode();
                    n.FromSettings(c);
                    _cams.Add(new CamItem(_nextId++, n));
                }
            }
            catch (Exception ex) { Log("LoadFromArray: " + ex.Message); }
        }

        public void SaveToArray(out byte[] _data)
        {
            _data = new byte[0];
            try
            {
                KameraSettings s = new KameraSettings();
                foreach (CamItem i in _cams) s.Cams.Add(i.Cam.ToSettings());
                _data = s.ToBytes();
            }
            catch (Exception ex) { Log("SaveToArray: " + ex.Message); }
        }

        public void GetLinks(ref List<CPluginLink> _lst)
        {
            foreach (CamItem i in _cams) _lst.Add(i.Cam.KoShow);
        }

        public bool Validate(ref List<CPluginErrorItem> _lst)
        {
            string name = T("#99-2_pluginname") + ": ";
            if (_cams.Count == 0) _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, name + T("#99-2_no_cams")));
            foreach (CamItem i in _cams)
                if (i.Cam.ToSettings().Source.Length == 0)
                    _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, name + i + " – " + T("#99-2_no_source")));
            return true;
        }

        public void OnItemChanged(CHsBase _item, ItemChangedTypes _type)
        {
            if (_item == null) return;
            foreach (CamItem i in _cams)
            {
                CPluginLinkCObject l = i.Cam.KoShow;
                if (l.ID == 0 || l.ID != _item.ID) continue;
                if (_type == ItemChangedTypes.Removed) l.Reset();
                else if (_type == ItemChangedTypes.Changed) l.Text = _item.GetCaption();
            }
        }

        public bool CanDrop(DragDropDestination _destination, CHsBase _item, int _fieldID)
        {
            return _destination == DragDropDestination.Grid && _item != null && _current != null && _fieldID == FieldKoShow && _current.KoShow.CanDrop(_item);
        }

        public void DropOverGrid(CHsBase _item, int _fieldID)
        {
            if (_current != null && _item != null && _fieldID == FieldKoShow) _current.KoShow.SetID(_item.ID);
        }

        public CPluginListItem DropOverList(CHsBase _item) { return null; }
        public void Free() { _shown.Clear(); _current = null; }
    }
}
