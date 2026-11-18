# Mod 0.3.3 接入 RitsuLib 0.6.14

日期为 2026-11-16，基线为 `2579c6e`，任务分支为 `codex/ritsulib-0.6.14`。

## 实施约定

- 主 Mod 版本为 0.3.3，编译专用包精确锁定 `STS2.RitsuLib.Compat.0.111.0 [0.6.14]`，正式 restore 生成依赖锁文件。日常构建使用 locked restore。
- 固定 111 编译基线，目标 net9.0，共用 DLL/PCK 支持游戏 0.111.0 和 0.113.1。完整工坊运行包使用 0.6.14。
- 离线预检要求 0.6.14，headless 验证实际加载兼容分支程序集版本 0.6.14.0。保留动态探针、模块集合、SHA256、ZIP CRC、兼容分支及隔离路径验证。
- 规划阶段已用 ILSpy 核对两版兼容分支的存档、RNG、注册与日志接口。现有 raven_lamp 和 raven_native_ancients 数据结构继续使用。
- 同步当前开发规范、README 和环境示例，向 0.3.3 变更记录追加依赖升级结果。

## 验收与交付

- 更新现有 Python 和 PowerShell 夹具，覆盖 0.6.14、拒绝旧 0.6.13、预检与实际加载不匹配、包完整性及隔离要求。
- Release locked build 和 PCK 校验通过后，对同一产物执行双版本静态 API 与顺序隔离 headless。实际驱动必须为 headless，代理禁止启动或操作游戏 GUI。
- 保留初始化日志、功能/节点结果与产物哈希。游戏关闭后备份精确主 Mod 目录，部署验证产物并核对全部文件。
- 分阶段提交，保留现有 v0.3.3 标签，成功完成后新增附注 v0.3.3-ritsulib-0.6.14，注明“待用户 GUI 验收”。

## 实施与验证结果

主 Mod 0.3.3 已完成 RitsuLib 0.6.14 接入并部署。计划提交为 `1d51fc9`，依赖与验证器提交为 `4f81efe`。NuGet 正式 restore 生成了 0.6.14 的包内容哈希，随后构建使用 locked restore。探针保留实际加载程序集的动态版本读取。

| 检查 | 结果与证据 |
| --- | --- |
| Release 构建及 PCK | 111 编译基线，0 警告、0 错误，41 个 PCK 条目通过校验。[构建日志](../../artifacts/ritsulib-0614-build.log)、[构建记录](../../artifacts/verification/ritsulib-0614-20261116/build.json) |
| 双版本 API | 同一主 DLL 对工坊 0.6.14 的两条兼容分支均检查 162 类型、237 成员，0 缺失。[111](../../artifacts/ritsulib-0614-api-0.111.0.json)、[113](../../artifacts/ritsulib-0614-api-0.113.0.json) |
| Python 离线测试 | 29 项，28 通过，1 项符号链接权限用例跳过。[日志](../../artifacts/ritsulib-0614-python-tests.log) |
| headless 隔离夹具 | 正例和旧版本／预检与实际加载不匹配的负例通过。[夹具目录](../../artifacts/tests/headless-6bfc3b8a5fd240f588806340683e2c57) |
| 构建部署夹具 | locked restore、双版本基线、资源校验、备份回滚测试通过。[日志](../../artifacts/ritsulib-0614-build-fixtures.log) |
| 游戏 0.111.0 | 驱动 headless，实际 RitsuLib 0.6.14.0，退出码 0，2308 功能断言、320 节点检查。[环境报告](../../artifacts/verification/20261116-121429-897/environment-report.json)、[功能报告](../../artifacts/verification/ritsulib-0614-20261116/raven-0.111.0.json) |
| 游戏 0.113.1 | 同一产物，驱动 headless，实际 RitsuLib 0.6.14.0，退出码 0，2310 功能断言、320 节点检查。[环境报告](../../artifacts/verification/20261116-121504-584/environment-report.json)、[功能报告](../../artifacts/verification/ritsulib-0614-20261116/raven-0.113.1.json) |

两版都使用完整工坊 0.6.14，各自创建新隔离镜像。日志保留原有 Dummy shader、退出资源回收诊断及 113 的 WorkerThreadPool Invalid Task ID。验收覆盖自动功能、存档状态及节点检查，真实显示、鼠标手感和完整对局交互交由用户验收。

## 精确部署

- 部署前核对两版报告、镜像内的完整主 Mod 文件、当前游戏引用、PCK 校验记录及工坊包哈希，再验证目标游戏处于关闭状态。
- 已备份并部署同一份已验证的 DLL／清单／PCK，全部文件哈希一致。[部署结果](../../artifacts/verification/ritsulib-0614-20261116/deployment-result.json)、[部署日志](../../artifacts/verification/ritsulib-0614-20261116/deployment.log)、[备份](../../artifacts/backups/verified-ritsulib-0614-20261116-121632-806/Sts2ModScaffold)
- DLL SHA256 为 `e9f510586722825a7c540f068411897430c1dc4851d4a7c055b811cfec626127`。
- JSON SHA256 为 `c483b343d1d3f2c32a17d25f75b3fb29bdc0c2c362c4a53bdc5903476ea528a2`。
- PCK SHA256 为 `1988b582b18898521b21f73ebf545bef426c827a46836f561be8f4ef647f425f`。

附注标签 `v0.3.3-ritsulib-0.6.14` 对应本次升级记录，原 `v0.3.3` 指向的 0.6.13 验证提交保留。最终状态为 **已部署，待用户 GUI 验收**。
