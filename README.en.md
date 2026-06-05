# SonarGameBar

English | [简体中文](README.md)

SonarGameBar is an Xbox Game Bar widget for adjusting SteelSeries Sonar volume, including the mix between game audio and voice chat.

> [!IMPORTANT]
> SteelSeries does not provide a public Sonar control API. This project uses undocumented local GG/Sonar endpoints that may change after a GG update.

## Features

- Master, game, and chat volume controls
- Independent mute controls
- Game/Chat ChatMix control
- Automatic discovery of Sonar's dynamic loopback address
- Automatic reconnection after GG or Sonar restarts
- On-demand Bridge process that exits when the widget closes
- Game Bar pinning support

## Usage

After installing the debug package:

1. Start SteelSeries GG and enable Sonar.
2. Press `Win+G`.
3. Open the widget menu and select **Sonar Mixer**.

You can also activate the widget directly:

```powershell
Start-Process 'ms-gamebar://launch/activate/SonarGameBar.Widget_dybzwrprnmrze_App_SonarMixer'
```

## Build

Requirements:

- Windows 10/11 x64
- Visual Studio with UWP development tools
- Windows SDK `10.0.26100.0`
- .NET 8 SDK
- Xbox Game Bar
- SteelSeries GG with Sonar

Build and sign a local debug package:

```powershell
.\tools\Build-Debug.ps1
```

Install it from an elevated PowerShell window:

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Install-Debug.ps1"'
```

Uninstall the debug package and remove the test certificate:

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Uninstall-Debug.ps1"'
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for details.

## Project structure

```text
SonarGameBar.Core/     Sonar address discovery, state parsing, and control API
SonarGameBar.Bridge/   On-demand desktop process connecting the Widget to Sonar
SonarGameBar.Widget/   Xbox Game Bar UWP/XAML widget
tools/                 Build, install, uninstall, and public-release audit scripts
docs/                  Architecture and development documentation
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for architecture details.
