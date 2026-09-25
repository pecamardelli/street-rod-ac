# Builds the whole solution and runs the tests: the check to pass before a merge or a push. The tools compile game
# files by link, so a game change can break them without the game noticing (EngineBench did not build from PR #20 to
# the 2026-09-25 audit). Nothing runs this automatically yet: CI would need the actools DLLs, which are not in the repo.
#
# Exit code: 0 when everything builds and every test passes, 1 otherwise.
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot ".."

dotnet build (Join-Path $root "Street Rod AC.slnx") -c $Configuration -v q -clp:NoSummary
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: the solution does not build")
    exit 1
}

dotnet test --project (Join-Path $root "tests\StreetRodAC.Tests\StreetRodAC.Tests.csproj") -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("check: tests failed")
    exit 1
}

Write-Host "check: build and tests pass"
exit 0
