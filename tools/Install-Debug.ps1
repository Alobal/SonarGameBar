#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$certificatePath = Join-Path $root 'artifacts\SonarGameBar.Test.cer'

if (-not (Test-Path $certificatePath)) {
    throw 'Run tools\Build-Debug.ps1 before installing.'
}

Import-Certificate `
    -FilePath $certificatePath `
    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

$packageDirectory = Get-ChildItem (Join-Path $root 'SonarGameBar.Widget\AppPackages') -Directory |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$package = Get-ChildItem $packageDirectory.FullName -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$dependencies = Get-ChildItem (Join-Path $packageDirectory.FullName 'Dependencies\x64\*.appx') |
    Select-Object -ExpandProperty FullName

Get-Process SonarGameBar.Widget, SonarGameBar.Bridge -ErrorAction SilentlyContinue |
    Stop-Process -Force
Get-AppxPackage -Name SonarGameBar.Widget | Remove-AppxPackage

Add-AppxPackage `
    -Path $package.FullName `
    -DependencyPath $dependencies `
    -ForceApplicationShutdown

Get-AppxPackage -Name SonarGameBar.Widget |
    Select-Object Name, PackageFullName, Status, InstallLocation
