# Git 版本管理

## 当前基线

- 当前公开版本为 `SurpriseRaven 0.4.3`，使用完整工坊版 RitsuLib `0.6.14`。[公开发行与验收方案](plans/surprise-raven-release.md)
- 保留恢复标签：`v0.2.0`，对应回退 SL 实验后的玩法、工具链和测试源码；双版本验收记录见 [compat-111-113.md](plans/compat-111-113.md)。
- 开发仓库保留上游模板历史和原有 `origin`。公开源码快照与玩家发行位于 [Clay666666/SurpriseRaven](https://github.com/Clay666666/SurpriseRaven)，默认分支为 `main`。
- 0.3.1 的同一主 Mod 产物已通过 111/113 构建、静态 API 和隔离 headless 验证，真实 GUI 验收仍由用户完成。Git 标签不等于 GUI 验收通过。
- 0.3.2 的实现、验证与部署证据见 [顶部遗物献祭记录](plans/v0.3.2-native-relic-sacrifice.md)。版本标签仅在规定验证完成后创建，仍标记“待用户 GUI 验收”。

## 每次修改

1. 先运行 `git status --short --branch`，确认正在使用的分支及已有改动；不要把无关工作混入本次提交。
2. 从干净、已提交的基线创建任务分支，例如 `git switch -c codex/sl-safety`。实验留在独立分支，确认保留后再合并回开发分支。
3. 按项目要求先记录计划，再实现与验证。每个可独立验证的改动形成一个提交，说明行为变化和验证结果。
4. 显式暂存目标文件，检查 `git diff --cached --stat`、`git diff --cached` 和 `git diff --cached --check` 后提交。完整阶段结束时工作区应干净。
5. 版本号在 Mod 清单及中英文 README 中同步更新；通过验证后创建附注标签 `git tag -a vX.Y.Z -m "版本说明与验证范围"`。不移动或覆盖已有版本标签。

## 回退与部署

- 部署使用干净提交。普通部署走 `tools/build-mod.ps1`，双版本验收后用同一入口传入 `-VerifiedBuildReport <build-result.json> -VerificationIndex <passed-reports.json>`，复用验收产物。索引按版本记录 `Version` 和 `artifacts/verification` 下的报告目录名 `Directory`。
- 同一仓库各工作树共用 Git 公共目录下的部署记录。已安装文件须与记录匹配，候选提交须包含上一部署提交。应合入缺失提交后重建，回退从当前交付提交执行 `git revert`。历史临时部署脚本不再用于日常部署。

- 已提交的功能使用 `git revert <提交号>` 留下明确撤销记录；多提交功能按逆序撤销，处理冲突后重新验证。
- 临时检查旧版本可用 `git switch --detach v0.2.0`，检查前确保工作区干净；继续开发前切回具名分支。
- 尚未提交的工作先备份或提交到实验分支，再决定取舍。不要使用 `git reset --hard`、`git clean -fd` 或整仓 `git restore` 删除其他工作。
- Git 回退只恢复源码。需要恢复游戏内版本时，仍须经过构建、PCK 校验、游戏关闭检查和精确目标备份；不得修改游戏存档。

## 入库边界

提交源码、测试、依赖锁文件、工具脚本、兼容补丁、计划和可移植配置示例。机器路径从参数或本地 `.env` 提供。

游戏 DLL、下载工具、构建产物、日志、验证报告、备份、真实 `.env` 和生成的 Codex 配置均留在被忽略的本地目录。报告可以在文档中引用，但不会随仓库自动上传。

本仓库设置了本地 `push.default=nothing`，普通 `git push` 不会隐式向模板上游发布。用户指定自己的远程仓库并授权推送后，显式指定远程和分支；不修改用户全局 Git 配置。
