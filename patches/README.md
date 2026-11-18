# Game v0.111.0 / v0.113.1 compatibility patches

The installer locks each helper repository to its upstream commit. `tools/build-mod.ps1 -IncludeControlMods` applies these patches, or recognizes them as already applied with `git apply --reverse --check`. It never resets unrelated local work.

- STS2MCP base: `55e064850a68f3b4cde7e5fd525bf9b2dec4e885`.
- STS2MenuControl base: `a37f56786ecd2fcbcb8de391cf18142de00ee292`.
- ILSpy-Mcp base: `58b01c118dd08e85e8205c8b3a49d63e68282ef3`.

Verified using the local ILSpy MCP against `sts2.dll` from game v0.111.0 (Steam build 24724944):

- `StartRunLobby.MaxPlayers` is no longer public. The verified backing field is `private readonly int _maxPlayers`; use each helper's existing reflection approach to expose the actual value.
- `LoadRunLobby.ConnectedPlayerIds` is replaced by `PlayerIds` (`IEnumerable<ulong>`). `PlayerCount` returns the current lobby player count.

Local inspection outputs are under `artifacts/inspection/`; decompiled game code is not tracked or redistributed. Re-check these APIs when changing the supported game version. Current runtime acceptance covers startup and main-menu navigation; multiplayer behavior has not been playtested.

## Dual-version builds

The original `001` patches remain applicable to both supported games. New `002` patches are applied after them, without switching or reversing patches when the game target changes.

- `STS2MCP/002-game-v0.113.patch` adds a small compile-time merchant adapter. `Sts2ApiTarget=0.111.0` uses `MerchantRoom.GetLocalInventory()` and `MerchantEntry.OnTryPurchaseWrapper(MerchantInventory, bool = false)`. `Sts2ApiTarget=0.113.0` uses `RunManager.Instance.MerchantSynchronizer.GetLocalInventory()` and `PurchaseLocalEntry(MerchantEntry)`, preserving the new game's synchronized purchase path. Both purchase methods return `Task<bool>`.
- `STS2MenuControl/002-dual-game-build.patch` accepts the versioned game-reference cache and isolated intermediate output; no menu behavior or HTTP schema changes.
- Both project patches retain `obj/` and `bin/` exclusions when the build script supplies an external `BaseIntermediateOutputPath`. Unknown API targets fail compilation. Direct helper builds default to the 111 API; daily builds supply the target explicitly.

These APIs were inspected with ILSpy in the local game v0.113.1 (commit `d09d48e5`, Steam build 26660232). The 111 methods were separately inspected in the retained v0.111.0 assembly. Build targets map only release 0.111.0 to API 0.111.0 and release 0.113.1 to API 0.113.0.

Run `tests/control-mod-compat.Tests.ps1 -GameDir <113 directory> -BaselineGameDir <111 directory>` for fresh local clones at the locked upstream commits, ordered patch replay, already-applied checks, and both target builds. Existing tool clones and their unrelated changes are preserved. New verification covers compilation and static/offline checks only: control Mods are not copied into the headless mirror, and no menu/game-state actions are called. Earlier menu-navigation results above are historical and do not constitute 113 runtime acceptance.

The ILSpy patch declares accurate read-only, non-destructive, idempotent, closed-world annotations for its eight assembly-inspection tools. They read PE files and return text; they do not execute the target assembly. This fixes read-only tool classification without changing Codex approval or sandbox settings. A fresh Codex process successfully called `decompile_method` under the original read-only sandbox and `never` approval policy.

## Game-state tools and approval

HTTP GET does not imply a read-only tool. In the pinned STS2MCP source, `get_game_state` and `mp_get_game_state` can automatically open the merchant inventory, click the fake merchant, or open a treasure chest while building the response. Chest clicks can also trigger gameplay effects such as gold rewards. These tools therefore retain their upstream annotations and client approval behavior; this patch does not label them read-only or relax approval settings. The completed main-menu checks do not establish read-only behavior in other rooms.
