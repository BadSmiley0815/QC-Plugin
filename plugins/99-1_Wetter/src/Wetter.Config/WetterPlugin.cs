// QC-Plugin 99-1 "Wetter" – Einstellungen im Experten/QuadConfig (plugin_config.dll).
// Klasse laut interface.xml: nsconfig="QcWetter.Config.WetterPlugin"
using System;
using System.Collections.Generic;
using de.dacom_ha.config_framework;

namespace QcWetter.Config
{
    /// <summary>Eintrag einer Auswahlliste im Eigenschaftsraster.</summary>
    public sealed class Choice
    {
        public readonly string Key;
        readonly string _text;

        public Choice(string key, string text) { Key = key; _text = text; }

        public override string ToString()
        {
            return _text.StartsWith("#") ? WetterPlugin.T(_text) : _text;
        }
    }

    public sealed class WetterPlugin : IPlugin
    {
        // Feld-IDs im Raster (KO-Felder: FieldKoBase + Index in WetterSettings.KoIds)
        const int FieldSearch = 1, FieldResult = 2, FieldName = 3, FieldLat = 4, FieldLon = 5;
        const int FieldInterval = 10, FieldTu = 11, FieldWu = 12, FieldKoWu = 13;
        const int FieldKoBase = 101;

        static readonly Choice[] Intervals =
        {
            new Choice("15", "15 min"), new Choice("30", "30 min"), new Choice("60", "1 h"),
            new Choice("120", "2 h"), new Choice("180", "3 h"), new Choice("360", "6 h")
        };
        static readonly Choice[] TempUnits = { new Choice("celsius", "°C"), new Choice("fahrenheit", "°F") };
        static readonly Choice[] WindUnits = { new Choice("kmh", "km/h"), new Choice("ms", "m/s") };
        static readonly Choice[] KoWindUnits = { new Choice("ms", "m/s"), new Choice("kmh", "km/h") };

        string _lang = "de";
        string _search = "", _searched = "", _result = "";
        string _name = "", _lat = "", _lon = "";
        Choice _interval = Intervals[2], _tu = TempUnits[0], _wu = WindUnits[0], _koWu = KoWindUnits[0];
        readonly CPluginLinkCObject[] _kos = new CPluginLinkCObject[WetterSettings.KoIds.Length];
        readonly List<CGridFieldBase> _shown = new List<CGridFieldBase>();

        public WetterPlugin()
        {
            for (int i = 0; i < _kos.Length; i++) _kos[i] = new CPluginLinkCObject();
        }

        // ---- Eigenschaften, an die das Raster bindet (Name = Parameter "member" der Felder) ----

        public string Search { get { return _search; } set { _search = value ?? ""; } }
        public string Result { get { return _result; } set { } }
        public string LocationName { get { return _name; } set { _name = (value ?? "").Trim(); } }
        public string Latitude { get { return _lat; } set { _lat = (value ?? "").Trim(); } }
        public string Longitude { get { return _lon; } set { _lon = (value ?? "").Trim(); } }
        public Choice Interval { get { return _interval; } set { if (value != null) _interval = value; } }
        public Choice TempUnit { get { return _tu; } set { if (value != null) _tu = value; } }
        public Choice WindUnit { get { return _wu; } set { if (value != null) _wu = value; } }
        public Choice KoWindUnit { get { return _koWu; } set { if (value != null) _koWu = value; } }

        public CPluginLinkCObject KoTemp { get { return _kos[0]; } set { if (value != null) _kos[0] = value; } }
        public CPluginLinkCObject KoFeel { get { return _kos[1]; } set { if (value != null) _kos[1] = value; } }
        public CPluginLinkCObject KoHum { get { return _kos[2]; } set { if (value != null) _kos[2] = value; } }
        public CPluginLinkCObject KoWind { get { return _kos[3]; } set { if (value != null) _kos[3] = value; } }
        public CPluginLinkCObject KoDir { get { return _kos[4]; } set { if (value != null) _kos[4] = value; } }
        public CPluginLinkCObject KoRain { get { return _kos[5]; } set { if (value != null) _kos[5] = value; } }
        public CPluginLinkCObject KoLux { get { return _kos[6]; } set { if (value != null) _kos[6] = value; } }
        public CPluginLinkCObject KoLuxN { get { return _kos[7]; } set { if (value != null) _kos[7] = value; } }
        public CPluginLinkCObject KoLuxE { get { return _kos[8]; } set { if (value != null) _kos[8] = value; } }
        public CPluginLinkCObject KoLuxS { get { return _kos[9]; } set { if (value != null) _kos[9] = value; } }
        public CPluginLinkCObject KoLuxW { get { return _kos[10]; } set { if (value != null) _kos[10] = value; } }
        public CPluginLinkCObject KoPress { get { return _kos[11]; } set { if (value != null) _kos[11] = value; } }

        static readonly string[] KoMembers = { "KoTemp", "KoFeel", "KoHum", "KoWind", "KoDir", "KoRain", "KoLux", "KoLuxN", "KoLuxE", "KoLuxS", "KoLuxW", "KoPress" };

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
                    using (System.IO.Stream st = typeof(WetterPlugin).Assembly.GetManifestResourceStream("QcWetter.config_lang.xml"))
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
            try { CQConfig.FW.LogError("Wetter " + WetterSettings.PluginId + ": " + msg); } catch { }
        }

        static Choice Find(Choice[] list, string key, Choice fallback)
        {
            foreach (Choice c in list) if (c.Key == key) return c;
            return fallback;
        }

        WetterSettings ToSettings()
        {
            WetterSettings s = new WetterSettings();
            s.Name = _name;
            s.Lat = WetterSettings.ParseNum(_lat);
            s.Lon = WetterSettings.ParseNum(_lon);
            s.Interval = int.Parse(_interval.Key);
            s.Tu = _tu.Key;
            s.Wu = _wu.Key;
            s.KoWu = _koWu.Key;
            for (int i = 0; i < _kos.Length; i++) s.Kos[WetterSettings.KoIds[i]] = _kos[i].ID;
            return s;
        }

        CPluginLinkCObject LinkOfField(int fieldID)
        {
            int i = fieldID - FieldKoBase;
            return i >= 0 && i < _kos.Length ? _kos[i] : null;
        }

        /// <summary>Ortssuche bei Open-Meteo; übernimmt den ersten Treffer in Name/Breite/Länge.</summary>
        void RunSearch()
        {
            string q = _search.Trim();
            _searched = _search;
            if (q.Length < 2) return;
            try
            {
                List<GeoHit> hits = OpenMeteo.Geocode(q, _lang == "de" ? "de" : "en", 8000);
                if (hits.Count == 0)
                {
                    _result = T("#99-1_search_none");
                    return;
                }
                GeoHit h = hits[0];
                _name = h.Name;
                _lat = WetterSettings.Num(Math.Round(h.Lat, 4));
                _lon = WetterSettings.Num(Math.Round(h.Lon, 4));
                _result = h.ToString();
                if (hits.Count > 1)
                {
                    List<string> more = new List<string>();
                    for (int i = 1; i < hits.Count; i++) more.Add(hits[i].ToString());
                    _result += " – " + T("#99-1_search_more") + ": " + string.Join("; ", more.ToArray());
                }
                _search = "";
                _searched = "";
            }
            catch (Exception ex)
            {
                _result = T("#99-1_search_error") + ": " + ex.Message;
                Log("Ortssuche: " + ex.Message);
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
            _shown.Clear();
            List<CGridFieldBase> f = _fields.Fields;
            _fields.Title = T("#99-1_title");
            f.Add(Std(FieldSearch, "#99-1_search", this, "Search"));
            CGridFieldBase res = Std(FieldResult, "#99-1_result", this, "Result");
            res.Enabled = false;
            f.Add(res);
            f.Add(Std(FieldName, "#99-1_name", this, "LocationName"));
            f.Add(Std(FieldLat, "#99-1_lat", this, "Latitude"));
            f.Add(Std(FieldLon, "#99-1_lon", this, "Longitude"));
            f.Add(Lst(FieldInterval, "#99-1_interval", Intervals, this, "Interval"));
            f.Add(Lst(FieldTu, "#99-1_tu", TempUnits, this, "TempUnit"));
            f.Add(Lst(FieldWu, "#99-1_wu", WindUnits, this, "WindUnit"));
            for (int i = 0; i < _kos.Length; i++)
            {
                string key = "#99-1_ko_" + WetterSettings.KoIds[i];
                f.Add(new CGridFieldCobject(FieldKoBase + i, T(key), T(key + "_info"), this, KoMembers[i]));
                if (WetterSettings.KoIds[i] == "wind")
                    f.Add(Lst(FieldKoWu, "#99-1_kowu", KoWindUnits, this, "KoWindUnit"));
            }
            _shown.AddRange(f);
        }

        public void OnGridChanged()
        {
            if (_search != _searched)
            {
                RunSearch();
                Poke();
            }
        }

        public void LoadFromArray(byte[] _data)
        {
            try
            {
                WetterSettings s = WetterSettings.Parse(_data);
                _name = s.Name;
                _lat = WetterSettings.Num(s.Lat);
                _lon = WetterSettings.Num(s.Lon);
                _interval = Find(Intervals, s.Interval.ToString(), Intervals[2]);
                _tu = Find(TempUnits, s.Tu, TempUnits[0]);
                _wu = Find(WindUnits, s.Wu, WindUnits[0]);
                _koWu = Find(KoWindUnits, s.KoWu, KoWindUnits[0]);
                for (int i = 0; i < _kos.Length; i++)
                {
                    int id = s.Kos[WetterSettings.KoIds[i]];
                    if (id == 0) { _kos[i].Reset(); continue; }
                    try { _kos[i].SetID(id); }                    // löst die Bezeichnung des KOs auf
                    catch { _kos[i].ID = id; _kos[i].Text = "?"; }
                }
            }
            catch (Exception ex) { Log("LoadFromArray: " + ex.Message); }
        }

        public void SaveToArray(out byte[] _data)
        {
            _data = new byte[0];
            try { _data = ToSettings().ToBytes(); }
            catch (Exception ex) { Log("SaveToArray: " + ex.Message); }
        }

        public void GetLinks(ref List<CPluginLink> _lst)
        {
            foreach (CPluginLinkCObject l in _kos) _lst.Add(l);
        }

        public bool Validate(ref List<CPluginErrorItem> _lst)
        {
            if (!ToSettings().HasLocation)
                _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, T("#99-1_pluginname") + ": " + T("#99-1_no_location")));
            return true;
        }

        public void OnItemChanged(CHsBase _item, ItemChangedTypes _type)
        {
            if (_item == null) return;
            foreach (CPluginLinkCObject l in _kos)
            {
                if (l.ID == 0 || l.ID != _item.ID) continue;
                if (_type == ItemChangedTypes.Removed) l.Reset();
                else if (_type == ItemChangedTypes.Changed) l.Text = _item.GetCaption();
            }
        }

        public bool CanDrop(DragDropDestination _destination, CHsBase _item, int _fieldID)
        {
            if (_destination != DragDropDestination.Grid || _item == null) return false;
            CPluginLinkCObject l = LinkOfField(_fieldID);
            return l != null && l.CanDrop(_item);
        }

        public void DropOverGrid(CHsBase _item, int _fieldID)
        {
            CPluginLinkCObject l = LinkOfField(_fieldID);
            if (l != null && _item != null) l.SetID(_item.ID);
        }

        // Das Plugin hat keine Unterelemente.
        public CPluginListItems GetChilds() { return new CPluginListItems(); }
        public CPluginListItem CreateChild() { return null; }
        public bool DeleteChild(CPluginListItem _child) { return false; }
        public void OnSelectChild(CPluginListItem _child, CGridFields _fields) { }
        public void MoveChildUp(CPluginListItem _child) { }
        public void MoveChildDown(CPluginListItem _child) { }
        public bool CanAddChilds() { return false; }
        public bool CanDeleteChilds() { return false; }
        public bool CanMoveChilds() { return false; }
        public CPluginListItem DropOverList(CHsBase _item) { return null; }
        public void Free() { _shown.Clear(); }
    }
}
