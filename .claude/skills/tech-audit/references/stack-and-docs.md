# Section Focus Areas

> Companion file to `SKILL.md`. Lists what each non-security audit section pays attention to.
> Unit discovery, classification, and doc-URL resolution live in `workspace-classifiers.md` and `doc-sources.md`. The `security` section uses `security-checklist.md`.
> Phase 1 picks the relevant rows for each unit's classification (a CLI tool skips the WPF rows, etc.).

## CLEAN CODE

### Project rules (from `docs/CLAUDE.md` — flag violations only)

- Navigation only through `NavigationService` typed factory methods; screens never instantiate other screens
- Dialogs are modal overlays via `DialogService`, one at a time, never nested
- Logging through `IAppLogger` with a category — no `Console.WriteLine`, `Debug.WriteLine` or bare `Serilog.Log` in app code
- `Opponent` AI parameters converted at runtime by `OpponentAIAdapter`, never persisted
- Catalog (`catalog.db`) vs saves (`saves\*.db`) separation respected — static data not written to saves and vice versa

### WPF / MVVM

- Code-behind limited to view concerns; logic in ViewModels/services
- `INotifyPropertyChanged` raised consistently; no stale bindings from properties that never notify
- `async void` only on event handlers, with a try/catch inside; commands that run async work surface errors
- Services constructed once and injected; no hidden `new SomeService()` in ViewModels
- Event subscriptions to long-lived objects (services, scheduler, static events) unsubscribed on screen exit — classic WPF leak
- `IDisposable` owners (renderers, audio, LiteDB, timers) disposed on screen/app exit

### C# / .NET 10

- Nullable annotations honoured (CS86xx counts from Phase 0b); `!` suppression hiding a real null path
- Obsolete API usage (CS0618 counts)
- Exceptions: no swallowed `catch { }` without logging, except where the comment says why
- Duplication is its own section now (`duplication`, below) — `clean-code` doesn't repeat it
- Files shared with the tools by `<Compile Include>` stay free of WPF dependencies (the tools are `net10.0`, not `-windows`)
- Large classes / methods where a clear split exists (report only the worst offenders with line counts)

### Lua / scripts

- CSP Lua app: globals vs locals, per-frame allocations in `script.update`, dead code left from the Python app port

## PATTERN DEVIATIONS

See [`patterns.md`](./patterns.md) — census of recurring concerns, norm per concern, outliers.

## STABILITY

See [`stability-checklist.md`](./stability-checklist.md) — crash paths, hangs, leaks, state integrity, environment differences.

## DUPLICATION

### Detection (deterministic first)

Run the clone finder from the repo root — two passes:

```bash
python .claude/skills/tech-audit/scripts/find_duplicates.py --min-tokens 80 --json "$WORK/dup-exact.json"
python .claude/skills/tech-audit/scripts/find_duplicates.py --min-tokens 120 --rename --json "$WORK/dup-renamed.json"
```

- Pass 1 (literals normalized): copy-paste with changed constants.
- Pass 2 (identifiers normalized too): same code shape with renamed variables — the copy-paste-then-edit families. Noisier; confirm each by reading both sides.
- Diff mode still scans the whole repo (a new file can duplicate an old one), but report only clones where at least one side is in a changed file.

The script can't see **semantic duplication** — the same job done with different code. A reviewer covers that by concern, not by text:

- More than one parser/reader for the same format (INI, KN5, `pack.json`, AC `data` folder, SLRR cfg) — between the game and the tools too
- More than one place computing the same number (car value, part price, engine output, condition → price) that could drift apart
- Parallel class pairs that grew by copying (e.g. two 3D viewports, two base view-models, two dealer/garage flows)
- Hand-rolled helpers that duplicate a BCL or existing project helper (path sanitizing, clamping, INI access, file copy with backup)

### Triage

Not every clone deserves a refactor. For each cluster say:

- **Extract**: the copies must change together (logic, parsing, math, restore paths) — divergence would be a bug. Name the target (shared base class, helper, service) and where it lives.
- **Leave**: the similarity is incidental or the copies are expected to diverge (per-screen layout code, generated-looking tables), or extracting would cross a boundary on purpose (the converter re-implements something so it doesn't depend on WPF). Say why in one line.
- **Already drifting**: the copies differ in a way that looks accidental — one got a fix the other didn't. These are **BUG** candidates; show the differing lines.

### Output

Findings go under **DUPLICATION**, one entry per clone cluster (not per pair): the files:lines of every copy, token size, verdict (Extract / Leave / Drifting), and the extraction target. Include the script's summary line (files scanned, pairs, duplicated lines) for both passes so runs can be compared.

## XAML

(Replaces the web skill's CSS section.)

- Colours, brushes, fonts and sizes come from the `Styles/` ResourceDictionaries — flag hardcoded hex colours and font sizes in screens/dialogs (see `docs/architecture/styling.md`)
- Duplicated styles/templates across screens that should move to a shared dictionary
- `StaticResource` vs `DynamicResource`: `DynamicResource` only where the value actually changes at runtime
- Binding errors: bindings to properties that don't exist on the DataContext (cross-check ViewModel names; mention running with binding trace output if suspected)
- Missing `DataTemplate` registrations in `App.xaml` for screens/dialogs (see `docs/CLAUDE.md` → Quick Reference)
- Unused resources, styles and image assets (`Assets\Images\**` referenced nowhere)
- Layout at different window sizes / DPI: fixed pixel sizes where the screen is meant to scale
- Images: `DecodePixelWidth` on large backgrounds; frozen brushes/geometries in resources

Key files: `Street Rod AC/Styles/*.xaml`, `Street Rod AC/App.xaml`, `Street Rod AC/Screens/**/*.xaml`, `Street Rod AC/Dialogs/**/*.xaml`

## SECURITY

Moved to [`security-checklist.md`](./security-checklist.md).

## TEST COVERAGE

### Discovery & mapping

- Locate test projects (`*Tests.csproj`, xUnit/NUnit/MSTest references) — **there are none today**. The report should say so plainly and propose where tests pay off most, not list every untested file.
- Existing regression harness: `tools/EngineBench` drives the real part logic from the command line — note what it covers (engine curves, rated checks) and what it could cover cheaply.

### Prioritization (risk-ranked)

1. `CarDataOverlay` apply/restore and clone cleanup — filesystem tests against a temp "AC install"
2. `Parts/Scripting` VM — opcode semantics, bounds, budget; golden tests from real part scripts
3. Engine model / dyno (`EngineDyno`, `AcEngineData`) — golden curves already producible by EngineBench
4. `IniModificationService` round-trip (modify → restore leaves the file byte-identical)
5. Save migration (`part_aliases.json`, older save shapes) — load an old save fixture
6. Market / opponent evolution / scheduler — pure logic, cheap to test
7. ViewModels — lowest priority

### Test quality (once tests exist)

- Happy-path only; no meaningful assertions; over-mocked; tests that touch the real AC install or `Documents` folder; order dependence; `Thread.Sleep` timing

### Infrastructure

- Suggest a `tests/` folder with one test project per production project, added to `Street Rod AC.slnx`; note the `net10.0-windows` + WPF constraint for tests that reference the game project, and that linked-source files can be tested from a plain `net10.0` project the way the tools do

## PERFORMANCE

### WPF / UI thread

- File IO, LiteDB queries, catalog scans, part-script evaluation, or KN5 loading on the UI thread (look for sync calls in command handlers and `Enter()`)
- `.Result` / `.Wait()` on tasks from the UI thread (deadlock risk as well as jank)
- Large `ItemsControl`s without virtualization (`VirtualizingStackPanel`, `ScrollViewer.CanContentScroll`); `ItemsControl` inside `ScrollViewer` defeating virtualization
- Unfrozen `Freezable`s in resources; bitmaps decoded at full size
- `ObservableCollection` rebuilt item-by-item where a replace would do

### Rendering / audio (`Controls/`, `CarRendererWindow`, `ShowroomOverlay`, `Audio/`)

- Render loop: work per frame that could be cached; allocations per frame; rendering while hidden/minimized
- Resource lifetime: textures/meshes/devices recreated on every navigation instead of reused, or never disposed
- Engine audio sim: allocations or locks on the audio callback thread; timer resolution

### Data

- LiteDB: indexes (`EnsureIndex`) on fields queried by `Find` in hot paths; loading whole collections to filter in memory; one `LiteDatabase` per operation instead of a long-lived instance
- Parts catalog: repeated parsing of `pack.json` / KN5 that could be cached; start-up time of catalog load

### Tools

- Converter: only flag performance if it makes the re-run workflow painful (minutes, not seconds)

## DEPENDENCY HEALTH

- Phase 0b `--vulnerable`, `--deprecated`, `--outdated` results, with transitive packages called out
- Target framework support window (.NET 10 is LTS — note end-of-support date from the .NET support policy page)
- **SlimDX**: unmaintained, .NET Framework-era — note what depends on it (AcTools.Render) and whether Vortice already covers the need
- **JetBrains.Annotations 2023.3.0**: compile-time only; check if still needed now that nullable reference types are on
- **Newtonsoft.Json vs System.Text.Json**: not a finding by itself; flag only mixed use of both in the same flow
- Local `actools` DLLs: Debug build referenced from a sibling checkout — reproducibility risk; note the expected actools commit if discoverable
- FMOD / AC native DLLs loaded from the AC install at runtime: version assumptions documented?
- Multiple versions of the same package across projects (the tools and the game should agree on Newtonsoft)
