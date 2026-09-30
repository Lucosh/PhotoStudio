<div align="center">

<img src="Assets/icon-256.png" width="112" alt="PhotoStudio-Symbol">

# PhotoStudio

**Ein kostenloser Open-Source-Fotoeditor für Windows.**<br>
Ein ganzes Shooting aussortieren, RAW-Dateien entwickeln und retuschieren, auf Wunsch mit KI-Hilfe.

[![Herunterladen](https://img.shields.io/badge/Herunterladen-f%C3%BCr%20Windows-2F80ED?style=for-the-badge)](../../releases/latest)

[![Lizenz: MIT](https://img.shields.io/badge/Lizenz-MIT-2F80ED)](LICENSE)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Sprachen](https://img.shields.io/badge/Sprachen-DE%20%7C%20EN%20%7C%20IT%20%7C%20FR%20%7C%20ES-555)

[English](README.md) · [Italiano](README.it.md) · **Deutsch** · [Français](README.fr.md) · [Español](README.es.md)

</div>

<p align="center">
  <img src="Assets/screenshots/camera-raw.png" alt="Das Camera-Raw-Fenster von PhotoStudio mit Farbmischer und Color-Grading-Rädern">
</p>

<p align="center">
  <a href="#funktionen">Funktionen</a> ·
  <a href="#installation">Installation</a> ·
  <a href="#ist-es-sicher-download-prüfen">Ist es sicher?</a> ·
  <a href="#datenschutz">Datenschutz</a> ·
  <a href="#ki-einrichten">KI einrichten</a> ·
  <a href="#für-entwickler">Für Entwickler</a>
</p>

## Funktionen

### 🗂️ Aussortieren

- Einen ganzen Ordner oder nur einige Fotos öffnen und festlegen, welche Formate angezeigt werden (zum Beispiel nur RAW).
- Sterne und Farbmarkierungen, mit Filtern, um nur die gewünschten Fotos zu sehen.
- Serienaufnahmen werden automatisch gruppiert, damit du schnell das beste Bild findest.
- Fotos nebeneinander vergleichen, mit synchronisiertem Zoom.
- Aufnahmedaten (Kamera, Objektiv, ISO, Belichtungszeit, Blende) und Histogramm mit Warnung vor Beschneidung.
- Deine Arbeit wird gespeichert: öffne den Ordner erneut und mach dort weiter, wo du aufgehört hast.

### 🎞️ RAW-Entwicklung

- Öffnet über 25 Kamera-RAW-Formate (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 und weitere) sowie JPEG, PNG, TIFF, WebP und HEIC.
- Weißabgleich, Belichtung, Kontrast, Lichter, Tiefen, Weiß, Schwarz, Struktur, Klarheit, Dunst entfernen, Dynamik und Sättigung.
- Gradationskurve (RGB und pro Kanal), HSL-Farbmischer und Color-Grading-Räder.
- Schärfen, Rauschreduzierung, Vignettierung, Körnung, Freistellen und Ausrichten.
- Lokale Masken (linear, radial und nach Helligkeit), Vorher/Nachher-Ansicht, Vorgaben sowie Kopieren/Einfügen von Einstellungen.

### ☀️ Intelligentes Licht, ganz einfach

- **Intelligentes Licht**: ein Klick korrigiert die Belichtung und fügt Lichtmasken nur dort hinzu, wo das Foto sie braucht (dunkle Bereiche, helle Bereiche, Himmel, Motiv).
- **Einfache Lichtmasken**: ein vereinfachtes Bedienfeld für Einsteiger, mit fertigen Rezepten und ohne technische Regler.

### ✨ Bearbeitung mit KI (optional)

- Beschreibe, was du möchtest („wärmer, wie bei Sonnenuntergang“), oder lass die KI die beste Bearbeitung wählen.
- Funktioniert mit **Google Gemini** (kostenloser Schlüssel), **Anthropic Claude** oder **Ollama** (komplett offline auf deinem PC).
- Die KI wählt nur die Reglerwerte. Die Pixel berechnet PhotoStudio selbst, daher bleibt jede Änderung sichtbar und bearbeitbar.

### 🖌️ Bildbearbeitung und Stapelverarbeitung

- Ebenen, Auswahlen, Pinsel, Korrekturen (Tonwerte, Kurven, Farbton/Sättigung, Schwarzweiß …) und Filter (Weichzeichnen, Scharfzeichnen, Rauschen, Vignette …).
- Projektformat `.psx`, das die Ebenen erhält.
- Einstellungen oder Vorgaben auf viele Fotos gleichzeitig anwenden.
- Export mit Größenänderung, automatischer Umbenennung und Wasserzeichen.

### 🌍 In deiner Sprache

Die Oberfläche gibt es auf Deutsch, Englisch, Italienisch, Französisch und Spanisch. PhotoStudio verwendet die Sprache von Windows, ändern kannst du sie unter **Ansicht ▸ Sprache**.

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/culling.png" alt="Fenster zum Aussortieren mit Fotoleiste und Histogramm"><br>
      <sub><b>Aussortieren</b>: ein Shooting durchgehen, bewerten und den Ausschuss löschen.</sub>
    </td>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/masks.png" alt="Tab Masken mit den schnellen Rezepten"><br>
      <sub><b>Einfache Lichtmasken</b>: Rezepte mit einem Klick für Himmel, Tiefen und Motiv.</sub>
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <img src="Assets/screenshots/editor.png" alt="Hauptfenster mit Ebenen, Protokoll und Histogramm"><br>
      <sub><b>Editor</b>: Ebenen, Werkzeuge, Protokoll und Histogramm.</sub>
    </td>
  </tr>
</table>

<sub>Die Bildschirmfotos zeigen die englische Oberfläche.</sub>

## Installation

1. Öffne das [**neueste Release**](../../releases/latest).
2. Lade eine der beiden Dateien herunter:
   - **`PhotoStudio-x.y.z-win-x64-setup.exe`**: der Installer (empfohlen);
   - **`PhotoStudio-x.y.z-win-x64-portable.zip`**: ohne Installation, einfach entpacken und `PhotoStudio.exe` starten.
3. Starte es. Du brauchst nur Windows 10 oder 11 (64 Bit): .NET ist bereits enthalten.

Der Installer benötigt keine Administratorrechte, und du kannst das Programm unter **Einstellungen ▸ Apps** wieder entfernen.

> [!IMPORTANT]
> **„Der Computer wurde durch Windows geschützt“?** Windows zeigt diese Meldung bei neuen Programmen, die noch selten heruntergeladen wurden oder nicht mit einem kostenpflichtigen Zertifikat signiert sind. Klicke auf **Weitere Informationen** und dann auf **Trotzdem ausführen**. Wenn du vorher sichergehen willst, prüfe die Datei wie unten beschrieben.

## Ist es sicher? Download prüfen

- **Alles ist Open Source**: du kannst jede Codezeile in diesem Repository lesen.
- **Von GitHub gebaut, nicht auf einem privaten PC**: jedes Release wird von [GitHub Actions](.github/workflows/release.yml) direkt aus dem öffentlichen Code kompiliert. Der Link zum Build-Protokoll steht in den Release-Notizen.
- **Prüfsumme**: in PowerShell muss das Ergebnis dieses Befehls mit der Zeile in `SHA256SUMS.txt` desselben Releases übereinstimmen:

  ```powershell
  Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe
  ```

- **Signierte Build-Herkunft**: mit der [GitHub CLI](https://cli.github.com/) kannst du prüfen, dass die Datei aus diesem Repository stammt (ersetze `BESITZER/REPOSITORY` durch die Adresse dieses Repositorys):

  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo BESITZER/REPOSITORY
  ```

## Datenschutz

- Kein Konto, keine Werbung, keine Telemetrie. PhotoStudio funktioniert offline.
- Deine Fotos verlassen nie deinen PC, **außer du nutzt die KI-Funktionen**. Dann sendet PhotoStudio nur eine kleine Vorschau (höchstens 1024 Pixel an der langen Seite), die Reglerwerte, das Kameramodell mit den Aufnahmedaten und einige Helligkeitsstatistiken. Es sendet keine Dateinamen und keinen GPS-Standort, und nur an den Dienst, den du gewählt hast. Mit Ollama bleibt alles auf deinem Computer.
- API-Schlüssel werden von Windows verschlüsselt (DPAPI) und nur in `%APPDATA%\PhotoStudio` gespeichert. Nur dein Windows-Konto kann sie lesen.

## KI einrichten

Öffne **Bearbeiten ▸ KI-Einstellungen** und wähle einen Dienst:

| Dienst | Kosten | Was du brauchst |
|---|---|---|
| **Gemini** | Kostenlos (mit Tageslimit) | Einen Schlüssel von [Google AI Studio](https://aistudio.google.com/apikey). Empfohlenes Modell: `gemini-flash-latest`. |
| **Claude** | Kostenpflichtig | Einen Schlüssel aus der [Claude Console](https://console.anthropic.com/). |
| **Ollama** | Kostenlos, offline | [Ollama](https://ollama.com/) und ein Modell, das Bilder versteht, zum Beispiel `ollama pull gemma3`. |

## Für Entwickler

<details>
<summary><b>Aus dem Quellcode bauen</b></summary>

<br>

Benötigt das [.NET 10 SDK](https://dotnet.microsoft.com/download) unter Windows.

```powershell
git clone <Adresse des Repositorys>
cd PhotoStudio
dotnet run -c Release
```

So erzeugst du die Release-Dateien (einzelne exe, zip, Installer und Prüfsummen) wie GitHub:

```powershell
.\build.ps1 -Version 1.0.0      # für den Installer wird zusätzlich Inno Setup 6 benötigt
```

Um eine neue Version zu veröffentlichen, aktualisiere den Code und lade einen Tag hoch. GitHub baut und veröffentlicht das Release selbstständig:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

</details>

<details>
<summary><b>Übersetzungen</b></summary>

<br>

Die Übersetzungen liegen in [`Localization/`](Localization/): eine JSON-Datei pro Sprache, die den italienischen Text im Code seiner Übersetzung zuordnet.

- Um eine Übersetzung zu korrigieren, ändere ihren Wert.
- Um eine Sprache hinzuzufügen, kopiere `en.json`, übersetze die Werte und trage die Sprache in `Loc.Languages` in [`Core/Loc.cs`](Core/Loc.cs) ein.
- Fehlt ein Eintrag, erscheint der italienische Text.

</details>

### Mitwirken

Fehlerberichte, Ideen und Pull Requests sind in den [Issues](../../issues) willkommen. Sicherheitsprobleme meldest du vertraulich wie in [SECURITY.md](SECURITY.md) beschrieben.

## Lizenz

[MIT](LICENSE) © 2026 Luca Pezzoli. Komponenten von Drittanbietern sind in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) aufgeführt.
