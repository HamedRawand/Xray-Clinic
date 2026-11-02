; Server installer: copies the API (self-contained, no .NET needed) and runs scripts\Install-Server.ps1, which sets up
; PostgreSQL, the database, secrets, the HTTPS certificate, the Windows service and the firewall rule.
; Re-running a newer installer over an existing one is the upgrade path.
#define AppName "Diagnostic Clinic Manager Server"
#define AppVersion "1.1.2"
#define ServiceName "DiagnosticClinicManagerServer"

[Setup]
AppId={{5D2C9E6B-8A41-4F7E-B3D0-6C1A9F2E7D48}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Diagnostic Clinic Manager
DefaultDirName={autopf}\DiagnosticClinicManager\Server
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=installer_output
OutputBaseFilename=DiagnosticClinicManager_Server_Setup_v{#AppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\NasimXray.Client\Assets\AppIcon.ico
UninstallDisplayIcon={app}\DiagnosticClinicManager.Server.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\NasimXray.Api\publish\server-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Install-Server.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "Uninstall-Server.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\DEPLOYMENT.md"; DestDir: "{app}"; Flags: ignoreversion
; The PDF installation and configuration guide (built by tools\ManualBuilder), kept with the server for the administrator.
Source: "..\NasimXray.Client\Manual\*.pdf"; DestDir: "{app}\docs"; Flags: ignoreversion skipifsourcedoesntexist
; Optional: drop a postgresql-17*-windows-x64.exe into deploy\prereq before compiling to make the installer work offline.
Source: "prereq\*"; DestDir: "{app}\prereq"; Flags: ignoreversion skipifsourcedoesntexist

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Uninstall-Server.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[Code]
// The running service holds its own exe open, so stop it before files are replaced (the setup script starts it again).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(4000);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
        ExpandConstant('-NoProfile -ExecutionPolicy Bypass -File "{app}\scripts\Install-Server.ps1" -InstallDir "{app}" -Pause'),
        '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      MsgBox('Server setup did not finish. Read the messages in the setup window, fix the problem, then run' + #13#10 +
             '"' + ExpandConstant('{app}\scripts\Install-Server.ps1') + '"' + #13#10 +
             'again from an administrator PowerShell.', mbError, MB_OK);
  end;
end;
