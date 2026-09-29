<p align="center">
  <img src="Assets/icon-256.png" width="112" alt="Icona di PhotoStudio">
</p>

<h1 align="center">PhotoStudio</h1>

<p align="center">
  <b>Un editor di foto gratuito e open source per Windows.</b><br>
  Scegli le foto migliori di un servizio, sviluppa i RAW e ritocca, con l'aiuto facoltativo dell'AI.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licenza-MIT-2F80ED" alt="Licenza: MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
</p>

<p align="center">
  <a href="README.md">English</a> · <b>Italiano</b> · <a href="README.de.md">Deutsch</a> · <a href="README.fr.md">Français</a> · <a href="README.es.md">Español</a>
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Scarica l'ultima versione</b></a>
</p>

## Funzionalità

### 🗂️ Preselezione
- Apri una cartella intera o solo alcune foto e scegli quali formati vedere (per esempio solo i RAW).
- Stelle ed etichette colorate, con filtri per vedere solo le foto che ti interessano.
- Le raffiche vengono raggruppate da sole, così trovi subito lo scatto migliore.
- Confronto affiancato con zoom sincronizzato.
- Dati di scatto (fotocamera, obiettivo, ISO, tempo, diaframma) e istogramma con avviso di bruciature.
- Il lavoro viene salvato: riapri la cartella e riprendi da dove eri rimasto.

### 🎞️ Sviluppo RAW
- Apre più di 25 formati RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 e altri), oltre a JPEG, PNG, TIFF, WebP e HEIC.
- Bilanciamento del bianco, esposizione, contrasto, luci, ombre, bianchi, neri, texture, chiarezza, rimozione foschia, vividezza e saturazione.
- Curva di tono (RGB e per canale), mixer colori HSL e ruote di color grading.
- Nitidezza, riduzione del rumore, vignettatura, grana, ritaglio e raddrizzamento.
- Maschere locali (lineari, radiali e per luminosità), vista prima/dopo, preset e copia/incolla delle impostazioni.

### ☀️ Luce intelligente, anche per principianti
- **Luce intelligente**: con un clic corregge l'esposizione e aggiunge maschere di luce solo dove servono (zone scure, zone chiare, cielo, soggetto).
- **Maschere di luce (facile)**: un pannello semplificato per principianti, con poche scelte chiare e senza cursori tecnici.

### ✨ Modifica con l'AI (facoltativa)
- Descrivi cosa vuoi ("più calda, come al tramonto") oppure lascia che l'AI scelga la modifica migliore.
- Funziona con **Google Gemini** (chiave gratuita), **Anthropic Claude** oppure **Ollama** (senza Internet, sul tuo PC).
- L'AI sceglie solo i valori dei cursori. I pixel li calcola PhotoStudio, quindi ogni modifica resta visibile e modificabile.

### 🖌️ Editor di foto e lavoro su tante foto
- Livelli, selezioni, pennello, regolazioni (livelli, curve, tonalità/saturazione, bianco e nero…) e filtri (sfocatura, nitidezza, disturbo, vignettatura…).
- Formato di progetto `.psx`, che conserva i livelli.
- Applica impostazioni o preset a molte foto insieme.
- Esportazione con ridimensionamento, rinomina automatica e filigrana.

## Installazione

1. Apri l'[**ultima release**](../../releases/latest).
2. Scarica **`PhotoStudio-x.y.z-win-x64-setup.exe`** (installer) oppure il **`…-portable.zip`** (niente installazione: estrai lo zip e avvia `PhotoStudio.exe`).
3. Avvialo. Serve Windows 10 o 11 a 64 bit e nient'altro: .NET è già incluso.

L'installer non chiede i permessi di amministratore e si disinstalla da *Impostazioni ▸ App*.

> [!IMPORTANT]
> **Compare "Windows ha protetto il PC"?** Windows mostra questo messaggio per i programmi nuovi, non ancora molto scaricati o non firmati con un certificato a pagamento. Clicca **Ulteriori informazioni ▸ Esegui comunque**. Se prima vuoi esserne sicuro, verifica il file come spiegato qui sotto.

## È sicuro? Verifica il download

- **Il codice è tutto pubblico**: puoi leggere ogni riga in questo repository.
- **Lo compila GitHub, non un PC privato**: ogni release viene compilata da [GitHub Actions](.github/workflows/release.yml) direttamente dal codice pubblico. Il link al log della compilazione è nelle note della release.
- **Checksum**: in PowerShell, `Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe` deve dare lo stesso valore scritto in `SHA256SUMS.txt` della stessa release.
- **Provenienza firmata**: con la [GitHub CLI](https://cli.github.com/) puoi controllare che il file sia stato prodotto da questo repository:
  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo PROPRIETARIO/REPOSITORY
  ```
  (sostituisci `PROPRIETARIO/REPOSITORY` con l'indirizzo di questo repository).

## Privacy

- Niente account, niente pubblicità, nessuna telemetria. PhotoStudio funziona anche senza Internet.
- Le tue foto non escono mai dal PC, **a meno che tu non usi le funzioni AI**. In quel caso PhotoStudio invia solo un'anteprima piccola (al massimo 1024 pixel sul lato lungo), i valori dei cursori, il modello della fotocamera con i dati di scatto e alcune statistiche sulla luminosità. Non invia nomi dei file né la posizione GPS, e li invia solo al servizio che hai scelto. Con Ollama tutto resta sul tuo computer.
- Le chiavi API sono cifrate da Windows (DPAPI) e salvate solo in `%APPDATA%\PhotoStudio`. Può leggerle solo il tuo account Windows.

### Configurare l'AI
Vai in *Modifica ▸ Impostazioni AI* e scegli un servizio:
- **Gemini**: ottieni una chiave gratuita su [Google AI Studio](https://aistudio.google.com/apikey). Il modello consigliato è `gemini-flash-latest`.
- **Claude**: ottieni una chiave su [console.anthropic.com](https://console.anthropic.com/) (a pagamento).
- **Ollama**: installa [Ollama](https://ollama.com/) e un modello che vede le immagini (per esempio `ollama pull gemma3`). È gratuito e funziona senza Internet.

## Compilare dal codice sorgente

Serve il [.NET 10 SDK](https://dotnet.microsoft.com/download) su Windows.

```powershell
git clone <indirizzo del repository>
cd PhotoStudio
dotnet run -c Release
```

Per creare i file di una release (exe unico, zip, installer e checksum) come fa GitHub:

```powershell
.\build.ps1 -Version 1.0.0      # per l'installer serve anche Inno Setup 6
```

Per pubblicare una nuova versione: aggiorna il codice, poi `git tag v1.0.1` e `git push origin v1.0.1`. GitHub compila e pubblica la release da solo.

## Contribuire

Segnalazioni di bug, idee e pull request sono benvenute nelle [Issues](../../issues).

## Licenza

[MIT](LICENSE) © 2026 Luca Pezzoli. I componenti di terze parti sono elencati in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
