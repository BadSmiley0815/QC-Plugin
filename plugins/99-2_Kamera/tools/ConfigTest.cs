// Prüft plugin_config.dll von 99-2 ohne Experte: Felder, Übersetzungen, Kameras anlegen/verschieben/löschen,
// ONVIF-Abfrage gegen die nachgebaute Kamera, KO-Zuweisung, Speichern und Wiederladen.
//
// Aufruf: ConfigTest.exe <config_lang.xml> [roh]      (roh = der Experte liefert keine Übersetzungen)
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using de.dacom_ha.config_framework;
using QcKamera.Config;

sealed class FakeKo : CHsBase
{
    readonly string _caption;

    public FakeKo(int id, int itemType, string caption)
    {
        BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(CHsBase).GetField("_ID", f).SetValue(this, id);
        typeof(CHsBase).GetField("_ItemType", f).SetValue(this, itemType);
        _caption = caption;
    }

    public override string GetCaption() { return _caption; }
}

static class ConfigTest
{
    static int _fail;
    static readonly Dictionary<string, string> Lang = new Dictionary<string, string>();
    static readonly Dictionary<int, FakeKo> Kos = new Dictionary<int, FakeKo>();

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok     " : "  FEHLER ") + what);
        if (!ok) _fail++;
    }

    static string Tr(string key)
    {
        string v;
        return Lang.TryGetValue(key, out v) ? v : key;
    }

    static void CheckFields(CGridFields fields)
    {
        foreach (CGridFieldBase f in fields.Fields)
        {
            PropertyInfo pi = f.Object.GetType().GetProperty(f.Member);
            Check(pi != null && pi.CanRead && pi.CanWrite, string.Format("{0,3} {1,-20} {2,-10} → {3}", f.ID, f.GetType().Name, f.Member, f.Text));
            Check(f.Text.Length > 0 && !f.Text.StartsWith("#") && f.Comment.Length > 0 && !f.Comment.StartsWith("#"), "      Text und Hinweis übersetzt");
            if (f is CGridFieldCobject) Check(pi != null && pi.PropertyType == typeof(CPluginLinkCObject), "      Typ CPluginLinkCObject");
            if (f is CGridFieldList) Check(((CGridFieldList)f).Extended.Length > 1 && !((CGridFieldList)f).Extended[0].ToString().StartsWith("#"), "      Auswahlliste übersetzt: " + ((CGridFieldList)f).Extended[0]);
        }
    }

    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using (XmlReader x = XmlReader.Create(a[0]))
            while (x.Read())
                if (x.NodeType == XmlNodeType.Element && x.Name == "text") Lang[x.GetAttribute("key")] = x.GetAttribute("default");
        bool noTranslate = a.Length > 1 && a[1] == "roh";

        // Umgebung des Experten nachstellen
        for (int i = 0; i < 3; i++) Kos[5000 + i] = new FakeKo(5000 + i, 11001, "KO Klingel " + i);
        CQConfig fw = CQConfig.FW;
        fw.dtTranslate = delegate(string k) { return noTranslate ? k : Tr(k); };
        fw.EventLogError = delegate(string m) { Console.WriteLine("  LogError: " + m); };
        fw.EventLogWarning = delegate(string m) { Console.WriteLine("  LogWarning: " + m); };
        fw.EventLogMsg = delegate(string m) { Console.WriteLine("  LogMsg: " + m); };
        fw.dgoGetObject = delegate(int id) { FakeKo k; return Kos.TryGetValue(id, out k) ? k : null; };
        fw.dgoGetQCObject = delegate(int id) { return null; };

        KameraPlugin p = new KameraPlugin();
        p.Init("de", null, null);
        Console.WriteLine("Felder der Instanz:");
        CGridFields node = new CGridFields();
        p.OnSelectNode(node);
        Check(node.Fields.Count == 1 && node.Title == "Kameras" && p.ToString() == "Kameras", "Titel „" + node.Title + "“");
        CheckFields(node);

        Console.WriteLine("Prüfung ohne Kamera:");
        List<CPluginErrorItem> errs = new List<CPluginErrorItem>();
        p.Validate(ref errs);
        Check(errs.Count == 1, "Warnung: " + (errs.Count > 0 ? errs[0].Text : "-"));

        Console.WriteLine("Kameras anlegen:");
        Check(p.CanAddChilds() && p.CanDeleteChilds() && p.CanMoveChilds(), "Liste erlaubt Anlegen, Löschen, Verschieben");
        CamItem c1 = (CamItem)p.CreateChild(), c2 = (CamItem)p.CreateChild(), c3 = (CamItem)p.CreateChild();
        Check(p.GetChilds().Items.Count == 3 && c1.ToString() == "Kamera 1" && c1.ID != c2.ID, "3 Kameras, erste heißt „" + c1 + "“");
        Console.WriteLine("Felder einer Kamera:");
        CGridFields fields = new CGridFields();
        p.OnSelectChild(c1, fields);
        CheckFields(fields);
        errs.Clear();
        p.Validate(ref errs);
        Check(errs.Count == 3, "Warnung je Kamera ohne Adresse: " + (errs.Count > 0 ? errs[0].Text : "-"));

        Console.WriteLine("ONVIF-Abfrage (nachgebaute Kamera, Uhr 7 Stunden falsch):");
        using (TestHttpServer cam = new TestHttpServer(new List<byte[]>(), "rtsp://127.0.0.1:8554/stream"))
        {
            c1.Cam.Name = "Einfahrt";
            c1.Cam.Host = "127.0.0.1"; c1.Cam.Port = cam.Port.ToString();
            c1.Cam.User = TestRtspServer.User; c1.Cam.Password = TestRtspServer.Password;
            c1.Cam.Query = (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "Query"; })).Extended[1];
            p.OnGridChanged();
            Console.WriteLine("  Ergebnis: " + c1.Cam.Result);
            Check(c1.Cam.Rtsp == "rtsp://127.0.0.1:8554/stream", "Quadrant: kleines H.264-Profil → " + c1.Cam.Rtsp);
            Check(c1.Cam.RtspFull == "rtsp://127.0.0.1:8554/stream?profile=p1", "Vollbild: großes H.264-Profil (nicht H.265) → " + c1.Cam.RtspFull);
            Check(c1.Cam.Jpeg == cam.Base + "/snap.jpg", "Einzelbild → " + c1.Cam.Jpeg);
            Check(c1.Cam.Query.Key == "0", "Feld „abfragen“ steht wieder auf Nein");

            c2.Cam.Host = "127.0.0.1"; c2.Cam.Port = cam.Port.ToString(); c2.Cam.User = TestRtspServer.User; c2.Cam.Password = "falsch";
            c2.Cam.Query = c1.Cam.Query == null ? null : (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "Query"; })).Extended[1];
            p.OnGridChanged();
            Check(c2.Cam.Result.Contains("Anmeldung abgelehnt") && c2.Cam.Rtsp.Length == 0, "falsches Kennwort: " + c2.Cam.Result);
        }
        c3.Cam.Host = "127.0.0.1"; c3.Cam.Port = "9";
        c3.Cam.Query = (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "Query"; })).Extended[1];
        p.OnGridChanged();
        Check(c3.Cam.Result.Contains("nicht erreichbar"), "Kamera aus: " + c3.Cam.Result);

        Console.WriteLine("Von Hand, KO, Reihenfolge:");
        c2.Cam.Name = "Garten"; c2.Cam.Mjpeg = "http://192.168.1.21/axis-cgi/mjpg/video.cgi"; c2.Cam.Refresh = "250";
        c2.Cam.Mode = (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "Mode"; })).Extended[2];
        Check(p.CanDrop(DragDropDestination.Grid, Kos[5000], 101), "KO auf Feld 101 erlaubt");
        Check(!p.CanDrop(DragDropDestination.Grid, Kos[5000], 2) && !p.CanDrop(DragDropDestination.List, Kos[5000], 101), "KO auf Textfeld und auf Liste abgelehnt");
        p.DropOverGrid(Kos[5000], 101);
        Check(c1.Cam.KoShow.ID == 5000 && c1.Cam.KoShow.Text == "KO Klingel 0", "KO an Kamera 1: " + c1.Cam.KoShow.ID + " „" + c1.Cam.KoShow.Text + "“");
        p.MoveChildUp(c2);
        Check(p.GetChilds().Items[0] == c2 && p.GetChilds().Items[1] == c1, "Garten nach oben verschoben");
        Check(p.DeleteChild(c3) && p.GetChilds().Items.Count == 2, "dritte Kamera gelöscht");
        List<CPluginLink> links = new List<CPluginLink>();
        p.GetLinks(ref links);
        Check(links.Count == 2, "GetLinks liefert je Kamera einen Verweis");

        Console.WriteLine("Speichern / Laden:");
        byte[] data = null;
        p.SaveToArray(out data);
        string xml = new UTF8Encoding(false).GetString(data).TrimStart('﻿');
        Console.WriteLine("  " + xml.Replace(TestRtspServer.Password, "***"));
        KameraPlugin p2 = new KameraPlugin();
        p2.Init("de", null, null);
        p2.LoadFromArray(data);
        byte[] data2 = null;
        p2.SaveToArray(out data2);
        Check(Convert.ToBase64String(data) == Convert.ToBase64String(data2), "Wiedergeladen und identisch gespeichert");
        List<CPluginListItem> items = p2.GetChilds().Items;
        CamNode g = ((CamItem)items[0]).Cam, e = ((CamItem)items[1]).Cam;
        Check(items.Count == 2 && items[0].ToString() == "Garten" && g.Mode.Key == "mjpeg" && g.Refresh == "250", "Kamera 1: " + items[0] + ", " + g.Mode);
        Check(e.Name == "Einfahrt" && e.KoShow.ID == 5000 && e.KoShow.Text == "KO Klingel 0" && e.Password == TestRtspServer.Password, "Kamera 2: " + e.Name + " mit KO und Anmeldung");
        errs.Clear();
        p2.Validate(ref errs);
        Check(errs.Count == 0, "Prüfung ohne Meldung");

        Console.WriteLine("KO im Projekt gelöscht:");
        p2.OnItemChanged(Kos[5000], ItemChangedTypes.Removed);
        Check(e.KoShow.ID == 0, "Verweis zurückgesetzt");
        p2.LoadFromArray(new byte[0]);
        Check(p2.GetChilds().Items.Count == 0, "leere Daten ohne Ausnahme geladen");

        Console.WriteLine(_fail == 0 ? "\nALLES OK" : "\n" + _fail + " FEHLER");
        return _fail == 0 ? 0 : 1;
    }
}
