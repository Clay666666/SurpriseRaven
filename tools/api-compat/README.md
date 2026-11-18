# Offline API compatibility check

The checker reads PE metadata with the SDK's `System.Reflection.Metadata`. It never loads or executes inspected assemblies, starts a game, or copies dependencies to its output. It requires only the pinned local .NET 10 SDK and has no NuGet dependencies.

```powershell
tools\dotnet\dotnet.exe build tools\api-compat\ApiCompat.csproj -c Release
tools\dotnet\dotnet.exe tools\api-compat\bin\Release\net10.0\ApiCompat.dll `
  --mod '<verified payload>\SurpriseRaven.dll' `
  --reference '<111 or 113 game data>\sts2.dll' `
  --reference '<RitsuLib>\compat\<0.111.0 or 0.113.0>\STS2-RitsuLib.dll' `
  --reference '<RitsuLib>\compat\<0.111.0 or 0.113.0>\STS2-RitsuLib.Runtime.dll' `
  --reference '<RitsuLib>\shared\STS2-RitsuLib.Shared.dll' `
  --reference '<RitsuLib>\shared\STS2-RitsuLib.Ui.dll' `
  --reference '<RitsuLib>\shared\STS2-RitsuLib.Settings.dll'
```

Run once per target against the **same Mod DLL**. Default scopes are `sts2` and `STS2-RitsuLib*`; repeated `--scope` arguments replace those defaults. JSON output records the checked DLL and dependency hashes, assembly versions, checked counts, and missing types/members. Exit codes: `0` compatible metadata, `1` missing type/member, `2` invalid input or unreadable metadata. Missing scoped dependencies fail instead of silently being skipped; duplicate assembly names from different paths and checks matching no references are rejected.

Checks include return/parameter types, generic arity, calling convention, fields, inherited methods and assembly type forwarders. This is a static binary contract check, not runtime acceptance: Harmony targets, native resources, code behavior and reflection-only calls still require the separately authorized isolated headless checks. No game or dependency DLL belongs in Git or the tool output.

Run fixture tests (only tiny standalone managed libraries are compiled, never executed):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\api-compat\Test-ApiCompat.ps1
```

The fixtures cover matching/generic/inherited members, changed parameter and field types, a newly added optional argument, removed types, working forwarders and missing dependencies. Reports remain under `artifacts/tests/`.

## Legacy shuffle vectors

`python tools/api-compat/legacy-shuffle-vectors.py` regenerates the constants in `RavenHeadlessShuffleTestsHooks`. It independently transcribes the old generator and shuffle algorithm inspected with ILSpy from game 0.111.0 (`sts2.dll` SHA256 `0861bfa1df347538d932f22d580e75420f08082792eb914e53b4882764acdbe9`). It does not import the Mod implementation or execute a game assembly. The headless test runs the actual host RNG against these fixed permutations, next values and JSON-restored continuation vectors for empty, single-item and multi-item inputs.
