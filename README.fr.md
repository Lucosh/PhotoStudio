<div align="center">

<img src="Assets/icon-256.png" width="112" alt="Icône de PhotoStudio">

# PhotoStudio

**Un éditeur photo gratuit et open source pour Windows.**<br>
Triez toute une séance, développez vos fichiers RAW et retouchez-les, avec l'aide facultative de l'IA.

[![Télécharger](https://img.shields.io/badge/T%C3%A9l%C3%A9charger-pour%20Windows-2F80ED?style=for-the-badge)](../../releases/latest)

[![Licence : MIT](https://img.shields.io/badge/licence-MIT-2F80ED)](LICENSE)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Langues](https://img.shields.io/badge/langues-FR%20%7C%20EN%20%7C%20IT%20%7C%20DE%20%7C%20ES-555)

[English](README.md) · [Italiano](README.it.md) · [Deutsch](README.de.md) · **Français** · [Español](README.es.md)

</div>

<p align="center">
  <img src="Assets/screenshots/camera-raw.png" alt="La fenêtre Camera Raw de PhotoStudio, avec le mélangeur de couleurs et les roues d'étalonnage">
</p>

<p align="center">
  <a href="#fonctionnalités">Fonctionnalités</a> ·
  <a href="#installation">Installation</a> ·
  <a href="#est-ce-sûr--vérifiez-votre-téléchargement">Est-ce sûr ?</a> ·
  <a href="#confidentialité">Confidentialité</a> ·
  <a href="#configurer-lia">Configurer l'IA</a> ·
  <a href="#pour-les-développeurs">Pour les développeurs</a>
</p>

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
- Masques locaux (linéaires, radiaux et par luminosité), vue avant/après, préréglages et copier-coller des réglages.

### ☀️ Lumière intelligente, en toute simplicité

- **Lumière intelligente** : un clic corrige l'exposition, puis ajoute des masques de lumière seulement là où la photo en a besoin (zones sombres, zones claires, ciel, visages ou sujet). Elle reconnaît le ciel et les visages dans la photo (sur votre PC, rien n'est envoyé), laisse sombres les photos de nuit et peut donner la même correction à toute une série de prises de vue.
- **Masques de lumière faciles** : un panneau simplifié pour les débutants, avec des recettes prêtes et sans curseurs techniques.

### ✨ Retouche assistée par IA (facultative)

- Décrivez ce que vous voulez (« plus chaud, comme au coucher du soleil ») ou laissez l'IA choisir la meilleure retouche.
- Fonctionne avec **Google Gemini** (clé gratuite), **Anthropic Claude** ou **Ollama** (entièrement hors ligne, sur votre PC).
- L'IA choisit seulement les valeurs des curseurs. PhotoStudio calcule lui-même les pixels, donc chaque retouche reste visible et modifiable.

### 🖌️ Éditeur photo et traitement par lots

- Calques, sélections, pinceau, réglages (niveaux, courbes, teinte/saturation, noir et blanc…) et filtres (flou, netteté, bruit, vignettage…).
- Format de projet `.psx`, qui conserve les calques.
- Appliquez des réglages ou des préréglages à de nombreuses photos à la fois.
- Export avec redimensionnement, renommage automatique et filigrane.

### 🌍 Dans votre langue

L'interface existe en français, anglais, italien, allemand et espagnol. PhotoStudio utilise la langue de Windows, et vous pouvez la changer dans **Affichage ▸ Langue**.

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/culling.png" alt="Fenêtre de tri avec le bandeau de photos et l'histogramme"><br>
      <sub><b>Tri</b> : parcourez une séance, notez-la et supprimez les ratés.</sub>
    </td>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/masks.png" alt="Onglet Masques avec les recettes rapides"><br>
      <sub><b>Masques de lumière faciles</b> : des recettes en un clic pour le ciel, les ombres et le sujet.</sub>
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <img src="Assets/screenshots/editor.png" alt="Fenêtre principale avec calques, historique et histogramme"><br>
      <sub><b>Éditeur</b> : calques, outils, historique et histogramme.</sub>
    </td>
  </tr>
</table>

<sub>Les captures d'écran montrent l'interface en anglais.</sub>

## Installation

1. Ouvrez la [**dernière version**](../../releases/latest).
2. Téléchargez l'un des deux fichiers :
   - **`PhotoStudio-x.y.z-win-x64-setup.exe`** : le programme d'installation (recommandé) ;
   - **`PhotoStudio-x.y.z-win-x64-portable.zip`** : sans installation, il suffit de le décompresser et de lancer `PhotoStudio.exe`.
3. Lancez-le. Il suffit de Windows 10 ou 11 (64 bits) : .NET est déjà inclus.

Le programme d'installation ne demande pas de droits d'administrateur, et vous pouvez désinstaller le programme depuis **Paramètres ▸ Applications**.

> [!IMPORTANT]
> **« Windows a protégé votre ordinateur » ?** Windows affiche ce message pour les nouveaux programmes encore peu téléchargés ou non signés avec un certificat payant. Cliquez sur **Informations complémentaires**, puis sur **Exécuter quand même**. Si vous voulez d'abord en être sûr, vérifiez le fichier comme expliqué ci-dessous.

## Est-ce sûr ? Vérifiez votre téléchargement

- **Tout est open source** : vous pouvez lire chaque ligne de code de ce dépôt.
- **Compilé par GitHub, pas sur un PC personnel** : chaque version est compilée par [GitHub Actions](.github/workflows/release.yml) directement à partir du code public. Le lien vers le journal de compilation figure dans les notes de version.
- **Somme de contrôle** : dans PowerShell, le résultat de cette commande doit correspondre à la ligne de `SHA256SUMS.txt` de la même version :

  ```powershell
  Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe
  ```

- **Provenance signée** : avec la [GitHub CLI](https://cli.github.com/), vous pouvez vérifier que le fichier a été produit par ce dépôt (remplacez `PROPRIETAIRE/DEPOT` par l'adresse de ce dépôt) :

  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo PROPRIETAIRE/DEPOT
  ```

## Confidentialité

- Pas de compte, pas de publicité, aucune télémétrie. PhotoStudio fonctionne hors ligne.
- Vos photos ne quittent jamais votre PC, **sauf si vous utilisez les fonctions d'IA**. Dans ce cas, PhotoStudio envoie seulement un petit aperçu (1024 pixels maximum sur le grand côté), les valeurs des curseurs, le modèle d'appareil avec les données de prise de vue et quelques statistiques de luminosité. Il n'envoie ni noms de fichiers ni position GPS, et seulement au service que vous avez choisi. Avec Ollama, tout reste sur votre ordinateur.
- Les clés API sont chiffrées par Windows (DPAPI) et enregistrées uniquement dans `%APPDATA%\PhotoStudio`. Seul votre compte Windows peut les lire.

## Configurer l'IA

Ouvrez **Édition ▸ Paramètres IA** et choisissez un service :

| Service | Coût | Ce qu'il faut |
|---|---|---|
| **Gemini** | Gratuit (avec limites quotidiennes) | Une clé de [Google AI Studio](https://aistudio.google.com/apikey). Modèle conseillé : `gemini-flash-latest`. |
| **Claude** | Payant | Une clé de la [Claude Console](https://console.anthropic.com/). |
| **Ollama** | Gratuit, hors ligne | [Ollama](https://ollama.com/) et un modèle qui comprend les images, par exemple `ollama pull gemma3`. |

## Pour les développeurs

<details>
<summary><b>Compiler depuis les sources</b></summary>

<br>

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

Pour publier une nouvelle version, mettez à jour le code et poussez un tag. GitHub compile et publie la version tout seul :

```powershell
git tag v1.0.1
git push origin v1.0.1
```

</details>

<details>
<summary><b>Traductions</b></summary>

<br>

Les traductions se trouvent dans [`Localization/`](Localization/) : un fichier JSON par langue, qui associe le texte italien utilisé dans le code à sa traduction.

- Pour corriger une traduction, modifiez sa valeur.
- Pour ajouter une langue, copiez `en.json`, traduisez les valeurs et ajoutez la langue à `Loc.Languages` dans [`Core/Loc.cs`](Core/Loc.cs).
- Si une entrée manque, le texte italien s'affiche.

</details>

### Contribuer

Les rapports de bugs, les idées et les pull requests sont les bienvenus dans les [Issues](../../issues). Pour signaler un problème de sécurité en privé, consultez [SECURITY.md](SECURITY.md).

## Licence

[MIT](LICENSE) © 2026 Luca Pezzoli. Les composants tiers sont listés dans [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
