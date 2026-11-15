// QC-Plugin 99-2 "Kamera" – Einstellungen einer Plugin-Instanz als XML (Format siehe README).
// Gemeinsam für plugin_config.dll (Experte) und 99-2_client.dll (QuadClient).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace QcKamera
{
    sealed class CamSettings
    {
        public string Name = "", Host = "", User = "", Password = "";
        public int Port = 80;
        /// <summary>"auto", "rtsp", "mjpeg" oder "jpeg".</summary>
        public string Mode = "auto";
        /// <summary>RTSP-Adresse für den Quadranten (kleiner Stream) und für das Vollbild (großer Stream, optional).</summary>
        public string Rtsp = "", RtspFull = "";
        public string Mjpeg = "", Jpeg = "";
        /// <summary>Abstand der Einzelbilder in Millisekunden.</summary>
        public int Refresh = 1000;
        /// <summary>KO-ID: Wert ungleich 0 schaltet auf diese Kamera um.</summary>
        public int KoShow;

        /// <summary>Tatsächlich benutzte Quelle: "rtsp", "mjpeg", "jpeg" oder "" (nichts eingestellt).</summary>
        public string Source
        {
            get
            {
                if (Mode == "rtsp" && Rtsp.Length + RtspFull.Length > 0) return "rtsp";
                if (Mode == "mjpeg" && Mjpeg.Length > 0) return "mjpeg";
                if (Mode == "jpeg" && Jpeg.Length > 0) return "jpeg";
                return Rtsp.Length + RtspFull.Length > 0 ? "rtsp" : Mjpeg.Length > 0 ? "mjpeg" : Jpeg.Length > 0 ? "jpeg" : "";
            }
        }

        public string RtspFor(bool full)
        {
            return full ? (RtspFull.Length > 0 ? RtspFull : Rtsp) : (Rtsp.Length > 0 ? Rtsp : RtspFull);
        }
    }

    sealed class KameraSettings
    {
        public const string PluginId = "99-2";
        public const int FormatVersion = 1;
        public static readonly string[] Modes = { "auto", "rtsp", "mjpeg", "jpeg" };

        public readonly List<CamSettings> Cams = new List<CamSettings>();

        static string A(XmlReader x, string name) { return (x.GetAttribute(name) ?? "").Trim(); }

        public static KameraSettings Parse(byte[] data)
        {
            KameraSettings s = new KameraSettings();
            if (data == null || data.Length == 0) return s;
            CamSettings cur = null;
            using (MemoryStream ms = new MemoryStream(data))
            using (XmlReader x = XmlReader.Create(ms))
            {
                while (x.Read())
                {
                    if (x.NodeType != XmlNodeType.Element) continue;
                    if (x.Name == "cam")
                    {
                        cur = new CamSettings();
                        cur.Name = A(x, "name"); cur.Host = A(x, "host"); cur.User = A(x, "user");
                        cur.Password = x.GetAttribute("pass") ?? "";
                        int n;
                        if (int.TryParse(A(x, "port"), out n) && n > 0 && n < 65536) cur.Port = n;
                        if (int.TryParse(A(x, "refresh"), out n)) cur.Refresh = Math.Max(100, Math.Min(60000, n));
                        string m = A(x, "mode");
                        if (Array.IndexOf(Modes, m) >= 0) cur.Mode = m;
                        cur.Rtsp = A(x, "rtsp"); cur.RtspFull = A(x, "rtsp_full"); cur.Mjpeg = A(x, "mjpeg"); cur.Jpeg = A(x, "jpeg");
                        s.Cams.Add(cur);
                    }
                    else if (x.Name == "ko" && cur != null && A(x, "id") == "show")
                    {
                        int tag;
                        if (int.TryParse(A(x, "tag"), out tag)) cur.KoShow = tag;
                    }
                }
            }
            return s;
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlWriter x = XmlWriter.Create(ms))
                {
                    x.WriteStartElement("project");
                    x.WriteAttributeString("version", FormatVersion.ToString(CultureInfo.InvariantCulture));
                    foreach (CamSettings c in Cams)
                    {
                        x.WriteStartElement("cam");
                        x.WriteAttributeString("name", c.Name);
                        x.WriteAttributeString("host", c.Host);
                        x.WriteAttributeString("port", c.Port.ToString(CultureInfo.InvariantCulture));
                        x.WriteAttributeString("user", c.User);
                        x.WriteAttributeString("pass", c.Password);
                        x.WriteAttributeString("mode", c.Mode);
                        x.WriteAttributeString("rtsp", c.Rtsp);
                        x.WriteAttributeString("rtsp_full", c.RtspFull);
                        x.WriteAttributeString("mjpeg", c.Mjpeg);
                        x.WriteAttributeString("jpeg", c.Jpeg);
                        x.WriteAttributeString("refresh", c.Refresh.ToString(CultureInfo.InvariantCulture));
                        if (c.KoShow != 0)
                        {
                            x.WriteStartElement("ko");
                            x.WriteAttributeString("id", "show");
                            x.WriteAttributeString("tag", c.KoShow.ToString(CultureInfo.InvariantCulture));
                            x.WriteEndElement();
                        }
                        x.WriteEndElement();
                    }
                    x.WriteEndElement();
                    x.Flush();
                }
                return ms.ToArray();
            }
        }
    }
}
