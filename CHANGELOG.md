# Changelog

## 99-2 Kamera

### 0.1.4 – 15.11.2026

- Erster Test im Windows-QuadClient bestanden: Axis P1468-LE, RTSP H.264 mit 3840 × 2160 im Quadranten, flüssig.
  ONVIF-Abfrage im Experten über HTTPS trägt die Adressen ein.
- Hilfe: Abschnitt „Axis-Kameras mit aktuellem AXIS OS“ (ONVIF-Konto, kleines Medienprofil, nur HTTPS).

### 0.1.3 – 15.11.2026

- Erster Test im Experten: Kameraliste und Felder erscheinen.
- ONVIF-Abfrage: Antwortet die Kamera nicht über HTTP (Axis mit „nur HTTPS“, Port 80 zu), fragt das Plugin
  von selbst über HTTPS. Port 443 bedeutet immer HTTPS. Adressen, die die Kamera für Mediendienst und
  Einzelbild nennt, werden auf den Weg umgestellt, über den sie erreichbar war.
- Neu: `KameraTest.exe` (in `bin\99-2\`) prüft eine echte Kamera mit den Klassen des Plugins – ONVIF-Abfrage,
  RTSP-Empfang, Decoder, Einzelbild. Das Kennwort wird verdeckt abgefragt.

### 0.1.2 – 15.11.2026

- Behoben: Im Experten fehlten die Kameraliste und alle Felder („Das PlugIn <99-2> konnte nicht instanziert
  werden“). Die Einstellungs-DLLs beider Plugins hatten denselben Assemblynamen `plugin_config`; der Experte
  lädt dann nur die zuerst benutzte. Die Assembly heißt jetzt wie bei Gira `<ID>_config`, die Datei im Paket
  weiter `plugin_config.dll`.
- Hilfe im Experten (`help/p99-2_de.pdf`, `_en.pdf`): Kamera anlegen, Felder, übliche RTSP-Adressen von Axis,
  Hikvision und Dahua. Mit Beispielbild und nachgezeichneter Experten-Ansicht. Quelle ist `plugins\99-2_Kamera\help\*.html` mit `plugins\hilfe.css`; `build.ps1` druckt sie mit Edge als PDF.
- Vorschaubild im Experten, Bild in Hilfe und README: gezeichnete Beispielszene (Einfahrt) statt des rot-blauen
  Testbilds; sie füllt die Anzeige ohne schwarze Ränder.

### 0.1.0 – 14.11.2026

Erste Fassung. Gebaut und mit den eigenen Testprogrammen gegen eine nachgebaute Kamera geprüft,
**noch nicht im Experten, im QuadClient und an echten Kameras erprobt**.

- Anzeige im QuadClient (`99-2_client.dll`): RTSP mit H.264 (RTP über TCP, Digest/Basic), dekodiert mit dem
  H.264-Decoder von Windows; MJPEG über HTTP; Einzelbilder (JPEG). H.265 vorbereitet, ungeprüft.
- Mehrere Kameras je Instanz, blättern mit ◀ ▶, Vollbild per Klick ins Bild, automatisches Neuverbinden.
- Einstellungen im Experten (`plugin_config.dll`): Kameras als Liste; ONVIF-Abfrage trägt RTSP-Adressen
  (Quadrant/Vollbild) und Einzelbild-Adresse ein; KO „Kamera anzeigen“ je Kamera.
- Einstellungsformat als dokumentiertes XML (siehe README).
- `build.ps1` baut jetzt mehrere Plugins (`-Plugin 99-2` für eines); Ausgaben liegen unter `bin\<ID>\`.
- Tests: `DecoderTest`, `RtspTest`, `ConfigTest`, `Vorschau` gegen `tools\TestKamera.cs`.

## 99-1 Wetter

### 0.1.8 – 15.11.2026

- Assembly der Einstellungs-DLL heißt jetzt `99-1_config` statt `plugin_config` (Datei im Paket unverändert
  `plugin_config.dll`), damit sie sich im Experten nicht mit anderen eigenen Plugins überschneidet.
- Hilfe im Experten (`help/p99-1_de.pdf`, `_en.pdf`): Einrichten, Felder, DPTs der KOs, mit Beispielbild und nachgezeichneter Experten-Ansicht.

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
