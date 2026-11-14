// Prüft plugin_config.dll ohne Experte: Felder, Übersetzungsschlüssel, Ortssuche, KO-Zuweisung,
// Speichern und Wiederladen der Einstellungen.
//
// Aufruf: ConfigTest.exe <config_lang.xml> [live]      (live = Ortssuche wirklich bei Open-Meteo ausführen)
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using de.dacom_ha.config_framework;
using QcWetter.Config;

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

    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using (XmlReader x = XmlReader.Create(a[0]))
            while (x.Read())
                if (x.NodeType == XmlNodeType.Element && x.Name == "text") Lang[x.GetAttribute("key")] = x.GetAttribute("default");
        bool live = a.Length > 1 && a[1] == "live";

        // Umgebung des Experten nachstellen
        for (int i = 0; i < 12; i++) Kos[5000 + i] = new FakeKo(5000 + i, 11001, "KO Wetterstation " + i);
        CQConfig fw = CQConfig.FW;
        // Wie im Experten beobachtet: unbekannte Schlüssel kommen unverändert zurück → das Plugin muss selbst übersetzen.
        bool noTranslate = a.Length > 2 && a[2] == "roh";
        fw.dtTranslate = delegate(string k) { return noTranslate ? k : Tr(k); };
        fw.EventLogError = delegate(string m) { Console.WriteLine("  LogError: " + m); };
        fw.EventLogWarning = delegate(string m) { Console.WriteLine("  LogWarning: " + m); };
        fw.EventLogMsg = delegate(string m) { Console.WriteLine("  LogMsg: " + m); };
        fw.dgoGetObject = delegate(int id) { FakeKo k; return Kos.TryGetValue(id, out k) ? k : null; };
        fw.dgoGetQCObject = delegate(int id) { return null; };

        Console.WriteLine("Felder:");
        WetterPlugin p = new WetterPlugin();
        p.Init("de", null, null);
        CGridFields fields = new CGridFields();
        p.OnSelectNode(fields);
        foreach (CGridFieldBase f in fields.Fields)
        {
            PropertyInfo pi = f.Object.GetType().GetProperty(f.Member);
            Check(pi != null && pi.CanRead && pi.CanWrite, string.Format("{0,3} {1,-24} {2,-22} → {3}", f.ID, f.GetType().Name, f.Member, Tr(f.Text)));
            Check(f.Text.Length > 0 && !f.Text.StartsWith("#"), "      Text übersetzt: " + f.Text);
            Check(f.Comment.Length > 0 && !f.Comment.StartsWith("#"), "      Hinweis übersetzt: " + f.Comment);
            if (f is CGridFieldCobject) Check(pi != null && pi.PropertyType == typeof(CPluginLinkCObject), "      Typ CPluginLinkCObject");
        }
        foreach (string k in new string[] { "#99-1_pluginname", "#99-1_search_none", "#99-1_search_more", "#99-1_search_error", "#99-1_no_location" })
            Check(Lang.ContainsKey(k), "Text vorhanden: " + k);

        Console.WriteLine("Prüfung ohne Ort:");
        List<CPluginErrorItem> errs = new List<CPluginErrorItem>();
        p.Validate(ref errs);
        Check(errs.Count == 1, "Warnung 'kein Ort': " + (errs.Count > 0 ? errs[0].Text : "-"));

        Console.WriteLine("Ortssuche:");
        if (live)
        {
            p.Search = "Nürnberg";
            p.OnGridChanged();
            Console.WriteLine("  Ergebnis: " + p.Result);
            Check(p.LocationName == "Nürnberg" && p.Latitude.StartsWith("49.") && p.Longitude.StartsWith("11."), "Nürnberg → " + p.Latitude + " / " + p.Longitude);
        }
        else
        {
            p.LocationName = "Nürnberg"; p.Latitude = "49,45"; p.Longitude = "11.08";
            Console.WriteLine("  (übersprungen, Koordinaten von Hand; Komma als Dezimaltrenner)");
        }
        p.Interval = (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "Interval"; })).Extended[3];
        p.WindUnit = (Choice)((CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == "WindUnit"; })).Extended[1];

        Console.WriteLine("KO-Zuweisung per Ziehen:");
        Check(p.CanDrop(DragDropDestination.Grid, Kos[5000], 101), "KO auf Feld 101 erlaubt");
        Check(!p.CanDrop(DragDropDestination.Grid, Kos[5000], 3), "KO auf Textfeld 3 abgelehnt");
        Check(!p.CanDrop(DragDropDestination.List, Kos[5000], 101), "KO auf Liste abgelehnt");
        for (int i = 0; i < 12; i++) if (i != 6) p.DropOverGrid(Kos[5000 + i], 101 + i);     // Helligkeit bleibt frei
        Check(p.KoTemp.ID == 5000 && p.KoTemp.Text == "KO Wetterstation 0", "Temperatur: " + p.KoTemp.ID + " „" + p.KoTemp.Text + "“");
        List<CPluginLink> links = new List<CPluginLink>();
        p.GetLinks(ref links);
        Check(links.Count == 12, "GetLinks liefert 12 Verweise");

        Console.WriteLine("Speichern / Laden:");
        byte[] data = null;
        p.SaveToArray(out data);
        string xml = new UTF8Encoding(false).GetString(data).TrimStart('﻿');
        Console.WriteLine("  " + xml);
        WetterPlugin p2 = new WetterPlugin();
        p2.Init("de", null, null);
        p2.LoadFromArray(data);
        byte[] data2 = null;
        p2.SaveToArray(out data2);
        Check(Convert.ToBase64String(data) == Convert.ToBase64String(data2), "Wiedergeladen und identisch gespeichert");
        Check(p2.KoPress.ID == 5011 && p2.KoPress.Text == "KO Wetterstation 11" && p2.KoLuxS.ID == 5009 && p2.KoFeel.ID == 5001 && p2.KoLux.ID == 0, "KO-Verweise nach dem Laden aufgelöst");
        Check(p2.Interval.Key == "120" && p2.WindUnit.Key == "ms", "Auswahllisten nach dem Laden: " + p2.Interval + ", " + p2.WindUnit);
        errs.Clear();
        p2.Validate(ref errs);
        Check(errs.Count == 0, "Prüfung mit Ort ohne Meldung");

        Console.WriteLine("KO im Projekt gelöscht:");
        p2.OnItemChanged(Kos[5011], ItemChangedTypes.Removed);
        Check(p2.KoPress.ID == 0, "Verweis zurückgesetzt");
        p2.LoadFromArray(new byte[0]);
        Check(true, "leere Daten ohne Ausnahme geladen");

        Console.WriteLine(_fail == 0 ? "\nALLES OK" : "\n" + _fail + " FEHLER");
        return _fail == 0 ? 0 : 1;
    }
}
