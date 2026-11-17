<#
.SYNOPSIS
    Entfernt den Windows-Dienst Vemas.MailRelay und dessen Firewall-Regeln.

.DESCRIPTION
    Die Datenbank (Schema mailrelay) bleibt unangetastet.
    Mit -RemoveFiles werden zusätzlich Programmordner und Ereignisquelle entfernt.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$InstallPath = (Join-Path $env:ProgramFiles 'Vemas.MailRelay'),
    [switch]$RemoveFiles
)

$ErrorActionPreference = 'Stop'
$serviceName = 'Vemas.MailRelay'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Dienst $serviceName entfernt."
}

Get-NetFirewallRule -DisplayName 'Vemas.MailRelay *' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host "Firewall-Regeln entfernt."

if ($RemoveFiles) {
    if (Test-Path $InstallPath) {
        Remove-Item $InstallPath -Recurse -Force
        Write-Host "Programmordner $InstallPath entfernt (inkl. appsettings.json)."
    }
    if ([System.Diagnostics.EventLog]::SourceExists($serviceName)) {
        [System.Diagnostics.EventLog]::DeleteEventSource($serviceName)
        Write-Host "Ereignisquelle entfernt."
    }
}
