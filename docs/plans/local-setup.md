# 本地脚手架源码准备与开发说明

> 本文保留首次只克隆源码时的检查快照。后续工具链实施见 [toolchain-setup.md](toolchain-setup.md)，当前安装和验证命令以项目 README 为准。

记录日期：2026-11-09（Asia/Shanghai）。本文针对下述提交和本机环境；游戏、RitsuLib 或脚手架更新后需要重新核对兼容性。

## 1. 当前状态与源码来源

**源码已准备，完整开发环境尚未安装或验证。** 本阶段只克隆上游仓库并新增本文，没有执行初始化、重命名、依赖安装、编译、游戏部署或游戏测试，也没有配置 Codex MCP。

| 项目 | 当前值 |
| --- | --- |
| 本地目录 | 项目克隆目录（本机路径不入库） |
| 上游仓库 | [L4ntern0/SlaytheSpire2-ModScaffold](https://github.com/L4ntern0/SlaytheSpire2-ModScaffold) |
| Git remote | `origin` → `https://github.com/L4ntern0/SlaytheSpire2-ModScaffold.git` |
| 分支 | `master`，跟踪 `origin/master` |
| 实际提交 | [`4b6ba11c03f48b67314931d653638f3502ace843`](https://github.com/L4ntern0/SlaytheSpire2-ModScaffold/commit/4b6ba11c03f48b67314931d653638f3502ace843) |
| 提交说明 | `Merge pull request #1 from lyzhang0113/master` |
| 历史 | 完整克隆，未使用浅克隆 |
| 模板身份 | `Sts2ModScaffold`，正式 Mod 名称待开发初始化时确定 |
| 本次新增 | `docs/plans/local-setup.md`；原始跟踪文件保持不变 |

用户已明确本阶段仅准备源码和说明，因此仓库规范中的首次重命名、构建和自动游戏测试流程留到正式开发阶段。

## 2. 三部分分别负责什么

| 部分 | 职责 | 当前项目中的状态 |
| --- | --- | --- |
| 脚手架 | 提供 C# 工程、Harmony 入口、构建部署脚本，以及反编译和游戏控制工具的安装流程 | 源码已克隆；没有具体玩法实现 |
| RitsuLib | 为内容注册、生命周期、存档、设置、本地化和视觉接入提供公共能力 | 游戏已有加载记录；本工程尚未引用或声明依赖 |
| Godot 资源工程 | 编辑场景、导入贴图、组织动画，并将所需资源导出到 PCK | 需要后续建立；现有 PCK 脚本只封装清单 |

脚手架可以作为开发起点：游戏加载 Mod DLL 后调用入口，入口执行 `Harmony.PatchAll()`，让项目中的补丁接入游戏逻辑。当前 `src/Hooks/` 只有占位文件，尚未包含玩法补丁。

RitsuLib 与脚手架承担不同层次的工作。RitsuLib 提供可供 Mod 调用的公共功能；脚手架负责把代码组织、编译和部署起来。新场景、贴图和动画仍需制作资源，并建立资源导入、打包和游戏内加载流程。参考 [RitsuLib 官方仓库](https://github.com/BAKAOLC/STS2-RitsuLib)、[入门文档](https://sts2-ritsulib.ritsukage.com/guide/getting-started)、[Godot 场景制作](https://sts2-ritsulib.ritsukage.com/guide/godot-scene-authoring)及[视觉与动画](https://sts2-ritsulib.ritsukage.com/guide/creature-visuals-and-animation)。

## 3. 目录和脚本导览

| 路径 | 用途及注意事项 |
| --- | --- |
| `src/ModEntry.cs` | 游戏初始化入口、Harmony 补丁注册和日志输出；默认写入 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt` |
| `src/Hooks/` | 模板建议按功能拆分补丁的目录；当前为空壳 |
| `src/Sts2ModScaffold.csproj` | 默认目标 `net10.0`，引用 `references/` 下的游戏、Harmony 和 Godot DLL |
| `src/com.vibecoding.sts2mod.json` | Mod 清单；文件名虽带旧命名前缀，但内容中的 `id`、`name`、`pck_name` 均为 `Sts2ModScaffold` |
| `tools/pck_builder/build_pck.gd` | 使用 Godot 的 PCKPacker 打包 `res://mod_manifest.json` |
| `tools/*.ps1` | 启动、关闭游戏，等待进程、读取日志，以及重命名模板的辅助脚本 |
| `install.bat` / `install.ps1` | 首次环境安装入口，包含下载工具、构建辅助 Mod、部署和验证等操作 |
| `install-mod.bat` | 日常构建并部署 `src/` 中的 Mod；当前实现不会重建 STS2MCP 或 STS2MenuControl |
| `uninstall-mod.bat` | 移除脚手架及相关辅助 Mod 的脚本；本次未执行 |
| `AGENTS.md` / `rules.md` | 上游 AI 工作流和代码约束；后续开发前阅读，并结合实际集成需求处理模板限制 |
| `docs/plans/` | 项目计划与本次落地说明 |

`references/`、`tools/dotnet/`、`tools/godot/`、ILSpy/MCP 工具目录、`Modding-Tutorial/`、`.env` 和 `opencode.jsonc` 是安装阶段准备的本地内容，不随当前源码克隆完整提供。它们被 Git 忽略不代表已安装。

## 4. 本机环境快照

| 检查项 | 只读检查结果 | 证据 |
| --- | --- | --- |
| Git | `2.54.0.windows.1` | `git --version` |
| 系统 .NET SDK | 当前 `dotnet` 未列出已安装 SDK | `dotnet --list-sdks` 无输出 |
| 系统 .NET 运行时 | NETCore `8.0.15`、`8.0.21`；WindowsDesktop `8.0.15` | `dotnet --list-runtimes` |
| Godot 命令 | PATH 中未发现 `godot`；不据此断言其他目录没有编辑器 | `Get-Command godot` |
| 游戏目录 | 本机 Steam 游戏安装目录（通过 `-GameDir` 或 `.env` 配置） | 本地游戏文件 |
| 游戏版本 | `v0.111.0`，Steam 分支 `public-beta`，build `24724944` | 游戏 `release_info.json` 与 Steam `appmanifest_2868840.acf` |
| 游戏内置 .NET | `net9.0`，`Microsoft.NETCore.App 9.0.7` | 游戏 `data_sts2_windows_x86_64/sts2.runtimeconfig.json` |
| 游戏 GodotSharp | 文件版本 `4.5.1.0` | 游戏 `GodotSharp.dll` 版本信息 |
| RitsuLib 加载记录 | `0.6.10`，兼容分支 `0.111.0`，来源为 Steam 创意工坊 | 2026-11-08 的 `%APPDATA%\SlayTheSpire2\logs\godot.log` |

游戏内置运行时与系统 SDK 是两回事：能运行游戏，不等于已经具备编译 Mod 的工具。首次检查时上游工程中的 `GameDir` 含机器专属默认值；后续版本管理整理已移除，当前通过命令参数或本地 `.env` 提供游戏路径。

RitsuLib 的日志证明此前游戏加载过该库，不代表当前项目已接入，也不代替本项目的运行验证。开发时还需配置编译引用、Mod 清单依赖及必要的初始化；游戏侧则需有匹配版本的完整运行时文件。

## 5. 正式部署前需要处理的问题

以下来自当前提交的静态审查，本次只记录，未修复或执行验证。

### 目标框架与游戏运行时不一致

[`src/Sts2ModScaffold.csproj`](../../src/Sts2ModScaffold.csproj) 的目标为 `net10.0`，本机游戏宿主为 `.NET 9`。安装 .NET 10 SDK 不会升级游戏内置运行时。后续应将 Mod 编译目标与宿主对齐，优先评估 `net9.0`，再验证引用解析与实际游戏加载，不能仅凭编译成功宣称兼容。

### PCK 尚未包含美术资源

[`build_pck.gd`](../../tools/pck_builder/build_pck.gd) 仅调用一次 `add_file`，添加 `res://mod_manifest.json`。当前仓库没有用于场景和美术资源的完整 Godot 工程、导入流程和导出预设。后续应建立资源工程，验证场景、贴图和动画连同依赖被正确打包，并能在游戏中加载。挂载 C# 脚本的场景还需按 RitsuLib 文档处理脚本注册。

### 首次安装包含进程、日志和文件清理

[`install.ps1`](../../install.ps1) 的 `DeepVerify` 未指定时默认取 `Y`。深度验证会强制结束游戏、删除旧 `godot.log` 和 `mod_log.txt`、通过 Steam 启动游戏；其清理分支还会按进程名全局结束 `python` 和 `uv`，可能影响其他任务。

正常执行到脚本末尾时，还会删除游戏 `mods/` 中的脚手架测试 Mod。该步骤位于深度验证条件之外，即使指定 `-DeepVerify N` 也会执行。跳过深度验证不会把安装脚本变成只下载工具的命令，也不会跳过前面的安装和部署。

后续应先检查或修正这些行为，再显式指定游戏路径和验证选项，并独立验证游戏加载；只清理由本次流程创建的进程。首次安装结束后，若需要保留开发 Mod，还需另行构建部署。本次未运行这些操作。

### 重命名会产生身份字段不一致

[`rename-scaffold.ps1`](../../tools/rename-scaffold.ps1) 会将工程名及当前 `ModEntry.ModId` 中的 `Sts2ModScaffold` 替换为整理后的名称，但清单 `id` 设置为 `com.vibecoding.<小写名称>`，且没有同步 `pck_name`。因此不能把脚本输出的“成功”当作身份已一致。

正式初始化时需要统一检查工程输出 DLL 名、游戏 Mod 子目录、PCK 名、清单 `id`/`pck_name` 和 `ModEntry.ModId`。本次保留模板原有值。

### 首次安装会覆盖部署清单的依赖

`install.ps1` 生成游戏目录中的 Mod 清单时，将 `dependencies` 强制设为 `[]`；这不会直接修改源清单，但会让以后声明的 RitsuLib 依赖在该部署路径中丢失。后续接入 RitsuLib 前，应修正部署行为并核对最终产物。`install-mod.bat` 当前直接复制源 Mod 清单，两条部署路径的行为不同。

### 更新后需要刷新引用和构建产物

`install.ps1` 只在本地 `references/sts2.dll` 不存在时复制游戏引用，且会根据已有 DLL 跳过部分构建或安装。游戏更新后需刷新引用，确认所用工具与游戏 API 匹配，并验证部署的是本次构建产物。上游 Hook 列表仅作导航，具体签名以后以本机游戏程序集为准。

### Codex 接入和模板规范需要后续适配

安装脚本生成的是 OpenCode 的 `opencode.jsonc`，不会自动配置 Codex。ILSpy MCP 用于反编译，STS2MCP 用于游戏内控制；STS2MenuControl 提供主菜单 HTTP 控制接口。安装服务、配置客户端和验证工具可调用是不同步骤，本阶段均未执行。

上游规范限制修改工程文件和初始化逻辑，而目标框架调整、RitsuLib 编译引用和程序集发现可能需要修改这些位置。正式集成时，应根据已确定的开发目标同步调整工程与项目规范，明确必要的例外，不能机械沿用仅适合 Harmony 空壳的约束。本次保留全部原始规范。

## 6. 后续开发顺序（尚未执行）

1. 确定正式 Mod 身份和目标游戏版本，处理目标框架、重命名及安装脚本的问题。
2. 准备 SDK、匹配的 Godot 工具和游戏 DLL 引用，建立可重复的构建部署流程，验证空 Mod 加载。
3. 接入与目标游戏 API 匹配的 RitsuLib 编译依赖、运行时依赖和初始化；核对现有创意工坊版本，避免重复安装造成混淆。
4. 建立 Godot 资源工程，使用一个最小场景、贴图和动画验证导入、PCK 打包及游戏内显示。
5. 分模块实现玩法，每项修改先验证实际 API/Hook 签名，再构建、部署并检查游戏行为和日志。
6. 按后续需要接入 Codex 的反编译与游戏控制工具，完成工具可用性验证。

环境完成后的日常循环是：编写计划与代码/资源 → 编译 DLL 和打包资源 → 部署 → 启动游戏验证 → 查看日志。当前克隆只完成了这个流程的源码起点。

## 7. 本阶段验收

- Git 来源为上述仓库，当前分支和 `origin/master` 指向记录的提交。
- 使用 Git 对象检查核对克隆完整性，确认不是浅克隆。
- 相对 `origin/master` 的原始跟踪文件无差异；唯一新增文件为本文。
- 原始 JSON 清单和工程 XML 可解析，保留原始 Mod 名称与配置。
- 不以编译或游戏运行作为本阶段验收项：没有安装依赖、复制游戏 DLL、创建 MCP 配置或改动游戏内容。

可在项目根目录复核：

```powershell
git remote -v
git branch --show-current
git rev-parse HEAD origin/master
git rev-parse --is-shallow-repository
git fsck --full
git diff --exit-code origin/master --
git status --short --untracked-files=all
```

初次交付时，最后一条应仅显示 `?? docs/plans/local-setup.md`；本文尚未提交，未向上游推送。
