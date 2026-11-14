// Abfragen bei Open-Meteo (https://open-meteo.com, kostenlos, ohne Schlüssel, Daten CC BY 4.0).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace QcWetter
{
    public sealed class GeoHit
    {
        public string Name = "";
        public string Region = "";
        public double Lat;
        public double Lon;

        public override string ToString()
        {
            return Region.Length > 0 ? Name + " (" + Region + ")" : Name;
        }
    }

    public static class OpenMeteo
    {
        public const string SourceText = "Open-Meteo.com (CC BY 4.0)";

        public static string ForecastUrl(WetterSettings s)
        {
            return "https://api.open-meteo.com/v1/forecast?latitude=" + WetterSettings.Num(s.Lat)
                + "&longitude=" + WetterSettings.Num(s.Lon)
                + "&timezone=auto&forecast_days=7"
                + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_direction_10m,precipitation,is_day"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_direction_10m_dominant"
                + "&wind_speed_unit=" + (s.Wu == "ms" ? "ms" : "kmh")
                + "&temperature_unit=" + (s.Tu == "fahrenheit" ? "fahrenheit" : "celsius");
        }

        public static string GeocodeUrl(string name, string lang)
        {
            return "https://geocoding-api.open-meteo.com/v1/search?count=6&language=" + lang + "&format=json&name=" + Uri.EscapeDataString(name);
        }

        /// <summary>Ortssuche; leere Liste, wenn nichts gefunden wurde. Wirft bei Netzfehlern.</summary>
        public static List<GeoHit> Geocode(string name, string lang, int timeoutMs)
        {
            List<GeoHit> hits = new List<GeoHit>();
            object root = Json.Parse(HttpGet(GeocodeUrl(name, lang), timeoutMs));
            List<object> res = Json.Get(root, "results") as List<object>;
            if (res == null) return hits;
            foreach (object o in res)
            {
                double? lat = Json.Number(Json.Get(o, "latitude")), lon = Json.Number(Json.Get(o, "longitude"));
                if (lat == null || lon == null) continue;
                GeoHit h = new GeoHit();
                h.Name = Json.Get(o, "name") as string ?? "";
                h.Lat = lat.Value;
                h.Lon = lon.Value;
                List<string> parts = new List<string>();
                string a1 = Json.Get(o, "admin1") as string, c = Json.Get(o, "country") as string;
                if (!string.IsNullOrEmpty(a1)) parts.Add(a1);
                if (!string.IsNullOrEmpty(c)) parts.Add(c);
                h.Region = string.Join(", ", parts.ToArray());
                hits.Add(h);
            }
            return hits;
        }

        public static string HttpGet(string url, int timeoutMs)
        {
            // Ältere .NET-Zielversionen (4.5.x) schalten TLS 1.2 nicht von selbst ein; nur hinzufügen, nichts abschalten.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "GET";
            rq.Timeout = timeoutMs;
            rq.ReadWriteTimeout = timeoutMs;
            rq.UserAgent = "QC-Plugin-Wetter/" + WetterSettings.PluginId;
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader rd = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                return rd.ReadToEnd();
        }
    }
}
