# 开发

## 开发环境

- Windows 10/11 x64
- Visual Studio，包含 Universal Windows Platform 开发支持
- Windows SDK `10.0.26100.0`
- .NET 8 SDK
- Xbox Game Bar
- SteelSeries GG，并启用 Sonar

## 构建

```powershell
.\tools\Build-Debug.ps1
```

脚本会执行：

1. 生成本地 PNG 资源。
2. 构建 UWP 小组件和 self-contained Bridge。
3. 在需要时创建本地 `CN=SonarGameBar` 测试签名证书。
4. 签名调试 MSIX。

生成物已被 Git 忽略。

## 安装

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Install-Debug.ps1"'
```

安装需要管理员确认，因为 Windows 包部署会读取本机受信任发布者证书存储。

## 启动

按 `Win+G` 并选择 **Sonar Mixer**，也可以运行：

```powershell
Start-Process 'ms-gamebar://launch/activate/SonarGameBar.Widget_dybzwrprnmrze_App_SonarMixer'
```

## Bridge 诊断

读取当前状态：

```powershell
dotnet run --project .\SonarGameBar.Bridge -- status
```

写入操作：

```powershell
dotnet run --project .\SonarGameBar.Bridge -- set-volume game 0.5
dotnet run --project .\SonarGameBar.Bridge -- set-mute game false
dotnet run --project .\SonarGameBar.Bridge -- set-chatmix 0
```

## 卸载

```powershell
Start-Process powershell.exe -Verb RunAs -ArgumentList '-ExecutionPolicy Bypass -File ".\tools\Uninstall-Debug.ps1"'
```

## 公开发布检查

```powershell
.\tools\Test-PublicRelease.ps1
dotnet run --project .\SonarGameBar.Core.Tests\SonarGameBar.Core.Tests.csproj
git status --short
git diff --cached
```

不要提交生成的包、符号文件、证书、安装 transcript、日志或本地工具元数据。
