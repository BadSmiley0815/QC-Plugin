# Changelog

## 99-1 Wetter

### 0.1.6 – 14.11.2026

- Erster Test im Windows-QuadClient bestanden (Anzeige, KO-Werte, Lage im Quadranten).
- Dunkles Design an den QuadClient angeglichen: Kacheln `#1A1A1A` mit schwarzen Fugen statt heller
  Trennlinien, Unterleiste `#1E1E1E` (Farben am QuadClient gemessen).

### 0.1.5 – 14.11.2026

- Helligkeit wahlweise als ein Wert (`lux`, z. B. Wetterstation mit GPS) oder je Himmelsrichtung
  (`lux_n`, `lux_e`, `lux_s`, `lux_w`). Sind Richtungs-KOs zugewiesen, zeigt die Anzeige diese in klx.

### 0.1.4 – 14.11.2026

- Zweites Temperatur-KO: `temp` = gemessen, `feel` = gefühlt. Anzeige „gefühlt …“ unter der Temperatur;
  ohne KO kommt der Wert von Open-Meteo (`apparent_temperature`).

### 0.1.3 – 14.11.2026

- Experte: Feldnamen und Hinweise werden vom Plugin selbst übersetzt (das Raster zeigte die rohen Schlüssel `#99-1_...`), Gruppenüberschrift „Wetter“.
- Erwartete DPTs stehen in den Feldnamen und Hinweisen der KOs.
- Luftdruck: Werte über 2000 gelten als Pa (DPT 9.006) und werden in hPa angezeigt.

### 0.1.1 – 14.11.2026

Erste Fassung. Gebaut und mit den eigenen Testprogrammen geprüft, **noch nicht im Experten und am HomeServer erprobt**.

- Einstellungen im Experten (`plugin_config.dll`): Ortssuche über Open-Meteo, Ort/Breite/Länge,
  Aktualisierung, Einheiten, sieben KOs der Wetterstation, Einheit des Wind-KO.
- Anzeige im QuadClient (`99-1_client.dll`): Unterleiste „◀ Ort [Datum Uhrzeit] ▶“, links „Jetzt“,
  rechts 4 von 7 Tagen, eigene Symbole, helles und dunkles Design, Quellenangabe Open-Meteo.
- Letzte Vorhersage bleibt offline mit Zeitstempel stehen.
- Einstellungsformat als dokumentiertes XML (siehe README) für den QC-Client.
- `build.ps1`: Bau ohne Visual Studio, Tests (`ConfigTest`, `Vorschau`), Paket `dist\99-1_Wetter.hsp`.
