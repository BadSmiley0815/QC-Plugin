// QC-Plugin 99-1 "Wetter" – Einstellungen einer Instanz (Projektdaten).
// Dieses Format wird vom Experten gespeichert (SaveToArray), vom QuadClient gelesen (onProjectData)
// und ist im README beschrieben, damit auch der QC-Client (qc.html) es lesen kann.
//
//   <project version="1" name="Nürnberg" lat="49.45" lon="11.08" interval="60" tu="celsius" wu="kmh" kowu="ms">
//     <ko id="temp" tag="1234"/>   (nur zugewiesene KOs; tag = KO-ID des HS, wie sie der QuadClient verwendet)
//   </project>
//
// Hinweis: nur C# 5 (eingebauter Compiler des .NET Framework 4).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace QcWetter
{
    public sealed class WetterSettings
    {
        public const string PluginId = "99-1";
        public const int FormatVersion = 1;

        /// <summary>Kennungen der KOs in fester Reihenfolge.</summary>
        public static readonly string[] KoIds = { "temp", "feel", "hum", "wind", "dir", "rain", "lux", "lux_n", "lux_e", "lux_s", "lux_w", "press" };

        public string Name = "";
        public double Lat = double.NaN;
        public double Lon = double.NaN;
        /// <summary>Aktualisierung der Vorhersage in Minuten.</summary>
        public int Interval = 60;
        /// <summary>Anzeige-Einheit Temperatur: "celsius" oder "fahrenheit".</summary>
        public string Tu = "celsius";
        /// <summary>Anzeige-Einheit Wind: "kmh" oder "ms".</summary>
        public string Wu = "kmh";
        /// <summary>Einheit, in der das Wind-KO der Wetterstation sendet: "ms" oder "kmh".</summary>
        public string KoWu = "ms";
        /// <summary>KO-Kennung → KO-ID (0 = nicht zugewiesen).</summary>
        public readonly Dictionary<string, int> Kos = new Dictionary<string, int>();

        public WetterSettings()
        {
            foreach (string k in KoIds) Kos[k] = 0;
        }

        public bool HasLocation
        {
            get { return !double.IsNaN(Lat) && !double.IsNaN(Lon) && Lat >= -90 && Lat <= 90 && Lon >= -180 && Lon <= 180; }
        }

        public string TempUnitText { get { return Tu == "fahrenheit" ? "°F" : "°C"; } }
        public string WindUnitText { get { return Wu == "ms" ? "m/s" : "km/h"; } }

        public static string Num(double v)
        {
            return double.IsNaN(v) ? "" : v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Zahl lesen; Punkt oder Komma als Dezimaltrenner. NaN, wenn leer oder ungültig.</summary>
        public static double ParseNum(string s)
        {
            if (s == null) return double.NaN;
            s = s.Trim().Replace(',', '.');
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        public static WetterSettings Parse(byte[] data)
        {
            WetterSettings s = new WetterSettings();
            if (data == null || data.Length == 0) return s;
            using (MemoryStream ms = new MemoryStream(data))
            using (XmlReader xml = XmlReader.Create(ms))
            {
                while (xml.Read())
                {
                    if (xml.NodeType != XmlNodeType.Element) continue;
                    if (xml.Name == "project")
                    {
                        s.Name = xml.GetAttribute("name") ?? "";
                        s.Lat = ParseNum(xml.GetAttribute("lat"));
                        s.Lon = ParseNum(xml.GetAttribute("lon"));
                        int iv;
                        if (int.TryParse(xml.GetAttribute("interval"), out iv)) s.Interval = Math.Max(15, Math.Min(1440, iv));
                        if (xml.GetAttribute("tu") == "fahrenheit") s.Tu = "fahrenheit";
                        if (xml.GetAttribute("wu") == "ms") s.Wu = "ms";
                        if (xml.GetAttribute("kowu") == "kmh") s.KoWu = "kmh";
                    }
                    else if (xml.Name == "ko")
                    {
                        string id = xml.GetAttribute("id");
                        int tag;
                        if (id != null && s.Kos.ContainsKey(id) && int.TryParse(xml.GetAttribute("tag"), out tag)) s.Kos[id] = tag;
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
                    xml.WriteAttributeString("name", Name ?? "");
                    xml.WriteAttributeString("lat", Num(Lat));
                    xml.WriteAttributeString("lon", Num(Lon));
                    xml.WriteAttributeString("interval", Interval.ToString(CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("tu", Tu);
                    xml.WriteAttributeString("wu", Wu);
                    xml.WriteAttributeString("kowu", KoWu);
                    foreach (string k in KoIds)
                    {
                        if (Kos[k] == 0) continue;
                        xml.WriteStartElement("ko");
                        xml.WriteAttributeString("id", k);
                        xml.WriteAttributeString("tag", Kos[k].ToString(CultureInfo.InvariantCulture));
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
