// QC-Plugin 99-3 "Energiefluss mit Verbrauchern" – Einstellungen im Experten/QuadConfig (plugin_config.dll).
// Klasse laut interface.xml: nsconfig="QcEnergie.Config.EnergiePlugin"
// Die Instanz hat die zwölf Grund-KOs; Verbraucher und Einzelwerte sind frei anlegbare Unterelemente (Liste im Experten).
//
// Idee und Vorbild: Plugin "Energiefluss" (180-1) von johannesbonn (KNX-User-Forum). Nichts daraus übernommen.
using System;
using System.Collections.Generic;
using de.dacom_ha.config_framework;

namespace QcEnergie.Config
{
    /// <summary>Eintrag einer Auswahlliste im Eigenschaftsraster.</summary>
    public sealed class Choice
    {
        public readonly string Key;
        readonly string _text;

        public Choice(string key, string text) { Key = key; _text = text; }

        public override string ToString()
        {
            return _text.StartsWith("#") ? EnergiePlugin.T(_text) : _text;
        }
    }

    /// <summary>
    /// Ein Eintrag der Liste "Unterelemente": ein Verbraucher (kleiner Kreis mit Symbol) oder ein Einzelwert hinter
    /// einem Hauptkreis. Name der Eigenschaft = Parameter "member" der Felder.
    /// </summary>
    public sealed class EntryNode
    {
        internal static readonly Choice[] KindList = { new Choice("vb", "#99-3_kind_vb"), new Choice("teil", "#99-3_kind_teil") };
        internal static readonly Choice[] IconList = MakeList(EnergieSettings.Icons, "#99-3_icon_");
        internal static readonly Choice[] AnList = MakeList(EnergieSettings.Ans, "#99-3_an_");

        static Choice[] MakeList(string[] keys, string prefix)
        {
            Choice[] l = new Choice[keys.Length];
            for (int i = 0; i < keys.Length; i++) l[i] = new Choice(keys[i], prefix + keys[i]);
            return l;
        }

        internal static Choice Find(Choice[] list, string key)
        {
            foreach (Choice c in list) if (c.Key == key) return c;
            return list[0];
        }

        string _name = "";
        Choice _kind = KindList[0], _icon = Find(IconList, "steckdose"), _an = AnList[0];
        CPluginLinkCObject _ko = new CPluginLinkCObject();

        public Choice Kind { get { return _kind; } set { if (value != null) _kind = value; } }
        public string Name { get { return _name; } set { _name = EnergieSettings.Clean(value); } }
        public Choice Icon { get { return _icon; } set { if (value != null) _icon = value; } }
        public Choice An { get { return _an; } set { if (value != null) _an = value; } }
        public CPluginLinkCObject Ko { get { return _ko; } set { if (value != null) _ko = value; } }

        internal bool IsTeil { get { return _kind.Key == "teil"; } }

        internal Verbraucher ToVb()
        {
            Verbraucher v = new Verbraucher();
            v.Name = _name; v.Icon = _icon.Key; v.An = _an.Key; v.Tag = _ko.ID;
            return v;
        }

        internal Teil ToTeil()
        {
            Teil t = new Teil();
            t.Name = _name; t.An = _an.Key; t.Tag = _ko.ID;
            return t;
        }

        internal static EntryNode From(Verbraucher v)
        {
            EntryNode n = new EntryNode();
            n._name = v.Name;
            n._icon = Find(IconList, v.ShownIcon);
            n._an = Find(AnList, v.An);
            EnergiePlugin.SetLink(n._ko, v.Tag);
            return n;
        }

        internal static EntryNode From(Teil t)
        {
            EntryNode n = new EntryNode();
            n._kind = KindList[1];
            n._name = t.Name;
            n._an = Find(AnList, t.An);
            EnergiePlugin.SetLink(n._ko, t.Tag);
            return n;
        }
    }

    /// <summary>Listeneintrag im Experten: "Garage" oder "PV-Anlage › Wechselrichter Süd", mit "(frei)" ohne KO.</summary>
    public sealed class EntryItem : CPluginListItem
    {
        public EntryItem(int id, EntryNode e) : base(id, e) { }
        public EntryNode Entry { get { return (EntryNode)Tag; } }

        public override string ToString()
        {
            EntryNode e = Entry;
            string s = e.IsTeil
                ? e.An + " › " + (e.Name.Length > 0 ? e.Name : e.Kind.ToString())
                : (e.Name.Length > 0 ? e.Name : e.Icon.ToString());
            return e.Ko.ID == 0 ? s + " " + EnergiePlugin.T("#99-3_free") : s;
        }
    }
    public sealed class EnergiePlugin : IPlugin
    {
        const int FieldHint = 1, FieldCredit = 2;
        const int FieldKoBase = 101;                       // + Index in EnergieSettings.KoIds
        const int FieldKind = 1, FieldName = 2, FieldIcon = 3, FieldAn = 4, FieldVbKo = 201;
        const int MaxEntries = EnergieSettings.MaxVb + 4 * EnergieSettings.MaxTeil;

        static string _lang = "de";
        internal static bool German { get { return _lang == "de"; } }

        int _nextId = 1;
        readonly CPluginLinkCObject[] _kos = new CPluginLinkCObject[EnergieSettings.KoIds.Length];
        readonly List<EntryItem> _vbs = new List<EntryItem>();
        EntryNode _current;
        readonly List<CGridFieldBase> _shown = new List<CGridFieldBase>();

        public EnergiePlugin()
        {
            for (int i = 0; i < _kos.Length; i++) _kos[i] = new CPluginLinkCObject();
        }

        public override string ToString() { return T("#99-3_title"); }

        // ---- Eigenschaften, an die das Raster bindet ----

        public string Hint { get { return T("#99-3_hint_text"); } set { } }
        public string Credit { get { return T("#99-3_credit_text"); } set { } }

        public CPluginLinkCObject KoPv { get { return _kos[0]; } set { if (value != null) _kos[0] = value; } }
        public CPluginLinkCObject KoBat { get { return _kos[1]; } set { if (value != null) _kos[1] = value; } }
        public CPluginLinkCObject KoHaus { get { return _kos[2]; } set { if (value != null) _kos[2] = value; } }
        public CPluginLinkCObject KoNetz { get { return _kos[3]; } set { if (value != null) _kos[3] = value; } }
        public CPluginLinkCObject KoSoc { get { return _kos[4]; } set { if (value != null) _kos[4] = value; } }
        public CPluginLinkCObject KoBatState { get { return _kos[5]; } set { if (value != null) _kos[5] = value; } }
        public CPluginLinkCObject KoPvKwh { get { return _kos[6]; } set { if (value != null) _kos[6] = value; } }
        public CPluginLinkCObject KoEvKwh { get { return _kos[7]; } set { if (value != null) _kos[7] = value; } }
        public CPluginLinkCObject KoEvPct { get { return _kos[8]; } set { if (value != null) _kos[8] = value; } }
        public CPluginLinkCObject KoAutPct { get { return _kos[9]; } set { if (value != null) _kos[9] = value; } }
        public CPluginLinkCObject KoEinsp { get { return _kos[10]; } set { if (value != null) _kos[10] = value; } }
        public CPluginLinkCObject KoBezug { get { return _kos[11]; } set { if (value != null) _kos[11] = value; } }

        static readonly string[] KoMembers = { "KoPv", "KoBat", "KoHaus", "KoNetz", "KoSoc", "KoBatState", "KoPvKwh", "KoEvKwh", "KoEvPct", "KoAutPct", "KoEinsp", "KoBezug" };

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
                    using (System.IO.Stream st = typeof(EnergiePlugin).Assembly.GetManifestResourceStream("QcEnergie.config_lang.xml"))
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
            Trace("FEHLER " + msg);
            try { CQConfig.FW.LogError("Energiefluss " + EnergieSettings.PluginId + ": " + msg); } catch { }
        }

        static readonly object TraceLock = new object();

        /// <summary>
        /// Protokoll für die Fehlersuche: %LOCALAPPDATA%\QC-Plugin\99-3\experte.log. Hält fest, was QuadConfig lädt und
        /// speichert (Zeit, Prozess, Instanz, Länge, Inhalt). Enthält KO-IDs und Namen, keine Zugangsdaten.
        /// </summary>
        internal static void Trace(string msg)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QC-Plugin", EnergieSettings.PluginId);
                string file = System.IO.Path.Combine(dir, "experte.log");
                lock (TraceLock)
                {
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.FileInfo fi = new System.IO.FileInfo(file);
                    if (fi.Exists && fi.Length > 200000) System.IO.File.Delete(file);
                    System.IO.File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + System.Diagnostics.Process.GetCurrentProcess().Id + "] " + msg + "\r\n", System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }

        static string Show(byte[] data)
        {
            if (data == null) return "null";
            string s = System.Text.Encoding.UTF8.GetString(data).TrimStart((char)0xFEFF);
            return data.Length + " Byte: " + (s.Length > 1500 ? s.Substring(0, 1500) + " …" : s);
        }

        /// <summary>Daten, die sich beim Laden nicht lesen ließen; sie werden unverändert zurückgegeben, solange nichts geändert wurde.</summary>
        byte[] _unreadable;
        readonly int _instance = System.Threading.Interlocked.Increment(ref _instances);
        static int _instances;

        internal static void SetLink(CPluginLinkCObject l, int id)
        {
            if (id == 0) { l.Reset(); return; }
            try { l.SetID(id); }                              // löst die Bezeichnung des KOs auf
            catch { l.ID = id; l.Text = "?"; }
        }

        EnergieSettings ToSettings()
        {
            EnergieSettings s = new EnergieSettings();
            for (int i = 0; i < _kos.Length; i++) s.Kos[EnergieSettings.KoIds[i]] = _kos[i].ID;
            foreach (EntryItem i in _vbs)
            {
                if (i.Entry.IsTeil) s.Teile.Add(i.Entry.ToTeil());
                else s.Vbs.Add(i.Entry.ToVb());
            }
            s.AssignIds();
            return s;
        }

        /// <summary>Verweis, der zum Feld des gerade gezeigten Rasters gehört.</summary>
        CPluginLinkCObject LinkOfField(int fieldID)
        {
            if (_current != null) return fieldID == FieldVbKo ? _current.Ko : null;
            int i = fieldID - FieldKoBase;
            return i >= 0 && i < _kos.Length ? _kos[i] : null;
        }

        IEnumerable<CPluginLinkCObject> AllLinks()
        {
            foreach (CPluginLinkCObject l in _kos) yield return l;
            foreach (EntryItem i in _vbs) yield return i.Entry.Ko;
        }

        // ---- IPlugin ----

        public void Init(string _defaultLang, System.Xml.XmlReader _configXml, CPluginImageManager _images)
        {
            Trace("Instanz " + _instance + " Init, Sprache " + _defaultLang);
            if (!string.IsNullOrEmpty(_defaultLang)) _lang = _defaultLang.ToLowerInvariant() == "de" ? "de" : "en";
            if (_lang != _builtinLang) { _builtinLang = _lang; _builtin = null; }
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
            List<CGridFieldBase> f = _fields.Fields;
            _fields.Title = T("#99-3_title");
            CGridFieldBase hint = Std(FieldHint, "#99-3_hint", this, "Hint");
            hint.Enabled = false;
            f.Add(hint);
            CGridFieldBase credit = Std(FieldCredit, "#99-3_credit", this, "Credit");
            credit.Enabled = false;
            f.Add(credit);
            for (int i = 0; i < _kos.Length; i++)
            {
                string key = "#99-3_ko_" + EnergieSettings.KoIds[i];
                f.Add(new CGridFieldCobject(FieldKoBase + i, T(key), T(key + "_info"), this, KoMembers[i]));
            }
            _shown.AddRange(f);
        }

        public void OnSelectChild(CPluginListItem _child, CGridFields _fields)
        {
            EntryItem item = _child as EntryItem;
            _shown.Clear();
            _current = item != null ? item.Entry : null;
            if (_current == null) return;
            EntryNode v = _current;
            List<CGridFieldBase> f = _fields.Fields;
            _fields.Title = T("#99-3_entry_title");
            f.Add(Lst(FieldKind, "#99-3_kind", EntryNode.KindList, v, "Kind"));
            f.Add(Std(FieldName, "#99-3_vb_name", v, "Name"));
            f.Add(Lst(FieldIcon, "#99-3_vb_icon", EntryNode.IconList, v, "Icon"));
            f.Add(Lst(FieldAn, "#99-3_vb_an", EntryNode.AnList, v, "An"));
            f.Add(new CGridFieldCobject(FieldVbKo, T("#99-3_vb_ko"), T("#99-3_vb_ko_info"), v, "Ko"));
            _shown.AddRange(f);
        }

        public void OnGridChanged() { }

        public CPluginListItems GetChilds()
        {
            CPluginListItems l = new CPluginListItems();
            l.Sort = false;
            foreach (EntryItem i in _vbs) l.Items.Add(i);
            return l;
        }

        // Freie Liste: Verbraucher und Einzelwerte anlegen, löschen, verschieben.
        public CPluginListItem CreateChild()
        {
            if (_vbs.Count >= MaxEntries) return null;
            EntryItem i = new EntryItem(_nextId++, new EntryNode());
            _vbs.Add(i);
            return i;
        }

        public bool DeleteChild(CPluginListItem _child)
        {
            EntryItem i = _child as EntryItem;
            if (i == null || !_vbs.Remove(i)) return false;
            if (_current == i.Entry) _current = null;
            return true;
        }

        public bool CanAddChilds() { return _vbs.Count < MaxEntries; }
        public bool CanDeleteChilds() { return true; }
        public bool CanMoveChilds() { return true; }

        void Move(CPluginListItem child, int step)
        {
            int i = _vbs.IndexOf(child as EntryItem), j = i + step;
            if (i < 0 || j < 0 || j >= _vbs.Count) return;
            EntryItem t = _vbs[i];
            _vbs[i] = _vbs[j];
            _vbs[j] = t;
        }

        public void MoveChildUp(CPluginListItem _child) { Move(_child, -1); }
        public void MoveChildDown(CPluginListItem _child) { Move(_child, 1); }

        public void LoadFromArray(byte[] _data)
        {
            Trace("Instanz " + _instance + " LoadFromArray " + Show(_data));
            _unreadable = null;
            EnergieSettings s;
            try { s = EnergieSettings.Parse(_data); }
            catch (Exception ex)
            {
                // Nichts überschreiben: Die Datei bleibt beim nächsten Speichern erhalten, bis hier etwas eingetragen wird.
                _unreadable = _data;
                Log("LoadFromArray, Einstellungen nicht lesbar: " + ex);
                return;
            }
            _vbs.Clear();
            _current = null;
            for (int i = 0; i < _kos.Length; i++)
            {
                try { SetLink(_kos[i], s.Kos[EnergieSettings.KoIds[i]]); }
                catch (Exception ex) { Log("LoadFromArray, KO " + EnergieSettings.KoIds[i] + ": " + ex.Message); }
            }
            foreach (Verbraucher v in s.Vbs)
            {
                try { _vbs.Add(new EntryItem(_nextId++, EntryNode.From(v))); }
                catch (Exception ex) { Log("LoadFromArray, Verbraucher " + v.Id + ": " + ex.Message); }
            }
            foreach (Teil q in s.Teile)
            {
                try { _vbs.Add(new EntryItem(_nextId++, EntryNode.From(q))); }
                catch (Exception ex) { Log("LoadFromArray, Einzelwert " + q.An + ": " + ex.Message); }
            }
            Trace("Instanz " + _instance + " geladen: " + Count());
        }

        string Count()
        {
            int kos = 0, withKo = 0;
            foreach (CPluginLinkCObject l in _kos) if (l.ID != 0) kos++;
            foreach (EntryItem i in _vbs) if (i.Entry.Ko.ID != 0) withKo++;
            return kos + " Grund-KOs, " + _vbs.Count + " Einträge (" + withKo + " mit KO)";
        }

        public void SaveToArray(out byte[] _data)
        {
            _data = new byte[0];
            try
            {
                EnergieSettings s = ToSettings();
                if (_unreadable != null && !s.HasAny && _vbs.Count == 0)
                {
                    _data = _unreadable;                    // unlesbare Datei nicht durch eine leere ersetzen
                    Trace("Instanz " + _instance + " SaveToArray: unlesbare Daten unverändert zurück, " + _data.Length + " Byte");
                    return;
                }
                _data = s.ToBytes();
                Trace("Instanz " + _instance + " SaveToArray (" + Count() + ") " + Show(_data));
            }
            catch (Exception ex) { Log("SaveToArray: " + ex); }
        }
        public void GetLinks(ref List<CPluginLink> _lst)
        {
            foreach (CPluginLinkCObject l in AllLinks()) _lst.Add(l);
        }

        public bool Validate(ref List<CPluginErrorItem> _lst)
        {
            EnergieSettings s = ToSettings();
            string name = T("#99-3_pluginname") + ": ";
            if (!s.HasAny) _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, name + T("#99-3_no_ko")));
            int vb = 0;
            foreach (Verbraucher v in s.Vbs) if (v.Tag != 0) vb++;
            if (vb > EnergieSettings.MaxVb) _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, name + T("#99-3_too_many_vb")));
            foreach (string an in EnergieSettings.Ans)
            {
                int n = 0;
                foreach (Teil q in s.Teile) if (q.An == an && q.Tag != 0) n++;
                if (n > EnergieSettings.MaxTeil)
                    _lst.Add(new CPluginErrorItem(EPluginErrorMsgType.Warning, name + T("#99-3_too_many_teil") + " (" + T("#99-3_an_" + an) + ")"));
            }
            return true;
        }

        public void OnItemChanged(CHsBase _item, ItemChangedTypes _type)
        {
            if (_item == null) return;
            foreach (CPluginLinkCObject l in AllLinks())
            {
                if (l.ID == 0 || l.ID != _item.ID) continue;
                if (_type == ItemChangedTypes.Removed)
                {
                    Trace("Instanz " + _instance + " OnItemChanged Removed: KO " + l.ID + " (" + _item.GetType().Name + ") zurückgesetzt");
                    l.Reset();
                }
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

        public CPluginListItem DropOverList(CHsBase _item) { return null; }
        public void Free() { Trace("Instanz " + _instance + " Free (" + Count() + ")"); _shown.Clear(); _current = null; }
    }
}
