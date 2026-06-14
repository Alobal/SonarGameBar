# SonarGameBar

[简体中文](README.md) | English

SonarGameBar is an Xbox Game Bar widget for adjusting SteelSeries Sonar mixing and volume directly from `Win+G`, including the balance between game and voice audio.

![Features](README/image.png)

## Usage

After installing the debug package:

1. Start SteelSeries GG and make sure Sonar is enabled.
2. Press `Win+G` to open Xbox Game Bar.
3. Open the Widget Menu and select **Sonar Mixer**.

You can also open the widget manually:

```powershell
Start-Process 'ms-gamebar://launch/activate/SonarGameBar.Widget_dybzwrprnmrze_App_SonarMixer'
```

## Build

Development requirements:

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

Build an optimized Release package signed with the test certificate:

```powershell
.\tools\Build-Release.ps1
```

Every push and pull request runs the release audit, tests, and complete Widget build in
GitHub Actions. After a successful run, download `SonarGameBar-debug-x64-<commit-sha>`
and `SonarGameBar-release-x64-<commit-sha>` from the run's artifact list. Both
artifacts contain the signed MSIX, dependency packages, and test certificate.

Installing the debug package requires one administrator UAC prompt to trust the local test publisher certificate:

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Install-Debug.ps1"'
```

Uninstall the debug package and remove the test certificate:

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Uninstall-Debug.ps1"'
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for detailed development instructions.

## Project Structure

```text
SonarGameBar.Core/     Sonar endpoint discovery, state parsing, and control API
SonarGameBar.Bridge/   On-demand desktop process connecting the widget to Sonar
SonarGameBar.Widget/   Xbox Game Bar UWP/XAML widget
tools/                 Build, install, uninstall, and release-audit scripts
docs/                  Architecture and development documentation
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for architecture details.
