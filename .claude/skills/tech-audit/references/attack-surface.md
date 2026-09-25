# Trust-Surface Inventory

> Overwritten by Phase 3 of each security/all run. Generated 2026-09-25 @ 6aa3d39 (diff window 965c642..6aa3d39 touched almost every source file, so this is a full re-inventory). Paths relative to repo root; game paths under `Street Rod AC/`. Previous snapshot: `git show 965c642:.claude/skills/tech-audit/references/attack-surface.md`.

## 1. Writes outside the app's own folders

| Target | Site | Restore |
| ------ | ---- | ------- |
| AC car `data/` folder (unpacked from `data.acd`), keep folder + manifest | `Services/Assetto Corsa/CarDataOverlay.cs:206-270` (keep copy :232, data write :270, ACD unpack :690), manifest `SafeFile` :480 | restore from manifest :431-462; start-up sweep reads manifest :357 |
| AC car sound bank + GUIDs swap | `CarDataOverlay.cs:282-310` (`File.Move` original to keep) | `CarDataOverlay.cs:494-501` |
| Same-model clone folders in `content\cars` | `CarDataOverlay.cs:554-598` (marker file first, then copy) | `CarDataOverlay.cs:627-632` deletes marked clones only |
| AC `cfg\*.ini` (race, assists, …) | `Services/Configuration/IniModificationService.cs:151,278,418` via `SafeFile` | `Services/Configuration/AcConfigBackup.cs` keep folder + manifest (:89-216), restore :176-182 |
| CSP new mode install `apps\lua\…\sr_race` in the AC install | `Services/Assetto Corsa/SrRaceMode.cs:51-92` (`SafeFile`, stale files deleted) | reinstalled on each launch; not restored (app-owned folder) |
| Race result archive / quarantine in `Documents\Assetto Corsa\out` | `Services/Race/RaceResultIngestionService.cs:464-715` | n/a (app-owned subfolders) |
| Saves `saves\*.db` + rotated backups | `Services/Storage/SaveDatabase.cs:39-204`, `GameStateRepository.cs:100-205` | n/a |
| Settings JSON | `Services/Settings/GameSettingsService.cs:49,129` (`SafeFile`) | n/a |
| Loudness cache | `Audio/EngineLoudness.cs:209` (plain `File.WriteAllText`) | n/a (cache) |
| Slot shifts (dev tool data) | `Parts/SlotShifts.cs:139-144` | n/a |

## 2. Untrusted-input parsers

| Input | Reader |
| ----- | ------ |
| `data.acd` | `Parts/Export/AcdFile.cs` |
| AC `ui_car.json` / `ui_track.json`, track configs | `Services/Assetto Corsa/AssettoCorsaContentService.cs:177,249,274` (System.Text.Json), `Services/Catalog/*` |
| KN5 / car models | AcTools / AcTools.Render (`Controls/CarViewport3D.cs`, `D3DViewportBase.cs`) |
| `pack.json`, `part_aliases.json`, engine builds | `Parts/PartsCatalog.cs:73,104,126` (Newtonsoft, default settings) |
| Sound overrides / facts / banks | `Parts/Sounds/SoundLibrary.cs:139,174` |
| Loudness cache | `Audio/EngineLoudness.cs:185` |
| SLRR compiled script bytecode | `Parts/Scripting/*` (VM), `Parts/Logic/*` |
| SLRR SCX/RPK/cfg | `tools/SlrrPartsConverter/Slrr/*` |
| Lua result JSON | `Services/Race/Validation/RaceResultValidator.cs:59`, `RaceResultIngestionService.cs` |
| Overlay / INI-backup manifests | `CarDataOverlay.cs:357`, `AcConfigBackup.cs:199` |
| Settings | `Services/Settings/GameSettingsService.cs:91` |
| Slot shifts | `Parts/SlotShifts.cs:45` |
| LiteDB saves | `Services/Storage/SaveDatabase.cs`, `GameStateRepository.cs`, `Services/Race/RaceSessionRepository.cs` |

No `TypeNameHandling`, `BinaryFormatter`, `XmlSerializer` or custom `BsonMapper` anywhere.

## 3. Paths built from content

Car ids / clone ids → `Path.Combine(_carsPath, id)` in `CarDataOverlay.cs`; ACD entry names → `CarDataOverlay.cs:690`; part/pack folders in `PartsCatalog.cs`; sound bank folders in `SoundLibrary.cs`; converter output folders from SLRR names (`Helpers/PathNames.cs` sanitizer, linked into the converter).

## 4. Process launches

| Site | Target | Args source | UseShellExecute |
| ---- | ------ | ----------- | --------------- |
| `Services/Assetto Corsa/AssettoCorsaLauncher.cs:474` | `acs.exe` from the settings path | none from content | see finding file |

(The `ShowroomOverlay` launch of `acShowroom.exe` is gone.)

## 5. Native interop

- FMOD Studio/Core: `LibraryImport` in `Audio/FmodStudio.cs` (resolver :94, `unsafe` :197-220); DSP plugins in `Audio/FmodPlugins.cs` (`unsafe` class)
- D3D9 shared texture: `Controls/SharedTextureBridge.cs` (`LibraryImport` user32), Vortice.Direct3D9, AcTools.Render/SlimDX
- Win32: `CarDataOverlay.cs:708` (kernel32 `DllImport`)

## 6. Network

None (`HttpClient`/`WebClient` absent). Planned local LLM integration (`docs/ai-integration.md`) will activate the Network rows.

## 7. Cross-unit trust boundaries

- Race mode (`apps/new-modes/sr_race`) → `Documents\Assetto Corsa\out\…\*.json` → `RaceResultIngestionService` / `RaceResultValidator` → saves
- Game → `SrRaceMode` installs the mode into the AC install before each race
- Converter → `Street Rod AC\Assets\Parts\**` (pack.json, KN5, aliases, builds) → `PartsCatalog` at runtime; EngineBench reads the same
- Game ↔ AC process: `CarDataOverlay` + `AcConfigBackup`/INI changes applied before launch, restored after `acs.exe` exits and at start-up
