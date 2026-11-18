# 渡鸦页面左右避让、B键修复与部署

日期：2026-11-10。用户批准实施并在验证后部署主Mod。

## 已确认问题

- 安装目录DLL为旧产物5470…，上轮修复产物c872…未部署。本轮必须部署与headless验证相同的产物并检查SHA256。
- 页面当前仅避让TopBar，遗漏独立RelicInventory；B监听在没有焦点的根面板，无法收到子控件键盘事件。

## 实现范围

1. 从已提交基线d25c3d0创建codex/raven-header-and-b-key。
2. 顶边统一取原版TopBar背景/地图/牌组/暂停按钮和有效可见遗物格的实际最低下沿，转换至面板局部坐标后向上取整加12。排除待删除、隐藏及完全移出可见区域的节点。继续仅缩放Y，保持X、宽度和底边。
3. 给现有背包按钮设置PhysicalKeycode=B的Godot原生Shortcut，ShortcutContext为整个Mod面板，关闭附带视觉反馈/提示；沿用现有ToggleBag。仅顶层可见可交互时启用，文本输入/过场/忙碌时禁用，关闭时注销订阅；移除旧B分支，不改Esc/F/手柄路径。
4. 无美术、动画、音效、玩法概率或存档格式改动，不修改版本号。

## 验收与交付

- 真实FlowContainer夹具覆盖单/多行、增删、窗口变窄、移出屏幕及祖先位移缩放；等待排版完成后核对左右边界、X/宽度/底边及命中。
- 独立SubViewport通过PushInput分发B事件，覆盖子按钮/背包格焦点、空/非英文字符、重复/释放/Ctrl+B、文本输入、被覆盖/隐藏/忙碌/关闭后的门控及恢复。
- Release locked build/PCK、相关离线回归及唯一允许的verify-environment.ps1 -HeadlessGame通过。确认驱动headless，不发送按键到真实菜单，不启动GUI。
- 再次检查游戏已关闭，精确备份主Mod目录，部署同一份验证产物，逐文件SHA256匹配；失败恢复。不得触碰其他Mod或存档，不全局强杀进程。
- 分阶段提交；最终状态为已部署、待用户GUI验收。

## 实施结果

已完成，代码提交 `dcbd3ea`，分支 `codex/raven-header-and-b-key`。

### 代码与验证

- NativeTop已纳入TopBar和RelicInventory.RelicNodes；排除待删除、隐藏、完全离屏节点，转换实际矩形并取面板区域内的下沿。仍然只压缩Y，保留X/宽度/底边。
- 背包沿用现有按钮与ToggleBag，绑定物理B原生Shortcut，Context为整个面板。顶层/活动界面、可见性、文本输入、忙碌/过场及关闭门控均已接线；原生按钮快捷键反馈/提示关闭。Esc/F/手柄代码未改。
- 探针只在隔离测试环境异步等待节点检查，让FlowContainer完成排版并向独立SubViewport分发输入，不向真实菜单发送按键。

| 检查 | 结果与证据 |
| --- | --- |
| Release locked build/PCK | 0警告、0错误，38项素材、41个PCK条目；[构建日志](../../artifacts/verification/header-b-fix-20261110/release-build.log) |
| Python unittest | 23通过，1项符号链接权限用例跳过；[日志](../../artifacts/verification/header-b-fix-20261110/python-tests.log) |
| headless隔离夹具 | 通过；模拟目录且不启动游戏；[日志](../../artifacts/verification/header-b-fix-20261110/headless-fixture-tests.log) |
| 实际隔离headless | 退出码0，驱动headless，RitsuLib与主Mod初始化通过；[环境报告](../../artifacts/verification/20261110-193924-883/environment-report.json) |
| 功能/节点检查 | 2029功能断言、259节点检查通过；[报告](../../artifacts/verification/header-b-fix-20261110/raven-headless.json) |
| 布局专项 | 真实HFlowContainer覆盖0/1/2/3/5行、增删/窄化/恢复、隐藏与平移离屏、祖先变换、命中及预览居中，共144项检查 |
| B键专项 | 独立SubViewport.PushInput覆盖子控件/背包格焦点、无字符/非英文字符、重复/释放/Ctrl+B、文本输入、遮挡/忙碌/过场/隐藏/关闭和重新绑定，共22项检查 |
| 静态检查 | git diff --check通过，全部Hook文件不超过200行；相关原版API已通过本机ILSpy核对 |

### 精确产物部署

- 游戏关闭状态下，将[已验证的同一份产物](../../artifacts/staging/20261110-193913-00d340a2/mods/Sts2ModScaffold)部署为真实主Mod。未再次构建，未启用辅助Mod部署。
- 部署前逐一核对源三文件与隔离测试镜像的SHA256，并核对环境报告中的DLL/PCK哈希。只使用现有构建脚本的白名单安全函数，未执行其构建入口。
- 完整备份旧主Mod（包含隐藏文件），核验备份后才替换；目标路径及重解析点防护、替换前游戏关闭检查、逐文件哈希与精确文件集合检查通过。
- [部署报告](../../artifacts/verification/header-b-fix-20261110/deployment-result.json)、[部署日志](../../artifacts/verification/header-b-fix-20261110/deployment.log)、[旧版本备份](../../artifacts/backups/verified-header-b-20261110-194102-860/Sts2ModScaffold)。未修改其他Mod、存档或旧日志。
- 安装DLL SHA256已由旧的 `5470d2439657f6f303a432cfc4800f31e781555aa36a8178998dfcc3030cb739` 更新为本次 `1776564c4eb45e4bc645c99fe8baabb74efe7e88cc78fdd12c091fc49372c759`，备份仍保留旧哈希。
- JSON清单SHA256为 `c9ae6ecd93d7c9a4fe8f42a5139d661b60a6403053e7522e9632e059121796c7`，PCK SHA256为 `0184410c76c83af240fcce16733677155ca10fbc0ce0ba85ed0ea4577ae27807`。资源包与上一轮一致，版本号仍为0.3.0。

### 验收边界

[引擎日志](../../artifacts/verification/20261110-193924-883/headless/headless-engine.log)保留既有30次Dummy shader诊断及退出RID/资源诊断；本轮未修改shader或媒体，未宣称真实GPU渲染或零错误运行。新增的异步夹具会加载更多UI缓存和文本资源，其警告保留在同一日志。

全程未启动或操作游戏GUI，未开局/继续游戏/菜单往返。当前状态：**已部署，待用户GUI验收**。用户手动启动游戏后检查左右顶部不重叠、鼠标开包和B键开关背包；最终显示与输入法实际环境验收以用户结果为准。
