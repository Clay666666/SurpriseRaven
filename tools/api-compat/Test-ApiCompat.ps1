[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnet = Join-Path $projectRoot 'tools\dotnet\dotnet.exe'
$toolProject = Join-Path $PSScriptRoot 'ApiCompat.csproj'
$toolDll = Join-Path $PSScriptRoot 'bin\Release\net10.0\ApiCompat.dll'
$testRoot = Join-Path $projectRoot ('artifacts\tests\api-compat-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $testRoot 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>', $utf8)

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Build-Fixture([string]$Name, [string]$Assembly, [string]$Source, [string]$Dependency = '') {
    $directory = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $reference = ''
    if ($Dependency) {
        $escaped = [Security.SecurityElement]::Escape($Dependency)
        $reference = "<ItemGroup><Reference Include=`"FixtureDependency`"><HintPath>$escaped</HintPath><Private>false</Private></Reference></ItemGroup>"
    }
    $project = "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>$Assembly</AssemblyName></PropertyGroup>$reference</Project>"
    [IO.File]::WriteAllText((Join-Path $directory 'Fixture.csproj'), $project, $utf8)
    [IO.File]::WriteAllText((Join-Path $directory 'Fixture.cs'), $Source, $utf8)
    & $dotnet build (Join-Path $directory 'Fixture.csproj') -c Release --nologo *> (Join-Path $directory 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Fixture build failed: $directory\build.log" }
    return Join-Path $directory "bin\Release\net10.0\$Assembly.dll"
}

function Invoke-Check([string]$Name, [string]$Mod, [string[]]$References, [string]$Scope = 'CompatFixture*') {
    $arguments = @($toolDll, '--mod', $Mod, '--scope', $Scope)
    foreach ($reference in $References) { $arguments += @('--reference', $reference) }
    $report = Join-Path $testRoot "$Name.json"
    & $dotnet @arguments > $report
    $exitCode = $LASTEXITCODE
    return [pscustomobject]@{ ExitCode = $exitCode; Report = (Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json) }
}

& $dotnet build $toolProject -c Release --nologo *> (Join-Path $testRoot 'tool-build.log')
if ($LASTEXITCODE -ne 0) { throw "Tool build failed: $testRoot\tool-build.log" }
$apiSource = @'
namespace Fixture {
    public class Parent { public long Field; public string Echo(int value) => ""; public int Optional(int value, int extra = 0) => value; public Parent Self() => this; }
    public class Child : Parent { }
    public class Bag<T> { public T Echo(T value) => value; }
    public class Gone { }
}
'@
$original = Build-Fixture 'original' 'CompatFixture' $apiSource
$mod = Build-Fixture 'consumer' 'Consumer' @'
using Fixture;
public class Consumer {
    public string Run(Child api, Bag<int> bag, Gone unused) { api.Field = bag.Echo(2); return api.Self().Echo(api.Optional(3)); }
}
'@ $original
$pass = Invoke-Check 'original' $mod @($original)
Assert-True ($pass.ExitCode -eq 0 -and $pass.Report.status -eq 'passed' -and $pass.Report.checked_members -ge 5) 'Original API/generic/inherited member check failed.'

$broken = Build-Fixture 'broken' 'CompatFixture' @'
namespace Fixture {
    public class Parent { public int Field; public string Echo(long value) => ""; public int Optional(int value, int extra = 0, long newArgument = 0) => value; public Parent Self() => this; }
    public class Child : Parent { }
    public class Bag<T> { public T Echo(T value) => value; }
}
'@
$failure = Invoke-Check 'changed-signatures' $mod @($broken)
Assert-True ($failure.ExitCode -eq 1 -and $failure.Report.status -eq 'failed') 'Changed API incorrectly passed.'
foreach ($member in @('Field', 'Echo', 'Optional')) {
    Assert-True (@($failure.Report.failures | Where-Object { $_.kind -eq 'missing_member' -and $_.member -eq $member }).Count -ge 1) "Changed $member signature was not detected."
}
Assert-True (@($failure.Report.failures | Where-Object { $_.kind -eq 'missing_type' -and $_.type -eq 'Fixture.Gone' }).Count -eq 1) 'Removed type was not detected.'

$core = Build-Fixture 'core' 'CompatFixture.Core' $apiSource
$forwarder = Build-Fixture 'forwarder' 'CompatFixture' @'
using System.Runtime.CompilerServices;
[assembly: TypeForwardedTo(typeof(Fixture.Parent))]
[assembly: TypeForwardedTo(typeof(Fixture.Child))]
[assembly: TypeForwardedTo(typeof(Fixture.Bag<>))]
[assembly: TypeForwardedTo(typeof(Fixture.Gone))]
'@ $core
$forwarded = Invoke-Check 'forwarded' $mod @($forwarder, $core)
Assert-True ($forwarded.ExitCode -eq 0) 'Valid type forwarding or forwarded signatures were rejected.'
$missing = Invoke-Check 'missing-runtime' $mod @($forwarder)
Assert-True ($missing.ExitCode -eq 1 -and @($missing.Report.failures).Count -gt 0) 'Missing forwarded runtime silently passed.'
$absent = Invoke-Check 'missing-library' $mod @($core)
Assert-True ($absent.ExitCode -eq 1 -and @($absent.Report.failures).Count -gt 0) 'Missing dependency silently passed.'

Write-Output "API compatibility fixtures passed. Reports preserved in $testRoot"
