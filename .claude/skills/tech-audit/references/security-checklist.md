# Security & Integrity Checklist

Drives the `security` section. Applied per unit based on the signals captured in `workspace-classifiers.md`. **Activate only the rows whose signal is present** — don't spend subagent budget on P/Invoke rows in a unit with no `DllImport`.

Threat model (see `SKILL.md` → "What security means here"): the user's AC install and saves must survive anything; mods are untrusted input; native interop must be memory-safe; dependencies must be sound.

## Discovery greps (Phase 3 worklist)

```bash
# Writes into AC / outside the app
git grep -nE 'content\\\\cars|content/cars|\\\\cfg\\\\|race\.ini|assists\.ini|video\.ini|File\.(Copy|Move|Delete|WriteAll|Replace)|Directory\.(Delete|Move)' -- '*.cs'
# Restore paths
git grep -nE 'finally|Restore|Manifest|IsClone|Recover' -- '*.cs'
# Process launches
git grep -nE 'Process\.Start|ProcessStartInfo|UseShellExecute' -- '*.cs'
# Deserialization
git grep -nE 'TypeNameHandling|JsonSerializerSettings|BinaryFormatter|XmlSerializer|BsonMapper|JsonConvert\.Deserialize' -- '*.cs'
# Native / unsafe
git grep -nE 'DllImport|LibraryImport|unsafe|fixed \(|Marshal\.|IntPtr|stackalloc|SafeHandle' -- '*.cs'
# Binary parsers of untrusted formats
git grep -nE 'BinaryReader|ReadInt32|ReadBytes|ReadSingle|new byte\[' -- '*.cs'
# Paths from content
git grep -nE 'Path\.Combine|Path\.GetFullPath|GetInvalidFileNameChars' -- '*.cs'
# Network
git grep -nE 'HttpClient|WebClient|WebRequest|Uri\(' -- '*.cs'
# Secrets
git grep -nEi '(api[_-]?key|secret|password|token)\s*[:=]' -- ':!*.md'
```

## Cross-cutting (every unit)

| Check                       | What to verify                                                                                                                   | Pointer                                                                    |
| --------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| Secrets in source           | No API keys/tokens in tracked files; future LLM endpoints/keys come from settings, not code                                     | secrets grep above                                                         |
| Tracked binaries/state      | No `saves\*.db`, `catalog.db`, logs or user paths committed                                                                      | `git ls-files` check in Phase 0b                                           |
| Hardcoded user paths        | No `C:\Users\<name>`, `C:\GAMES\...`, `D:\...` literals outside settings defaults                                                | `git grep -nE '[A-Z]:\\\\' -- '*.cs'`                                      |
| Package vulnerabilities     | `dotnet list package --vulnerable --include-transitive` clean                                                                    | Phase 0b                                                                   |
| Package sources             | `nuget.config` clears sources and lists only nuget.org (true today — flag if it changes)                                         | `nuget.config`                                                             |
| Local DLL references        | `HintPath` DLLs outside the repo are unversioned and unverified; note which build of actools is expected, and Debug vs Release  | `.csproj` `Reference` items                                                |
| Nullable                    | `Nullable=enable` in every project; CS86xx warnings in parsers and restore paths are BUG candidates                             | Phase 0b build warnings                                                    |

## WPF desktop app (the game)

### Integrity of the AC install (highest priority)

| Signal                        | Check                                                                                                                                                                                                 |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CarDataOverlay`              | Manifest written **before** the first byte of AC content changes, flushed to disk; originals copied, not moved, until the swap succeeds                                                               |
| `CarDataOverlay`              | Restore runs in the launcher's `finally` **and** at app start-up; restore is idempotent (safe to run twice, safe after a partial restore)                                                             |
| `CarDataOverlay`              | A failure restoring one car does not abort restoring the rest; failures are logged via `IAppLogger` and surfaced to the user                                                                         |
| Same-model clones             | Clone folders carry the marker; cleanup deletes only marked folders; every scanner of `content\cars` skips `AcCarFolder.IsClone` folders                                                              |
| `IniModificationService`      | Original values captured before modification and restored after AC exits; no full-file rewrite that drops keys/comments the app didn't set; encoding preserved                                      |
| Launcher                      | AC process exit is awaited without blocking the UI thread; app shutdown / unhandled exception while AC runs still triggers restore (`AppDomain.UnhandledException`, `Application.Exit`)             |
| Destructive IO                | Every `Directory.Delete(recursive: true)` / `File.Delete` target is proven to be inside a folder the app owns (or a marked clone) — resolve with `Path.GetFullPath` and prefix-check                  |
| `data.acd` (ACD key)          | Packing/unpacking uses the correct per-folder key; a failed repack leaves the original in place                                                                                                      |

### Untrusted content

| Signal                         | Check                                                                                                                                                             |
| ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Paths from content (CWE-22)    | Car ids, part ids, sound bank names, JSON/INI fields used in `Path.Combine` are validated (no `..`, no rooted paths, no invalid chars) before IO                  |
| Binary parsers (CWE-125/787)   | Counts/lengths/offsets read from files are range-checked before `new T[count]`, `ReadBytes(n)`, seeking or indexing; malformed files throw a caught, logged error |
| Resource limits (CWE-400)      | No unbounded allocation or loop driven by a file header; huge textures/meshes rejected or capped                                                                |
| JSON (CWE-502)                 | Newtonsoft `TypeNameHandling` is `None` (default) for any file a mod or the user can edit; `MaxDepth` set where input is external                                |
| Lua result JSON                | Launcher treats result JSON as untrusted: missing/extra fields, wrong types, stale files from an earlier race, and a partially written file are all handled      |
| Script VM (`Parts/Scripting`)  | Instruction budget / timeout per call, call-depth limit, bounds on every operand/stack/array access; host functions exposed to scripts cannot touch files or processes |
| Script VM                      | A script fault is contained (caught, logged, part treated as inert) and cannot crash the app or corrupt the build it was evaluating                              |
| LiteDB saves                   | Opening a corrupted or foreign save fails gracefully; connection mode suits single-process use; no user-controlled strings concatenated into `BsonExpression`s   |
| Imported catalog               | Re-import of a changed/removed AC car cannot leave dangling references that throw at runtime                                                                    |

### Native interop

| Signal                        | Check                                                                                                                                                            |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `DllImport` / `LibraryImport` | Signatures match the native headers (calling convention, `CharSet`, `bool` marshalling, struct layout/`Pack`); prefer `LibraryImport` source-gen on .NET 7+      |
| FMOD (`Audio/`)               | Every FMOD result code checked; handles released in the right order (event instances → banks → system); DSP callbacks rooted (delegate kept alive, no GC collect) |
| FMOD DSP plugins              | Re-implemented plugin callbacks never throw across the native boundary; buffer lengths honoured                                                                   |
| D3D9 / D3DImage               | `Lock`/`Unlock` balanced; back buffer re-set on device loss (`IsFrontBufferAvailableChanged`); shared textures and devices disposed; no use after dispose         |
| `unsafe` blocks               | Pointer arithmetic bounded by the buffer length; `fixed` scopes not escaped; `stackalloc` sizes bounded                                                          |
| Handles                       | Native handles wrapped in `SafeHandle` or owned by an `IDisposable` with a finalizer guard                                                                       |

### Network (activates when `HttpClient` appears — planned Ollama / LM Studio)

| Signal       | Check                                                                                                                  |
| ------------ | ---------------------------------------------------------------------------------------------------------------------- |
| `HttpClient` | Single shared instance or `IHttpClientFactory`; explicit timeout; cancellation wired to UI lifetime                    |
| `HttpClient` | Endpoint from settings, defaulting to `localhost`; no silent fallback to a remote host; what leaves the machine is documented |
| LLM output   | Model output is treated as untrusted text: never used as a path, command, or deserialized type; length capped before display |

## CLI tool (`SlrrPartsConverter`, `EngineBench`)

| Signal       | Check                                                                                                                         |
| ------------ | ----------------------------------------------------------------------------------------------------------------------------- |
| CLI args     | Output paths validated; the converter refuses to write outside the intended output root; `--replace` cannot delete unrelated folders |
| Parsers      | SCX/RPK/cfg readers apply the same bounds rules as the game's parsers (these read community mods)                            |
| Determinism  | Output is deterministic (the re-run workflow diffs scratch runs) — flag dictionary-order or time-dependent output              |
| Exit codes   | Non-zero exit on failure so `convert-parts.ps1` can stop                                                                       |

## CSP Lua app (`apps/lua/sr_race_manager`)

| Signal        | Check                                                                                                                  |
| ------------- | ---------------------------------------------------------------------------------------------------------------------- |
| Results file  | Written atomically (temp file + rename) so the launcher never reads a half-written JSON                                |
| Results file  | Filename/race id cannot collide with a previous race's file; the launcher and the app agree on the naming              |
| Shutdown      | `ac.shutdownAssettoCorsa()` only after the result is flushed                                                            |
| SDK usage     | Only APIs documented in the current CSP Lua SDK; guarded for older CSP versions if the manifest allows them             |

## AC Python app (legacy, `apps/python`)

Superseded; audit only if it changed in the diff window. Flag if anything still launches or depends on it.

## Build/ops scripts (`tools/*.ps1`)

| Signal | Check                                                                                                         |
| ------ | ------------------------------------------------------------------------------------------------------------- |
| `.ps1` | `$ErrorActionPreference = 'Stop'` (or equivalent) so a failed step doesn't carry on; paths quoted (the repo path has spaces) |
| `.ps1` | No destructive `Remove-Item -Recurse` on a path built from an unset variable                                   |

## CWE sanity sweep (union of all units)

| CWE                                   | Concrete check in this codebase                                                                    |
| ------------------------------------- | -------------------------------------------------------------------------------------------------- |
| CWE-22 Path traversal                 | Content-derived names never escape the intended folder                                             |
| CWE-78 OS command injection           | `Process.Start` arguments never built from content strings without quoting/validation              |
| CWE-125/787 Out-of-bounds read/write  | Binary parsers, `unsafe` code, script VM stacks                                                    |
| CWE-400 Uncontrolled resource use     | Script VM budget; file-header-driven allocations                                                   |
| CWE-502 Unsafe deserialization        | `TypeNameHandling`, `BinaryFormatter`, custom `BsonMapper`                                         |
| CWE-667 Improper locking / CWE-362    | Scheduler tasks, audio thread vs UI thread, render loop vs disposal                                |
| CWE-459 Incomplete cleanup            | AC content overlay and clones left behind after a crash                                            |
| CWE-209 Error message info leak       | Not relevant for a local app — skip unless crash reports ever leave the machine                    |

## Output rules for security findings

- File:line on every finding.
- Severity: `BUG` (reachable now — corrupts the AC install/saves, crashes on a malformed mod, memory-unsafe) > `SECURITY` (defense-in-depth gap) > `HARDENING` (best-practice nit).
- Each finding must answer: "what input or event triggers it," "what does the doc/standard say," and "concrete fix."
- A passing check is not a finding — only call out positive patterns at the end of the report.
