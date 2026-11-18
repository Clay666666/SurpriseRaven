# AGENTS.md — 本地 Mod 开发工作流

开发前阅读 `rules.md`。RitsuLib 接入见 `docs/plans/ritsulib-setup.md`；日常命令见 `README.md`。

## 开发测试强制规则：禁用游戏 GUI

- 本规则适用于整个 Mod 开发过程中的所有测试，包括环境检查、功能验证、调试和回归测试：**严禁启动游戏 GUI**。
- 禁止可见、隐藏、最小化或移到屏幕外的游戏窗口；隐藏窗口不能替代真正的 headless。不得通过 Steam URI、直接启动游戏或其他备用路径绕过此要求。
- 默认执行编译、静态检查和离线测试。需要游戏运行时验证时，只能使用 `tools/verify-environment.ps1 -HeadlessGame` 的隔离流程，并确认实际显示驱动为 `headless`。
- Headless 启动失败、超时或能力不足时，保留诊断并明确报告未验证；**不得回退 GUI 测试**。
- 最终游戏 GUI 验收由用户手动完成；代理不得启动、操作或自动化 GUI。Headless 通过后标记为“待用户 GUI 验收”，用户确认前不得宣称最终验收通过。

## 当前范围与工程

- 主 Mod 为「惊喜渡鸦 / SurpriseRaven」，技术 ID 为 `SurpriseRaven`，目标 `net9.0`；项目内 .NET 10 SDK 负责编译。
- 主 Mod 以游戏 0.111.0 和编译专用 RitsuLib Compat.0.111.0 0.6.14 为基线，共用 DLL 支持 0.111.0/0.113.1；运行时统一使用完整工坊 0.6.14。已接入渡鸦玩法及 2D 前端。
- 玩法代码按模块写在 `src/Hooks/`；入口负责 Harmony 注册与日志。
- 工具链维护可以修改工程目标、安装/构建/验证脚本、客户端配置和文档。不要机械套用原模板“只能改 Hooks”的限制。
- 未明确要求正式初始化时，不自动重命名 Mod。身份字段必须与部署产物一致。

## 实现与验证

1. 先在 `docs/plans/` 写清实现及验收标准，再编写代码。
2. 涉及游戏 API 时，先通过 ILSpy MCP 检查本机 `references/<API版本>/sts2.dll`；两版分别核对。根目录引用仅供旧工具使用，上游 Hook 表仅作导航，不能猜测签名。
3. 按功能拆分代码，记录必要日志；不要修改游戏自带程序集。
4. 遵守上方强制测试规则，执行离线检查或隔离 headless 验证。
5. 保留错误输出和旧日志，检查本次启动后的 Mod 初始化记录与接口结果。构建成功不等于游戏兼容验证成功。

## 命令与配置

- 首次安装：`install.bat -GameDir "游戏目录"`；默认不启动游戏。`-NoDeploy` 只准备工具与构建产物。
- 日常构建部署：`install-mod.bat "游戏目录"`，默认只处理主 Mod。
- 显式包含辅助 Mod：`tools/build-mod.ps1 -IncludeControlMods`；`-BuildOnly` 不部署。
- `-GameDir`/`GAME_DIR` 指定部署目标；`-BaselineGameDir`/`BASELINE_GAME_DIR` 指定主 Mod 的 111 编译基线。不得用新版引用覆盖该基线。
- 工具验收：`tools/verify-environment.ps1`；隔离无界面游戏验收添加 `-HeadlessGame`。旧 `-Game` 已禁用。
- RitsuLib 离线检查：`tools/verify-ritsulib.ps1`；主项目依赖锁文件随源码提交，普通构建使用 locked restore。
- Codex 配置：`tools/configure-codex.ps1` 生成项目 `.codex/config.toml`，不覆盖用户全局配置。
- 项目工具位于 `tools/`，版本由锁文件和 `global.json` 固定。不得在安装或 MCP 启动时自动升级。
- Python 优先复用已有解释器，为 STS2MCP 创建独立 `.venv`，不得向用户 Conda 基础环境安装包。

## MCP 与控制接口

- `ilspy`：本地 stdio MCP，用于类型、方法和程序集分析。先读 `tools/list`，遵循真实 schema。
- 参数使用 camelCase，例如 `assemblyPath`、`typeName`、`methodName`；不要沿用原模板的 snake_case 示例。
- `sts2mcp`：Python stdio MCP，连接游戏内 `localhost:15526` HTTP 服务。
- `get_game_state` 使用 `format="json"` 获取结构化状态；游戏关闭时连接错误应说明，不能伪报状态。
- STS2MenuControl 是 `localhost:8081` 的普通 HTTP API，不能把它直接注册成 MCP URL。
- 菜单检查：`GET /health`、`GET /api/v1/menu`；操作：`POST /api/v1/menu`，并检查 JSON 的错误字段。
- 自动验收不得调用菜单往返、开局、继续/放弃游戏、切换或删除档案。状态接口也可能带自动交互副作用，不能当成纯读取测试工具。

## 文件与进程边界

- 游戏运行时不覆盖 Mod DLL；先让用户结束现有游戏，不能全局强杀。
- 自动验收只管理自身启动并记录的游戏 PID；不得按名称结束所有 Python、uv 或游戏进程。
- 不删除旧游戏日志，不在安装末尾移除已部署的开发 Mod。
- Headless 使用单独 APPDATA/LOCALAPPDATA/TEMP/TMP、禁用 Steam，只复制 RitsuLib 和本次主 Mod。禁止链接真实 Mod、设置或存档，原版硬链接资源不得写入。
- 部署前先完成构建和 PCK 校验，备份精确目标，失败恢复；不触碰其他 Mod 或存档。
- 游戏版本变化时刷新引用并重新验证 API。第三方工具兼容改动以主仓库 `patches/` 下补丁保存，不能仅修改被忽略的工具克隆。
- 本机绝对路径、下载、构建产物与生成的 Codex 配置不提交；不要提交游戏 DLL。

## Git 版本管理

- 具体流程见 `docs/version-control.md`；开始修改前检查分支和工作区，保留已有无关工作。
- 新功能或实验从已提交基线建立 `codex/` 任务分支；完成一个可独立验证的阶段后，检查暂存区并提交，记录验证结果。
- 正式保留的版本使用附注标签；不移动既有标签，不强制推送。上游模板 remote 不作为本 Mod 的默认发布位置。
- 回退已提交功能优先 `git revert`，不要全仓硬重置或清除未跟踪文件。Git 回退与游戏部署分别核验。
