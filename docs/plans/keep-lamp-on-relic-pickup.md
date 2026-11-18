# 保留拾取遗物时的路灯面板

基线 `503ca94`，Mod 0.3.3。0.3.0 前端曾用 `HasUponPickupEffect` 预先关闭路灯，便于原版选择界面接管。草莓、磨刀石、古钱币均有该标志。草莓和磨刀石的领取日志随后出现路灯重新打开。日志中的古钱币是原版所得并被用作投入物。

- 2D 和备用文字界面在现有面板上结算。保留原版获取效果、异步领取保护及玻璃眼珠临时奖励的退出修复。
- 让原版界面栈处理选牌、临时奖励等覆盖及恢复。两版 ILSpy 核对确认 `NOverlayStack.Push/Remove` 隐藏并恢复同一个底层面板。
- 用隔离夹具验证三个原版拾取效果、领取次数、原生界面栈恢复、失效及异常、三选一原件待领。真实对局手动验收。
- 完成双版本构建、静态检查和隔离 headless，再备份部署精确产物。保留诊断和现有标签，待用户 GUI 验收。

## 验证与部署

实现提交 `f550f85`。领取保留面板实例，原版栈处理覆盖和恢复。隔离夹具执行草莓、磨刀石和古钱币的原版 `AfterObtained` 并核对收益，也通过真实原版栈验证模拟选择页覆盖、恢复、Busy 期间刷新、重复操作保护和异常清理。已有事务夹具继续覆盖候选失效与三选一原件状态。

| 游戏 | 验证结果 |
| --- | --- |
| 0.111.0 | 静态 API 通过，headless 退出码 0，2308 项功能断言、357 项节点检查通过。[环境报告](../../artifacts/verification/20261117-015130-551/environment-report.json) |
| 0.113.1 | 同一产物静态 API 通过，headless 退出码 0，2310 项功能断言、357 项节点检查通过。[环境报告](../../artifacts/verification/20261117-015147-871/environment-report.json) |

Release 为 0 警告、0 错误，PCK 41 项通过校验。保留首轮测试的焦点诊断，最终复跑已消除该错误。引擎原有 headless shader、退出资源回收及 113 的 Invalid Task ID 诊断仍留在日志。

已备份现有主 Mod，并在游戏关闭时部署两版验证用的同一份 0.3.3 产物。逐文件哈希一致，DLL SHA256 为 `5e462773325c11385d763d409af15a01ca57e3a32dfc018ce82333e92a4b6676`。**已部署，待用户 GUI 验收。** [部署记录](../../artifacts/verification/keep-lamp-on-relic-pickup/deployment-result.json) · [备份](../../artifacts/backups/verified-keep-lamp-pickup-20261117-015239-328/Sts2ModScaffold) · [手动验收清单](raven-lamp-gui-checklist.md)
