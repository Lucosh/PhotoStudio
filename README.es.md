<div align="center">

<img src="Assets/icon-256.png" width="112" alt="Icono de PhotoStudio">

# PhotoStudio

**Un editor de fotos gratuito y de código abierto para Windows.**<br>
Selecciona las mejores fotos de una sesión, revela tus archivos RAW y retócalos, con la ayuda opcional de la IA.

[![Descargar](https://img.shields.io/badge/Descargar-para%20Windows-2F80ED?style=for-the-badge)](../../releases/latest)

[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-2F80ED)](LICENSE)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-2F80ED)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Idiomas](https://img.shields.io/badge/idiomas-ES%20%7C%20EN%20%7C%20IT%20%7C%20DE%20%7C%20FR-555)

[English](README.md) · [Italiano](README.it.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · **Español**

</div>

<p align="center">
  <img src="Assets/screenshots/camera-raw.png" alt="La ventana Camera Raw de PhotoStudio, con el mezclador de color y las ruedas de gradación de color">
</p>

<p align="center">
  <a href="#funciones">Funciones</a> ·
  <a href="#instalación">Instalación</a> ·
  <a href="#es-seguro-verifica-la-descarga">¿Es seguro?</a> ·
  <a href="#privacidad">Privacidad</a> ·
  <a href="#configurar-la-ia">Configurar la IA</a> ·
  <a href="#para-desarrolladores">Para desarrolladores</a>
</p>

## Funciones

### 🗂️ Preselección

- Abre una carpeta entera o solo algunas fotos, y elige qué formatos ver (por ejemplo, solo los RAW).
- Estrellas y etiquetas de color, con filtros para ver solo las fotos que te interesan.
- Las ráfagas se agrupan automáticamente para que encuentres rápido la mejor toma.
- Comparación lado a lado con zoom sincronizado.
- Datos de disparo (cámara, objetivo, ISO, velocidad, diafragma) e histograma con aviso de recorte.
- Tu trabajo se guarda: vuelve a abrir la carpeta y continúa donde lo dejaste.

### 🎞️ Revelado RAW

- Abre más de 25 formatos RAW (CR2, CR3, NEF, ARW, DNG, RAF, ORF, RW2 y otros), además de JPEG, PNG, TIFF, WebP y HEIC.
- Balance de blancos, exposición, contraste, iluminaciones, sombras, blancos, negros, textura, claridad, eliminación de neblina, intensidad y saturación.
- Curva de tonos (RGB y por canal), mezclador de color HSL y ruedas de gradación de color.
- Enfoque, reducción de ruido, viñeta, grano, recorte y enderezado.
- Máscaras locales (lineales, radiales y por luminosidad), vista antes/después, preajustes y copiar/pegar ajustes.

### ☀️ Luz inteligente, fácil para todos

- **Luz inteligente**: con un clic corrige la exposición y añade máscaras de luz solo donde la foto las necesita (zonas oscuras, zonas claras, cielo, sujeto).
- **Máscaras de luz fáciles**: un panel simplificado para principiantes, con recetas listas y sin controles técnicos.

### ✨ Edición con IA (opcional)

- Describe lo que quieres («más cálida, como al atardecer») o deja que la IA elija la mejor edición.
- Funciona con **Google Gemini** (clave gratuita), **Anthropic Claude** u **Ollama** (totalmente sin conexión, en tu PC).
- La IA solo elige los valores de los controles. PhotoStudio calcula los píxeles, así que cada edición sigue siendo visible y editable.

### 🖌️ Editor de fotos y trabajo por lotes

- Capas, selecciones, pincel, ajustes (niveles, curvas, tono/saturación, blanco y negro…) y filtros (desenfoque, enfoque, ruido, viñeta…).
- Formato de proyecto `.psx`, que conserva las capas.
- Aplica ajustes o preajustes a muchas fotos a la vez.
- Exportación con cambio de tamaño, renombrado automático y marca de agua.

### 🌍 En tu idioma

La interfaz está en español, inglés, italiano, alemán y francés. PhotoStudio usa el idioma de Windows y puedes cambiarlo en **Vista ▸ Idioma**.

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/culling.png" alt="Ventana de preselección con la tira de fotos y el histograma"><br>
      <sub><b>Preselección</b>: recorre una sesión, puntúala y elimina las descartadas.</sub>
    </td>
    <td width="50%" valign="top">
      <img src="Assets/screenshots/masks.png" alt="Pestaña Máscaras con las recetas rápidas"><br>
      <sub><b>Máscaras de luz fáciles</b>: recetas de un clic para cielo, sombras y sujeto.</sub>
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <img src="Assets/screenshots/editor.png" alt="Ventana principal con capas, historial e histograma"><br>
      <sub><b>Editor</b>: capas, herramientas, historial e histograma.</sub>
    </td>
  </tr>
</table>

<sub>Las capturas de pantalla muestran la interfaz en inglés.</sub>

## Instalación

1. Abre la [**última versión**](../../releases/latest).
2. Descarga uno de los dos archivos:
   - **`PhotoStudio-x.y.z-win-x64-setup.exe`**: el instalador (recomendado);
   - **`PhotoStudio-x.y.z-win-x64-portable.zip`**: sin instalación, descomprímelo y ejecuta `PhotoStudio.exe`.
3. Ejecútalo. Solo necesitas Windows 10 u 11 de 64 bits: .NET ya está incluido.

El instalador no pide permisos de administrador y puedes desinstalar el programa desde **Configuración ▸ Aplicaciones**.

> [!IMPORTANT]
> **¿Aparece «Windows protegió su PC»?** Windows muestra este mensaje con los programas nuevos, que todavía se han descargado poco o no están firmados con un certificado de pago. Haz clic en **Más información** y luego en **Ejecutar de todas formas**. Si antes quieres asegurarte, verifica el archivo como se explica abajo.

## ¿Es seguro? Verifica la descarga

- **Todo es de código abierto**: puedes leer cada línea de código de este repositorio.
- **Lo compila GitHub, no un PC personal**: cada versión la compila [GitHub Actions](.github/workflows/release.yml) directamente desde el código público. El enlace al registro de compilación está en las notas de la versión.
- **Suma de comprobación**: en PowerShell, el resultado de este comando debe coincidir con la línea de `SHA256SUMS.txt` de la misma versión:

  ```powershell
  Get-FileHash .\PhotoStudio-x.y.z-win-x64-setup.exe
  ```

- **Procedencia firmada**: con la [GitHub CLI](https://cli.github.com/) puedes comprobar que el archivo lo ha generado este repositorio (sustituye `PROPIETARIO/REPOSITORIO` por la dirección de este repositorio):

  ```powershell
  gh attestation verify .\PhotoStudio-x.y.z-win-x64-setup.exe --repo PROPIETARIO/REPOSITORIO
  ```

## Privacidad

- Sin cuentas, sin publicidad y sin telemetría. PhotoStudio funciona sin conexión.
- Tus fotos nunca salen de tu PC, **salvo que uses las funciones de IA**. En ese caso PhotoStudio envía solo una vista previa pequeña (como máximo 1024 píxeles en el lado largo), los valores de los controles, el modelo de cámara con los datos de disparo y algunas estadísticas de luminosidad. No envía nombres de archivo ni la ubicación GPS, y solo al servicio que hayas elegido. Con Ollama todo se queda en tu ordenador.
- Las claves API las cifra Windows (DPAPI) y se guardan solo en `%APPDATA%\PhotoStudio`. Solo tu cuenta de Windows puede leerlas.

## Configurar la IA

Abre **Edición ▸ Ajustes de IA** y elige un servicio:

| Servicio | Coste | Qué necesitas |
|---|---|---|
| **Gemini** | Gratis (con límites diarios) | Una clave de [Google AI Studio](https://aistudio.google.com/apikey). Modelo recomendado: `gemini-flash-latest`. |
| **Claude** | De pago | Una clave de la [Claude Console](https://console.anthropic.com/). |
| **Ollama** | Gratis, sin conexión | [Ollama](https://ollama.com/) y un modelo que entienda imágenes, por ejemplo `ollama pull gemma3`. |

## Para desarrolladores

<details>
<summary><b>Compilar desde el código fuente</b></summary>

<br>

Necesitas el [SDK de .NET 10](https://dotnet.microsoft.com/download) en Windows.

```powershell
git clone <dirección del repositorio>
cd PhotoStudio
dotnet run -c Release
```

Para crear los archivos de una versión (exe único, zip, instalador y sumas de comprobación) igual que GitHub:

```powershell
.\build.ps1 -Version 1.0.0      # el instalador necesita además Inno Setup 6
```

Para publicar una nueva versión, actualiza el código y sube una etiqueta. GitHub compila y publica la versión automáticamente:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

</details>

<details>
<summary><b>Traducciones</b></summary>

<br>

Las traducciones están en [`Localization/`](Localization/): un archivo JSON por idioma, que asocia el texto italiano usado en el código con su traducción.

- Para corregir una traducción, cambia su valor.
- Para añadir un idioma, copia `en.json`, traduce los valores y añade el idioma a `Loc.Languages` en [`Core/Loc.cs`](Core/Loc.cs).
- Si falta una entrada, se muestra el texto italiano.

</details>

### Contribuir

Los informes de errores, las ideas y las pull requests son bienvenidos en [Issues](../../issues). Para informar en privado de un problema de seguridad, consulta [SECURITY.md](SECURITY.md).

## Licencia

[MIT](LICENSE) © 2026 Luca Pezzoli. Los componentes de terceros se enumeran en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
