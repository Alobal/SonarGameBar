param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$widgetProject = Join-Path $root 'SonarGameBar.Widget\SonarGameBar.Widget.csproj'
$artifactDir = Join-Path $root 'artifacts'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw 'Visual Studio Installer vswhere.exe was not found.'
    }

    $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $installationPath) {
        throw 'Visual Studio with MSBuild was not found.'
    }

    $candidate = Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $candidate)) {
        throw "MSBuild was not found at '$candidate'."
    }

    return $candidate
}

function Find-SignTool {
    $windowsKitsBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem $windowsKitsBin -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+){1,3}$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1

    if (-not $candidate) {
        throw 'Windows SDK SignTool was not found.'
    }

    return $candidate
}

$msbuild = Find-MSBuild
$signTool = Find-SignTool

& (Join-Path $PSScriptRoot 'GenerateAssets.ps1')
& $msbuild $widgetProject /restore /t:Build /p:Configuration=$Configuration /p:Platform=x64 /m /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "$Configuration Widget build failed."
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=SonarGameBar' -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject 'CN=SonarGameBar' `
        -FriendlyName 'SonarGameBar Local Test Signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
        -NotAfter (Get-Date).AddYears(2)
}

$certificatePath = Join-Path $artifactDir 'SonarGameBar.Test.cer'
Export-Certificate -Cert $cert -FilePath $certificatePath -Force | Out-Null

$packageDirectoryPattern = if ($Configuration -eq 'Debug') {
    '*_x64_Debug_Test'
} else {
    '*_x64_Test'
}

$packageDirectory = Get-ChildItem (Join-Path $root 'SonarGameBar.Widget\AppPackages') -Directory |
    Where-Object { $_.Name -like $packageDirectoryPattern } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $packageDirectory) {
    throw "The $Configuration Widget package directory was not produced."
}

$package = Get-ChildItem $packageDirectory.FullName -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "The $Configuration Widget MSIX was not produced."
}

& $signTool sign /fd SHA256 /sha1 $cert.Thumbprint $package.FullName
if ($LASTEXITCODE -ne 0) {
    throw "$Configuration MSIX signing failed."
}

if ($OutputDirectory) {
    $resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    if (-not $resolvedOutput.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to write package output outside the workspace: $resolvedOutput"
    }

    if (Test-Path $resolvedOutput) {
        Remove-Item $resolvedOutput -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
    Copy-Item (Join-Path $packageDirectory.FullName '*') $resolvedOutput -Recurse
    Copy-Item $certificatePath $resolvedOutput
    Write-Host "Copied package artifact to: $resolvedOutput"
}

Write-Host "Built and signed $Configuration package: $($package.FullName)"
