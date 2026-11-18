# 玻璃眼珠奖励退出与概率显示调整

日期 2026-11-16。基线 `c02c521`，任务分支 `codex/glass-eye-exit-and-hide-odds`。

## 实现

- ILSpy 已核对两版本机游戏程序集。`GlassEye.AfterObtained()` 等待 `RewardsCmd.OfferCustom` 的临时奖励页结束。两版 `NRewardsScreen.TryEnableProceedButton()` 都调用前进检查，渡鸦领取事务持有的 Busy 会误拦离开按钮。
- 在临时奖励页按钮检查中忽略渡鸦 Busy 条件，保留原版检查结果。使用同步线程局部作用域，Harmony Finalizer 负责异常清理。继续保护终结奖励、跨章节及地图移动。
- 移除左上角概率条的节点、百分比和提示，以及备用文字界面的概率行。保留轮次、烛火、消息、离开控件与原有概率规则。保持公开接口、存档格式及版本不变。

## 验收

- 隔离 headless 夹具覆盖临时奖励可离开、终结奖励仍受保护、原版否决、禁止跳过、嵌套及异常清理。验证挂起的遗物领取在原版奖励结束后结算一次。
- HUD 节点检查覆盖 0/3/6 灯和结束状态，确认轮次、烛火、消息和退出仍在，概率标签和悬浮提示消失。更新手动验收清单。
- Release 锁定依赖构建并校验 PCK，对同一产物执行 0.111.0／0.113.1 静态 API 和隔离 headless 验证，保留初始化、驱动和回归记录。
- 再次检查游戏已关闭，备份主 Mod，部署同一份验收产物并核对哈希，失败恢复。最终 GUI 由用户手动验收。

## 验证与部署结果

实现提交为 `c8f4ac5`，夹具隔离修订为 `72cf2fb`。Release 锁定依赖构建 0 警告、0 错误，PCK 校验通过（41 项）。同一主 DLL 在两版静态校验中各检查 163 个类型和 240 个成员，均无缺失。Python 离线回归 28 项通过，1 项符号链接权限用例跳过。[构建与验证记录](../../artifacts/verification/glass-eye-exit-and-hide-odds/)

| 游戏 | 隔离 headless 结果 |
| --- | --- |
| 0.111.0 | 实际驱动 `headless`，退出码 0，2308 个玩法断言、342 个节点检查通过。[环境报告](../../artifacts/verification/20261116-123622-208/environment-report.json) |
| 0.113.1 | 实际驱动 `headless`，退出码 0，2310 个玩法断言、342 个节点检查通过。[环境报告](../../artifacts/verification/20261116-123637-003/environment-report.json) |

原生节点夹具调用了打过补丁的按钮检查，覆盖临时、终结、禁止跳过、原版否决、嵌套及异常路径。夹具用受控信号模拟奖励结束，确认领取结算一次并释放 Busy。复核发现游戏会扫描 Mod 程序集中的具体模型，最终夹具只在隔离 headless 中创建测试模型。真实对局的按钮显示和点击返回由用户手动验收。隔离报告保留引擎 Dummy shader、任务和退出资源回收诊断。

最终版本已部署到配置的 0.113.1 游戏目录。每次替换前均确认游戏已关闭并核对备份。最终 DLL、PCK 和清单与两版验证使用的产物逐文件哈希一致。DLL SHA256 为 `0c1a19b8aa3d923122b0a92adba4605382bbf17190598835030dd52e15c48dfa`。[最终部署核对](../../artifacts/verification/glass-eye-exit-and-hide-odds/deployment-result.json) · [本次修复前的主 Mod 备份](../../artifacts/backups/verified-glass-eye-exit-20261116-123520-997/Sts2ModScaffold/)

**已部署，待用户 GUI 验收。** [手动验收清单](raven-lamp-gui-checklist.md)
