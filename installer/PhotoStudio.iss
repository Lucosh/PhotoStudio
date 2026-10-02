; PhotoStudio installer (Inno Setup 6).
; Built by ..\build.ps1, which passes the version and the folders below with /D.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef FileVersion
  #define FileVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef AppUrl
  #define AppUrl ""
#endif

#define AppName "PhotoStudio"
#define AppExe "PhotoStudio.exe"
#define AppPublisher "Luca Pezzoli"

[Setup]
AppId={{36A643E6-988B-4486-9DBF-BB6030C27361}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
#if AppUrl != ""
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
#endif
AppCopyright=Copyright (C) 2026 {#AppPublisher}
VersionInfoVersion={#FileVersion}
VersionInfoProductVersion={#FileVersion}
VersionInfoDescription={#AppName} Setup
; Installs for the current user without administrator rights; the first page lets the user choose "all users" instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
SetupIconFile=..\Assets\PhotoStudio.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
ShowLanguageDialog=auto
Compression=lzma2/max
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-{#AppVersion}-win-x64-setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.Integration=Windows integration:
it.Integration=Integrazione con Windows:
de.Integration=Windows-Integration:
fr.Integration=Intégration à Windows :
es.Integration=Integración con Windows:

en.AssocPsx=Open PhotoStudio projects (.psx) with PhotoStudio
it.AssocPsx=Apri i progetti PhotoStudio (.psx) con PhotoStudio
de.AssocPsx=PhotoStudio-Projekte (.psx) mit PhotoStudio öffnen
fr.AssocPsx=Ouvrir les projets PhotoStudio (.psx) avec PhotoStudio
es.AssocPsx=Abrir los proyectos de PhotoStudio (.psx) con PhotoStudio

en.FolderMenu=Add "Cull with PhotoStudio" to the folder context menu
it.FolderMenu=Aggiungi "Preselezione con PhotoStudio" al menu contestuale delle cartelle
de.FolderMenu=„Mit PhotoStudio aussortieren“ zum Kontextmenü von Ordnern hinzufügen
fr.FolderMenu=Ajouter « Trier avec PhotoStudio » au menu contextuel des dossiers
es.FolderMenu=Añadir "Preseleccionar con PhotoStudio" al menú contextual de las carpetas

en.FolderVerb=Cull with PhotoStudio
it.FolderVerb=Preselezione con PhotoStudio
de.FolderVerb=Mit PhotoStudio aussortieren
fr.FolderVerb=Trier avec PhotoStudio
es.FolderVerb=Preseleccionar con PhotoStudio

en.PsxType=PhotoStudio project
it.PsxType=Progetto PhotoStudio
de.PsxType=PhotoStudio-Projekt
fr.PsxType=Projet PhotoStudio
es.PsxType=Proyecto de PhotoStudio

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "psxassoc"; Description: "{cm:AssocPsx}"; GroupDescription: "{cm:Integration}"
Name: "foldermenu"; Description: "{cm:FolderMenu}"; GroupDescription: "{cm:Integration}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Open with" list: PhotoStudio is offered for images and RAW files, without taking over any default.
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".psx"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".jpeg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".png"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".tif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".tiff"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".bmp"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".gif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".webp"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".heic"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".dng"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cr2"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".cr3"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".nef"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".nrw"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".arw"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".raf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".orf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".rw2"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pef"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".srw"; ValueData: ""

; PhotoStudio's own project format.
Root: HKA; Subkey: "Software\Classes\.psx"; ValueType: string; ValueName: ""; ValueData: "PhotoStudio.Project"; Flags: uninsdeletevalue; Tasks: psxassoc
Root: HKA; Subkey: "Software\Classes\.psx\OpenWithProgids"; ValueType: string; ValueName: "PhotoStudio.Project"; ValueData: ""; Flags: uninsdeletevalue; Tasks: psxassoc
Root: HKA; Subkey: "Software\Classes\PhotoStudio.Project"; ValueType: string; ValueName: ""; ValueData: "{cm:PsxType}"; Flags: uninsdeletekey; Tasks: psxassoc
Root: HKA; Subkey: "Software\Classes\PhotoStudio.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: psxassoc
Root: HKA; Subkey: "Software\Classes\PhotoStudio.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: psxassoc

; Folder context menu: opens the folder in the culling window (Preselezione).
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoStudio.Cull"; ValueType: string; ValueName: ""; ValueData: "{cm:FolderVerb}"; Flags: uninsdeletekey; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoStudio.Cull"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe},0"; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoStudio.Cull\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: foldermenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; After an automatic update (PhotoStudio runs this setup with /SILENT /UPDATE=1) the program starts again by itself.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
