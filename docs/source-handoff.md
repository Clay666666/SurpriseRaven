# 惊喜渡鸦 0.4.3 源码构建

要在游戏中使用惊喜渡鸦，请直接解压玩家包。以下步骤用于从公开仓库的同一标签构建 DLL、清单与 PCK。[玩家安装说明](../README.md) · [English player instructions](../README_en.md)

## 准备输入

- Windows x64、Git，以及已有的 64 位 Python 3.11 或更高版本。首次安装需要网络下载锁定版本的 .NET SDK、Godot 和辅助工具。安装器为 STS2MCP 创建独立的 Python 环境。
- 一份本机 Windows Steam 游戏 0.111.0 作为编译基线。部署或验证的目标可以是 0.111.0 或 0.113.1。编译目标框架为 `net9.0`，NuGet 依赖锁定到 `STS2.RitsuLib.Compat.0.111.0` 0.6.14。运行两个版本时都使用完整工坊版 RitsuLib 0.6.14。[RitsuLib 工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
- 构建完整 PCK 需要 6 个纹理、22 个音频文件。具有相应使用权的开发者按规格中每项 `source` 的路径准备本地文件，格式、尺寸、帧格须匹配。公开发行的成品媒体只随玩家包中的 PCK 提供。源码仓库保留素材规格、事件映射和打包工具。[素材规格](../assets/raven/raven-assets.json) · [媒体声明](../THIRD_PARTY_NOTICES.md)

## 编译与打包

在 PowerShell 中克隆发布标签。构建脚本读取当前 Git 提交号并将其写入 DLL。

```powershell
git clone --branch v0.4.3 --single-branch https://github.com/Clay666666/SurpriseRaven.git
Set-Location SurpriseRaven
```

将媒体根目录放在项目的 `artifacts/artifact/`，或在项目根目录的本地 UTF-8 `.env` 里设置 `RAVEN_ASSETS_DIR=<素材根目录的绝对路径>`。首次安装用下列命令准备锁定工具、游戏引用及构建产物。将尖括号内的路径换成本机目录。目标为 0.111.0 时，前两项可指向同一份游戏。

```powershell
.\install.bat -GameDir "<目标游戏目录>" -BaselineGameDir "<0.111.0 游戏目录>" -RitsuLibDir "<完整工坊 RitsuLib 目录>" -NoDeploy
```

日常只编译主 Mod 时运行下列命令。先读取 `artifacts/last-build.json` 的 `payloads`，找到 `Id` 为 `SurpriseRaven` 的记录。其 `Directory` 下是同名 DLL、JSON、PCK。PCK 有 31 项，包括 28 个媒体文件、前端配置与素材清单，以及内部 Mod 清单。

```powershell
.\tools\build-mod.ps1 -BuildOnly
```

## 验证

使用同一份 DLL、JSON、PCK 分别检查两版游戏。对玩家 ZIP 做回归时，将 `$modPayload` 指向从 ZIP 解出的 `mods/SurpriseRaven`。先做离线检查，再运行仓库规定的隔离 headless 入口。

```powershell
$modPayload = "<解出的 mods/SurpriseRaven 目录>"
$ritsuRuntime = "<完整工坊 RitsuLib 目录>"
.\tools\verify-ritsulib.ps1 -RitsuLibDir $ritsuRuntime
.\tools\verify-environment.ps1 -HeadlessGame -GameDir "<0.111.0 游戏目录>" -RitsuLibDir $ritsuRuntime -PayloadDir $modPayload
.\tools\verify-environment.ps1 -HeadlessGame -GameDir "<0.113.1 游戏目录>" -RitsuLibDir $ritsuRuntime -PayloadDir $modPayload
```

核对每次运行生成的 `artifacts/verification/` 报告，确认实际显示驱动为 `headless`，并检查 Mod 初始化与玩法验证结果。最终游戏画面和输入体验由用户手工验收。[0.4.3 发布验收标准](plans/surprise-raven-release.md)
