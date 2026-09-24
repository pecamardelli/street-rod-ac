# Pattern Deviations

Drives the `patterns` section. A deviation is code that solves a recurring problem differently from how the rest of the codebase solves it. The baseline is **this repo's own majority practice** (plus the rules in `docs/CLAUDE.md`), not generic advice — if 47 commands use `RelayCommand`, the 48th that wires a `Click` handler in code-behind is the finding, not the 47.

## Method

1. **Census (deterministic).** For every concern in the table below, run its greps across the game project and count each variant. Record the counts in the findings file under "Pattern census" — the diff of that table between runs shows drift.
2. **Establish the norm.** The documented rule wins when there is one. Otherwise the majority variant is the norm, **unless** the minority is the newer, better practice the code is migrating to (e.g. `LibraryImport` over `DllImport`) — then say which way the migration should go and count what's left.
3. **List the outliers** with file:line, and for each say what the norm is and what it costs to deviate (a bug class, a leak, a second place to change).
4. **Split-brain concerns** — two variants with no clear majority (e.g. two JSON libraries) — are one finding each, with a recommendation on which to keep and the file list to migrate.

Don't flag a deviation when the code comments why it differs, or when the context forces it (the tools are `net10.0`, not WPF; audio callbacks can't use the UI dispatcher).

## Concerns and census greps

Run from the repo root; scope with `-- 'Street Rod AC/*.cs'` etc.

| Concern                  | Norm (baseline 2026-09-23)                                                          | Census greps                                                                                          |
| ------------------------ | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| Navigation               | `NavigationService.NavigateTo<Screen>()` factories; screens never `new` a screen VM | `new [A-Z]\w*ScreenViewModel\(` outside `Navigation/`                                                  |
| Dialogs                  | `DialogService.ShowDialog(vm)`, one at a time                                       | `MessageBox\.Show`, `new \w+Window\(`, `\.ShowDialog\(\)` on a `Window`                               |
| Screen/dialog base       | `BaseScreenViewModel` / `BaseDialogViewModel` (17 screens)                           | classes under `Screens/` or `Dialogs/` implementing `INotifyPropertyChanged` directly                  |
| Commands                 | `RelayCommand` / `RelayCommand<T>` (≈60 uses)                                       | `Click="` handlers in `.xaml` whose code-behind does more than view work; `ICommand` implemented ad hoc |
| Async commands           | no async command type today — async work runs from `async void` handlers (21)       | `async void` sites; each must catch and log, or it's a stability finding                               |
| Logging                  | `IAppLogger` with a category (51 uses)                                              | `Console\.WriteLine` (11 today — `AssettoCorsaContentService`, `MainWindowViewModel`), `Debug\.WriteLine`, `Trace\.`, bare `Log\.` outside `Logging/` |
| Settings access          | `AppSettings.Instance` (34) — static singleton                                      | `AppSettings` passed in (2) vs read statically; new code should follow one                             |
| Service construction     | composed in `App.xaml.cs` / `NavigationService` and injected (0 `new …Service(` in VMs) | `new \w+(Service|Repository)\(` under `Screens/`, `Dialogs/`, `ViewModels/`, `Controls/`              |
| JSON                     | **split-brain**: Newtonsoft (10 files) and System.Text.Json (7 files)               | `System\.Text\.Json` vs `Newtonsoft` per file; note which reads which asset                            |
| Persistence              | repositories own `LiteDatabase` (`GameStateRepository`, `RaceSessionRepository`)     | `new LiteDatabase\(` anywhere else                                                                     |
| File writes              | (establish on first run) atomic write via temp + move, or direct `WriteAllText`     | `File\.WriteAll`, `File\.Move`, `File\.Replace`, `\.tmp`                                               |
| AC content changes       | only through `CarDataOverlay` (see `docs/CLAUDE.md`)                                 | writes under `content\cars` or into car folders outside `CarDataOverlay`                               |
| Scanning `content\cars`  | skip clones via `AcCarFolder.IsClone`                                               | `Directory\.(Get|Enumerate)Directories` over the cars folder without an `IsClone` check                |
| Number parsing/format    | invariant culture for any file format (INI, JSON, cfg, SLRR data)                   | `(float|double|decimal|int)\.(Try)?Parse\(` and `ToString\(` without `CultureInfo`/`NumberStyles`       |
| Threading to UI          | `Dispatcher.InvokeAsync` (1 use)                                                    | `Dispatcher\.Invoke\(` (sync), `BeginInvoke`, `Application\.Current\.Dispatcher` in services           |
| Disposal                 | (establish on first run) — only 2 types implement `IDisposable` despite D3D/FMOD/LiteDB owners | types holding `LiteDatabase`, FMOD handles, D3D objects, `Timer`, `CancellationTokenSource` without `IDisposable` |
| Error handling           | catch, log via `IAppLogger`, degrade (120 `catch` sites, 1 empty)                   | `catch\s*\{\s*\}`, `catch \(Exception\) \{ \}`, catch-and-rethrow `throw ex;`                           |
| Part data access         | through `PartsCatalog` / `CarPartsService`                                          | direct reads of `Assets\Parts\**\pack.json` elsewhere                                                  |
| Nullable                 | annotations honoured                                                                | `#nullable disable`, `null!`, `!` on values that come from files/content                               |
| Naming                   | .NET conventions; `_camelCase` fields; `Async` suffix on awaitables                 | public fields, `async Task` methods without `Async` suffix in services                                 |

Update the "Norm" column when the codebase deliberately moves (and date the new baseline). Add a row when a new recurring concern appears — e.g. the LLM client once `HttpClient` lands.

## Output

Findings go under **PATTERN DEVIATIONS** in the report, each as:

- **<file>:<line>** — does X; the norm is Y (<N> of <M> sites) — **Cost:** <what breaks or doubles> — **Fix:** <change>

Plus the census table (concern → variant counts), which is the part worth diffing between runs.
