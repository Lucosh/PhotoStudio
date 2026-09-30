<p align="center">
  <img src="Assets/icon-256.png" width="112" alt="PhotoStudio icon">
</p>

<h1 align="center">PhotoStudio</h1>

<p align="center">
  <b>A free, open-source photo editor for Windows.</b><br>
  Sort a whole shoot, develop your RAW files and retouch them, with optional AI help.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-2F80ED" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
</p>

<p align="center">
  <b>English</b> · <a href="README.it.md">Italiano</a> · <a href="README.de.md">Deutsch</a> · <a href="README.fr.md">Français</a> · <a href="README.es.md">Español</a>
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Download the latest version</b></a>
</p>

> [!NOTE]
> The interface is available in English, Italian, German, French and Spanish: PhotoStudio uses the Windows language, and you can change it in *View ▸ Language*.

## Features

### 🗂️ Culling
- Open a whole folder or just a few photos, and choose which formats to show (for example, only RAW files).
- Star ratings and colour labels, with filters to show only the photos you want.
- Bursts are grouped automatically, so you can pick the best shot quickly.
- Compare photos side by side with synchronised zoom.
- Shooting data (camera, lens, ISO, shutter speed, aperture) and a histogram with clipping warnings.
- Your work is saved: reopen the folder and carry on where you left off.

### 🎞️ RAW development
- Opens more than 25 camera RAW formats (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 and more), as well as JPEG, PNG, TIFF, WebP and HEIC.
- White balance, exposure, contrast, highlights, shadows, whites, blacks, texture, clarity, dehaze, vibrance and saturation.
- Tone curve (RGB and per channel), HSL colour mixer and colour grading wheels.
- Sharpening, noise reduction, vignette, grain, crop and straighten.
- Local masks (linear, radial and by brightness), a before/after view, presets, and copy/paste of settings.

### ☀️ Smart light, made easy
- **Smart light**: one click fixes the exposure, then adds light masks only where the photo needs them (dark areas, bright areas, sky, subject).
- **Easy light masks**: a simplified panel for beginners, with a few clear choices and no technical sliders.

### ✨ AI-assisted editing (optional)
- Describe what you want ("warmer, like a sunset") or let the AI choose the best edit.
- Works with **Google Gemini** (free key), **Anthropic Claude**, or **Ollama** (completely offline, on your PC).
- The AI only picks slider values. PhotoStudio renders the pixels itself, so every edit stays visible and editable.

### 🖌️ Photo editor and batch work
- Layers, selections, brush, adjustments (levels, curves, hue/saturation, black and white…) and filters (blur, sharpen, noise, vignette…).
- `.psx` project format, which keeps your layers.
- Apply settings or presets to many photos at once.
- Export with resizing, automatic renaming and a watermark.

## Installation

1. Open the [**latest release**](../../releases/latest).
2. Download **`PhotoStudio-x.y.z-win-x64-setup.exe`** (installer) or the **`…-portable.zip`** (no installation: unzip it and run `PhotoStudio.exe`).
3. Run it. It needs Windows 10 or 11 (64-bit) and nothing else: .NET is already included.

The installer does not need administrator rights and can be removed from *Settings ▸ Apps*.

> [!IMPORTANT]
> **"Windows protected your PC"?** Windows shows this message for new programs that are not yet widely downloaded or signed with a paid certificate. Click **More info ▸ Run anyway**. If you want to be sure first, verify the file as described below.

## Is it safe? Verify your download

- **Everything is open source**: you can read every line of code in this repository.
- **Built by GitHub, not on a personal PC**: each release is compiled by [GitHub Actions](.github/workflows/release.yml) directly from the public code. The link to the build log is in the release notes.
- **Checksum**: in PowerShell, `Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe` must match the line in `SHA256SUMS.txt` of the same release.
- **Signed build provenance**: with the [GitHub CLI](https://cli.github.com/) you can check that the file was produced by this repository:
  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo OWNER/REPO
  ```
  (replace `OWNER/REPO` with the address of this repository).

## Privacy

- No accounts, no ads and no telemetry. PhotoStudio works offline.
- Your photos never leave your PC, **unless you use the AI features**. In that case PhotoStudio sends only a small preview (at most 1024 pixels on the long side), the slider values, the camera model and shooting settings, and a few brightness statistics. It sends no file names and no GPS position, and only to the service you chose. With Ollama, everything stays on your computer.
- API keys are encrypted with Windows (DPAPI) and saved only in `%APPDATA%\PhotoStudio`, readable only by your Windows account.

### Setting up the AI
Go to *Modifica ▸ Impostazioni AI* (Edit ▸ AI settings) and choose a provider:
- **Gemini**: get a free key at [Google AI Studio](https://aistudio.google.com/apikey). The recommended model is `gemini-flash-latest`.
- **Claude**: get a key at [console.anthropic.com](https://console.anthropic.com/) (paid use).
- **Ollama**: install [Ollama](https://ollama.com/) and a vision model (for example `ollama pull gemma3`). It is free and runs offline.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
git clone <repository address>
cd PhotoStudio
dotnet run -c Release
```

To create the release files (single exe, zip, installer and checksums) as GitHub does:

```powershell
.\build.ps1 -Version 1.0.0      # the installer also needs Inno Setup 6
```

To publish a new version: update the code, then `git tag v1.0.1` and `git push origin v1.0.1`. GitHub builds and publishes the release by itself.

## Contributing

Bug reports, ideas and pull requests are welcome in [Issues](../../issues).

**Translations** live in [`Localization/`](Localization/): one JSON file per language, mapping the Italian text used in the code to its translation. To fix a translation, edit the value; to add a language, copy `en.json`, translate the values and add the language to `Loc.Languages` in [`Core/Loc.cs`](Core/Loc.cs). A missing entry simply shows the Italian text.

## License

[MIT](LICENSE) © 2026 Luca Pezzoli. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
