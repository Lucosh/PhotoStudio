<p align="center">
  <img src="Assets/icon-256.png" width="112" alt="PhotoStudio-Symbol">
</p>

<h1 align="center">PhotoStudio</h1>

<p align="center">
  <b>Ein kostenloser Open-Source-Fotoeditor für Windows.</b><br>
  Ein ganzes Shooting aussortieren, RAW-Dateien entwickeln und retuschieren, auf Wunsch mit KI-Hilfe.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/Lizenz-MIT-2F80ED" alt="Lizenz: MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.it.md">Italiano</a> · <b>Deutsch</b> · <a href="README.fr.md">Français</a> · <a href="README.es.md">Español</a>
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Neueste Version herunterladen</b></a>
</p>

> [!NOTE]
> Die Benutzeroberfläche ist derzeit auf Italienisch. Deutsch, Englisch, Französisch und Spanisch folgen.

## Funktionen

### 🗂️ Aussortieren
- Einen ganzen Ordner oder nur einige Fotos öffnen und festlegen, welche Formate angezeigt werden (zum Beispiel nur RAW).
- Sterne und Farbmarkierungen, mit Filtern, um nur die gewünschten Fotos zu sehen.
- Serienaufnahmen werden automatisch gruppiert, damit du schnell das beste Bild findest.
- Fotos nebeneinander vergleichen, mit synchronisiertem Zoom.
- Aufnahmedaten (Kamera, Objektiv, ISO, Belichtungszeit, Blende) und Histogramm mit Warnung vor Clipping.
- Deine Arbeit wird gespeichert: Öffne den Ordner erneut und mach dort weiter, wo du aufgehört hast.

### 🎞️ RAW-Entwicklung
- Öffnet über 25 Kamera-RAW-Formate (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 und weitere) sowie JPEG, PNG, TIFF, WebP und HEIC.
- Weißabgleich, Belichtung, Kontrast, Lichter, Tiefen, Weiß, Schwarz, Struktur, Klarheit, Dunst entfernen, Dynamik und Sättigung.
- Gradationskurve (RGB und pro Kanal), HSL-Farbmischer und Color-Grading-Räder.
- Schärfen, Rauschreduzierung, Vignette, Körnung, Freistellen und Ausrichten.
- Lokale Masken (linear, radial und nach Helligkeit), Vorher/Nachher-Ansicht, Vorgaben sowie Kopieren/Einfügen von Einstellungen.

### ☀️ Intelligentes Licht, ganz einfach
- **Intelligentes Licht**: Ein Klick korrigiert die Belichtung und fügt Lichtmasken nur dort hinzu, wo das Foto sie braucht (dunkle Bereiche, helle Bereiche, Himmel, Motiv).
- **Einfache Lichtmasken**: ein vereinfachtes Bedienfeld für Einsteiger, mit wenigen klaren Optionen und ohne technische Regler.

### ✨ Bearbeitung mit KI (optional)
- Beschreibe, was du möchtest („wärmer, wie bei Sonnenuntergang“), oder lass die KI die beste Bearbeitung wählen.
- Funktioniert mit **Google Gemini** (kostenloser Schlüssel), **Anthropic Claude** oder **Ollama** (komplett offline auf deinem PC).
- Die KI wählt nur die Reglerwerte. Die Pixel berechnet PhotoStudio selbst, daher bleibt jede Änderung sichtbar und bearbeitbar.

### 🖌️ Bildbearbeitung und Stapelverarbeitung
- Ebenen, Auswahlen, Pinsel, Korrekturen (Tonwerte, Kurven, Farbton/Sättigung, Schwarzweiß …) und Filter (Weichzeichnen, Scharfzeichnen, Rauschen, Vignette …).
- Projektformat `.psx`, das die Ebenen erhält.
- Einstellungen oder Vorgaben auf viele Fotos gleichzeitig anwenden.
- Export mit Größenänderung, automatischer Umbenennung und Wasserzeichen.

## Installation

1. Öffne das [**neueste Release**](../../releases/latest).
2. Lade **`PhotoStudio-x.y.z-win-x64-setup.exe`** (Installer) oder die **`…-portable.zip`** herunter (ohne Installation: entpacken und `PhotoStudio.exe` starten).
3. Starte es. Du brauchst Windows 10 oder 11 (64 Bit) und sonst nichts: .NET ist bereits enthalten.

Der Installer benötigt keine Administratorrechte und lässt sich unter *Einstellungen ▸ Apps* entfernen.

> [!IMPORTANT]
> **„Der Computer wurde durch Windows geschützt“?** Windows zeigt diese Meldung bei neuen Programmen, die noch selten heruntergeladen wurden oder nicht mit einem kostenpflichtigen Zertifikat signiert sind. Klicke auf **Weitere Informationen ▸ Trotzdem ausführen**. Wenn du vorher sichergehen willst, prüfe die Datei wie unten beschrieben.

## Ist es sicher? Download prüfen

- **Alles ist Open Source**: Du kannst jede Codezeile in diesem Repository lesen.
- **Von GitHub gebaut, nicht auf einem privaten PC**: Jedes Release wird von [GitHub Actions](.github/workflows/release.yml) direkt aus dem öffentlichen Code kompiliert. Der Link zum Build-Protokoll steht in den Release-Notizen.
- **Prüfsumme**: In PowerShell muss `Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe` mit der Zeile in `SHA256SUMS.txt` desselben Releases übereinstimmen.
- **Signierte Build-Herkunft**: Mit der [GitHub CLI](https://cli.github.com/) kannst du prüfen, dass die Datei aus diesem Repository stammt:
  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo BESITZER/REPOSITORY
  ```
  (ersetze `BESITZER/REPOSITORY` durch die Adresse dieses Repositorys).

## Datenschutz

- Kein Konto, keine Werbung, keine Telemetrie. PhotoStudio funktioniert offline.
- Deine Fotos verlassen nie deinen PC, **außer du nutzt die KI-Funktionen**. Dann sendet PhotoStudio nur eine kleine Vorschau (höchstens 1024 Pixel an der langen Seite), die Reglerwerte, das Kameramodell mit den Aufnahmedaten und einige Helligkeitsstatistiken. Es sendet keine Dateinamen und keinen GPS-Standort, und nur an den Dienst, den du gewählt hast. Mit Ollama bleibt alles auf deinem Computer.
- API-Schlüssel werden von Windows verschlüsselt (DPAPI) und nur in `%APPDATA%\PhotoStudio` gespeichert. Nur dein Windows-Konto kann sie lesen.

### KI einrichten
Öffne *Modifica ▸ Impostazioni AI* (Bearbeiten ▸ KI-Einstellungen) und wähle einen Dienst:
- **Gemini**: Einen kostenlosen Schlüssel gibt es bei [Google AI Studio](https://aistudio.google.com/apikey). Empfohlenes Modell: `gemini-flash-latest`.
- **Claude**: Einen Schlüssel gibt es bei [console.anthropic.com](https://console.anthropic.com/) (kostenpflichtig).
- **Ollama**: Installiere [Ollama](https://ollama.com/) und ein Modell, das Bilder versteht (zum Beispiel `ollama pull gemma3`). Kostenlos und offline.

## Aus dem Quellcode bauen

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

Neue Version veröffentlichen: Code aktualisieren, dann `git tag v1.0.1` und `git push origin v1.0.1`. GitHub baut und veröffentlicht das Release selbstständig.

## Mitwirken

Fehlerberichte, Ideen und Pull Requests sind in den [Issues](../../issues) willkommen.

## Lizenz

[MIT](LICENSE) © 2026 Luca Pezzoli. Komponenten von Drittanbietern sind in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) aufgeführt.
