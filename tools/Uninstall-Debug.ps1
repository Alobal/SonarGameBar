#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

Get-AppxPackage -Name SonarGameBar.Widget | Remove-AppxPackage

Get-ChildItem Cert:\LocalMachine\TrustedPeople |
    Where-Object { $_.Subject -eq 'CN=SonarGameBar' } |
    Remove-Item

Get-ChildItem Cert:\CurrentUser\TrustedPeople, Cert:\CurrentUser\Root, Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=SonarGameBar' } |
    Remove-Item

Remove-Item (Join-Path $env:TEMP 'SonarGameBar.Bridge.log') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'SonarGameBar') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Removed SonarGameBar Widget and its local test certificate.'
