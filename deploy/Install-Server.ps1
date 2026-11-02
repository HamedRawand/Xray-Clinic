<#
.SYNOPSIS
    Sets up (or upgrades) the Diagnostic Clinic Manager server on this PC: PostgreSQL, database, secrets,
    HTTPS certificate, Windows Service and firewall rule.

.DESCRIPTION
    Run elevated (the server installer does this for you). Safe to run again:
      * first run  - installs PostgreSQL if missing, creates the database + login, generates every secret,
                     issues the HTTPS certificate, registers and starts the service, opens the firewall port.
      * later runs - (upgrade) keeps the existing database, secrets and certificate, takes a safety backup,
                     refreshes the service registration and starts the new version.

    Everything private is written to %ProgramData%\DiagnosticClinicManager (administrators + the service only).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Split-Path -Parent $PSScriptRoot),
    [string]$DataDir = (Join-Path $env:ProgramData 'DiagnosticClinicManager'),
    [string]$ServiceName = 'DiagnosticClinicManagerServer',
    [string]$ServiceExeName = 'DiagnosticClinicManager.Server.exe',
    [int]$HttpsPort = 5443,
    [int]$HttpPort = 5080,
    [string]$DatabaseName = 'clinic',
    [string]$DatabaseUser = 'clinic_app',
    [string]$TimeZoneId = 'Asia/Kabul',

    # PostgreSQL
    [string]$PostgresDirectory = 'C:\Program Files\PostgreSQL\17',
    [int]$PostgresPort = 5432,
    [string]$PostgresInstallerPath,
    [string]$PostgresInstallerUrl = 'https://get.enterprisedb.com/postgresql/postgresql-17.11-1-windows-x64.exe',
    [System.Security.SecureString]$PostgresSuperPassword,
    [string]$ExistingConnectionString,   # skip creating a database; use this one (advanced / testing)

    # Network
    [string[]]$ExtraHostNames = @(),
    [string]$FirewallRemoteAddress = 'LocalSubnet',
    [switch]$NoFirewallRule,
    [switch]$ReissueCertificate,

    [switch]$SkipPreUpgradeBackup,
    [switch]$Pause
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is painfully slow with the progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ProductName       = 'Diagnostic Clinic Manager'
$FirewallRuleName  = "$ProductName Server (HTTPS $HttpsPort)"
$SettingsPath      = Join-Path $DataDir 'server.settings.json'
$SecretsPath       = Join-Path $DataDir 'INSTALL-SECRETS.txt'
$CertDir           = Join-Path $DataDir 'certs'
$BackupDir         = Join-Path $DataDir 'Backups'
$PfxPath           = Join-Path $CertDir 'server.pfx'
$ServiceExe        = Join-Path $InstallDir $ServiceExeName
$DefaultSettings   = (Join-Path (Join-Path $env:ProgramData 'DiagnosticClinicManager') 'server.settings.json')

# ----------------------------------------------------------------------------------------------- helpers
function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text)   { Write-Host "    $Text" -ForegroundColor Green }
function Write-Note([string]$Text) { Write-Host "    $Text" -ForegroundColor Yellow }

function New-RandomText([int]$Length) {
    # Letters and digits only: safe inside a connection string, a command line and JSON without any escaping.
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] ($Length * 2)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    $chars = for ($i = 0; $i -lt $Length; $i++) { $alphabet[$bytes[$i] % $alphabet.Length] }
    -join $chars
}

function New-RandomBase64([int]$ByteCount) {
    $bytes = New-Object byte[] $ByteCount
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    [Convert]::ToBase64String($bytes)
}

function ConvertFrom-Secure([System.Security.SecureString]$Secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

# SIDs instead of names so the script works on any Windows display language.
function Protect-Folder([string]$Path) {
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict access to $Path (icacls exit code $LASTEXITCODE)." }
}

function Invoke-Psql([string]$Password, [string]$User, [string]$Database, [string]$Sql) {
    $psql = Join-Path $script:PostgresBin 'psql.exe'
    $ErrorActionPreference = 'Continue'   # psql may print notices on stderr; judge success by its exit code instead
    $env:PGPASSWORD = $Password
    try {
        $out = & $psql -h localhost -p $PostgresPort -U $User -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql 2>&1
        if ($LASTEXITCODE -ne 0) { throw "psql failed: $out" }
        ($out | Out-String).Trim()
    }
    finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
}

function Find-PostgresService {
    Get-Service | Where-Object { $_.Name -like 'postgresql*' } | Sort-Object Name -Descending | Select-Object -First 1
}

function Get-LanAddresses {
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        Select-Object -ExpandProperty IPAddress
}

# ------------------------------------------------------------------------------------------- pre-flight
$summaryAdminPassword = $null
try {
    Write-Host ''
    Write-Host "$ProductName - server setup" -ForegroundColor White
    Write-Host ('-' * 60)

    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'This script must run as Administrator (right-click -> Run as administrator).'
    }
    if ([Environment]::OSVersion.Version.Major -lt 10) { throw 'The server needs Windows 10 / Windows Server 2016 or newer (.NET 8).' }
    if (-not [Environment]::Is64BitOperatingSystem)    { throw 'The server needs a 64-bit version of Windows.' }
    if (-not (Test-Path $ServiceExe))                  { throw "Server program not found: $ServiceExe" }

    $isUpgrade = Test-Path $SettingsPath
    if ($isUpgrade) { Write-Note "Existing installation found ($SettingsPath) - upgrading, keeping your database and secrets." }

    # Stop a running copy so files and the certificate can be replaced safely.
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Status -ne 'Stopped') {
        Write-Step 'Stopping the running server'
        Stop-Service -Name $ServiceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }

    # ----------------------------------------------------------------------------------------- data folder
    Write-Step 'Preparing the data folder'
    foreach ($dir in @($DataDir, $CertDir, $BackupDir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Protect-Folder $DataDir
    Write-Ok $DataDir

    # ----------------------------------------------------------------------------------------- PostgreSQL
    Write-Step 'Checking PostgreSQL'
    $pgService = Find-PostgresService
    $installedPgNow = $false
    $generatedSuperPassword = $null

    if (-not $pgService -and -not $isUpgrade) {
        $installer = $PostgresInstallerPath
        if (-not $installer) {
            $bundled = Get-ChildItem -Path (Join-Path $InstallDir 'prereq') -Filter 'postgresql-17*-windows-x64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($bundled) { $installer = $bundled.FullName }
        }
        if (-not $installer) {
            $installer = Join-Path $env:TEMP (Split-Path $PostgresInstallerUrl -Leaf)
            if (-not (Test-Path $installer)) {
                Write-Note "Downloading PostgreSQL 17 (about 370 MB) from $PostgresInstallerUrl - this can take a while..."
                Start-BitsTransfer -Source $PostgresInstallerUrl -Destination $installer
            }
        }
        if (-not (Test-Path $installer)) { throw "PostgreSQL installer not found: $installer" }

        $signature = Get-AuthenticodeSignature $installer
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*EnterpriseDB*') {
            throw "The PostgreSQL installer is not validly signed by EnterpriseDB ($($signature.Status)). Refusing to run it."
        }

        $generatedSuperPassword = New-RandomText 24
        Write-Note 'Installing PostgreSQL silently (a few minutes)...'
        $pgArgs = @(
            '--mode', 'unattended', '--unattendedmodeui', 'none',
            '--superpassword', $generatedSuperPassword,
            '--serverport', $PostgresPort,
            '--servicename', 'postgresql-x64-17',
            '--prefix', "`"$PostgresDirectory`"",
            '--datadir', "`"$PostgresDirectory\data`"",
            '--enable-components', 'server,commandlinetools',
            '--disable-components', 'pgAdmin,stackbuilder',
            '--install_runtimes', '0'
        )
        $proc = Start-Process -FilePath $installer -ArgumentList $pgArgs -Wait -PassThru
        if ($proc.ExitCode -ne 0) { throw "PostgreSQL setup failed (exit code $($proc.ExitCode))." }
        $installedPgNow = $true
        $pgService = Find-PostgresService
        if (-not $pgService) { throw 'PostgreSQL was installed but its Windows service was not found.' }
    }
    if (-not $pgService) { throw 'PostgreSQL is not installed on this PC, and this is an upgrade. Install PostgreSQL 17 and run setup again.' }

    if ($pgService.Status -ne 'Running') { Start-Service $pgService.Name; $pgService.WaitForStatus('Running', [TimeSpan]::FromSeconds(60)) }

    # Locate the bin folder from the service's own command line (works for any install location / version).
    $pgImage = (Get-CimInstance Win32_Service -Filter "Name='$($pgService.Name)'").PathName
    $pgBin = if ($pgImage -match '"?([^"]*?)\\pg_ctl\.exe') { $Matches[1] } else { Join-Path $PostgresDirectory 'bin' }
    $script:PostgresBin = $pgBin
    if (-not (Test-Path (Join-Path $pgBin 'psql.exe'))) { throw "psql.exe not found in $pgBin" }
    Write-Ok "PostgreSQL service '$($pgService.Name)' is running ($pgBin)"

    # ----------------------------------------------------------------------------------- settings / secrets
    if ($isUpgrade) {
        $settings = Get-Content $SettingsPath -Raw | ConvertFrom-Json
        $connection = [string]$settings.ConnectionStrings.Default
    }
    elseif ($ExistingConnectionString) {
        # Advanced / test use: point the server at a database that already exists instead of creating one.
        Write-Note 'Using the supplied connection string; no database or login is created.'
        $connection = $ExistingConnectionString
        $backupPassword = New-RandomText 32
        $summaryAdminPassword = (New-RandomText 14) + 'a1#'
    }
    else {
        Write-Step 'Creating the database'
        if ($generatedSuperPassword) { $superPlain = $generatedSuperPassword }
        elseif ($PostgresSuperPassword) { $superPlain = ConvertFrom-Secure $PostgresSuperPassword }
        else { $superPlain = ConvertFrom-Secure (Read-Host "Password of the PostgreSQL 'postgres' user" -AsSecureString) }

        # Fail early with a clear message if the password is wrong.
        try { Invoke-Psql $superPlain 'postgres' 'postgres' 'SELECT 1' | Out-Null }
        catch { throw "Could not sign in to PostgreSQL as 'postgres' (wrong password?). $_" }

        $dbPassword = New-RandomText 32
        $roleExists = Invoke-Psql $superPlain 'postgres' 'postgres' "SELECT 1 FROM pg_roles WHERE rolname = '$DatabaseUser'"
        if ($roleExists -eq '1') { Invoke-Psql $superPlain 'postgres' 'postgres' "ALTER ROLE $DatabaseUser WITH LOGIN PASSWORD '$dbPassword'" | Out-Null }
        else                     { Invoke-Psql $superPlain 'postgres' 'postgres' "CREATE ROLE $DatabaseUser WITH LOGIN PASSWORD '$dbPassword'" | Out-Null }

        $dbExists = Invoke-Psql $superPlain 'postgres' 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$DatabaseName'"
        if ($dbExists -ne '1') {
            Invoke-Psql $superPlain 'postgres' 'postgres' "CREATE DATABASE $DatabaseName OWNER $DatabaseUser ENCODING 'UTF8' TEMPLATE template0" | Out-Null
            Write-Ok "Database '$DatabaseName' created, owned by login '$DatabaseUser'"
        }
        else {
            Write-Note "Database '$DatabaseName' already exists - reusing it (its tables are kept and migrated)."
            Invoke-Psql $superPlain 'postgres' 'postgres' "ALTER DATABASE $DatabaseName OWNER TO $DatabaseUser" | Out-Null
        }
        $connection = "Host=localhost;Port=$PostgresPort;Database=$DatabaseName;Username=$DatabaseUser;Password=$dbPassword"

        # ---------------------------------------------------------------------------------------- certificate
        $backupPassword = New-RandomText 32
        $summaryAdminPassword = (New-RandomText 14) + 'a1#'
    }

    # ---------------------------------------------------------------------------------------- certificate
    Write-Step 'HTTPS certificate'
    $certPassword = $null
    $needCert = $ReissueCertificate -or -not (Test-Path $PfxPath)
    if ($isUpgrade -and -not $needCert) { $certPassword = [string]$settings.Https.PfxPassword }

    if ($needCert) {
        $hostName = [Net.Dns]::GetHostName()
        $dnsNames = @($hostName, 'localhost') + $ExtraHostNames
        try { $fqdn = [Net.Dns]::GetHostEntry($hostName).HostName; if ($fqdn -and $dnsNames -notcontains $fqdn) { $dnsNames += $fqdn } } catch { }
        $ipNames = @('127.0.0.1') + @(Get-LanAddresses)
        $sanText = '2.5.29.17={text}' + ((@($dnsNames | ForEach-Object { "DNS=$_" }) + @($ipNames | ForEach-Object { "IPAddress=$_" })) -join '&')

        $certPassword = New-RandomText 32
        $cert = New-SelfSignedCertificate -Subject "CN=$hostName" -FriendlyName "$ProductName server" `
            -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable `
            -NotAfter (Get-Date).AddYears(5) -CertStoreLocation 'Cert:\LocalMachine\My' -TextExtension @($sanText)
        try {
            Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password (ConvertTo-SecureString $certPassword -AsPlainText -Force) | Out-Null
        }
        finally { Remove-Item -Path ("Cert:\LocalMachine\My\" + $cert.Thumbprint) -Force -ErrorAction SilentlyContinue }
        Write-Ok "New certificate for: $($dnsNames -join ', ') / $($ipNames -join ', ')"
    }
    else { Write-Ok 'Keeping the existing certificate (use -ReissueCertificate to replace it).' }

    $pfx = New-Object Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $certPassword)
    $thumbprint = $pfx.Thumbprint
    $certExpires = $pfx.NotAfter
    $pfx.Dispose()
    Write-Ok "Thumbprint: $thumbprint  (expires $($certExpires.ToString('yyyy-MM-dd')))"

    # ----------------------------------------------------------------------------------------- settings file
    if (-not $isUpgrade) {
        $newSettings = [ordered]@{
            ConnectionStrings = [ordered]@{ Default = $connection }
            Jwt     = [ordered]@{ Secret = (New-RandomBase64 64) }
            Backup  = [ordered]@{ Folder = $BackupDir; EncryptionPassword = $backupPassword; PostgresBinDirectory = $pgBin }
            Clinic  = [ordered]@{ TimeZoneId = $TimeZoneId }
            Http    = [ordered]@{ Port = $HttpPort }
            Https   = [ordered]@{ Enabled = $true; Port = $HttpsPort; PfxPath = $PfxPath; PfxPassword = $certPassword }
            Seed    = [ordered]@{ InitialAdminPassword = $summaryAdminPassword }
        }
        Write-Utf8NoBom $SettingsPath ($newSettings | ConvertTo-Json -Depth 6)
        Write-Ok "Settings written to $SettingsPath"
    }
    elseif ($needCert) {
        # Upgrade with a new certificate: only the certificate password changes.
        $settings.Https.PfxPassword = $certPassword
        $settings.Https.Port = $HttpsPort
        Write-Utf8NoBom $SettingsPath ($settings | ConvertTo-Json -Depth 6)
    }

    # ------------------------------------------------------------------------------ safety backup (upgrade)
    if ($isUpgrade -and -not $SkipPreUpgradeBackup) {
        Write-Step 'Safety backup before upgrading'
        $dumpFile = Join-Path $BackupDir ("pre-upgrade-{0}.dump" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $csb = @{}
        foreach ($part in $connection.Split(';')) { $kv = $part.Split('=', 2); if ($kv.Count -eq 2) { $csb[$kv[0]] = $kv[1] } }
        $env:PGPASSWORD = $csb['Password']
        $ErrorActionPreference = 'Continue'
        try {
            & (Join-Path $pgBin 'pg_dump.exe') -h $csb['Host'] -p $csb['Port'] -U $csb['Username'] -Fc -f $dumpFile $csb['Database']
            if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit code $LASTEXITCODE). Not upgrading without a backup; fix the problem or re-run with -SkipPreUpgradeBackup." }
        }
        finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue; $ErrorActionPreference = 'Stop' }
        Write-Ok "Saved $dumpFile"
    }

    # ----------------------------------------------------------------------------------------- the service
    Write-Step 'Registering the Windows service'
    $binPath = "`"$ServiceExe`""
    $displayName = "$ProductName Server"
    $dependency = $pgService.Name
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        & sc.exe create $ServiceName binPath= $binPath start= delayed-auto obj= 'NT AUTHORITY\LocalService' DisplayName= $displayName depend= $dependency | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc create failed (exit code $LASTEXITCODE)." }
    }
    else {
        & sc.exe config $ServiceName binPath= $binPath start= delayed-auto obj= 'NT AUTHORITY\LocalService' DisplayName= $displayName depend= $dependency | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc config failed (exit code $LASTEXITCODE)." }
    }
    & sc.exe description $ServiceName "Database access, login and reporting service for $ProductName." | Out-Null
    # Restart automatically after a crash (5 s, 5 s, then 30 s); reset the failure counter after a day.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

    # A non-default settings location (test installs) is handed to the service through its environment.
    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if ($SettingsPath -ne $DefaultSettings) {
        New-ItemProperty -Path $serviceKey -Name Environment -PropertyType MultiString -Value @("DCM_SERVER_SETTINGS=$SettingsPath") -Force | Out-Null
    }
    Write-Ok "Service '$ServiceName' registered (starts automatically with Windows, after PostgreSQL)."

    # ----------------------------------------------------------------------------------------- firewall
    if (-not $NoFirewallRule) {
        Write-Step 'Firewall'
        Get-NetFirewallRule -DisplayName $FirewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -DisplayName $FirewallRuleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $HttpsPort `
            -RemoteAddress $FirewallRemoteAddress -Profile Any | Out-Null
        Write-Ok "Inbound TCP $HttpsPort allowed from: $FirewallRemoteAddress (plain HTTP port $HttpPort stays closed to other PCs)"
    }

    # ----------------------------------------------------------------------------------------- start + check
    Write-Step 'Starting the server (the database is created / upgraded on first start)'
    Write-Note 'The very first start can take a few minutes: antivirus scans the newly installed files. Please wait...'
    Start-Service -Name $ServiceName
    $healthy = $false
    $url = "https://localhost:$HttpsPort/api/system/clinic"
    # The certificate callback runs on a .NET thread that has no PowerShell runspace, so a script block cannot be
    # used for it - a small compiled helper (accepts any certificate for this localhost check and records its thumbprint).
    if (-not ('DcmLocalCertCheck' -as [type])) {
        Add-Type -TypeDefinition @'
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class DcmLocalCertCheck
{
    public static string Thumbprint;
    public static readonly RemoteCertificateValidationCallback Callback =
        (sender, certificate, chain, errors) => { Thumbprint = new X509Certificate2(certificate).Thumbprint; return true; };
}
'@
    }
    $previousCallback = [Net.ServicePointManager]::ServerCertificateValidationCallback
    [Net.ServicePointManager]::ServerCertificateValidationCallback = [DcmLocalCertCheck]::Callback
    try {
        $deadline = (Get-Date).AddSeconds(120)
        while ((Get-Date) -lt $deadline -and -not $healthy) {
            Start-Sleep -Seconds 3
            try { $null = Invoke-RestMethod -Uri $url -UseBasicParsing -TimeoutSec 10; $healthy = $true } catch { }
        }
    }
    finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback }

    if (-not $healthy) {
        $logHint = Join-Path $DataDir 'Logs'
        throw "The server did not answer on $url within 2 minutes. Check the log files in $logHint and the Windows Event Viewer (Application log)."
    }
    Write-Ok "Server answers on $url"
    if ([DcmLocalCertCheck]::Thumbprint -and [DcmLocalCertCheck]::Thumbprint -ne $thumbprint) {
        Write-Note "Warning: the server presented thumbprint $([DcmLocalCertCheck]::Thumbprint), not the expected $thumbprint."
    }
    elseif ([DcmLocalCertCheck]::Thumbprint) { Write-Ok 'The server presents the expected certificate.' }

    # The initial admin password only matters while the database has no users, which is over now - drop it.
    $written = Get-Content $SettingsPath -Raw | ConvertFrom-Json
    if ($written.PSObject.Properties.Name -contains 'Seed') {
        $written.PSObject.Properties.Remove('Seed')
        Write-Utf8NoBom $SettingsPath ($written | ConvertTo-Json -Depth 6)
    }

    # ----------------------------------------------------------------------------------------- secrets file
    $hostForClients = [Net.Dns]::GetHostName()
    $lanList = @(Get-LanAddresses)
    if (-not $isUpgrade) {
        $lines = @(
            "$ProductName - server install secrets ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))",
            "Move this file to safe storage (e.g. a printed copy in the clinic safe or a password manager) and then DELETE it from this PC.",
            '',
            "First sign-in to the app   user: admin   password: $summaryAdminPassword   (you must change it at first sign-in)",
            "Backup encryption password: $((Get-Content $SettingsPath -Raw | ConvertFrom-Json).Backup.EncryptionPassword)",
            "   WITHOUT this password the encrypted backups can never be restored. Keep a copy somewhere other than this PC.",
            "Database login: $DatabaseUser (the app's own - password is inside $SettingsPath)"
        )
        if ($installedPgNow) { $lines += "PostgreSQL 'postgres' superuser password: $generatedSuperPassword" }
        $lines += @('', "HTTPS certificate thumbprint: $thumbprint", "Server settings file: $SettingsPath")
        Write-Utf8NoBom $SecretsPath ($lines -join [Environment]::NewLine)
    }

    Write-Host ''
    Write-Host ('=' * 60) -ForegroundColor Green
    Write-Host ' Server is running.' -ForegroundColor Green
    Write-Host ('=' * 60) -ForegroundColor Green
    Write-Host " Enter this address in each PC's Server Settings:"
    Write-Host "     https://${hostForClients}:$HttpsPort/" -ForegroundColor White
    foreach ($ip in $lanList) { Write-Host "     https://${ip}:$HttpsPort/   (same server, by IP address)" }
    Write-Host " Then click 'Trust This Certificate' and check the fingerprint matches:"
    Write-Host "     $thumbprint" -ForegroundColor White
    if (-not $isUpgrade) {
        Write-Host ''
        Write-Host " First sign-in:  admin  /  $summaryAdminPassword   (you will be asked to change it)" -ForegroundColor White
        Write-Host " Saved, together with the backup password, in: $SecretsPath" -ForegroundColor Yellow
        Write-Host ' Copy that file somewhere safe, then delete it from this PC.' -ForegroundColor Yellow
    }
    Write-Host ''
    $exitCode = 0
}
catch {
    Write-Host ''
    Write-Host "SETUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}

if ($Pause) { Read-Host 'Press Enter to close' | Out-Null }
exit $exitCode
