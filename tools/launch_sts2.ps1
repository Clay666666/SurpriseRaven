[CmdletBinding()]
param()
# Tests must never launch a game GUI. This old entry point is intentionally retired.
[Console]::Error.WriteLine('GUI game launching is disabled. Use tools/verify-environment.ps1 -HeadlessGame for isolated headless verification.')
exit 1
