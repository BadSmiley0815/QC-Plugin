// QC-Plugin 99-3 "Energiefluss mit Verbrauchern" – Einstellungen einer Instanz (Projektdaten).
// Dieses Format wird vom Experten gespeichert (SaveToArray), vom QuadClient gelesen (onProjectData)
// und ist im README beschrieben; der QC-Client (qc.html) liest es unter der Klasse 99-3.
//
//   <project version="1">
//     <ko id="pv" tag="1234"/>                 (nur zugewiesene KOs; tag = KO-ID des HS)
//     <vb id="wb1" name="Wallbox Garage" icon="wallbox" an="haus" tag="1250"/>
//     <teil an="pv" name="Wechselrichter Süd" tag="1260"/>
//   </project>
//
// Idee und Vorbild der Anzeige: Plugin "Energiefluss" (180-1) von johannesbonn (KNX-User-Forum).
// Aus dessen Paket ist nichts übernommen; Zeichnung, Symbole und Code sind eigene.
//
// Hinweis: nur C# 5 (eingebauter Compiler des .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace QcEnergie
{
    /// <summary>Ein Verbraucher: kleiner Kreis mit Symbol an einem Anschlusspunkt.</summary>
    public sealed class Verbraucher
    {
        /// <summary>wb1…, wm, sm, tr, s1…: gibt nur Vorgabe-Name und -Symbol; wird beim Speichern aus dem Symbol vergeben.</summary>
        public string Id = "s1";
        /// <summary>Frei; leer = Vorgabe nach der Kennung.</summary>
        public string Name = "";
        /// <summary>Einer aus EnergieSettings.Icons; leer = Vorgabe nach der Kennung.</summary>
        public string Icon = "";
        /// <summary>Anschlusspunkt: haus, pv, bat oder netz.</summary>
        public string An = "haus";
        /// <summary>KO der Leistung in W (0 = nicht belegt).</summary>
        public int Tag;

        string Kind { get { return Id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'); } }
        string Number { get { return Id.Substring(Kind.Length); } }

        public string DefaultIcon
        {
            get
            {
                switch (Kind)
                {
                    case "wb": return "wallbox";
                    case "wm": return "wasch";
                    case "sm": return "spuel";
                    case "tr": return "trockner";
                    default: return "steckdose";
                }
            }
        }

        public string DefaultName(bool german)
        {
            string n;
            switch (Kind)
            {
                case "wb": n = "Wallbox"; break;
                case "wm": n = german ? "Waschmaschine" : "Washing machine"; break;
                case "sm": n = german ? "Spülmaschine" : "Dishwasher"; break;
                case "tr": n = german ? "Trockner" : "Dryer"; break;
                default: n = german ? "Verbraucher" : "Load"; break;
            }
            return Number.Length > 0 ? n + " " + Number : n;
        }

        public string ShownName(bool german) { return Name.Length > 0 ? Name : DefaultName(german); }
        public string ShownIcon { get { return Icon.Length > 0 ? Icon : DefaultIcon; } }
    }

    /// <summary>Ein Einzelwert hinter einem Hauptkreis (z. B. ein Wechselrichter hinter „PV-Anlage“).</summary>
    public sealed class Teil
    {
        /// <summary>Kreis: pv, bat, netz oder haus.</summary>
        public string An = "pv";
        /// <summary>Frei; leer = „Wechselrichter n“, „Speicher n“, „Zähler n“, „Bereich n“.</summary>
        public string Name = "";
        /// <summary>KO der Leistung in W (0 = nicht belegt).</summary>
        public int Tag;

        public static string DefaultName(string an, int number, bool german)
        {
            string n;
            switch (an)
            {
                case "pv": n = german ? "Wechselrichter" : "Inverter"; break;
                case "bat": n = german ? "Speicher" : "Battery"; break;
                case "netz": n = german ? "Zähler" : "Meter"; break;
                default: n = german ? "Bereich" : "Area"; break;
            }
            return n + " " + number.ToString(CultureInfo.InvariantCulture);
        }
    }

    public sealed class EnergieSettings
    {
        public const string PluginId = "99-3";
        public const int FormatVersion = 1;
        /// <summary>So viele Verbraucher zeigt die Anzeige höchstens.</summary>
        public const int MaxVb = 17;
        /// <summary>So viele Einzelwerte zeigt die Anzeige je Kreis höchstens.</summary>
        public const int MaxTeil = 8;

        /// <summary>Kennungen der Grund-KOs in fester Reihenfolge (dieselben zwölf Bedeutungen wie bei 180-1).</summary>
        public static readonly string[] KoIds = { "pv", "bat", "haus", "netz", "soc", "batstate", "pvkwh", "evkwh", "evpct", "autpct", "einsp", "bezug" };
        public static readonly string[] Icons = { "wallbox", "auto", "wasch", "spuel", "trockner", "herd", "kuehl", "wp", "boiler", "klima", "licht", "steckdose", "pool", "tv", "pc", "sonst" };
        public static readonly string[] Ans = { "haus", "pv", "bat", "netz" };

        /// <summary>KO-Kennung → KO-ID (0 = nicht zugewiesen).</summary>
        public readonly Dictionary<string, int> Kos = new Dictionary<string, int>();
        /// <summary>Verbraucher in der Reihenfolge der Datei; sie bestimmt die Plätze in der Anzeige.</summary>
        public readonly List<Verbraucher> Vbs = new List<Verbraucher>();
        /// <summary>Einzelwerte aller Kreise in der Reihenfolge der Datei.</summary>
        public readonly List<Teil> Teile = new List<Teil>();

        public EnergieSettings()
        {
            foreach (string k in KoIds) Kos[k] = 0;
        }

        /// <summary>Ist irgendein KO zugewiesen?</summary>
        public bool HasAny
        {
            get
            {
                foreach (string k in KoIds) if (Kos[k] != 0) return true;
                foreach (Verbraucher v in Vbs) if (v.Tag != 0) return true;
                foreach (Teil t in Teile) if (t.Tag != 0) return true;
                return false;
            }
        }

        /// <summary>Verbraucher mit KO, höchstens MaxVb – das zeigt die Anzeige.</summary>
        public List<Verbraucher> ShownVbs()
        {
            List<Verbraucher> l = new List<Verbraucher>();
            foreach (Verbraucher v in Vbs)
                if (v.Tag != 0 && l.Count < MaxVb) l.Add(v);
            return l;
        }

        /// <summary>Einzelwerte eines Kreises mit KO, höchstens MaxTeil.</summary>
        public List<Teil> ShownTeile(string an)
        {
            List<Teil> l = new List<Teil>();
            foreach (Teil t in Teile)
                if (t.An == an && t.Tag != 0 && l.Count < MaxTeil) l.Add(t);
            return l;
        }

        /// <summary>
        /// Vergibt die Kennungen der Verbraucher nach ihrem Symbol: Wallbox/Auto → wb1…wb4, die erste Waschmaschine → wm,
        /// die erste Spülmaschine → sm, der erste Trockner → tr, alles andere → s1, s2, …
        /// </summary>
        public void AssignIds()
        {
            int wb = 0, s = 0;
            bool wm = false, sm = false, tr = false;
            foreach (Verbraucher v in Vbs)
            {
                string icon = v.ShownIcon;
                if ((icon == "wallbox" || icon == "auto") && wb < 4) v.Id = "wb" + (++wb);
                else if (icon == "wasch" && !wm) { v.Id = "wm"; wm = true; }
                else if (icon == "spuel" && !sm) { v.Id = "sm"; sm = true; }
                else if (icon == "trockner" && !tr) { v.Id = "tr"; tr = true; }
                else v.Id = "s" + (++s);
                v.Icon = icon;                              // die Kennung trägt jetzt eine andere Vorgabe, das Symbol bleibt
            }
        }

        /// <summary>
        /// Entfernt Zeichen, die in XML nicht vorkommen dürfen (Steuerzeichen, einzelne Surrogate). Ein solches Zeichen
        /// in einem Namen ließe sonst das Schreiben der ganzen Einstellungen scheitern.
        /// </summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            System.Text.StringBuilder b = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b.Append(c).Append(s[++i]); continue; }
                if (c == '\t' || c == '\n' || c == '\r') { b.Append(' '); continue; }
                if (c < 0x20 || char.IsSurrogate(c) || c == 0xFFFE || c == 0xFFFF) continue;
                b.Append(c);
            }
            return b.ToString().Trim();
        }

        static int ParseTag(string s)
        {
            int t;
            return s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out t) && t > 0 ? t : 0;
        }

        static bool ValidId(string id)
        {
            string kind = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return kind == "wb" || kind == "wm" || kind == "sm" || kind == "tr" || kind == "s";
        }

        public static EnergieSettings Parse(byte[] data)
        {
            EnergieSettings s = new EnergieSettings();
            if (data == null || data.Length == 0) return s;
            using (MemoryStream ms = new MemoryStream(data))
            using (XmlReader xml = XmlReader.Create(ms))
            {
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element) continue;
                    if (xml.Name == "ko")
                    {
                        string id = xml.GetAttribute("id");
                        if (id != null && s.Kos.ContainsKey(id)) s.Kos[id] = ParseTag(xml.GetAttribute("tag"));
                    }
                    else if (xml.Name == "vb")
                    {
                        Verbraucher v = new Verbraucher();
                        string id = (xml.GetAttribute("id") ?? "").Trim();
                        v.Id = ValidId(id) ? id : "s" + (s.Vbs.Count + 1).ToString(CultureInfo.InvariantCulture);
                        v.Name = (xml.GetAttribute("name") ?? "").Trim();
                        string icon = xml.GetAttribute("icon") ?? "", an = xml.GetAttribute("an") ?? "";
                        v.Icon = Array.IndexOf(Icons, icon) >= 0 ? icon : "";
                        v.An = Array.IndexOf(Ans, an) >= 0 ? an : "haus";
                        v.Tag = ParseTag(xml.GetAttribute("tag"));
                        s.Vbs.Add(v);
                    }
                    else if (xml.Name == "teil")
                    {
                        string an = xml.GetAttribute("an") ?? "";
                        if (Array.IndexOf(Ans, an) < 0) continue;   // ohne gültigen Kreis nicht zuzuordnen
                        Teil t = new Teil();
                        t.An = an;
                        t.Name = (xml.GetAttribute("name") ?? "").Trim();
                        t.Tag = ParseTag(xml.GetAttribute("tag"));
                        s.Teile.Add(t);
                    }
                }
            }
            return s;
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlWriter xml = XmlWriter.Create(ms))
                {
                    xml.WriteStartElement("project");
                    xml.WriteAttributeString("version", FormatVersion.ToString(CultureInfo.InvariantCulture));
                    foreach (string k in KoIds)
                    {
                        if (Kos[k] == 0) continue;
                        xml.WriteStartElement("ko");
                        xml.WriteAttributeString("id", k);
                        xml.WriteAttributeString("tag", Kos[k].ToString(CultureInfo.InvariantCulture));
                        xml.WriteEndElement();
                    }
                    // Auch Einträge ohne KO schreiben (tag="0"): so bleiben sie im Experten erhalten.
                    foreach (Verbraucher v in Vbs)
                    {
                        xml.WriteStartElement("vb");
                        xml.WriteAttributeString("id", v.Id);
                        xml.WriteAttributeString("name", Clean(v.Name));
                        xml.WriteAttributeString("icon", v.ShownIcon);
                        xml.WriteAttributeString("an", v.An);
                        xml.WriteAttributeString("tag", v.Tag.ToString(CultureInfo.InvariantCulture));
                        xml.WriteEndElement();
                    }
                    foreach (Teil t in Teile)
                    {
                        xml.WriteStartElement("teil");
                        xml.WriteAttributeString("an", t.An);
                        xml.WriteAttributeString("name", Clean(t.Name));
                        xml.WriteAttributeString("tag", t.Tag.ToString(CultureInfo.InvariantCulture));
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                    xml.Flush();
                }
                return ms.ToArray();
            }
        }
    }
}
