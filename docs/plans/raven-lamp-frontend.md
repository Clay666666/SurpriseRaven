# 渡鸦路灯 2D 前端：实现与验收

日期：2026-11-09；用户批准实施。替换纯文字占位界面，复刻网页/原版渡鸦路灯的动画与音效细节。保留 Sts2ModScaffold/net9.0/RitsuLib 0.6.10 与既有规则、概率表、奖池。

## 用户决策

- 2D 简化面板，不渲染 3D 模型；星空舞台 + 代码绘制路灯与台座。
- 10 支蜡烛：4 支常亮烛台 + 6 支按 `state.Lights` 依次点亮（B4、B5、B、B1、B2、B3，两侧交替自下而上）；吞噬时 10 支全灭并冒烟。
- 渡鸦为代码绘制的分件剪影（身体/头/喙/翅/尾/腿），补间实现待机、起飞叼走、飞回投放、落地。
- 交互：B/按钮打开遗物背包，拖到右侧“献祭”区；也可选中后点“献祭”按钮（键盘/手柄）。奖励落在三个落点，悬停显示原生遗物提示，点击或 F 拾取后才真正替换/获得。
- 音效以 WAV 为主，映射集中在 `assets/raven/raven-frontend.json`。
- 2026-11-09 的开发版本把腾讯《三角洲行动》素材保存在本机，源码仓库只提交选材规格、映射与脚本。0.4.3 正式发行采用下方列明的分发范围。

## 2026-11-10 追加要求

- 2D 构图参考 bilibili「惊喜渡鸦模拟器」：中央双头路灯、渡鸦停在灯顶、两侧蜡烛、灯下献祭圆环；渡鸦沿曲线飞出、俯冲叼物、飞回投放。
- 美术尽量与杀戮尖塔2一致：复用原版 `scenes/ui/common_banner.tscn`（奖励界面横幅）、`scenes/ui/back_button.tscn`（左下返回，Esc）、遗物稀有度光效 VFX、原版字体与配色；程序化元素采用深色粗描边+暖色高光的手绘风格；星空背景压暗以贴合原版色调。
- 进入/退出渡鸦使用原版房间过场 `NGame.Instance.Transition.RoomFadeOut()/RoomFadeIn()`（与进入地图房间一致，服从快速模式）；过场前后均保证成对调用，过场中不重复进入。
- 局内拾取尽量不离开面板：无拾取效果的替换/获得与吞噬在面板内结算；带 `HasUponPickupEffect` 的遗物仍临时移出面板交给原版界面，结束后返回。

## 已核实的游戏/引擎事实

- MegaDot 4.5.1（D3D12 Forward+，另有 Vulkan/OpenGL 启动器）。游戏音效走 FMOD，但 Godot 音频可用：`default_bus_layout` 含 `SFX` 总线；`NGame` 启动与 `NMasterVolumeSlider`/`NSfxVolumeSlider` 调用 `NDebugAudioManager.SetMasterAudioVolume/SetSfxAudioVolume`，后台静音同步 Master。Mod 音效输出到 `SFX` 总线即可服从设置。
- `ModManager` 以 `ProjectSettings.LoadResourcePack` 挂载 `{id}.pck`，原始文件可用 `FileAccess` 读取 `res://` 路径。
- 运行时解码：`Image.LoadPngFromBuffer/LoadWebpFromBuffer`、`ImageTexture.CreateFromImage`、`AudioStreamWav.LoadFromBuffer(bytes, options)`；引擎包含 `CPUParticles2D`。
- 游戏快捷键在 `NInputManager._UnhandledKeyInput` 处理；面板 `GuiInput` 接受的按键不会泄漏给游戏。
- `RelicModel.Icon` 通过 `ResourceLoader.Load` 读取图集；`NHoverTipSet.CreateAndShow(owner, relic.HoverTips, HoverTip.GetHoverTipAlignment(owner))` / `NHoverTipSet.Remove(owner)` 显示原生提示。
- 本项目没有 Godot 源生成器：不重写 `_Process/_Input/_GetDragData`；逐帧用 `SceneTree.ProcessFrame`，输入用 `GuiInput`/按钮事件，拖拽每帧轮询鼠标。
- RitsuLib 局内存储用 System.Text.Json，新增字段带默认值即可兼容，SchemaVersion 保持 1。

## 素材与构建

- `assets/raven/raven-assets.json`：逻辑 id → 来源相对路径、打包路径、类型（png/webp/wav）、尺寸与帧格。
- `tools/stage-raven-assets.ps1`：从 `RAVEN_ASSETS_DIR`（`-RavenAssetsDir` > `.env` > `artifacts/artifact`）复制素材，校验文件头、尺寸、帧格、WAV PCM16，写入 `manifest.json`（SHA256、大小、尺寸、WAV 时长）并复制 `frontend.json`。缺失/损坏时构建失败，不触碰已安装 Mod。
- `tools/pck_builder/build_pck.gd -- <清单> <素材目录> <报告.json> <输出.pck>`：素材打包到 `res://Sts2ModScaffold/raven/`，报告列出每个条目与大小，构建脚本逐项核对。
- 素材本体永不入库（`artifacts/` 已忽略，`.gitignore` 额外拒绝 `assets/raven/` 下的媒体文件）。

## 前端运行时（`src/Hooks/Raven/Frontend/`）

- 资源：读取 PCK 清单与素材并缓存；缺失时降级为无贴图/无音效（程序化绘制仍可用），记录一次日志。
- 时间线：纯逻辑 `RavenTimeline`，所有时长在 JSON 中；“存活”与“吞噬”两种序列，按绝对时间触发提示（叼走、起飞、离场、飞回、投放、揭晓、落地 / 熄灭、吞噬语音、结束）。
- 舞台：星空背景（缓慢漂移+暗角）、台座与双头路灯、10 支蜡烛（火焰 8×8 序列帧 40fps、错帧 7、帧间插值；光晕闪烁 0.96+0.04·sin(8t+i)；熄灭烟 8×8 30fps、2.5–3s、前 45% 扩大、55% 后淡出）、渡鸦、三个落点。
- 礼花：升档或三选一时 CPUParticles2D 爆发：金纸屑（4×4）、彩纸（2×2）、烟团（8×8）、闪光。
- 奖励：遗物图标 + 稀有度光圈（普通灰、罕见紫、稀有金、先古红），下落弹跳、漂浮；悬停原生提示，点击/F/确认键拾取。
- HUD：标题、轮次、亮灯 n/6、当前灯数五档概率条、消息、结果大字、底部提示、背包与退出按钮。
- 背包：暗色渐变面板，网格列出全部自有遗物；不可投入者压暗并标注原因；拖拽虚影跟随鼠标，右侧 18% 红色渐变“献祭”区高亮。

## 音效映射（WAV）

| 事件 | 文件 |
|---|---|
| 投入 | raven_calls 01–09 随机 |
| 升档 / 升到先古 | dialogue 08、09 / 05、06 |
| 三选一 | dialogue 04 |
| 平换、先古不变 | dialogue 07 |
| 降档 / 降到普通 | dialogue 02 / 01 |
| 吞噬 | dialogue 03、10、11 随机 + interactions/final_submission_extinguish |
| 奖励落地 | item_drop |
| 点灯 / 熄灭 | candle_light / candle_extinguish 01–06 |
| 庆祝 | celebration 01–03 |
| 待机 | 就绪/拾取阶段，打开 3s 后首叫，之后每次操作后 30s，raven_calls |

加权随机、避免连续重复；常驻节点播放，输出 `SFX` 总线，默认音量 0.6。

## 两段式结算

- `PlanRound`（同步，无 RelicCmd）：校验资格与 revision → 掷结果、生成候选（替换 1、三选一 3）、掷下一灯数 → 写入 `RavenState.Pending` 并 `Touch`。不 Dismiss，动画在面板内连续播放；消息不提前泄露结果。
- `CollectAsync`：沿用 StartOperation（Busy、抑制、Dismiss 交给原版接管）。替换类执行 `RelicCmd.Replace`（校验原索引/Id 与 `CanClaim`，失败保留原件）；三选一 `RelicCmd.Obtain`；先古不变无操作；之后应用灯数。顶部遗物栏此时才变化。
- `ResolveSwallowAsync`：熄灭动画后 Dismiss → `RelicCmd.Remove` → 结算页。
- 退出：动画中禁止；拾取阶段替换类先自动拾取再退出，三选一视为放弃，先古不变直接退出。
- 读档：有未结算回合则以拾取阶段打开（无动画），未结算吞噬直接结算；`InFlight` 仍冻结；旧存档 `BonusCandidates` 迁移为“三选一待拾取，灯数不变”。

## 验收

1. 锁定依赖 Release 构建 0 警告 0 错误；PCK 头正确，报告条目与素材清单一致。
2. PowerShell 夹具测试覆盖：素材缺失/尺寸错误时构建失败且不改动安装；夹具素材进入暂存与报告。Python unittest 通过。
3. 只用 `tools/verify-environment.ps1 -HeadlessGame` 验证：驱动 headless、路径隔离；原有断言与新增断言（PCK 素材哈希/解码、配置映射完整、时间线顺序与门控、待结算回合 JSON 往返/迁移/退出规则、舞台节点构建与释放）全部通过；保留旧日志。
4. 游戏关闭时备份部署；状态“headless 通过，待用户 GUI 验收”。代理不启动游戏 GUI。
5. 用户 GUI 验收：视觉（火焰、光晕、点灯、熄灭烟、礼花、渡鸦三种动画）、交互（拖拽、按钮献祭、悬停提示、F/点击拾取、三选一、Esc）、语音与待机叫声、音量设置与后台静音、D3D12/Vulkan/OpenGL、存档退出续玩不重掷。

## 风险

- 开发期间尚未确认原媒体的公开分发范围。0.4.3 项目维护者已确认可将当前 28 项媒体打入惊喜渡鸦正式玩家包的 PCK。第三方权利仍归原权利人，源码仓库继续只收录素材规格和映射。[0.4.3 素材与第三方声明](../../THIRD_PARTY_NOTICES.md)
- 语音映射按文件名推断，时长默认值无官方数据，需 GUI 验收后调 JSON。
- WAV 约 12MB，PCK 体积增大。
- 结算时序变化：投入锁定、拾取生效；房间被强制切换时未拾取的替换结果会被丢弃并记录日志（地图已延后，正常流程无法触发）。

## 实施结果

待填写。
