; BoxArt Studio — Inno Setup script
; Builds BoxArtStudio_Setup_v1.3.exe from the Release win-x64 build output.
; Paths are relative to this script so it builds from any checkout.

#define AppName      "BoxArt Studio"
#define AppVersion   "1.3"
#define AppExe       "BoxArtStudio.exe"
#define BuildDir     "GameIconsStudio\bin\Release\net8.0-windows10.0.19041.0\win-x64"
#define AppIco       "GameIconsStudio\Assets\app.ico"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=BoxArt Studio
DefaultDirName={localappdata}\Programs\BoxArt Studio
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=installer
OutputBaseFilename=BoxArtStudio_Setup_v1.3
SetupIconFile={#AppIco}
UninstallDisplayIcon={app}\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "crash_log.txt,*.pdb,*\publish\*"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\Assets\app.ico"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
