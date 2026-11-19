; Inno Setup script for the Windows installer (built by .github/workflows/release.yml on windows-latest).
;   iscc /DAppVersion=0.9.0 /DSourceDir=..\..\build\windows /DOutDir=..\..\build\dist tools\release\FishingSim.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\windows"
#endif
#ifndef OutDir
  #define OutDir "..\..\build\dist"
#endif

[Setup]
AppId={{6C0E1A7B-5F38-4F2B-9E0D-3B7E2F1A9C44}
AppName=Fishing Sim
AppVersion={#AppVersion}
AppPublisher=FishingSim
DefaultDirName={autopf}\FishingSim
DefaultGroupName=Fishing Sim
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir={#OutDir}
OutputBaseFilename=FishingSim-{#AppVersion}-windows-setup
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\FishingSim.exe
Compression=lzma2/max
SolidCompression=yes
DiskSpanning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Fishing Sim"; Filename: "{app}\FishingSim.exe"; IconFilename: "{app}\icon.ico"
Name: "{group}\{cm:UninstallProgram,Fishing Sim}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Fishing Sim"; Filename: "{app}\FishingSim.exe"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\FishingSim.exe"; Description: "{cm:LaunchProgram,Fishing Sim}"; Flags: nowait postinstall skipifsilent
