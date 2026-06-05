# SonarGameBar

[English](README.en.md) | 简体中文

SonarGameBar 是一个 Xbox Game Bar 小组件，方便在Win+G里调节赛睿sonar的音量, 比如游戏和人声的混合比例。

> [!IMPORTANT]
> SteelSeries 没有提供公开的 Sonar 控制 API。本项目调用 GG 在本机暴露的非公开接口，GG 更新后可能需要适配。

## 功能

- 调节总音量、游戏音量和聊天音量
- 分别静音总音量、游戏与聊天通道
- 调节 Game/Chat ChatMix
- 自动发现 Sonar 的动态本机地址
- Sonar 或 GG 重启后自动重连
- Widget 打开时按需启动 Bridge，关闭后 Bridge 自动退出
- 支持固定在游戏画面上

## 使用

安装调试包后：

1. 启动 SteelSeries GG，并确认 Sonar 已启用。
2. 按 `Win+G` 打开 Xbox Game Bar。
3. 打开“小组件菜单”，选择 **Sonar Mixer**。

也可以直接激活小组件：

```powershell
Start-Process 'ms-gamebar://launch/activate/SonarGameBar.Widget_dybzwrprnmrze_App_SonarMixer'
```

## 构建

开发环境：

- Windows 10/11 x64
- Visual Studio，包含 UWP 开发工具
- Windows SDK `10.0.26100.0`
- .NET 8 SDK
- Xbox Game Bar
- SteelSeries GG 与 Sonar

构建并签名本地调试包：

```powershell
.\tools\Build-Debug.ps1
```

安装调试包需要一次管理员 UAC，用于信任本地测试发布者证书：

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Install-Debug.ps1"'
```

卸载调试包并移除测试证书：

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Uninstall-Debug.ps1"'
```

详细开发说明见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 项目结构

```text
SonarGameBar.Core/     Sonar 地址发现、状态解析和控制 API
SonarGameBar.Bridge/   按需运行的桌面进程，连接 Widget 与 Sonar
SonarGameBar.Widget/   Xbox Game Bar UWP/XAML 小组件
tools/                 构建、安装、卸载和发布前审计脚本
docs/                  架构与开发文档
```

架构详情见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
