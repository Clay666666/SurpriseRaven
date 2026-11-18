# 脚手架工具链实施计划

> 这是前一阶段工具链记录，其中 GUI 主菜单验收仅为历史结果。当前测试严禁游戏 GUI；新的 RitsuLib/headless 流程见 [ritsulib-setup.md](ritsulib-setup.md)。

日期：2026-11-09。用户已授权按本计划安装工具、修改环境脚本、部署并进行主菜单验证。

## 范围

- 保留 Sts2ModScaffold 身份；Mod 目标改为 net9.0，SDK 使用项目内 .NET 10。
- 安装项目内 Godot 4.5.1 Mono、ILSpy GUI/MCP、uv 和两个游戏控制 Mod。
- 优先复用本机已有 Python 解释器（当时为 Conda Python 3.13.12），为 STS2MCP 建立独立虚拟环境，不修改原 Conda 环境；解释器路径由本地探测或 `-PythonExe` 指定。
- 生成项目级 Codex MCP 配置并验证握手、工具枚举、反编译及游戏状态读取。
- RitsuLib、玩法与美术资源管线不在本阶段范围内。

## 实现

1. 固定工具版本和上游提交，修复安装失败处理、运行时检测和路径混用。
2. 共用可重复构建部署流程，刷新引用、保留依赖、先生成产物后部署，失败回滚。
3. 移除自动删除开发 Mod、删除旧日志和全局结束 Python/uv 的行为。
4. 根据本机 v0.111.0 的实际 API 适配辅助 Mod，保存可重复应用的补丁。
5. Codex 使用两个 stdio 服务：ILSpy MCP 与 STS2MCP；MenuControl 保持 HTTP 接口。
6. 更新中文说明和项目规范，明确当前用户授权优先于模板的禁改工程约束。

## 验收

- 本地工具版本正确，主 Mod 与辅助 Mod 构建成功。
- 安装及日常构建可重复执行；错误路径、构建失败与 PCK 失败不会产生不完整部署。
- MCP 完成 initialize、tools/list 及一次实际反编译。
- 启动游戏到主菜单，确认 Mod 初始化和控制接口；读取菜单状态并进行角色选择页往返。
- 不开始新局或放弃现有进度；只管理本流程启动的游戏进程，保留旧日志。
- 最终记录实际版本、验证结果及当前聊天是否需要重新加载 MCP。

## 实施结果

2026-11-09 已完成安装、兼容修复、部署与主菜单验收。RitsuLib 开发引用和美术资源工程仍未接入。

| 项目 | 实际结果 |
| --- | --- |
| 主 Mod | `net9.0`，保留 `Sts2ModScaffold` 身份 |
| 本地 SDK / 运行时 | SDK 10.0.403、Core8 8.0.33、WindowsDesktop10 10.0.14 |
| 编辑及辅助工具 | Godot 4.5.1 Mono、ILSpy 10.0.0.8282-preview2、uv 0.13.7 |
| Python | 复用现有 Miniconda Python 3.13.12，独立 `.venv`，31 个依赖包；未下载另一个解释器 |
| 游戏部署 | `mods/Sts2ModScaffold`、`mods/STS2_MCP`、`mods/STS2MenuControl` |
| MCP | ILSpy 8 个工具、STS2MCP 64 个工具，真实握手与工具枚举通过 |
| Codex | 新进程在原 read-only 沙箱、never 审批策略下成功调用 ILSpy `decompile_method`；未放宽权限 |
| 游戏验收 | 三个 Mod 初始化，读取主菜单，进入角色选择页并返回；未开始或放弃游戏 |

最终整条 `install.ps1 -DeepVerify Y` 流程通过，重复运行复用已有工具和下载。完整记录见：

- [最终安装日志](../../artifacts/final-install.log)
- [环境与游戏报告](../../artifacts/verification/20261109-031239-990/environment-report.json)
- [MCP 报告](../../artifacts/verification/20261109-031239-990/mcp-report.json)
- [真实 Codex 调用结果](../../artifacts/codex-integration-annotated.txt)

构建部署隔离测试覆盖错误运行时、编译/PCK 失败、依赖与中文清单保留、重复构建、引用刷新、备份及中途失败回滚；Python 配置/菜单就绪/异常诊断的 7 项测试通过。卸载隔离测试覆盖预览不修改、精确目标、运行中拒绝及重复执行，未对真实游戏执行卸载。

最后补齐了重复部署保留 `STS2_MCP.conf` 的处理：只保留该已知配置文件，清理旧 DLL；隔离测试覆盖 BuildOnly、重复部署和回滚时设置不变。真实重复部署也通过了配置文件 SHA256 不变检查，记录见 `artifacts/final-redeploy.log`。

安装中修正了 Godot Windows GUI 程序输出捕获、PowerShell 5.1 的 Python 参数引号，以及裸 PCK 元数据清单被游戏重复扫描的问题。两个控制 Mod 的 v0.111.0 兼容改动和 ILSpy 只读工具元数据修复均以可重放补丁保存。

当前聊天若尚未载入新 MCP，需要重新加载项目或在新聊天中使用。STS2MCP 的状态接口在部分房间带自动交互副作用，保持原审批语义；本阶段未进行战斗或联机测试。游戏退出日志仍有引擎资源释放诊断，验收结论限于上述加载和菜单功能。
