<div align="center">

<img src="Assets/icon-256.png" width="112" alt="Icona di PhotoStudio">

# PhotoStudio

**Un editor di foto gratuito e open source per Windows.**<br>
Scegli le foto migliori di un servizio, sviluppa i RAW e ritocca, con l'aiuto facoltativo dell'AI.

[![Scarica](https://img.shields.io/badge/Scarica-per%20Windows-2F80ED?style=for-the-badge)](../../releases/latest)

[![Licenza: MIT](https://img.shields.io/badge/licenza-MIT-2F80ED)](LICENSE)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Lingue](https://img.shields.io/badge/lingue-IT%20%7C%20EN%20%7C%20DE%20%7C%20FR%20%7C%20ES-555)

[English](README.md) · **Italiano** · [Deutsch](README.de.md) · [Français](README.fr.md) · [Español](README.es.md)

</div>

<p align="center">
  <img src="Assets/screenshots/camera-raw.png" alt="La finestra Camera Raw di PhotoStudio, con il mix colori e le ruote di color grading">
</p>

<p align="center">
  <a href="#funzionalità">Funzionalità</a> ·
  <a href="#installazione">Installazione</a> ·
  <a href="#è-sicuro-verifica-il-download">È sicuro?</a> ·
  <a href="#privacy">Privacy</a> ·
  <a href="#configurare-lai">AI</a> ·
  <a href="#per-sviluppatori">Per sviluppatori</a>
</p>

## Funzionalità

### 🗂️ Preselezione

- Apri una cartella intera o solo alcune foto e scegli quali formati vedere (per esempio solo i RAW).
- Stelle ed etichette colorate, con filtri per vedere solo le foto che ti interessano.
- Le raffiche vengono raggruppate da sole, così trovi subito lo scatto migliore.
- Confronto affiancato con zoom sincronizzato.
- Un controllo automatico segnala le foto mosse o sfocate e le persone con gli occhi chiusi, e sceglie lo scatto migliore di ogni raffica.
- Dati di scatto (fotocamera, obiettivo, ISO, tempo, diaframma) e istogramma con avviso di bruciature.
- Il lavoro viene salvato: riapri la cartella e riprendi da dove eri rimasto.

### 🎞️ Sviluppo RAW

- Apre più di 25 formati RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 e altri), oltre a JPEG, PNG, TIFF, WebP e HEIC.
- Bilanciamento del bianco, esposizione, contrasto, luci, ombre, bianchi, neri, texture, chiarezza, rimozione foschia, vividezza e saturazione.
- Curva di tono (RGB e per canale), mix colori HSL e ruote di color grading.
- Nitidezza, riduzione del rumore, vignettatura, grana, ritaglio e raddrizzamento.
- Maschere locali (lineari, radiali e per luminosità), vista prima/dopo, preset e copia/incolla delle impostazioni.
- **Maschere AI del soggetto**: la maschera segue il contorno della persona, dell'animale o dell'oggetto, trovato da una rete sul tuo PC.
- **Riduzione rumore AI** e **rimessa a fuoco AI** (sul soggetto o su tutta la foto), con reti neurali che girano sul tuo PC, sulla scheda video quando possibile. Le reti (circa 70 e 115 MB) si scaricano dalle release di questo progetto la prima volta che le usi.

### ☀️ Luce intelligente, anche per principianti

- **Luce intelligente**: con un clic corregge l'esposizione e aggiunge maschere di luce solo dove servono (zone scure, zone chiare, cielo, volti o soggetto). Riconosce il cielo e i volti nella foto (sul tuo PC, senza inviare nulla), lascia scure le notturne e può dare la stessa correzione a un'intera serie di scatti.
- **Maschere di luce (facile)**: un pannello semplificato per principianti, con ricette pronte e senza cursori tecnici.

### ✨ Modifica con l'AI (facoltativa)

- Descrivi cosa vuoi ("più calda, come al tramonto") oppure lascia che l'AI scelga la modifica migliore.
- Funziona con **Google Gemini** (chiave gratuita), **Anthropic Claude** oppure **Ollama** (senza Internet, sul tuo PC).
- L'AI sceglie solo i valori dei cursori. I pixel li calcola PhotoStudio, quindi ogni modifica resta visibile e modificabile.

### 🖌️ Editor di foto e lavoro su tante foto

- Livelli, selezioni, pennello, regolazioni (livelli, curve, tonalità/saturazione, bianco e nero…) e filtri (sfocatura, nitidezza, disturbo, vignettatura…).
- Formato di progetto `.psx`, che conserva i livelli.
- **Ingrandimento AI 2×** (menu Immagine), che ricostruisce i dettagli invece di allargare solo i pixel.
- Applica impostazioni o preset a molte foto insieme.
- Esportazione con ridimensionamento, rinomina automatica e filigrana.

### 🌍 Nella tua lingua

L'interfaccia è in italiano, inglese, tedesco, francese e spagnolo. PhotoStudio usa la lingua di Windows e puoi cambiarla in **Visualizza ▸ Lingua**.

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/culling.png" alt="Finestra di preselezione con la striscia delle foto e l'istogramma"><br>
      <sub><b>Preselezione</b>: sfoglia un servizio, dai le stelle ed elimina gli scarti.</sub>
    </td>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/masks.png" alt="Scheda Maschere con le ricette veloci"><br>
      <sub><b>Maschere di luce facili</b>: ricette con un clic per cielo, ombre e soggetto.</sub>
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <img src="Assets/screenshots/editor.png" alt="Finestra principale con livelli, cronologia e istogramma"><br>
      <sub><b>Editor</b>: livelli, strumenti, cronologia e istogramma.</sub>
    </td>
  </tr>
</table>

<sub>Le schermate mostrano l'interfaccia in inglese.</sub>

## Installazione

1. Apri l'[**ultima release**](../../releases/latest).
2. Scarica uno dei due file:
   - **`PhotoStudio-x.y.z-win-x64-setup.exe`**: l'installer (consigliato);
   - **`PhotoStudio-x.y.z-win-x64-portable.zip`**: niente installazione, estrai lo zip e avvia `PhotoStudio.exe`.
3. Avvialo. Serve solo Windows 10 o 11 a 64 bit: .NET è già incluso.

L'installer non chiede i permessi di amministratore e puoi disinstallare il programma da **Impostazioni ▸ App**.

> [!IMPORTANT]
> **Compare "Windows ha protetto il PC"?** Windows mostra questo messaggio per i programmi nuovi, non ancora molto scaricati o non firmati con un certificato a pagamento. Clicca **Ulteriori informazioni**, poi **Esegui comunque**. Se prima vuoi esserne sicuro, verifica il file come spiegato qui sotto.

## È sicuro? Verifica il download

- **Il codice è tutto pubblico**: puoi leggere ogni riga in questo repository.
- **Lo compila GitHub, non un PC privato**: ogni release viene compilata da [GitHub Actions](.github/workflows/release.yml) direttamente dal codice pubblico. Il link al log della compilazione è nelle note della release.
- **Checksum**: in PowerShell, il risultato di questo comando deve coincidere con la riga di `SHA256SUMS.txt` della stessa release:

  ```powershell
  Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe
  ```

- **Provenienza firmata**: con la [GitHub CLI](https://cli.github.com/) puoi controllare che il file sia stato prodotto da questo repository (sostituisci `PROPRIETARIO/REPOSITORY` con l'indirizzo di questo repository):

  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo PROPRIETARIO/REPOSITORY
  ```

## Privacy

- Niente account, niente pubblicità, nessuna telemetria. PhotoStudio funziona anche senza Internet.
- Le tue foto non escono mai dal PC, **a meno che tu non usi le funzioni AI**. In quel caso PhotoStudio invia solo un'anteprima piccola (al massimo 1024 pixel sul lato lungo), i valori dei cursori, il modello della fotocamera con i dati di scatto e alcune statistiche sulla luminosità. Non invia nomi dei file né la posizione GPS, e li invia solo al servizio che hai scelto. Con Ollama tutto resta sul tuo computer.
- Una volta al giorno PhotoStudio chiede a GitHub se c'è una nuova versione (**Aiuto ▸ Cerca aggiornamenti**). Non invia niente su di te o sulle tue foto. Se trova una versione nuova chiede prima di installarla, e controlla il download con il file `SHA256SUMS.txt` della release.
- Le chiavi API sono cifrate da Windows (DPAPI) e salvate solo in `%APPDATA%\PhotoStudio`. Può leggerle solo il tuo account Windows.

## Configurare l'AI

Apri **Modifica ▸ Impostazioni AI** e scegli un servizio:

| Servizio | Costo | Cosa serve |
|---|---|---|
| **Gemini** | Gratuito (con limiti giornalieri) | Una chiave da [Google AI Studio](https://aistudio.google.com/apikey). Modello consigliato: `gemini-flash-latest`. |
| **Claude** | A pagamento | Una chiave dalla [Claude Console](https://console.anthropic.com/). |
| **Ollama** | Gratuito, senza Internet | [Ollama](https://ollama.com/) e un modello che vede le immagini, per esempio `ollama pull gemma3`. |

## Per sviluppatori

<details>
<summary><b>Compilare dal codice sorgente</b></summary>

<br>

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

Per pubblicare una nuova versione aggiorna il codice e pubblica un tag. GitHub compila e pubblica la release da solo:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

</details>

<details>
<summary><b>Traduzioni</b></summary>

<br>

Le traduzioni sono in [`Localization/`](Localization/): un file JSON per lingua, che associa il testo italiano usato nel codice alla sua traduzione.

- Per correggere una traduzione, modifica il suo valore.
- Per aggiungere una lingua, copia `en.json`, traduci i valori e aggiungi la lingua a `Loc.Languages` in [`Core/Loc.cs`](Core/Loc.cs).
- Se una voce manca, compare il testo italiano.

</details>

### Contribuire

Segnalazioni di bug, idee e pull request sono benvenute nelle [Issues](../../issues). Per segnalare in privato un problema di sicurezza, leggi [SECURITY.md](SECURITY.md).

## Licenza

[MIT](LICENSE) © 2026 Luca Pezzoli. I componenti di terze parti sono elencati in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
