<#
.SYNOPSIS
    Removes the Diagnostic Clinic Manager server service and its firewall rule.

.DESCRIPTION
    Deliberately leaves the PostgreSQL database, the backups, the certificate and the settings file in place
    (%ProgramData%\DiagnosticClinicManager) so removing the program can never destroy patient data.
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'DiagnosticClinicManagerServer',
    [int]$HttpsPort = 5443
)

$ErrorActionPreference = 'Continue'

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) } catch { }
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed."
}

Get-NetFirewallRule -DisplayName "Diagnostic Clinic Manager Server (HTTPS $HttpsPort)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Your data (database, backups, certificate, settings) was NOT touched: C:\ProgramData\DiagnosticClinicManager'
exit 0
