# Trust-Surface Inventory

> Overwritten by Phase 3 of each security/all run. Generated 2026-09-24 @ 965c642 (full sweep). Paths relative to repo root; game paths under `Street Rod AC/`.

## 1. Writes outside the app's own folders

| Target | Site | Restore |
| ------ | ---- | ------- |
| AC car `data/` folder (unpacked from `data.acd`), keep folder + manifest | `Services/Assetto Corsa/CarDataOverlay.cs:120-170` (keep copy, manifest `File.WriteAllText` :161, data write :170, ACD unpack :457) | `CarDataOverlay.cs:247-270` restore from manifest; start-up sweep of keep folders :92 |
| AC car sound bank + GUIDs swap | `CarDataOverlay.cs:189-209` (`File.Move` original to keep) | `CarDataOverlay.cs:287-294` |
| Same-model clone folders in `content\cars` | `CarDataOverlay.cs:345-388` (marker file first, then copy) | `CarDataOverlay.cs:405-426` delete marked clones only |
| AC `cfg\race.ini` / `assists.ini` | `Services/Configuration/IniModificationService.cs:161,244` (full rewrite), `Parsers/IniWriter.cs:56,129,151` (temp → copy, backup) | see IniModificationService restore (reviewed in findings) |
| Race result archive / quarantine in `Documents\Assetto Corsa\out` | `Services/Race/RaceResultIngestionService.cs:134,154,264,272` | n/a (app-owned subfolders) |
| Saves `saves\*.db` + rotated backups | `Services/Storage/GameStateRepository.cs:87-148` | n/a |
| Settings JSON | `Services/Settings/GameSettingsService.cs:72` | n/a |
| Loudness cache | `Audio/EngineLoudness.cs:190` | n/a |
| Slot shifts (dev tool data) | `Parts/SlotShifts.cs:92-96` | n/a |

## 2. Untrusted-input parsers

| Input | Reader |
| ----- | ------ |
| `data.acd` | `Parts/Export/AcdFile.cs:38` (`new byte[size]` from header) |
| AC `ui_car.json` / `ui_track.json`, skins | `Services/Assetto Corsa/AssettoCorsaContentService.cs`, `Services/Catalog/CarImportService.cs:138,305`, `CarProfileService.cs:213,223` |
| KN5 / car models | AcTools / AcTools.Render (`Controls/CarViewport3D.cs`, `DealerLotViewport3D.cs`) |
| `pack.json`, `part_aliases.json`, engine builds | `Parts/PartsCatalog.cs:53-73` (Newtonsoft, default settings) |
| Sound overrides / facts / banks | `Parts/Sounds/SoundLibrary.cs:139,154,174,383` |
| SLRR compiled script bytecode | `Parts/Scripting/*` (VM), `Parts/Logic/PartScriptRuntime.cs` |
| SLRR SCX/RPK/cfg | `tools/SlrrPartsConverter/Slrr/*` |
| Lua result JSON | `Services/Race/Validation/RaceResultValidator.cs:57`, `RaceResultIngestionService.cs` |
| Overlay manifest | `CarDataOverlay.cs:247` |
| Settings | `Services/Settings/GameSettingsService.cs:37` |
| LiteDB saves | `Services/Storage/GameStateRepository.cs:36,63`, `Services/Race/RaceSessionRepository.cs:45-127` |

No `TypeNameHandling`, `BinaryFormatter`, `XmlSerializer` or custom `BsonMapper` anywhere.

## 3. Paths built from content

Car ids / clone ids → `Path.Combine(_carsPath, id)` in `CarDataOverlay.cs`; part/pack folders in `PartsCatalog.cs`; sound bank folders in `SoundLibrary.cs`; converter output folders from SLRR names in `tools/SlrrPartsConverter/Program.cs:692,1348-1384`.

## 4. Process launches

| Site | Target | Args source | UseShellExecute |
| ---- | ------ | ----------- | --------------- |
| `Services/Assetto Corsa/AssettoCorsaLauncher.cs:127-134` | `acs.exe` from settings path | none from content | true |
| `ShowroomOverlay.xaml.cs:76-86` | hardcoded `C:\GAMES\Street Rod AC\acShowroom.exe` | — | false |

## 5. Native interop

- FMOD Studio/Core: 23 `DllImport` in `Audio/FmodStudio.cs:90-156`, resolver :70; DSP plugins in `Audio/FmodPlugins.cs` (`unsafe`)
- D3D9 shared texture: `Controls/SharedTextureBridge.cs` (+ user32 :154), Vortice.Direct3D9, AcTools.Render/SlimDX
- Win32: `ShowroomOverlay.xaml.cs:19-25` (user32), `CarDataOverlay.cs:475` (kernel32)

## 6. Network

None (`HttpClient`/`WebClient` absent). Planned local LLM integration (`docs/ai-integration.md`) will activate the Network rows.

## 7. Cross-unit trust boundaries

- Race mode (`apps/new-modes/sr_race`) → `Documents\Assetto Corsa\out\sr_race_manager\*.json` → `RaceResultIngestionService` / `RaceResultValidator` → saves
- Converter → `Street Rod AC\Assets\Parts\**` (pack.json, KN5, aliases, builds) → `PartsCatalog` at runtime; EngineBench reads the same
- Game ↔ AC process: `CarDataOverlay` + INI changes applied before launch, restored after `acs.exe` exits
