# Audit Unit Classifiers

Used by Phase 1 of the tech-audit skill to classify each discovered unit by **signature**, not by directory name. Add new rules here as the repo grows.

## Classification rules (first match wins)

| #   | Signature (must hold)                                                         | Classification            | Notes                                                                 |
| --- | ----------------------------------------------------------------------------- | ------------------------- | --------------------------------------------------------------------- |
| 1   | `.csproj` with `OutputType=WinExe` AND `UseWPF=true`                           | **WPF desktop app**       | The game. Split into slices for Phase 4                               |
| 2   | `.csproj` with a test framework reference (`xunit`, `NUnit`, `MSTest.*`)       | **Test project**          | None today                                                            |
| 3   | `.csproj` with `OutputType=Exe`                                                | **CLI tool**              | Note linked `<Compile Include>` items — they belong to the game slice |
| 4   | `.csproj` with no `OutputType` (library)                                       | **Library**               | None today                                                            |
| 5   | folder with `manifest.ini` + `*.lua`                                          | **CSP Lua app**           | Runs inside AC via Custom Shaders Patch                               |
| 6   | folder under `apps/python` with a `*.py` AC app                               | **AC Python app**         | Legacy — superseded by the Lua app; removed 2026-09-24, no unit exists |
| 7   | `*.ps1` under `tools/`                                                        | **Build/ops script**      |                                                                       |
| 8   | (none of the above)                                                           | **Unknown**               | Manual classification needed; flag in findings                        |

AC-side components that live in the AC install (the `sr_race` new-mode, FFB limiter under `C:\GAMES\Street Rod AC\extension\...`) are **out of scope** — they are not in the repo. List them under Coverage gaps if the diff window touches the code that configures them.

## What to capture per unit

```yaml
- name: 'Street Rod AC'
  path: 'Street Rod AC/Street Rod AC.csproj'
  classification: WPF desktop app
  target_framework: 'net10.0-windows'
  flags: { nullable: true, unsafe: true, wpf: true, winforms: true, platform: x64 }
  packages: [LiteDB@5.0.21, Newtonsoft.Json@13.0.3, ...]
  local_references:           # HintPath DLLs
    - { name: AcTools, path: '..\..\actools\Output\x64\Debug\AcTools.dll', exists: true }
  linked_sources: []          # for tools: game files compiled in by link
  source_files: { cs: 262, xaml: 32 }
  signals:
    writes_ac_install: true
    parses_untrusted_binary: true
    runs_script_vm: true
    native_interop: [fmod, d3d9]
    unsafe_code: true
    network: false
    db: litedb
    process_launch: true
```

These signals drive Phase 3 (trust-surface inventory) and Phase 4 (subagent worklists).

## Signal detection heuristics

- **writes_ac_install:** `CarDataOverlay`, `IniModificationService`, writes under `content\cars` or AC `cfg`
- **parses_untrusted_binary:** `BinaryReader` over files from AC content or SLRR packs; AcTools `Kn5`/`DataWrapper` use
- **runs_script_vm:** `Parts/Scripting` referenced (the game and both tools link it)
- **native_interop:** `DllImport`/`LibraryImport` (FMOD in `Audio/`), `Vortice.Direct3D9`, SlimDX, `AcTools.Render`, `D3DImage`
- **unsafe_code:** `AllowUnsafeBlocks=true` and actual `unsafe` blocks
- **network:** `HttpClient`, `WebClient`, sockets (none today; planned LLM integration)
- **db:** `LiteDB`
- **process_launch:** `Process.Start` (AC launcher)
- **lua_results:** the CSP app writes JSON the game reads — a cross-unit trust boundary

When a signal is present, the corresponding rows of `security-checklist.md` activate for that unit.

## When the rules miss

If `Unknown` appears for a unit the user clearly cares about, ask once at the start of the next interactive run (not during autonomous runs). Better: add a rule here based on what the unit actually does.
