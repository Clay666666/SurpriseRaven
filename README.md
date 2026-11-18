# 惊喜渡鸦 · SurpriseRaven

[English](README_en.md)

惊喜渡鸦是《杀戮尖塔 2》的单人 Mod。捡到符合条件的遗物后，渡鸦会在当前房间亮起路灯。你可以赌一件已有的遗物，试试能否换来更高稀有度的奖励，也可能被渡鸦一口吞掉。

**下载 0.4.3**　[玩家安装包（ZIP）](https://github.com/Clay666666/SurpriseRaven/releases/download/v0.4.3/SurpriseRaven-0.4.3.zip) · [发布说明与校验值](https://github.com/Clay666666/SurpriseRaven/releases/tag/v0.4.3)

## 安装与升级

1. 准备 Windows Steam 版《杀戮尖塔 2》**0.111.0 或 0.113.1**。两版共用同一安装包。订阅并启用完整工坊版 **RitsuLib 0.6.14**，等待 Steam 下载完成。[RitsuLib 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
2. 关闭游戏。在 Steam 库中右键游戏，选择「管理」→「浏览本地文件」，打开游戏安装目录。若装过开发版渡鸦路灯，将旧的 `mods/Sts2ModScaffold/` 文件夹移到 `mods` 目录之外，再安装新版。可以保留旧目录作为备份。已有单人存档中的渡鸦进度沿用原保存键。
3. 将玩家 ZIP 中的 `mods` 文件夹解压到游戏安装目录，合并同名目录。确认磁盘上的层级如下。GitHub 的「Source code」归档供开发者使用。
4. 通过 Steam 以启用 Mod 的方式启动游戏，确认 RitsuLib 和惊喜渡鸦均已启用。开始或继续单人游戏即可。Mod 界面文字为简体中文。

```text
Slay the Spire 2/
└── mods/
    └── SurpriseRaven/
        ├── SurpriseRaven.dll
        ├── SurpriseRaven.json
        └── SurpriseRaven.pck
```

## 怎样把遗物交给渡鸦

- **触发机会。** 正常获得一件罕见、稀有、先古或商店遗物后，等游戏完成拾取与选择，路灯会自动出现。每件符合条件的遗物提供一次当前房间的机会。涅奥直接赠送的先古遗物、读档和渡鸦自己的奖励不会触发新机会。多人游戏中路灯关闭。
- **献祭。** 从屏幕顶部的原生遗物栏，将带青色光晕的遗物拖到灯下圆环，松开鼠标。可以投入早已持有的遗物。初始遗物及其升级品、涅奥赠礼池、事件、已登记的任务遗物和 32 种拾取时立即给收益的先古遗物不可投入。[先古禁投名单](docs/plans/raven-ui-ancient-stakes.md#禁投名单核对)
- **看灯数决定是否继续。** 每次机会以随机 **0–5** 根玩法蜡烛起步，另外四根装饰蜡烛不计入概率。每轮未被吞噬时有 **50%** 概率多亮一根，上限 **6**，满灯后仍可继续。每档稀有度都有自己的概率，灯越多也可能越容易被吞噬。六灯时投入普通遗物，升档、平换、吞噬的概率分别为 70%、13%、17%。[逐档置换概率表](docs/plans/raven-excel-odds.md#实际抽签概率)
- **领取或离开。** 渡鸦可能升档、平换、降档或吞噬。只有稀有和先古投入能触发三选一。先古不会被吞噬。奖励落地后，点击遗物、悬停后按 F，或聚焦后按确认键领取。三选一时要分别取回原件，并从额外奖励中领走一件。按 Esc 或界面返回按钮离开时，未领取的遗物将被放弃。

## 排错

| 遇到的问题 | 检查方法 |
| --- | --- |
| 下载的 ZIP 里只有源码，找不到 DLL 或 PCK | 下载并解压发布页 Assets 下的 [SurpriseRaven-0.4.3.zip 玩家包](https://github.com/Clay666666/SurpriseRaven/releases/download/v0.4.3/SurpriseRaven-0.4.3.zip)。 |
| Mod 列表里没有惊喜渡鸦 | 查找 `mods/SurpriseRaven/SurpriseRaven.json`。同一层应有 DLL 和 PCK。若多解压了一层 ZIP 名或 `mods`，把 `SurpriseRaven` 文件夹移到游戏的 `mods` 直属目录。 |
| 依赖缺失或进游戏时报加载错误 | 查看游戏版本，确认完整工坊版 RitsuLib 0.6.14 已安装并启用。Mod 清单里的依赖 ID 是 `STS2-RitsuLib`。若从开发版升级，同时检查旧目录已移出。 |
| 获得遗物后没有弹出路灯 | 先确认是单人模式、遗物稀有度符合条件，并完成当前的战斗或奖励选择。需要定位问题时，可在游戏用户数据目录的 `logs/mod_log.txt` 搜索 `Hooks active` 和 `[Raven]`，并对照同目录的 `godot.log`。 |

## 开发与素材

源码构建需要 0.111.0 游戏基线和开发用素材输入。运行玩家包只需上面的游戏与工坊前置。项目保留模板的 MIT 许可证。PCK 内的第三方音画按项目维护者确认的范围随 Mod 发行。[源码构建](docs/source-handoff.md) · [代码许可](LICENSE) · [素材与第三方声明](THIRD_PARTY_NOTICES.md)
