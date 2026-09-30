<p align="center">
  <img src="Assets/icon-256.png" width="112" alt="Icône de PhotoStudio">
</p>

<h1 align="center">PhotoStudio</h1>

<p align="center">
  <b>Un éditeur photo gratuit et open source pour Windows.</b><br>
  Triez toute une séance, développez vos fichiers RAW et retouchez-les, avec l'aide facultative de l'IA.
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licence-MIT-2F80ED" alt="Licence : MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.it.md">Italiano</a> · <a href="README.de.md">Deutsch</a> · <b>Français</b> · <a href="README.es.md">Español</a>
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇ Télécharger la dernière version</b></a>
</p>

> [!NOTE]
> L'interface existe en français, anglais, italien, allemand et espagnol : PhotoStudio utilise la langue de Windows, et vous pouvez la changer dans *Affichage ▸ Langue*.

## Fonctionnalités

### 🗂️ Tri des photos
- Ouvrez un dossier entier ou seulement quelques photos, et choisissez les formats à afficher (par exemple, uniquement les RAW).
- Étoiles et libellés de couleur, avec des filtres pour n'afficher que les photos voulues.
- Les rafales sont regroupées automatiquement pour trouver vite la meilleure prise de vue.
- Comparaison côte à côte avec zoom synchronisé.
- Données de prise de vue (appareil, objectif, ISO, vitesse, ouverture) et histogramme avec alerte d'écrêtage.
- Votre travail est enregistré : rouvrez le dossier et reprenez là où vous vous étiez arrêté.

### 🎞️ Développement RAW
- Ouvre plus de 25 formats RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 et d'autres), ainsi que JPEG, PNG, TIFF, WebP et HEIC.
- Balance des blancs, exposition, contraste, hautes lumières, ombres, blancs, noirs, texture, clarté, correction du voile, vibrance et saturation.
- Courbe des tonalités (RVB et par couche), mélangeur de couleurs TSL et roues d'étalonnage des couleurs.
- Netteté, réduction du bruit, vignettage, grain, recadrage et redressement.
- Masques locaux (linéaires, radiaux et par luminosité), vue avant/après, paramètres prédéfinis et copier-coller des réglages.

### ☀️ Lumière intelligente, en toute simplicité
- **Lumière intelligente** : un clic corrige l'exposition, puis ajoute des masques de lumière seulement là où la photo en a besoin (zones sombres, zones claires, ciel, sujet).
- **Masques de lumière faciles** : un panneau simplifié pour les débutants, avec quelques choix clairs et sans curseurs techniques.

### ✨ Retouche assistée par IA (facultative)
- Décrivez ce que vous voulez (« plus chaud, comme au coucher du soleil ») ou laissez l'IA choisir la meilleure retouche.
- Fonctionne avec **Google Gemini** (clé gratuite), **Anthropic Claude** ou **Ollama** (entièrement hors ligne, sur votre PC).
- L'IA choisit seulement les valeurs des curseurs. PhotoStudio calcule lui-même les pixels, donc chaque retouche reste visible et modifiable.

### 🖌️ Éditeur photo et traitement par lots
- Calques, sélections, pinceau, réglages (niveaux, courbes, teinte/saturation, noir et blanc…) et filtres (flou, netteté, bruit, vignettage…).
- Format de projet `.psx`, qui conserve les calques.
- Appliquez des réglages ou des paramètres prédéfinis à de nombreuses photos à la fois.
- Export avec redimensionnement, renommage automatique et filigrane.

## Installation

1. Ouvrez la [**dernière version**](../../releases/latest).
2. Téléchargez **`PhotoStudio-x.y.z-win-x64-setup.exe`** (programme d'installation) ou le **`…-portable.zip`** (sans installation : décompressez-le et lancez `PhotoStudio.exe`).
3. Lancez-le. Il faut Windows 10 ou 11 (64 bits), rien d'autre : .NET est déjà inclus.

Le programme d'installation ne demande pas de droits d'administrateur et se désinstalle depuis *Paramètres ▸ Applications*.

> [!IMPORTANT]
> **« Windows a protégé votre ordinateur » ?** Windows affiche ce message pour les nouveaux programmes encore peu téléchargés ou non signés avec un certificat payant. Cliquez sur **Informations complémentaires ▸ Exécuter quand même**. Si vous voulez d'abord en être sûr, vérifiez le fichier comme expliqué ci-dessous.

## Est-ce sûr ? Vérifiez votre téléchargement

- **Tout est open source** : vous pouvez lire chaque ligne de code de ce dépôt.
- **Compilé par GitHub, pas sur un PC personnel** : chaque version est compilée par [GitHub Actions](.github/workflows/release.yml) directement à partir du code public. Le lien vers le journal de compilation figure dans les notes de version.
- **Somme de contrôle** : dans PowerShell, `Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe` doit correspondre à la ligne de `SHA256SUMS.txt` de la même version.
- **Provenance signée** : avec la [GitHub CLI](https://cli.github.com/), vous pouvez vérifier que le fichier a été produit par ce dépôt :
  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo PROPRIETAIRE/DEPOT
  ```
  (remplacez `PROPRIETAIRE/DEPOT` par l'adresse de ce dépôt).

## Confidentialité

- Pas de compte, pas de publicité, aucune télémétrie. PhotoStudio fonctionne hors ligne.
- Vos photos ne quittent jamais votre PC, **sauf si vous utilisez les fonctions d'IA**. Dans ce cas, PhotoStudio envoie seulement un petit aperçu (1024 pixels maximum sur le grand côté), les valeurs des curseurs, le modèle d'appareil avec les données de prise de vue et quelques statistiques de luminosité. Il n'envoie ni noms de fichiers ni position GPS, et seulement au service que vous avez choisi. Avec Ollama, tout reste sur votre ordinateur.
- Les clés API sont chiffrées par Windows (DPAPI) et enregistrées uniquement dans `%APPDATA%\PhotoStudio`. Seul votre compte Windows peut les lire.

### Configurer l'IA
Ouvrez *Modifica ▸ Impostazioni AI* (Édition ▸ Paramètres IA) et choisissez un service :
- **Gemini** : obtenez une clé gratuite sur [Google AI Studio](https://aistudio.google.com/apikey). Le modèle conseillé est `gemini-flash-latest`.
- **Claude** : obtenez une clé sur [console.anthropic.com](https://console.anthropic.com/) (payant).
- **Ollama** : installez [Ollama](https://ollama.com/) et un modèle qui comprend les images (par exemple `ollama pull gemma3`). Gratuit et hors ligne.

## Compiler depuis les sources

Nécessite le [SDK .NET 10](https://dotnet.microsoft.com/download) sous Windows.

```powershell
git clone <adresse du dépôt>
cd PhotoStudio
dotnet run -c Release
```

Pour créer les fichiers d'une version (exe unique, zip, programme d'installation et sommes de contrôle) comme le fait GitHub :

```powershell
.\build.ps1 -Version 1.0.0      # le programme d'installation nécessite aussi Inno Setup 6
```

Pour publier une nouvelle version : mettez à jour le code, puis `git tag v1.0.1` et `git push origin v1.0.1`. GitHub compile et publie la version tout seul.

## Contribuer

Les rapports de bugs, les idées et les pull requests sont les bienvenus dans les [Issues](../../issues).

## Licence

[MIT](LICENSE) © 2026 Luca Pezzoli. Les composants tiers sont listés dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
