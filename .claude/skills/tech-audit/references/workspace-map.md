# Workspace Map

> Overwritten by Phase 1 of each tech-audit run — a snapshot, not history. Generated 2026-09-25 @ 6aa3d39.

## Units (8)

| # | Unit | Path | Classification | TFM | Flags |
| - | ---- | ---- | -------------- | --- | ----- |
| 1 | Street Rod AC | `Street Rod AC/Street Rod AC.csproj` | WPF desktop app | net10.0-windows | Nullable, WPF + WinForms, x64, AllowUnsafeBlocks |
| 2 | StreetRodAC.Tests | `tests/StreetRodAC.Tests/StreetRodAC.Tests.csproj` | Test project (xUnit 2.9.3) | net10.0-windows | Nullable, WPF, ProjectReference → game |
| 3 | EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable |
| 4 | SlrrPartsConverter | `tools/SlrrPartsConverter/SlrrPartsConverter.csproj` | CLI tool | net10.0 | Nullable, x64 |
| 5 | sr_race | `apps/new-modes/sr_race` (`manifest.ini`, `mode.lua`, `siren.wav`) | CSP new mode | — | installed by `SrRaceMode` |
| 6 | convert-parts.ps1 | `tools/convert-parts.ps1` | Build/ops script | — | drives SlrrPartsConverter |
| 7 | make_siren.py | `tools/siren/make_siren.py` | Build/ops script (Python) | — | generates `siren.wav` |
| 8 | sr_race_harness | `tools/sr_race_harness/test_chase.py` | Test harness (Python) | — | exercises the chase logic of `mode.lua` offline |

`apps/lua` and `apps/python` do not exist (the Python app was removed 2026-09-24).

## Packages

| Package | Game | Tests | EngineBench | Converter | Latest (2026-09-25) |
| ------- | ---- | ----- | ----------- | --------- | ------------------- |
| LiteDB | 5.0.21 | (via game) | — | — | 5.0.21 |
| Newtonsoft.Json | 13.0.3 | (via game) | 13.0.3 | 13.0.3 | 13.0.4 |
| Serilog | 4.3.0 | (via game) | — | — | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 | (via game) | — | — | 7.0.0 |
| Vortice.Direct3D9 | 3.8.3 | (via game) | — | — | 3.8.3 |
| JetBrains.Annotations | 2023.3.0 | — | — | 2023.3.0 | 2026.2.0 |
| Microsoft.NET.Test.Sdk | — | 18.10.1 | — | — | 18.10.1 |
| xunit | — | 2.9.3 (deprecated → xunit.v3) | — | — | — |
| xunit.runner.visualstudio | — | 3.1.5 | — | — | 4.0.0 |

Vulnerable: 0 · Deprecated: 1 (xunit). Sources: `nuget.config` clears and lists nuget.org only.

## Local DLL references (HintPath, unversioned)

| DLL | Referenced by | Path | Exists |
| --- | ------------- | ---- | ------ |
| AcTools.dll | game, converter | `..\actools\Output\x64\Debug\` (sibling checkout) | yes |
| AcTools.Render.dll | game | `..\actools\Output\x64\Debug\` | yes |
| SlimDX.dll | game | `..\actools\Libraries\SlimDX-x64\` | yes |

Native at runtime: FMOD Studio/Core (from the AC install via `NativeLibrary.SetDllImportResolver`, `LibraryImport`), d3d9, user32, kernel32.

## Linked source (audited under the game)

- **EngineBench** links `Parts/{PartDefinition,EngineBuild,PartsCatalog,SlotShifts,PartKinds}.cs`, `Parts/Cars/*.cs` (minus `CarCondition`, `RepairShop`), `Parts/{Scripting,Logic,Export,Sounds}/*.cs`, `Models/Race/RaceStartState.cs`, `Models/GameState/PartInstance.cs`, `Helpers/{SafeFile,PathNames}.cs` — **does not build at 6aa3d39**: `Parts/Cars/EngineTuner.cs` uses `GameRules`, which is not linked
- **SlrrPartsConverter** links `Parts/{PartDefinition,EngineBuild,SlotShifts,Kn5Bitmap}.cs`, `Parts/Scripting/*.cs`, `Helpers/{SafeFile,PathNames}.cs`

## Game project layout (302 .cs, 32 .xaml)

Services 99 (17.0k lines) · Parts 52 (7.6k) · Screens 48 (7.7k) · Models 46 (3.8k) · Controls 7 (3.7k) · Audio 7 (2.4k) · Dialogs 18 · Converters 6 · Helpers 4 · ViewModels 4 · Navigation 3 · Logging 3 · Components 1 · Configuration 1 · window code-behind. Tests: 20 .cs.

## Signals

| Signal | Game | EngineBench | Converter | Lua |
| ------ | ---- | ----------- | --------- | --- |
| writes_ac_install | yes (`CarDataOverlay`, `AcConfigBackup`, `IniModificationService`, `SrRaceMode`) | writes to a given output folder | writes `Assets\Parts` | writes `Documents\Assetto Corsa\out` |
| parses_untrusted | AC car folders, ACD, KN5 (AcTools), pack.json, sound banks, result JSON | pack.json | SCX/RPK/cfg, script bytecode | — |
| runs_script_vm | yes | yes | yes | — |
| native_interop | FMOD (`LibraryImport`), D3D9 (Vortice, SlimDX, AcTools.Render), user32, kernel32 | — | — | — |
| unsafe_code | yes (`FmodPlugins`, `FmodStudio`) | — | — | — |
| network | none | none | none | none |
| db | LiteDB (`SaveDatabase`) | — | — | — |
| process_launch | `acs.exe` (`AssettoCorsaLauncher`) | — | — | — |

## Repo metadata

- No `.github/workflows`, no `Directory.Build.props` / `Directory.Packages.props`. One test project (added 2026-09-24).
- Two remotes: `github` and `gitlab`.
- Architecture rules: `docs/CLAUDE.md`, `docs/architecture/*`, `docs/systems/*`, `docs/ac-integration/*`.
