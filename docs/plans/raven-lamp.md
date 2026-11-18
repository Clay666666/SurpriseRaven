# 渡鸦路灯：实现与验收

当前置换概率见[新版权重表](raven-excel-odds.md)。下方保留初版方案与验收记录。

日期：2026-11-09；用户授权实施。保留 Sts2ModScaffold/net9.0/RitsuLib 0.6.10。所有新增界面使用代码生成的文字、按钮和六灯方块；正式图片、动画、场景美术不在本阶段。

## 规则

- 单机正常获得 Uncommon/Rare/Ancient/Shop，每件给予当前房间一次独立机会；涅奥直接赠送的Ancient不发机会，其派生奖励按原规则判断。原版获得链完成后依次参与/放弃；离开房间清除未处理机会。读档不补发；路灯及其派生奖励不递归发机会。
- 每次初始均匀亮0–5灯；每轮结束50%概率增加一灯，封顶6，满灯仍可继续。每轮任选自有遗物，排除初始遗物及其升级品、Event及显式注册的任务遗物。升级品按模型身份禁投，其他来源获得及读档恢复后同样生效。
- 档位依次为普通→罕见→稀有→先古。None 按普通计算，商店遗物按罕见计算，奖励池不产出商店遗物。普通投入删除降档权重，按表中其余四项权重之和重新计算概率。三选一保留原件，并提供同档至多三个不同且未持有的合法候选。
- Ancient投入不会被吞：吞噬/降档映射为Rare，升档/平换映射为原件不变，三选一为保留原件+Ancient三选一。新Ancient仅来自Rare升档或Ancient三选一。降成Rare后不再享受保护。
- 普通吞噬只移除本次投入并结束当前机会。退出保留所得，放弃未领取额外奖励。原件保留不重新Obtain；历史拾取效果沿用原版不倒扣。

|灯|吞噬|升档|平换|降档|三选一|
|---|---:|---:|---:|---:|---:|
|0|5|15|50|25|5|
|1|8|18|45|23|6|
|2|11|21|41|20|7|
|3|15|24|36|17|8|
|4|20|27|31|13|9|
|5|25|29|26|10|10|
|6|30|30|23|5|12|

## 接入

- 只包装RelicCmd.Obtain非泛型成功完成Task；来源抑制覆盖整个派生奖励链。主线程、非战斗、原版奖励结束才开放UI。
- 原版Overlay栈+原生Godot控件，默认焦点为退出/放弃；结算前移出Overlay，让原版拾取选择接管，结束后恢复。结算禁止重复提交与跨房间推进。
- 独立Mod RNG与RitsuLib玩家局内存储；保存完整SerializableRelic候选属性，跟随原版存档，不强制SaveRun(null)。
- 奖池按解锁、角色、已持有和资格过滤；Ancient额外做初始化、章节/卡组/初始遗物/宠物限制。无目标则原件不变；候选不足三件显示实际数量。
- 任务遗物无统一原版标记，提供显式ModelId排除注册；默认不按名称猜测。异常冻结机会并记录真实阶段，禁止自动重复发奖。

## 验收

1. 构建/静态检查；所有功能自动测试仅在隔离headless中运行，涵盖概率边界、机会、点灯、先古保护、三选一、存储、重复请求、异常和占位节点构建/销毁。
2. 游戏启动仅通过 tools/verify-environment.ps1 -HeadlessGame，核实headless驱动、隔离路径和初始化；不自动开局、切档或菜单导航。不得GUI回退，保留旧日志。
3. 通过构建/PCK检查后在游戏关闭时安全部署；由用户手动GUI验证机会出现、输入遮罩、连续下注、退出、三选一、先古、原版选牌交接、保存继续。
4. 代理交付状态为“headless通过，待用户GUI验收”；用户确认GUI通过才完成最终验收。代理不启动或操控GUI。

## 2026-11-09 初始遗物及其升级品禁投调整

实施前记录：用户已确认以下实现与验收范围；本节结果须在本次验证后填写，下面原有结果保留为历史记录。

- 新增独立投放资格，供遗物列表和提交校验共用。Starter及通过本机已核验的TouchOfOrobas.GetUpgradedStarterRelic映射得到的升级品禁投；过滤未知映射的Circlet回退。投放限制不扩大到机会资格或奖励池。
- 在RelicCmd.Obtain执行前，按涅奥事件已选选项的遗物实例及Ancient分类记录直接奖励来源；快照随获取作用域传到异步成功通知。仅该直接奖励不入队、不消耗机会RNG、不保存，并记录原因；派生奖励独立判断。
- 保留公开接口与存档结构；不撤销已发机会。界面明确说明初始遗物及升级品禁投；同步中英文README及人工GUI清单。
- 验收覆盖五角色初始遗物/升级品的列表过滤、提交拒绝及序列化恢复，其他Ancient和Circlet正常投放，涅奥直接奖励与派生奖励的区别，以及异步上下文变化、失败、取消、抑制与重复完成。
- 完成锁定依赖构建/PCK校验后，仅以tools/verify-environment.ps1 -HeadlessGame验证实际headless驱动、隔离路径及本次初始化。游戏关闭时备份并部署；人工界面验收仍由用户完成。

本次结果：**已构建并安全部署，headless通过，待用户GUI验收**。

- 锁定依赖Release构建0警告、0错误，PCK校验通过；主Mod仍为0.2.0，未附带游戏或RitsuLib程序集。
- 隔离headless通过1770条功能断言及37项界面节点检查。新增用例覆盖五组真实初始/升级品映射、实际库存列表过滤、提交早退、JSON恢复、Circlet回退、涅奥精确奖励实例和异步来源快照。库存夹具仅创建脱离RunManager的模型，不执行开局或拾取流程；工厂的发现标记仅写入隔离测试数据。
- 实际驱动为headless，退出码0；RitsuLib及主Mod初始化、Mod日志、隔离用户数据和缓存路径均已核实。真实涅奥选择流程、按钮显示及点击仍由用户GUI验收。
- 已按现有脚本备份并部署主Mod；部署DLL/PCK/JSON的SHA256与本次headless验证产物逐项一致。旧Mod及旧日志保留。
- 引擎退出时的RID/资源释放诊断与上次基线逐项一致；订阅者异常为原有预期测试夹具输出，未影响成功结果。
- 首次构建发现测试夹具不能直接写入游戏属性的私有setter，已改为仅测试使用的反射赋值后重新构建通过；失败输出保留在artifacts/staging/20261109-180547-bd6dc39f/Sts2ModScaffold-build.log。

本次报告：

- [环境与MCP检查](../../artifacts/verification/20261109-180637-645/environment-report.json)
- [1770条断言与37项节点检查](../../artifacts/verification/20261109-180637-645/headless/raven-headless.json)
- [Headless驱动、隔离路径与进程报告](../../artifacts/verification/20261109-180637-645/headless/headless-report.json)
- [本次Mod初始化与机会过滤日志](../../artifacts/verification/20261109-180637-645/headless/mod_log.txt)
- [构建及安全部署记录](../../artifacts/staging/20261109-180708-e1c204e6/build-result.json)
- [部署产物一致性](../../artifacts/raven-starter-deployment-verification.json)

## 历史结果（本次调整前）

代码与自动验收已完成，最终状态：**headless通过，待用户GUI验收**。

|项目|结果|
|---|---|
|锁定依赖Release构建|0警告、0错误；PCK校验通过；无游戏/RitsuLib DLL副本混入主Mod|
|隔离headless功能检查|1707条断言通过：概率、阶梯、先古保护、点灯、RNG恢复、完整候选属性、机会队列、获取观察器异常/取消/抑制|
|占位界面节点检查|37条通过：六灯、按钮回调、禁用、默认退出焦点、节点清理、地图延后条件|
|真实初始化|headless显示驱动，隔离数据路径，RitsuLib与主Mod初始化，退出码0；未开局或操作菜单|
|部署|0.2.0已安全部署，旧主Mod已备份；DLL/PCK/JSON三项SHA256与headless验收产物一致|
|人工GUI|待用户按验收清单完成；代理未启动/操控GUI|

正常奖励收齐后原版奖励屏仍留在Overlay栈，地图也优先于Overlay。本次增加已完成奖励屏叠加和地图打开延后，机会全部处理后重放原地图请求；真实显示/鼠标/原版选择衔接仍须用户GUI验收。

本次引擎退出仍有RID/资源释放诊断，与接入RitsuLib前次headless基线的诊断计数一致，不宣称引擎退出日志零警告。获取观察器测试会故意记录一条标为 `expected headless subscriber failure fixture` 的异常，验证原成功结果不受订阅者异常影响。

- [最终headless环境报告](../../artifacts/verification/20261109-043253-314/environment-report.json)
- [1707+37项功能报告](../../artifacts/verification/20261109-043253-314/headless/raven-headless.json)
- [初始化、驱动、PID与隔离参数](../../artifacts/verification/20261109-043253-314/headless/headless-report.json)
- [完整测试输出](../../artifacts/raven-final-headless.log)
- [部署输出](../../artifacts/raven-deploy.log)
- [部署文件一致性](../../artifacts/raven-deployment-verification.json)
- [用户GUI验收清单](raven-lamp-gui-checklist.md)
