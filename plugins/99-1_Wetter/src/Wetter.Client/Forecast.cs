// Vorhersage von Open-Meteo: Modell, Auswertung der Antwort, WMO-Wettercodes.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace QcWetter.Client
{
    public enum Sky { Sun, Part, Cloud, Fog, Drizzle, Rain, Snow, Storm }

    public sealed class DayForecast
    {
        public DateTime Date;
        public double? Code, Max, Min, RainSum, RainProb, WindMax, WindDir;
    }

    public sealed class Forecast
    {
        public double? Temp, Feel, Hum, Code, Wind, WindDir, Precip;
        public bool IsDay = true;
        public readonly List<DayForecast> Days = new List<DayForecast>();

        /// <summary>Tage ab heute.</summary>
        public List<DayForecast> FromToday(DateTime today)
        {
            List<DayForecast> l = new List<DayForecast>();
            foreach (DayForecast d in Days) if (d.Date >= today.Date) l.Add(d);
            return l;
        }

        static double? At(object root, string name, int i)
        {
            List<object> a = Json.Get(root, "daily", name) as List<object>;
            return a != null && i < a.Count ? Json.Number(a[i]) : null;
        }

        public static Forecast Parse(string json)
        {
            object root = Json.Parse(json);
            Forecast f = new Forecast();
            f.Temp = Json.Number(Json.Get(root, "current", "temperature_2m"));
            f.Feel = Json.Number(Json.Get(root, "current", "apparent_temperature"));
            f.Hum = Json.Number(Json.Get(root, "current", "relative_humidity_2m"));
            f.Code = Json.Number(Json.Get(root, "current", "weather_code"));
            f.Wind = Json.Number(Json.Get(root, "current", "wind_speed_10m"));
            f.WindDir = Json.Number(Json.Get(root, "current", "wind_direction_10m"));
            f.Precip = Json.Number(Json.Get(root, "current", "precipitation"));
            double? isDay = Json.Number(Json.Get(root, "current", "is_day"));
            f.IsDay = isDay == null || isDay.Value != 0;

            List<object> time = Json.Get(root, "daily", "time") as List<object>;
            if (time == null) throw new FormatException("Antwort ohne Tageswerte");
            for (int i = 0; i < time.Count; i++)
            {
                DateTime dt;
                if (!DateTime.TryParseExact(time[i] as string, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) continue;
                DayForecast d = new DayForecast();
                d.Date = dt;
                d.Code = At(root, "weather_code", i);
                d.Max = At(root, "temperature_2m_max", i);
                d.Min = At(root, "temperature_2m_min", i);
                d.RainSum = At(root, "precipitation_sum", i);
                d.RainProb = At(root, "precipitation_probability_max", i);
                d.WindMax = At(root, "wind_speed_10m_max", i);
                d.WindDir = At(root, "wind_direction_10m_dominant", i);
                f.Days.Add(d);
            }
            return f;
        }

        /// <summary>WMO-Wettercode → Symbolart.</summary>
        public static Sky SkyOf(double? code)
        {
            if (code == null) return Sky.Cloud;
            switch ((int)code.Value)
            {
                case 0: return Sky.Sun;
                case 1: case 2: return Sky.Part;
                case 3: return Sky.Cloud;
                case 45: case 48: return Sky.Fog;
                case 51: case 53: case 55: case 56: case 57: return Sky.Drizzle;
                case 61: case 63: case 65: case 66: case 67: case 80: case 81: case 82: return Sky.Rain;
                case 71: case 73: case 75: case 77: case 85: case 86: return Sky.Snow;
                case 95: case 96: case 99: return Sky.Storm;
                default: return Sky.Cloud;
            }
        }

        static readonly string[] DirsDe = { "N", "NO", "O", "SO", "S", "SW", "W", "NW" };
        static readonly string[] DirsEn = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Windrichtung in Grad (0 = Nord) → Himmelsrichtung.</summary>
        public static string DirText(double? deg, bool german)
        {
            if (deg == null || double.IsNaN(deg.Value)) return "";
            double g = ((deg.Value % 360) + 360) % 360;
            int i = (int)Math.Round(g / 45.0) % 8;
            return german ? DirsDe[i] : DirsEn[i];
        }
    }
}
