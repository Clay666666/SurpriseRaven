# 奖励界面精简、详情清理与先古禁投

日期 2026-11-17，基线 503ca94。用户确认人工审核项 1–5 禁投，6–8 允许。

## 实现

- 左上角仅显示轮次，移除数字烛火。奖励图标下方无逐件说明，三选一候选组上方只显示一个居中“三选一！！！”标题。两个候选显示“二选一！！！”，一个候选不显示选择标题。常规奖励结果和底部重复说明收起，异常信息仍可读。文字备用界面保留必要的遗物识别与领取控件。
- 主动焦点仅用于方向导航，普通刷新保留有效焦点。鼠标移出原件及候选后关闭详情。禁用、隐藏、回收与关闭时清理各自 tooltip，处理备用按钮重复创建提示及顶部原件隐藏的 owner 清理。
- 按固定遗物类型禁止献祭已在拾取时兑现牌组、资源或地图收益的 32 种非涅奥先古。包括大～抱抱、玩具盒、佩尔之牙、佩尔的增生组织、黄金罗盘。领主阳伞、战锤、切肉刀继续允许。
- 禁投作用于所有来源和读档实例，候选生成与领取仍沿用现有规则。已提交的待领奖励继续正常结算，新增献祭在 RNG 和扣除前拒绝。使用明确名单，不依赖不完整的 HasUponPickupEffect。

## 验收与交付

- 隔离 SubViewport 验证真实悬停进入/移出、方向导航、刷新、原件先领/后领、隐藏及清理，兼顾原生和备用按钮。
- 验证标题与候选组居中、数量变化、无逐件文案和烛火计数。验证 32 种禁投和三种明确放行、保存恢复、提交无副作用及奖励池可领取。
- 和同目录正在进行的拾取面板修复整合已提交结果，完成 Release locked build、PCK、两版静态 API 和隔离 headless。保留诊断，确认真正 headless，禁止游戏 GUI。
- 游戏关闭后备份主 Mod，部署同一份验证产物并核验哈希。结果标记待用户 GUI 验收。

## 禁投名单核对

用本机两版 ILSpy 核对先古奖励池和拾取回调，以下 32 种与现有涅奥禁投名单无交集。按类型判定，跳过拾取附带的选项也不改变资格。

| 拾取效果 | 先古遗物 |
| --- | --- |
| 加牌或选牌 | 佩尔之角（`PaelsHorn`）、故事书（`Storybook`）、原初之爪（`SereTalon`）、卓越斗篷（`DistinguishedCape`）、玻璃眼珠（`GlassEye`）、海玻璃（`SeaGlass`）、珠宝盒（`JewelryBox`）、坦克斯的哨子（`TanxsWhistle`）、尘封魔典（`DustyTome`） |
| 升级或附魔 | 佩尔之爪（`PaelsClaw`）、佩尔的增生组织（`PaelsGrowth`）、营养汤（`NutritiousSoup`）、美味饼干（`YummyCookie`）、放电异虾（`ElectricShrymp`）、沙堡（`SandCastle`）、华美手镯（`BeautifulBracelet`）、三刃回旋镖（`TriBoomerang`） |
| 删牌或变牌 | 佩尔之牙（`PaelsTooth`）、大～抱抱（`BiiigHug`）、腌制活雾（`PreservedFog`）、古老牙齿（`ArchaicTooth`）、利爪（`Claws`）、星盘（`Astrolabe`）、空鸟笼（`EmptyCage`）、潘多拉魔盒（`PandorasBox`） |
| 资源发放 | 玩具盒（`ToyBox`）、召唤铃铛（`CallingBell`）、炼金箱（`AlchemicalCoffer`）、布质果实（`LoomingFruit`）、图章戒指（`SignetRing`） |
| 地图或初始遗物 | 黄金罗盘（`GoldenCompass`）、欧洛巴斯之触（`TouchOfOrobas`） |

佩尔之牙、佩尔的增生组织、大～抱抱、玩具盒和黄金罗盘兼有持有效果，按用户审核结果禁止献祭。领主阳伞、战锤和切肉刀在后续房间、战斗或营地触发，按审核结果继续允许。

## 验证与部署结果

界面与规则提交为 `5c84865`。整合拾取面板修复后，验证与部署使用 `9ca25fd` 的产物。Release 锁定构建无警告和错误，PCK 校验 41 项通过。Python 回归 28 项通过，1 项符号链接权限用例跳过。

| 游戏 | 静态 API 与隔离结果 |
| --- | --- |
| 0.111.0 | 185 个类型、247 个成员全部匹配。显示驱动为 `headless`，退出码 0，2412 项功能断言及 422 项节点检查通过。[环境报告](../../artifacts/verification/20261117-015617-633/environment-report.json) |
| 0.113.1 | 同一产物。185 个类型、247 个成员全部匹配。显示驱动为 `headless`，退出码 0，2414 项功能断言及 422 项节点检查通过。[环境报告](../../artifacts/verification/20261117-015635-266/environment-report.json) |

首轮隔离检查发现离树时释放焦点会触发引擎错误，且真实鼠标移动会退出方向导航模式。最终修改只在节点仍在树中时释放焦点，并分别验证鼠标切换和方向导航。已保留初次失败日志、引擎诊断及最终报告。[验证记录](../../artifacts/verification/raven-ui-ancient-stakes/)

确认游戏关闭后，备份并替换 0.113.1 游戏目录中的主 Mod，DLL、清单和 PCK 哈希与验证产物一致。DLL SHA256 为 `96c80e0025ce28c46b83b5950d799e0561830617cd075c16bd1c821f5f902d49`。[部署记录](../../artifacts/verification/raven-ui-ancient-stakes/deployment-result.json) · [部署前备份](../../artifacts/backups/verified-raven-ui-ancient-20261117-015702-635/Sts2ModScaffold/)

**已部署，待用户 GUI 验收。** [手动验收清单](raven-lamp-gui-checklist.md)
