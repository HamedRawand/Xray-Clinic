#define AppName "Diagnostic Clinic Manager"
#define AppVersion "1.1.0"
#define AppPublisher "Diagnostic Clinic Manager"
#define AppExeName "DiagnosticClinicManager.exe"

[Setup]
; AppId is deliberately unchanged from the first release so existing installs upgrade in place.
AppId={{B7E4F1A2-3C9D-4E5F-8A1B-2D3E4F5A6B7C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\DiagnosticClinicManager
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=DiagnosticClinicManager_Setup_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[InstallDelete]
; Releases before the product rename shipped the client as NasimXray.Client.exe — remove those leftovers on upgrade.
Type: files; Name: "{app}\NasimXray.Client.*"

[Files]
Source: "publish\NasimXray.Client-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; No "skipifsilent" flag — OPD Clinic's memory documents that flag causing the app to never
; relaunch after a silent auto-update, since Inno Setup skips skipifsilent [Run] entries
; specifically in /SILENT mode, which the update flow uses when relaunching after install.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall
