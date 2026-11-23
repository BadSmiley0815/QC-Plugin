// Prüft plugin_config.dll von 99-3 ohne Experte: Felder, Übersetzungen, die Liste aus Verbrauchern und Einzelwerten,
// KO-Zuweisung, Reihenfolge, Speichern und Wiederladen, Lesen einer Datei im vereinbarten Format.
//
// Aufruf: ConfigTest.exe <config_lang.xml> [roh]      (roh = der Experte liefert keine Übersetzungen)
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using de.dacom_ha.config_framework;
using QcEnergie;
using QcEnergie.Config;

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
            if (f is CGridFieldList)
            {
                bool all = true;
                foreach (object o in ((CGridFieldList)f).Extended) if (o.ToString().StartsWith("#") || o.ToString().Length == 0) all = false;
                Check(all, "      Auswahlliste übersetzt (" + ((CGridFieldList)f).Extended.Length + " Einträge)");
            }
        }
    }

    static Choice Pick(CGridFields fields, string member, string key)
    {
        CGridFieldList l = (CGridFieldList)fields.Fields.Find(delegate(CGridFieldBase f) { return f.Member == member; });
        foreach (object o in l.Extended) if (((Choice)o).Key == key) return (Choice)o;
        return null;
    }

    static string Xml(byte[] data) { return new UTF8Encoding(false).GetString(data).TrimStart('﻿'); }

    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using (XmlReader x = XmlReader.Create(a[0]))
            while (x.Read())
                if (x.NodeType == XmlNodeType.Element && x.Name == "text") Lang[x.GetAttribute("key")] = x.GetAttribute("default");
        bool noTranslate = a.Length > 1 && a[1] == "roh";

        // Umgebung des Experten nachstellen
        for (int i = 0; i < 40; i++) Kos[5000 + i] = new FakeKo(5000 + i, 11001, "KO Energie " + i);
        CQConfig fw = CQConfig.FW;
        fw.dtTranslate = delegate(string k) { return noTranslate ? k : Tr(k); };
        fw.EventLogError = delegate(string m) { Console.WriteLine("  LogError: " + m); };
        fw.EventLogWarning = delegate(string m) { Console.WriteLine("  LogWarning: " + m); };
        fw.EventLogMsg = delegate(string m) { Console.WriteLine("  LogMsg: " + m); };
        fw.dgoGetObject = delegate(int id) { FakeKo k; return Kos.TryGetValue(id, out k) ? k : null; };
        fw.dgoGetQCObject = delegate(int id) { return null; };

        EnergiePlugin p = new EnergiePlugin();
        p.Init("de", null, null);
        Console.WriteLine("Felder der Instanz:");
        CGridFields node = new CGridFields();
        p.OnSelectNode(node);
        Check(node.Fields.Count == 14 && node.Title == "Energiefluss" && p.ToString() == "Energiefluss", "14 Felder, Titel „" + node.Title + "“");
        CheckFields(node);
        Check(p.Credit.Contains("johannesbonn") && p.Credit.Contains("180-1"), "Vorbild genannt: " + p.Credit);

        Console.WriteLine("Prüfung ohne KO:");
        List<CPluginErrorItem> errs = new List<CPluginErrorItem>();
        p.Validate(ref errs);
        Check(errs.Count == 1, "Warnung: " + (errs.Count > 0 ? errs[0].Text : "-"));

        Console.WriteLine("Liste der Verbraucher und Einzelwerte:");
        Check(p.GetChilds().Items.Count == 0 && p.CanAddChilds() && p.CanDeleteChilds() && p.CanMoveChilds(), "leere Liste; Anlegen, Löschen, Verschieben erlaubt");
        EntryItem wb = (EntryItem)p.CreateChild(), wm = (EntryItem)p.CreateChild(), hz = (EntryItem)p.CreateChild(), wr1 = (EntryItem)p.CreateChild(), wr2 = (EntryItem)p.CreateChild(), weg = (EntryItem)p.CreateChild();
        Check(p.GetChilds().Items.Count == 6 && wb.ID != wm.ID && wb.ToString() == "Steckdose (frei)", "6 Einträge angelegt, neu: „" + wb + "“");
        Check(wb.Entry.Kind.Key == "vb" && wb.Entry.Icon.Key == "steckdose" && wb.Entry.An.Key == "haus", "Vorgabe: Verbraucher, Steckdose, am Haus");
        Console.WriteLine("Felder eines Eintrags:");
        CGridFields fields = new CGridFields();
        p.OnSelectChild(wb, fields);
        Check(fields.Fields.Count == 5 && fields.Title == "Verbraucher / Einzelwert", "5 Felder, Titel „" + fields.Title + "“");
        CheckFields(fields);

        Console.WriteLine("KO-Zuweisung per Ziehen:");
        Check(p.CanDrop(DragDropDestination.Grid, Kos[5020], 201), "Eintrag gewählt: KO auf Feld 201 erlaubt");
        Check(!p.CanDrop(DragDropDestination.Grid, Kos[5020], 101) && !p.CanDrop(DragDropDestination.Grid, Kos[5020], 2) && !p.CanDrop(DragDropDestination.List, Kos[5020], 201),
            "Eintrag gewählt: Grund-KO-Feld, Textfeld und Liste abgelehnt");
        p.DropOverGrid(Kos[5020], 201);
        wb.Entry.Name = "Garage";
        wb.Entry.Icon = Pick(fields, "Icon", "wallbox");
        Check(wb.Entry.Ko.ID == 5020 && wb.ToString() == "Garage", "Wallbox belegt: „" + wb + "“, KO „" + wb.Entry.Ko.Text + "“");
        wm.Entry.Icon = Pick(fields, "Icon", "wasch");
        Check(wm.ToString() == "Waschmaschine (frei)", "ohne Name zeigt die Liste das Symbol: „" + wm + "“");
        p.OnSelectChild(hz, fields = new CGridFields());
        p.DropOverGrid(Kos[5021], 201);
        hz.Entry.Name = "Heizstab";
        hz.Entry.Icon = Pick(fields, "Icon", "boiler");
        hz.Entry.An = Pick(fields, "An", "pv");
        Check(hz.Entry.Icon != null && hz.Entry.An != null && hz.Entry.Ko.ID == 5021, "Heizstab an der PV-Anlage");
        p.OnSelectChild(wr1, fields = new CGridFields());
        p.DropOverGrid(Kos[5030], 201);
        wr1.Entry.Kind = Pick(fields, "Kind", "teil");
        wr1.Entry.An = Pick(fields, "An", "pv");
        wr1.Entry.Name = "Wechselrichter Süd";
        p.OnSelectChild(wr2, fields = new CGridFields());
        p.DropOverGrid(Kos[5031], 201);
        wr2.Entry.Kind = Pick(fields, "Kind", "teil");
        wr2.Entry.An = Pick(fields, "An", "pv");
        Check(wr1.ToString() == "PV-Anlage › Wechselrichter Süd" && wr2.ToString() == "PV-Anlage › Einzelwert", "Einzelwerte: „" + wr1 + "“, „" + wr2 + "“");
        Check(p.DeleteChild(weg) && p.GetChilds().Items.Count == 5 && !p.DeleteChild(weg), "Eintrag gelöscht");
        p.OnSelectNode(node = new CGridFields());
        Check(p.CanDrop(DragDropDestination.Grid, Kos[5000], 101) && !p.CanDrop(DragDropDestination.Grid, Kos[5000], 201) && !p.CanDrop(DragDropDestination.Grid, Kos[5000], 1),
            "Instanz gewählt: nur die Grund-KO-Felder nehmen ein KO");
        for (int i = 0; i < 12; i++) if (i != 7) p.DropOverGrid(Kos[5000 + i], 101 + i);     // Verbrauch heute bleibt frei
        Check(p.KoPv.ID == 5000 && p.KoNetz.ID == 5003 && p.KoBatState.ID == 5005 && p.KoEvKwh.ID == 0 && p.KoBezug.ID == 5011, "zwölf Grund-KOs, eines frei");
        List<CPluginLink> links = new List<CPluginLink>();
        p.GetLinks(ref links);
        Check(links.Count == 17, "GetLinks liefert 12 + 5 Verweise");

        Console.WriteLine("Reihenfolge:");
        p.MoveChildUp(hz);
        Check(p.GetChilds().Items[1] == hz && p.GetChilds().Items[2] == wm, "Heizstab vor die Waschmaschine verschoben");

        Console.WriteLine("Speichern / Laden:");
        byte[] data = null;
        p.SaveToArray(out data);
        string xml = Xml(data);
        Console.WriteLine("  " + xml);
        Check(xml.Contains("<project version=\"1\">") && xml.Contains("<ko id=\"pv\" tag=\"5000\" />") && !xml.Contains("id=\"evkwh\""), "Grund-KOs: nur zugewiesene, als <ko id tag/>");
        Check(xml.Contains("<vb id=\"wb1\" name=\"Garage\" icon=\"wallbox\" an=\"haus\" tag=\"5020\" />"), "Verbraucher als <vb id name icon an tag/>, Kennung aus dem Symbol");
        Check(xml.Contains("<vb id=\"s1\" name=\"Heizstab\" icon=\"boiler\" an=\"pv\" tag=\"5021\" />") && xml.Contains("<vb id=\"wm\" name=\"\" icon=\"wasch\" an=\"haus\" tag=\"0\" />"),
            "Symbol, Anschlusspunkt, Eintrag ohne KO (tag=\"0\")");
        Check(xml.IndexOf("id=\"s1\"") < xml.IndexOf("id=\"wm\""), "Reihenfolge der Liste steht in der Datei");
        Check(xml.Contains("<teil an=\"pv\" name=\"Wechselrichter Süd\" tag=\"5030\" /><teil an=\"pv\" name=\"\" tag=\"5031\" />"), "Einzelwerte als <teil an name tag/>");
        EnergiePlugin p2 = new EnergiePlugin();
        p2.Init("de", null, null);
        p2.LoadFromArray(data);
        byte[] data2 = null;
        p2.SaveToArray(out data2);
        Check(Convert.ToBase64String(data) == Convert.ToBase64String(data2), "Wiedergeladen und identisch gespeichert");
        Check(p2.GetChilds().Items.Count == 5, "5 Einträge nach dem Laden");
        EntryItem h = (EntryItem)p2.GetChilds().Items[1], t1 = (EntryItem)p2.GetChilds().Items[3];
        Check(h.ToString() == "Heizstab" && h.Entry.Icon.Key == "boiler" && h.Entry.An.Key == "pv" && h.Entry.Ko.Text == "KO Energie 21", "Verbraucher nach dem Laden: " + h + ", " + h.Entry.Icon + ", " + h.Entry.An);
        Check(t1.Entry.Kind.Key == "teil" && t1.Entry.An.Key == "pv" && t1.Entry.Ko.ID == 5030, "Einzelwert nach dem Laden: " + t1);
        Check(p2.KoSoc.ID == 5004 && p2.KoSoc.Text == "KO Energie 4" && p2.KoEvKwh.ID == 0, "Grund-KOs nach dem Laden aufgelöst");
        errs.Clear();
        p2.Validate(ref errs);
        Check(errs.Count == 0, "Prüfung ohne Meldung");

        Console.WriteLine("Kennungen aus dem Symbol:");
        EnergieSettings ids = new EnergieSettings();
        foreach (string ic in new string[] { "wallbox", "auto", "wallbox", "wallbox", "wallbox", "wasch", "wasch", "spuel", "trockner", "herd" })
        {
            Verbraucher v = new Verbraucher();
            v.Icon = ic;
            ids.Vbs.Add(v);
        }
        ids.AssignIds();
        List<string> got = new List<string>();
        foreach (Verbraucher v in ids.Vbs) got.Add(v.Id + "=" + v.ShownIcon);
        string all = string.Join(" ", got.ToArray());
        Check(all == "wb1=wallbox wb2=auto wb3=wallbox wb4=wallbox s1=wallbox wm=wasch s2=wasch sm=spuel tr=trockner s3=herd", all);

        Console.WriteLine("Grenzen:");
        EnergiePlugin p3 = new EnergiePlugin();
        p3.Init("de", null, null);
        for (int i = 0; i < 18; i++) { p3.OnSelectChild(p3.CreateChild(), new CGridFields()); p3.DropOverGrid(Kos[5000 + i], 201); }
        for (int i = 0; i < 9; i++)
        {
            EntryItem e = (EntryItem)p3.CreateChild();
            p3.OnSelectChild(e, fields = new CGridFields());
            p3.DropOverGrid(Kos[5020 + i], 201);
            e.Entry.Kind = Pick(fields, "Kind", "teil");
            e.Entry.An = Pick(fields, "An", "netz");
        }
        errs.Clear();
        p3.Validate(ref errs);
        Check(errs.Count == 2, "Warnungen bei 18 Verbrauchern und 9 Einzelwerten: " + (errs.Count > 1 ? errs[0].Text + " | " + errs[1].Text : "-"));
        int made = p3.GetChilds().Items.Count;
        while (p3.CanAddChilds() && made < 100) { p3.CreateChild(); made++; }
        Check(made == 49 && p3.CreateChild() == null, "Liste endet bei 49 Einträgen (17 + 4 × 8)");

        Console.WriteLine("Datei im vereinbarten Format (wie die Testdatei des QC-Clients):");
        string demo = "<?xml version=\"1.0\" encoding=\"utf-8\"?><project version=\"1\">"
            + "<ko id=\"pv\" tag=\"5001\"/><ko id=\"bat\" tag=\"5002\"/><ko id=\"evkwh\" tag=\"0\"/><ko id=\"gibtsnicht\" tag=\"5003\"/>"
            + "<vb id=\"wb1\" name=\"Wallbox Garage\" icon=\"wallbox\" an=\"haus\" tag=\"5031\"/><vb id=\"wb2\" name=\"Wallbox Carport\" icon=\"auto\" an=\"haus\" tag=\"0\"/>"
            + "<vb id=\"wm\" tag=\"5032\"/><vb id=\"s1\" name=\"Heizstab\" icon=\"boiler\" an=\"pv\" tag=\"5033\"/>"
            + "<vb id=\"s2\" name=\"Notstrom\" icon=\"gibtsnicht\" an=\"bat\" tag=\"5034\"/><vb id=\"x9\" tag=\"5035\"/>"
            + "<teil an=\"pv\" name=\"Wechselrichter Süd\" tag=\"5036\"/><teil an=\"pv\" tag=\"5037\"/><teil an=\"pv\" name=\"ohne KO\" tag=\"0\"/><teil an=\"dach\" tag=\"5038\"/><teil an=\"haus\" tag=\"5039\"/></project>";
        EnergieSettings d = EnergieSettings.Parse(Encoding.UTF8.GetBytes(demo));
        Check(d.Kos["pv"] == 5001 && d.Kos["bat"] == 5002 && d.Kos["evkwh"] == 0 && d.Kos["haus"] == 0, "Grund-KOs gelesen, tag=\"0\" = nicht belegt, unbekannte Kennung übergangen");
        Check(d.Vbs.Count == 6 && d.ShownVbs().Count == 5 && d.Vbs[2].Id == "wm" && d.Vbs[5].Id == "s6", "6 Verbraucher, 5 mit KO; unbekannte Kennung x9 → s6");
        Check(d.Vbs[2].Tag == 5032 && d.Vbs[2].ShownName(true) == "Waschmaschine" && d.Vbs[2].ShownIcon == "wasch" && d.Vbs[2].An == "haus", "ohne Name/Symbol/Anschluss: Vorgabe nach der Kennung");
        Check(d.Vbs[4].ShownIcon == "steckdose" && d.Vbs[4].An == "bat" && d.Vbs[1].Tag == 0 && d.Vbs[1].ShownIcon == "auto", "unbekanntes Symbol → Vorgabe; Anschlusspunkt Speicher");
        Check(d.Teile.Count == 4 && d.ShownTeile("pv").Count == 2 && d.ShownTeile("haus").Count == 1 && d.ShownTeile("netz").Count == 0, "Einzelwerte: 2 an PV, 1 am Haus; ohne KO und mit unbekanntem Kreis nicht gezeigt");

        Console.WriteLine("Unerlaubte Zeichen und unlesbare Daten:");
        EnergiePlugin p4 = new EnergiePlugin();
        p4.Init("de", null, null);
        EntryItem bad = (EntryItem)p4.CreateChild();
        p4.OnSelectChild(bad, new CGridFields());
        p4.DropOverGrid(Kos[5020], 201);
        bad.Entry.Name = "\u0001Wall\u0000box\tGarage\uD800 ";
        Check(bad.Entry.Name == "Wallbox Garage", "Steuerzeichen aus dem Namen entfernt: „" + bad.Entry.Name + "“");
        byte[] d4 = null;
        p4.SaveToArray(out d4);
        Check(Xml(d4).Contains("name=\"Wallbox Garage\" icon=\"steckdose\" an=\"haus\" tag=\"5020\""), "gespeichert trotz Steuerzeichen: " + d4.Length + " Byte");
        EnergieSettings raw = new EnergieSettings();
        Verbraucher rv = new Verbraucher();
        rv.Name = "A\u0002B"; rv.Tag = 5021;
        raw.Vbs.Add(rv);
        Check(Xml(raw.ToBytes()).Contains("name=\"AB\""), "auch beim Schreiben der Einstellungen gesäubert");
        byte[] junk = Encoding.UTF8.GetBytes("<project version=\"1\"><ko id=\"pv\" tag=\"5000\"/><vb");
        EnergiePlugin p5 = new EnergiePlugin();
        p5.Init("de", null, null);
        p5.LoadFromArray(junk);
        byte[] d5 = null;
        p5.SaveToArray(out d5);
        Check(Convert.ToBase64String(d5) == Convert.ToBase64String(junk), "unlesbare Datei wird nicht durch eine leere ersetzt");
        p5.OnSelectNode(new CGridFields());
        p5.DropOverGrid(Kos[5001], 101);
        p5.SaveToArray(out d5);
        Check(Xml(d5).Contains("<ko id=\"pv\" tag=\"5001\" />"), "nach einer Eingabe gelten die neuen Einstellungen");

        Console.WriteLine("KO im Projekt gelöscht:");
        p2.OnItemChanged(Kos[5021], ItemChangedTypes.Removed);
        p2.OnItemChanged(Kos[5004], ItemChangedTypes.Removed);
        p2.OnItemChanged(Kos[5030], ItemChangedTypes.Removed);
        Check(h.Entry.Ko.ID == 0 && p2.KoSoc.ID == 0 && t1.Entry.Ko.ID == 0 && h.ToString().EndsWith("(frei)"), "Verweise zurückgesetzt");
        p2.LoadFromArray(new byte[0]);
        Check(p2.GetChilds().Items.Count == 0 && p2.KoPv.ID == 0, "leere Daten ohne Ausnahme geladen");
        Console.WriteLine(_fail == 0 ? "\nALLES OK" : "\n" + _fail + " FEHLER");
        return _fail == 0 ? 0 : 1;
    }
}
