# RitsuLib 代码接入与 headless 验收

日期：2026-11-09。用户已授权实施；测试严禁启动游戏 GUI（包括隐藏、最小化窗口），只允许真正 headless，无 GUI 回退。

## 范围和实现

- 保留 Sts2ModScaffold/net9.0 和现有 SDK/Python，接入编译专用 STS2.RitsuLib [0.6.10] 并锁依赖。
- 排除传递 NuGet GodotSharp 资产，继续引用本机游戏程序集；初始化先注册 ModTypeDiscoveryHub，再应用 Harmony。
- 清单声明 STS2-RitsuLib 依赖；日志使用 OS.GetUserDataDir；测试探针检查 headless 与隔离路径后延迟退出。
- 验证现有工坊运行时版本、兼容分支、11项DLL哈希与资源包CRC，正常游戏不安装第二份。
- 默认验证不启动游戏；旧 -Game/--game 路径拒绝；DeepVerify Y 仅调用 -HeadlessGame。
- 同D盘建立隔离测试镜像，硬链接复用原版只读资源，只复制完整 RitsuLib 和本次主 Mod。独立 APPDATA/LOCALAPPDATA/TEMP/TMP，不使用真实设置或存档。
- 直接启动镜像程序，必须包含 --headless --force-steam=off --log-file，帧数退出上限与120秒墙钟超时，只管理本次PID，禁止Steam GUI URI回退。
- 本阶段不添加玩法、场景脚本、SourceGenerators 或美术资源工程。

## 验收

- 依赖锁定、编译和唯一游戏GodotSharp引用通过；产物不携带RitsuLib/游戏DLL副本。
- 运行时包完整且匹配0.111.0；清单依赖保留。
- 测试入口拒绝GUI路径，默认不会启动游戏，模拟启动器核实headless参数和隔离环境。
- 实际headless日志确认RitsuLib与主Mod初始化、程序集注册和headless显示驱动，隔离数据路径正确。
- 无法通过的运行时项目明确报告未验证，不回退GUI。

## 实施与验收结果

已完成代码接入、依赖锁定、运行时预检和部署，以下均在 2026-11-09 验证：

| 检查 | 结果 |
| --- | --- |
| 依赖恢复 / 编译 | STS2.RitsuLib 0.6.10；net9.0；0 警告、0 错误；普通构建锁定恢复 |
| GodotSharp 绑定 | 唯一编译来源为本地游戏 `references/GodotSharp.dll`，没有使用 NuGet DLL |
| 运行时完整性 | 完整工坊包 0.6.10，兼容 API 0.111.0，11 DLL SHA256 和 35 资源项 CRC 全通过 |
| 产物 / 部署 | 主 Mod 仅自己的 DLL、清单、PCK；清单声明 STS2-RitsuLib；真实游戏未安装第二份 RitsuLib |
| Headless | 真正 `headless` 显示驱动、退出码 0、RitsuLib 和主 Mod 初始化、程序集注册探针全通过 |
| 数据隔离 | 用户数据和缓存位于 D 盘独立镜像内；原 150 个用户数据文件 SHA256 全部未变 |
| GUI 禁令 | 本轮未启动游戏 GUI；旧 GUI 启动、验证及菜单测试入口均拒绝，无 GUI 回退 |

第一次真实 headless 验收和最终完整 `install.ps1 -DeepVerify Y` 流程均通过。后者现在只路由到 headless 验证。运行时进程在初始化探针完成后自动退出。

结果文件：

- [最终安装输出](../../artifacts/ritsulib-final-install.log)
- [最终环境报告](../../artifacts/verification/20261109-034027-431/environment-report.json)
- [Headless 进程与参数报告](../../artifacts/verification/20261109-034027-431/headless/headless-report.json)
- [实际驱动、库版本及数据路径探针](../../artifacts/verification/20261109-034027-431/headless/headless-probe.json)
- [MSBuild 编译引用列表](../../artifacts/ritsulib-reference-paths.json)
- [用户数据保护检查](../../artifacts/ritsulib-final-validation.json)

Python 回归共 24 项，1 项 Windows 符号链接权限用例跳过，其余通过。PowerShell 构建部署及 headless 隔离夹具通过，包含旧 GUI 入口拒绝、数据和 Mod 复制范围、固定 headless 参数、失败检查及管道读取超时。没有通过隐藏窗口测试 GUI。

隔离镜像保留在游戏所在盘的 `SteamLibrary\sts2-mod-tests` 目录供诊断，实际位置记录在本地 headless 报告中。原版文件采用同盘硬链接，没有重新下载或复制整套游戏。运行时初始化测试不代表玩法或联机验收；游戏退出时仍有引擎 RID 资源释放诊断，本次不宣称引擎退出日志完全无警告。

后续开发从 `src/Hooks/` 开始；需要使用 RitsuLib 的注解发现时，入口注册已就绪。场景/贴图/动画与挂 C# 脚本的 Godot 资源工程按后续需求接入，不在本次范围内。
