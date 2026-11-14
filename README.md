# QC-Plugin

Eigene Plugins für den **Gira QuadClient** am Gira HomeServer – in C#/.NET nach dem
QuadClient-Plugin-SDK, nutzbar im Windows-QuadClient und im [QC-Client](https://github.com/BadSmiley0815/QC-Client) (`qc.html`).

> **Plugin-IDs vorläufig:** Eine Plugin-ID besteht aus *Entwickler-ID + Nummer* (z. B. `1-3`).
> Die eigene Entwickler-ID ist bei Gira angefragt; bis zur Vergabe verwenden die Plugins hier
> die vorläufige Entwickler-ID **99**. Danach werden alle Plugins in allen Dateien umgestellt.

## Plugins

| ID (vorläufig) | Name | Stand |
|---|---|---|
| 99-1 | Wetter | in Planung – Vorhersage (Open-Meteo) + aktuelle Werte der eigenen Wetterstation (KOs) |

## Aufbau eines Plugins (.hsp)

Eine `.hsp` ist ein ZIP mit:

| Datei | Zweck |
|---|---|
| `version.xml` | ID, Version, Name, Hersteller |
| `interface.xml` | Klassennamen der beiden Assemblies (`nsclient`, `nsconfig`) |
| `<ID>_client.dll` | Anzeige im QuadClient (WPF, implementiert `IQuadContent`) |
| `plugin_config.dll` | Einstellungen im Experten/QuadConfig (implementiert `IPlugin`) |
| `<ID>_lang.xml`, `config_lang.xml` | Texte für QuadClient bzw. Experte |
| `config_data.xml` | Daten für den Einstellungsdialog |
| `design_<n>/<ID>_design.xml`, Symbole | Darstellung |
| `help/p<ID>_<Sprache>.pdf` | Hilfe im Experten |

Gebaut wird gegen `hs_client_framework.dll` (QuadClient) und `config_framework.dll`
(Experte, `quad_config\config`). **Diese Gira/DaCom-Bibliotheken sind nicht im Repository** –
sie werden beim Bauen aus der eigenen Installation referenziert.

## Lizenz

GNU General Public License v3.0 – siehe [`LICENSE`](LICENSE). Jeder darf die Plugins nutzen,
ändern und weitergeben; geänderte Fassungen müssen ebenfalls unter der GPL-3.0 offen bleiben.
Abwandlungen bitte mit eigener Entwickler-ID und angepasster Plugin-ID veröffentlichen.

## Hinweise

- Unabhängiges Projekt, **nicht von Gira** und nicht von Gira unterstützt. „Gira“,
  „HomeServer“ und „QuadClient“ sind Marken ihrer Inhaber.
- Enthält keinen Code, keine Bibliotheken und keine Grafiken von Gira/DaCom.
- Entwickelt mit Claude (Anthropic).
