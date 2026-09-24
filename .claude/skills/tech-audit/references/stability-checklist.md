# Stability Checklist

Drives the `stability` section: anything that can crash the app, hang it, leak until it degrades, corrupt state, or behave differently on another machine. Severity is by user impact: **CRASH/CORRUPT** (process dies, save or AC install left wrong) > **HANG/LEAK** (freezes, grows until it fails) > **FRAGILE** (works today, breaks on a plausible input or machine).

Every finding needs a trigger: "what the user does, or what input/machine state, makes this happen."

## Discovery greps

```bash
git grep -nE 'DispatcherUnhandledException|AppDomain\.CurrentDomain\.UnhandledException|UnobservedTaskException' -- '*.cs'
git grep -nE 'async void' -- '*.cs'
git grep -nE '^\s*_ = [A-Za-z].*\(|Task\.Run\(|\.ContinueWith\(' -- '*.cs'
git grep -nE '\.Result\b|\.Wait\(\)|\.GetAwaiter\(\)\.GetResult\(\)' -- '*.cs'
git grep -nE 'Dispatcher\.(Invoke|BeginInvoke|InvokeAsync)|CheckAccess' -- '*.cs'
git grep -nE 'lock \(|Interlocked|volatile|ConcurrentDictionary|SemaphoreSlim' -- '*.cs'
git grep -nE 'new (DispatcherTimer|System\.Timers\.Timer|System\.Threading\.Timer|Timer)\(|CompositionTarget\.Rendering' -- '*.cs'
git grep -nE '\+= ' -- '*.cs'          # compare with: git grep -nE -e '-= ' -- '*.cs'
git grep -nE ': .*IDisposable|\.Dispose\(\)|using \(|using var' -- '*.cs'
git grep -nE '(float|double|decimal|int|long)\.(Try)?Parse\(|Convert\.To(Single|Double|Int32)\(|string\.Format\(|\.ToString\("' -- '*.cs'
git grep -nE 'catch\s*\{|catch \(Exception[^)]*\)\s*\{\s*\}' -- '*.cs'
git grep -nE 'while \(true\)|for \(;;\)' -- '*.cs'
git grep -nE '\.First\(\)|\.Single\(\)|\[0\]|\.Last\(\)' -- '*.cs'
git grep -nE 'DateTime\.Now|Environment\.(UserName|MachineName)|Environment\.GetFolderPath' -- '*.cs'
```

## Crash paths

| Check                                  | What to verify                                                                                                                                                 |
| -------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Global exception handlers              | `Application.DispatcherUnhandledException`, `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are wired in `App.xaml.cs`: log, flush Serilog, run AC-content restore, tell the user. **None were wired at the 2026-09-24 audit (965c642); the fix branch `fix/tech-audit-2026-09-24` wires them, so from then on check that each still logs, restores and flushes.** |
| `async void`                           | Only event handlers; the whole body inside try/catch that logs via `IAppLogger` — an exception escaping `async void` kills the process                         |
| Fire-and-forget                        | `_ = SomethingAsync()` / `Task.Run` results observed (continuation that logs faults) — or the failure disappears silently                                       |
| Exceptions in constructors / `Enter()` | Screen/dialog construction and `Enter()` survive missing assets, empty catalog, corrupt save — navigation must not dead-end                                     |
| Native callbacks                       | FMOD/DSP callbacks never let a managed exception cross into native code; delegates passed to native code kept alive for the native object's lifetime           |
| LINQ on content                        | `.First()`, `.Single()`, `[0]`, `dict[key]` on data that comes from files, mods, or the catalog — use the `OrDefault`/`TryGetValue` form and handle absence     |
| Nulls from content                     | CS86xx warnings (Phase 0b) in parsers, catalog import, save load — each one on a content path is a crash candidate                                              |
| Stack depth                            | Recursive walks over part trees / slot graphs / script calls bounded; a cyclic `part_aliases.json` or slot graph can't recurse forever                         |

## Hangs and threading

| Check                           | What to verify                                                                                                                                  |
| ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| Sync-over-async                 | No `.Result` / `.Wait()` / `GetResult()` on the UI thread (deadlock with the WPF sync context)                                                  |
| Long work on the UI thread      | Catalog scan, KN5 load, part-script evaluation, dyno runs, LiteDB bulk ops, file copies of car folders — off the UI thread with progress         |
| Cross-thread UI access          | Background code (scheduler tasks, audio, AC-process watcher) touches `ObservableCollection`s / bound properties only via the dispatcher           |
| Shared state                    | State touched by both the audio/render thread and the UI thread is locked or immutable; `lock` targets private, never `this` or a string          |
| Waiting on AC                   | Waiting for `acs.exe` to exit has a path for: AC never starting, AC crashing, the user killing it, and AC still running when the app closes      |
| Cancellation                    | Leaving a screen cancels its background work (`CancellationTokenSource` cancelled + disposed on exit)                                           |
| Infinite loops                  | `while (true)` loops have an exit on every failure path; the script VM has an instruction budget                                               |
| Library first use from threads  | Static/global state that a library builds lazily (LiteDB's `BsonMapper.Global` registering a type on first use, static caches) is warmed up once before two threads can reach it — missed by the first audit, found while fixing it |

## Leaks and resource lifetime

| Check                          | What to verify                                                                                                                                                |
| ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Event subscriptions            | `+=` on a longer-lived publisher (services, scheduler, `AppSettings`, static events, `CompositionTarget.Rendering`) has a matching `-=` on screen exit/dispose |
| Timers                         | `DispatcherTimer`/`Timer` stopped and released on exit; a running `DispatcherTimer` roots its target                                                          |
| Disposable owners              | Types owning D3D devices/textures, FMOD systems/banks/instances, `LiteDatabase`, streams, `CancellationTokenSource` implement `IDisposable` and are disposed by whoever created them — only 2 types implement it today |
| Screen churn                   | Navigating garage ↔ dealer ↔ garage N times doesn't grow native memory, GPU memory or handle count (renderers/viewports recreated without disposing the old one) |
| Files left open                | Streams in `using`; no `File.Open` without dispose that would keep AC or the next run from opening the file                                                    |
| Temp / work folders            | Temp files and race copies cleaned up on success, failure and next start                                                                                      |

## State and data integrity

| Check                         | What to verify                                                                                                                                                  |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Atomic writes                 | Saves, settings, manifests and JSON the app rewrites go through temp-file + `File.Replace`/`Move` — a crash mid-write must not leave a truncated file             |
| Save compatibility            | Loading a save from an older build works (migrations, `part_aliases.json`, missing fields defaulted); a save referencing a part/car no longer installed degrades gracefully |
| Partial operations            | Multi-step changes (buy car: money − price, add car, remove listing) can't half-apply if a step throws                                                           |
| Scheduler                     | A failing `IScheduledTask` doesn't stop the others or the clock; tasks are safe to run twice after a crash between run and "last run" being saved              |
| Randomness                    | Seeded `Random` where reproducibility matters (converter determinism, tests); no `new Random()` per call in tight loops                                         |
| Self-repair of stores         | A database or cache that rebuilds itself (catalog `AutoRebuild`, LiteDB's recovery on open) can't silently drop user data or run while another open is live     |
| State that isn't persisted    | Every value a later step reads back (a race event's track, the pending race context) is stored in the save, not only kept on a screen VM — follow each launch intent field to where it's read after a restart |

## Environment differences (works on your machine only)

| Check                     | What to verify                                                                                                                                                                         |
| ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Culture                   | Every parse/format of a file value (INI, cfg, JSON by hand, SLRR text, AC data) uses `CultureInfo.InvariantCulture` — on a comma-decimal locale (es-AR, de-DE…) `float.Parse("1.5")` is 15 or throws, and an INI written with `1,5` breaks AC. Spec strings from `ui_car.json` also use space, thin-space or dot thousands separators (`"1 250 kg"`, `"1.250kg"`) |
| Paths                     | No hardcoded `C:\GAMES\...`, `C:\Users\...`, drive letters; AC and Documents paths come from settings / `Environment.GetFolderPath`; paths with spaces and non-ASCII names work      |
| Missing optional installs | Missing CSP, missing Lua app, missing AC sound bank, missing actools-dependent assets → clear message, not a crash                                                                    |
| DPI / multi-monitor       | D3DImage viewports and overlays behave at 125–200% scaling and when the window moves between monitors                                                                                  |
| GPU/device loss           | D3D9 device loss (sleep/resume, UAC prompt, driver reset, RDP) is recovered or the viewport rebuilt, not a crash                                                                       |
| Float math                | Engine model / dyno / running-gear scaling can't produce NaN/∞ (divide by zero rpm, empty curves) — and NaN never gets written into AC data files                                     |

## Output

Findings go under **STABILITY** in the report, tagged `CRASH`, `CORRUPT`, `HANG`, `LEAK` or `FRAGILE`, each with trigger → effect → fix.
