# SurpriseRaven · 惊喜渡鸦

Local stable build **0.4.4** restores the lamp after a reload and includes louder candle effects and an offline audio review.

[简体中文](README.md)

SurpriseRaven adds a relic exchange to single-player *Slay the Spire 2*. When you obtain a qualifying relic, a raven offers you a chance to gamble a relic you already own. It might bring back a better one, or swallow your offering.

**Download 0.4.3**　[Player ZIP](https://github.com/Clay666666/SurpriseRaven/releases/download/v0.4.3/SurpriseRaven-0.4.3.zip) · [Release notes and checksum](https://github.com/Clay666666/SurpriseRaven/releases/tag/v0.4.3)

## Installation and upgrades

1. Use the Windows Steam edition of *Slay the Spire 2*, version **0.111.0 or 0.113.1**. Both use the same ZIP. Subscribe to the full **RitsuLib 0.6.14** Workshop package, let Steam finish downloading it, and enable it. [RitsuLib on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3747602295)
2. Close the game. In your Steam library, right-click the game and choose **Manage → Browse local files**. If you previously installed the Raven Lamp development build, move its `mods/Sts2ModScaffold/` folder out of `mods` before installing this version. You can keep the old folder as a backup. Ongoing single-player runs use the same saved Raven progress.
3. Extract the `mods` folder from the player ZIP into the game's installation directory and merge the folders. Check the layout below. GitHub's separate **Source code** archives are for developers.
4. Start the game through Steam with mods enabled. Check that both RitsuLib and SurpriseRaven are enabled, then start or continue a single-player run. The in-game Raven interface is in Simplified Chinese.

```text
Slay the Spire 2/
└── mods/
    └── SurpriseRaven/
        ├── SurpriseRaven.dll
        ├── SurpriseRaven.json
        └── SurpriseRaven.pck
```

## Playing the Raven Lamp

- **Earn a visit.** Obtain an Uncommon, Rare, Ancient, or Shop relic. Once the game finishes the pickup and any choices, the lamp opens automatically. Each qualifying relic gives you one opportunity in that room. A direct Ancient gift from Neow, reloading a save, and rewards granted by the raven do not grant visits. The lamp is single-player only.
- **Make an offering.** Drag a relic with a teal glow from the game's top relic bar onto the ring beneath the lamp, then release it. You can offer a relic you owned before the visit. Starting relics and their upgrades, relics from Neow's gift pool, Event relics, registered quest relics, and 32 Ancients with immediate pickup benefits are ineligible. [Excluded Ancients by name and model ID](docs/plans/raven-ui-ancient-stakes.md#禁投名单核对)
- **Read the candles.** A visit starts with **0–5** lit gameplay candles. The four decorative candles do not affect the odds. Each round your offering survives has a **50%** chance of lighting one more, up to **6**. You can keep playing at six lights. Odds depend on the offering's rarity and the number of lit candles. Extra light can also increase the chance of losing the relic. For example, a Common offering at six lights has a 70% chance to upgrade, 13% to trade at the same tier, and 17% to be swallowed. [Odds by rarity and candle count (Chinese)](docs/plans/raven-excel-odds.md#实际抽签概率)
- **Collect before leaving.** The raven can upgrade, trade, downgrade, or swallow your offering. Only Rare and Ancient offerings can trigger a choice of three bonus relics. Ancients cannot be swallowed. Once rewards land, click a relic, hover over it and press F, or focus it and press Confirm. On a bonus round, reclaim your original relic and pick one bonus reward separately. Escape or the back button leaves unclaimed relics behind.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| The ZIP contains source code with no DLL or PCK | Download and extract the [SurpriseRaven-0.4.3.zip player package](https://github.com/Clay666666/SurpriseRaven/releases/download/v0.4.3/SurpriseRaven-0.4.3.zip) from the release's Assets section. |
| SurpriseRaven is missing from the mod list | Look for `mods/SurpriseRaven/SurpriseRaven.json`. The DLL and PCK belong beside it. Move the folder up if extraction added another `mods` folder or ZIP-name folder. |
| Missing dependency or startup error | Check the game version and that the full RitsuLib 0.6.14 Workshop package is installed and enabled. Its manifest ID is `STS2-RitsuLib`. On upgrades, check that the old development folder was moved out. |
| The lamp didn't open | Check the relic tier and finish the current combat or reward selection in single-player. For diagnostics, search for `Hooks active` and `[Raven]` in `logs/mod_log.txt` under the game's user data directory. The engine log is `logs/godot.log` in the same location. |

## Development and credits

Building the mod requires a local 0.111.0 game installation and media inputs. The player ZIP only needs the supported game and Workshop dependency described above. The source retains the scaffold's MIT license. Third-party media is distributed inside the PCK as confirmed by the project maintainer. [Source build guide (Chinese)](docs/source-handoff.md) · [Code license](LICENSE) · [Media and third-party notices](THIRD_PARTY_NOTICES.md)
