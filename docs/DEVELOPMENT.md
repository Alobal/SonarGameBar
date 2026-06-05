# Development

## Requirements

- Windows 10/11 x64
- Visual Studio with Universal Windows Platform development support
- Windows SDK `10.0.26100.0`
- .NET 8 SDK
- Xbox Game Bar
- SteelSeries GG with Sonar enabled

## Build

```powershell
.\tools\Build-Debug.ps1
```

The script:

1. Generates local PNG assets.
2. Builds the UWP widget and self-contained Bridge.
3. Creates a local `CN=SonarGameBar` test-signing certificate when necessary.
4. Signs the debug MSIX.

Generated output is ignored by Git.

## Install

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Install-Debug.ps1"'
```

Administrator approval is required because Windows package deployment reads the machine trusted-publisher store.

## Launch

Press `Win+G` and select **Sonar Mixer**, or run:

```powershell
Start-Process 'ms-gamebar://launch/activate/SonarGameBar.Widget_dybzwrprnmrze_App_SonarMixer'
```

## Bridge diagnostics

Read current state:

```powershell
dotnet run --project .\SonarGameBar.Bridge -- status
```

Write operations:

```powershell
dotnet run --project .\SonarGameBar.Bridge -- set-volume game 0.5
dotnet run --project .\SonarGameBar.Bridge -- set-mute game false
dotnet run --project .\SonarGameBar.Bridge -- set-chatmix 0
```

## Uninstall

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Uninstall-Debug.ps1"'
```

## Public-release checks

```powershell
.\tools\Test-PublicRelease.ps1
git status --short
git diff --cached
```

Do not commit generated packages, symbols, certificates, installation transcripts, logs, or local tool metadata.
