#define AppName "Nasim Diagnostic X-ray Clinic"
#define AppVersion "1.0.0"
#define AppPublisher "Nasim Diagnostic X-ray Clinic"
#define AppExeName "NasimXray.Client.exe"

[Setup]
AppId={{B7E4F1A2-3C9D-4E5F-8A1B-2D3E4F5A6B7C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\NasimXrayClinic
DefaultGroupName=Nasim Diagnostic X-ray Clinic
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=NasimXrayClinic_Setup_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "publish\NasimXray.Client-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Nasim Diagnostic X-ray Clinic"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\Nasim Diagnostic X-ray Clinic"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; No "skipifsilent" flag — OPD Clinic's memory documents that flag causing the app to never
; relaunch after a silent auto-update, since Inno Setup skips skipifsilent [Run] entries
; specifically in /SILENT mode, which the update flow uses when relaunching after install.
Filename: "{app}\{#AppExeName}"; Description: "Launch Nasim Diagnostic X-ray Clinic"; Flags: nowait postinstall
