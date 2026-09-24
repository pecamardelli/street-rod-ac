# Workspace Map

> Overwritten by Phase 1 of each tech-audit run — a snapshot, not history. Generated 2026-09-24 @ 965c642.

## Units (6)

| # | Unit | Path | Classification | TFM | Flags |
| - | ---- | ---- | -------------- | --- | ----- |
| 1 | Street Rod AC | `Street Rod AC/Street Rod AC.csproj` | WPF desktop app | net10.0-windows | Nullable, WPF + WinForms, x64, AllowUnsafeBlocks |
| 2 | EngineBench | `tools/EngineBench/EngineBench.csproj` | CLI tool | net10.0 | Nullable |
| 3 | SlrrPartsConverter | `tools/SlrrPartsConverter/SlrrPartsConverter.csproj` | CLI tool | net10.0 | Nullable, x64 |
| 4 | sr_race_manager | `apps/lua/sr_race_manager` (`manifest.ini`, `sr_race_manager.lua`, 448 lines) | CSP Lua app | — | `LAZY = NONE` |
| 5 | StreetRodRaceApp | `apps/python/StreetRodRaceApp/StreetRodRaceApp.py` (577 lines) | AC Python app (legacy) | — | not referenced by any C# |
| 6 | convert-parts.ps1 | `tools/convert-parts.ps1` | Build/ops script | — | drives SlrrPartsConverter |

## Packages

| Package | Game | EngineBench | Converter | Latest (2026-09-24) |
| ------- | ---- | ----------- | --------- | ------------------- |
| LiteDB | 5.0.21 | — | — | 5.0.21 |
| Newtonsoft.Json | 13.0.3 | 13.0.3 | 13.0.3 | 13.0.4 |
| Serilog | 4.3.0 | — | — | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 | — | — | 7.0.0 |
| Vortice.Direct3D9 | 3.8.3 | — | — | 3.8.3 |
| JetBrains.Annotations | 2023.3.0 | — | 2023.3.0 | 2026.2.0 |

Vulnerable: 0 · Deprecated: 0. Sources: `nuget.config` clears and lists nuget.org only.

## Local DLL references (HintPath, unversioned)

| DLL | Referenced by | Path | Exists |
| --- | ------------- | ---- | ------ |
| AcTools.dll | game, converter | `..\actools\Output\x64\Debug\` (sibling checkout) | yes |
| AcTools.Render.dll | game | `..\actools\Output\x64\Debug\` | yes |
| SlimDX.dll | game | `..\actools\Libraries\SlimDX-x64\` | yes |

Native at runtime: FMOD Studio/Core (loaded from the AC install via `NativeLibrary.SetDllImportResolver`), d3d9, user32, kernel32.

## Linked source (audited under the game)

- **EngineBench** links `Parts/{PartDefinition,EngineBuild,PartsCatalog,SlotShifts,PartKinds}.cs`, `Parts/{Cars,Scripting,Logic,Export,Sounds}/*.cs`, `Models/GameState/PartInstance.cs`
- **SlrrPartsConverter** links `Parts/{PartDefinition,EngineBuild,SlotShifts}.cs`, `Parts/Scripting/*.cs`

## Game project layout (262 .cs, 32 .xaml)

Services 88 (11.9k lines) · Parts 44 (5.9k) · Screens 41 (6.2k) · Models 41 (3.2k) · Dialogs 9 · Controls 7 (3.7k) · Audio 6 (1.9k) · Converters 5 · ViewModels 4 · Navigation 3 · Logging 3 · Views 2 · Helpers 2 · Components 1 · Configuration 1 · window code-behind 4.

## Signals

| Signal | Game | EngineBench | Converter | Lua |
| ------ | ---- | ----------- | --------- | --- |
| writes_ac_install | yes (`CarDataOverlay`, `IniModificationService`) | writes to a given output folder | writes `Assets\Parts` | writes `Documents\Assetto Corsa\out` |
| parses_untrusted | AC car folders, ACD, KN5 (AcTools), pack.json, sound banks, result JSON | pack.json | SCX/RPK/cfg, script bytecode | — |
| runs_script_vm | yes | yes | yes | — |
| native_interop | FMOD, D3D9 (Vortice, SlimDX, AcTools.Render), user32, kernel32 | — | — | — |
| unsafe_code | yes (`FmodPlugins`) | — | — | — |
| network | none | none | none | none |
| db | LiteDB | — | — | — |
| process_launch | `acs.exe` (launcher), `acShowroom.exe` (overlay) | — | — | — |

## Repo metadata

- No `.github/workflows`, no `Directory.Build.props` / `Directory.Packages.props`, no test projects.
- `.gitignore` covers bin/obj/.vs/.aider*; no `.db`, keys, or secrets tracked.
- Architecture rules: `docs/CLAUDE.md`, `docs/architecture/*`, `docs/systems/*`, `docs/ac-integration/*`.
