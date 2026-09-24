---
name: tech-audit
description: >
  Use when the user wants a deep audit of Street Rod AC against current official documentation
  (.NET / WPF / LiteDB / Newtonsoft / FMOD / CSP Lua), defaults to a security-and-integrity sweep
  across every project in the solution plus the AC-side apps, and writes findings to a dated file.
  Also audits pattern deviations (code that departs from the repo's own conventions), duplicated
  code (token clone finder + semantic duplicates) and stability (crashes, hangs, leaks, corrupt state,
  locale/machine differences). Triggers on /tech-audit (no args runs security on everything);
  explicit sections: security, stability, patterns, duplication, clean-code, xaml, test-coverage,
  performance, dependencies, quality (= stability + patterns + duplication), all. Add `full` to
  ignore the diff window.
allowed-tools:
  - WebFetch
  - WebSearch
  - Read
  - Grep
  - Glob
  - Bash
  - PowerShell
  - Agent
  - Edit
  - Write
user-invocable: true
---

# Tech Audit Skill

Solution-aware, autonomous audit of Street Rod AC. Discovers the audit units from the solution file and the repo layout, fetches current official docs for what is actually referenced, and reports findings — by default with a security-and-integrity focus.

**Announce at start:** `Running tech-audit — section: <section> — units: <N>`

## What "security" means here

This is a single-user Windows desktop game, not a web service. There is no login, no network API, no multi-tenant data. The threat model is:

1. **The user's Assetto Corsa install and saves.** The app writes into `content\cars` (car data + sound swaps, same-model clones) and AC's INI files, and must always put them back. A crash, exception or kill mid-race that leaves a car modified or an INI changed is the worst class of bug this project has. See `docs/CLAUDE.md` → "AC Content" and the `CarDataOverlay` manifest/restore design.
2. **Untrusted content parsed by the app.** AC car folders (`data.acd`, `.ini`, `.kn5`, sound banks), SLRR part packs (SCX/RPK/cfg and compiled script bytecode run by `Parts/Scripting/ScriptVm`), `pack.json`/`part_aliases.json`, the Lua app's result JSON in `Documents\Assetto Corsa\out`, and LiteDB save files the user may copy around. Mods come from the internet — treat every one of those as attacker-controllable input.
3. **Native code.** FMOD P/Invoke, D3D9/D3DImage interop (Vortice, SlimDX, AcTools.Render), `unsafe` blocks. Memory-safety bugs here crash the game or corrupt memory.
4. **Supply chain.** NuGet packages plus unversioned local DLLs (`..\..\actools\Output\...`) referenced by `HintPath`.

`references/security-checklist.md` turns that into concrete checks.

## Defaults (autonomous)

- `$ARGUMENTS` empty → run `security`. Do **not** prompt the user.
- Section values: `security` (default) | `stability` | `patterns` | `duplication` | `clean-code` | `xaml` | `test-coverage` | `performance` | `dependencies` | `quality` | `all`.
- Bundles: `quality` = `stability` + `patterns` + `duplication`. `all` = every section. A bundle shares Phases 0–3 and writes one combined findings file named after the bundle.
- Output is durable: a dated findings file under `references/findings/`. Chat output is the executive summary only.
- Diff-aware: if `references/analysis-log.md` records a prior run, scope code review to files changed since the last logged commit (`git diff --name-only <sha>..HEAD`), but still run the deterministic checks (Phase 0b) in full. Pass `full` as a second arg (e.g. `security full`) to force a full sweep.
- **Read-only against the world.** Never launch the game or AC, never run `SlrrPartsConverter` or `tools/convert-parts.ps1` (they rewrite `Assets\Parts`), never run an `EngineBench` mode that writes, never touch the AC install (`C:\GAMES\...`) or `saves\*.db`. `tools/slot_shifts.json` is the user's work in progress — read it, never "fix" it.

## Phase 0 — Discovery (deterministic, no model judgment)

**Required.** Enumerate audit units from the solution and the repo — never hardcode a list.

```bash
# .NET projects in the solution (works with .slnx on the .NET 10 SDK)
dotnet sln "Street Rod AC.slnx" list

# Packages per project (top-level + transitive), machine readable
dotnet list "Street Rod AC.slnx" package --include-transitive --format json

# Non-.NET units: AC-side apps and scripts
ls apps/lua tools/*.ps1   # apps/python was removed 2026-09-24; list it too if it ever comes back
```

For every .NET project capture from its `.csproj`: path, `OutputType`, `TargetFramework`, `Nullable`, `AllowUnsafeBlocks`, `UseWPF`/`UseWindowsForms`, `PackageReference`s (id + version), `Reference`s with `HintPath` (local DLLs — note path and whether it exists on disk), and **linked `<Compile Include="..\..\Street Rod AC\...">` items**. The tools compile game source by link: those files belong to the game project and must be audited once, under the game — a tool's audit covers only its own files plus how it uses the linked ones.

For every non-.NET unit capture: language, entry file, manifest (`manifest.ini`), and which C# code consumes its output (e.g. the Lua app's result JSON → `AssettoCorsaLauncher`).

Also note repo-level metadata: `nuget.config` (package sources), `.gitignore`/`.gitattributes`, `.github/workflows/` (absent today — note if it appears), `Directory.Build.props`/`Directory.Packages.props` (absent today), `docs/` (the architecture rules the audit checks against).

This is the **unit matrix** every later phase reads from.

## Phase 0b — Deterministic checks (always full, cheap, ground truth)

Run these before any model review; their output is evidence, and findings drawn from them are high-confidence.

```bash
dotnet build "Street Rod AC.slnx" -c Debug -v q -clp:NoSummary 2>&1 | tee "$WORK/build.txt"
dotnet list "Street Rod AC.slnx" package --vulnerable --include-transitive 2>&1 | tee "$WORK/vulnerable.txt"
dotnet list "Street Rod AC.slnx" package --deprecated 2>&1 | tee "$WORK/deprecated.txt"
dotnet list "Street Rod AC.slnx" package --outdated 2>&1 | tee "$WORK/outdated.txt"
git ls-files | grep -iE '\.(db|pfx|snk|key|pem)$|secrets|\.env'   # things that should not be tracked
```

- `$WORK` is the session scratchpad when one is listed, else `$TEMP/tech-audit/<YYYY-MM-DD>/`. Never write work files into the repo.
- If the build fails because `..\..\actools\Output\x64\Debug\*.dll` is missing, record that under **Coverage gaps** and carry on — do not try to build actools.
- **For `patterns`, `quality`, `all`:** run the census greps from `references/patterns.md` and keep the counts (concern → variant → count, with file:line for minority variants) in `$WORK/census.md`.
- **For `duplication`, `quality`, `all`:** run both passes of the clone finder (`references/stack-and-docs.md` → DUPLICATION) into `$WORK/dup-exact.json` and `$WORK/dup-renamed.json`.
- **For `stability`, `quality`, `all`:** run the discovery greps from `references/stability-checklist.md` into `$WORK/stability-greps.txt`, and check the global exception handlers first — their absence changes the severity of every unguarded crash path.
- Group build warnings by code (CS8602 nullable, CS0618 obsolete, CA*, WPF binding-related, NU* package warnings) and count them. They feed `clean-code`, `dependencies` and `security` (CS0618 on crypto/serialization APIs, CA2300-series).

## Phase 1 — Classify units and resolve doc targets

**Classify each unit by signature**, not by folder name. See `references/workspace-classifiers.md`:

| Signature                                                   | Classification     |
| ----------------------------------------------------------- | ------------------ |
| `OutputType=WinExe` + `UseWPF`                              | WPF desktop app    |
| `OutputType=Exe`, no UI                                     | CLI tool           |
| `manifest.ini` + `*.lua` under `apps/lua`                   | CSP Lua app        |
| Python AC app under `apps/python`                           | AC Python app (legacy; removed 2026-09-24, no unit exists) |
| `*.ps1` under `tools/`                                      | Build/ops script   |

Write the classification + reference summary to `references/workspace-map.md` (overwrite each run — it's a snapshot, not history).

**Resolve canonical doc URLs** for everything referenced (NuGet packages, local DLL families, native libraries, runtimes):

1. Cache lookup: `references/doc-sources.md`.
2. Cache miss for a NuGet id: `curl -s https://api.nuget.org/v3/registration5-gz-semver2/<lowercase-id>/index.json --compressed` → `projectUrl` of the resolved version, or the nuget.org package page `https://www.nuget.org/packages/<id>/<version>`.
3. Still nothing useful: `WebSearch "<name> <major-version> documentation"`.

For sections other than `dependencies` and `all`, fetch only docs relevant to the section (filter via `references/security-checklist.md` and `references/stack-and-docs.md`).

## Phase 2 — Read current docs

`WebFetch` 2–4 pages per resolved library — focus on the version actually referenced (LiteDB 5.x, Newtonsoft 13.x, Serilog 4.x, .NET 10 / WPF on .NET 10). **Do not rely on training data alone.** The point of this skill is comparing the implementation against _current_ official docs.

For `security`, in addition to per-library docs, fetch:

- Microsoft .NET secure coding guidelines and the CA security rules page (see `doc-sources.md`)
- The native interop best-practices page (P/Invoke, `SafeHandle`, `LibraryImport`)
- CWE Top 25 (current edition) — for the CWE ids used in findings (CWE-22, 78, 125/787, 400, 502, 667)
- Newtonsoft `TypeNameHandling` guidance, if any `JsonSerializerSettings` exist
- The CSP Lua SDK docs, if the Lua app changed in the diff window

## Phase 3 — Enumerate the trust surface (security & all only)

Before reading code, build an inventory per unit. `Grep` for the patterns in `references/security-checklist.md` → "Discovery greps" and list, with file:line:

- **Writes outside the app's own folders**: AC install (`content\cars`, `cfg\*.ini`, `Documents\Assetto Corsa\cfg`), clones, sound swaps — and for each, where it is restored and whether the restore runs in `finally`, at start-up recovery, and on a second crash during restore
- **Untrusted-input parsers**: every reader of AC car data, KN5/ACD, SLRR SCX/RPK/cfg, script bytecode, `pack.json`, result JSON, INI — and whether sizes/counts/offsets read from the file are bounds-checked before allocating or indexing
- **Paths built from content**: file/folder names taken from car ids, part ids, JSON fields, INI values → `Path.Combine` → file IO
- **Process launches**: `Process.Start` sites, the argument source, `UseShellExecute`
- **Deserialization**: Newtonsoft settings (`TypeNameHandling`, `MaxDepth`), LiteDB `BsonMapper` custom mappings, anything `BinaryFormatter`/`XmlSerializer` over files
- **Script VM**: instruction budget, call-depth limit, array/heap bounds, what host functions the scripts can call (file/process access?)
- **Native interop**: `DllImport`/`LibraryImport` sites, handle ownership, `unsafe` blocks and their pointer bounds, COM/D3D object disposal
- **Network**: `HttpClient` sites (the planned Ollama/LM Studio integration in `docs/ai-integration.md`) — endpoint source, timeouts, what content leaves the machine
- **Cross-unit trust boundaries**: Lua app → result JSON → launcher; converter → `Assets\Parts` → runtime catalog; game ↔ AC process

Write the inventory to `references/attack-surface.md` (overwrite each run; in diff mode, list only new/changed surfaces and point to the prior snapshot via `git show <sha>:<path>`). Subsequent code review uses it as the worklist.

## Phase 4 — Per-unit code review (parallel, bounded)

Dispatch **one Explore subagent per audit slice**. Derive slices from the unit matrix, never hardcode them. The game project is large (≈260 `.cs` + ≈30 `.xaml`), so split it by top-level folder groups of similar size, e.g. `Parts/` · `Services/` · `Audio/ + Controls/ + interop windows` · `Screens/ + ViewModels/ + Views/ + Dialogs/ + Navigation/` · `Models/ + remaining`. Small units (each CLI tool, the Lua app, scripts) can share one agent. In diff mode, drop slices with no changed files. Keep the total at or under 8 agents.

Cross-cutting sections get their own agents on top of the slice agents, because a per-folder reviewer can't see them:

- **`duplication`** — one agent that works from `$WORK/dup-*.json` across the whole repo: reads both sides of every clone, clusters pairs into families, triages Extract / Leave / Drifting, and hunts semantic duplicates by concern (see `stack-and-docs.md` → DUPLICATION).
- **`patterns`** — the controller builds the census itself (it's deterministic); slice agents get the census and the norm table and report outliers in their slice with the cost of each deviation.
- **`stability`** — runs inside the slice agents (it's local to the code), but the app-wide checks (global exception handlers, start-up restore, shutdown path, save migration) go to whichever agent owns `App.xaml.cs` + `Services/`.

When several sections run together (`quality`, `all`), give each slice agent all the active checklists at once rather than dispatching one agent per section per slice — the budget stays at ≤ 8 agents.

Each subagent receives:

- Its slice (paths) and classification
- The relevant slice of the docs digest from Phase 2
- The relevant slice of `attack-surface.md` from Phase 3 and the Phase 0b output lines for its files
- The relevant section checklists: `references/security-checklist.md` (security), `references/stability-checklist.md` (stability), `references/patterns.md` + the census (patterns), `references/stack-and-docs.md` (the rest)
- The project rules from `docs/CLAUDE.md` and any `docs/systems/*.md` page covering its slice — violations of documented rules are findings; the rules themselves are not
- A hard cap: **≤ 40 findings per agent, file:line + ≤ 1 sentence each, no prose preamble**
- **Required final step:** the subagent MUST write its findings to `$WORK/audit-<slice>.md` before returning, and its terminal output MUST include the line `Wrote N findings to <path>`. If the file is missing, the controller treats that as a failed dispatch and retries once.

Each subagent looks for:

1. Violations of documented best practices for the APIs it actually uses
2. Deprecated/obsolete API usage for the referenced versions
3. Missing patterns the docs recommend (`IDisposable`, `SafeHandle`, `ConfigureAwait` where relevant, bounds checks)
4. Security and integrity issues, cross-checked against the trust-surface inventory
5. Performance anti-patterns

## Phase 5 — Verify, aggregate, write findings, update index

1. Confirm every dispatched slice produced its `$WORK/audit-<slice>.md`. Do **not** accept findings from chat results alone — they cannot be re-read after a context reset.
2. **Verify every BUG and SECURITY finding yourself** by reading the cited lines before it goes in the report. Drop anything the code does not bear out; downgrade anything that is real but not exploitable/reachable. A handful of true findings beats forty plausible ones.
   - **Check intent before calling a mismatch a bug.** When the code disagrees with a doc, a comment or a "norm", run `git log -L <line>,<line>:<file>` (or `git log -S`) on the code side first. A commit that set the value on purpose makes the doc the finding, not the code. Example: the opponent skill floor of 90 (commit 61ca613) disagreed with docs that said 80–100. The first audit told us to "clamp to 80", but below 90 AC's AI drives too badly to make a race.
   - **Rate by likelihood, not by the worst outcome.** A bug that needs a rare trigger (for example a ≥25 g spike in the few seconds before AC quits) is tagged *Plausible* and ranked below one that fires on a normal run, even if both end the same way.
3. Write one findings file:

```
references/findings/<YYYY-MM-DD>-<section>.md
```

Structure (sorted by severity; include only the sections that ran):

```markdown
# Tech Audit — <section> — <YYYY-MM-DD>

**Units:** <N> — <list of names with classification>
**Diff window:** <SHA..HEAD> | full
**Docs fetched:** <N URLs across M libraries>
**Deterministic checks:** build <ok|failed: why> · <N> warnings (<top codes>) · vulnerable pkgs <N> · deprecated <N> · outdated <N>
**Duplication scan:** exact <N files / N pairs / ~N lines> · renamed <N pairs / ~N lines>   (if run)

Legend: ✔ = re-read by the controller · *Plausible* = real code path, but it needs an unlikely trigger or timing · no mark = verified by the slice reviewer. Every BUG and SECURITY entry carries a **Fix** line.

## Executive summary

<5–10 lines, severity counts, top 3 fixes>

## BUGS (must fix)

- **<unit>/<file>:<line>** — <finding>
  - **Why:** docs say X vs we do Y (or: CWE-nnn, how it goes wrong)
  - **Fix:** <concrete change>

## STABILITY (should fix)

<one line: N crash · N corrupt · N hang · N leak · N fragile>

- **[CRASH|CORRUPT|HANG|LEAK|FRAGILE] <unit>/<file>:<line>** — <finding>
  - **Trigger:** <what the user does / what input or machine state>
  - **Fix:** <concrete change>

## SECURITY / INTEGRITY (should fix)

…

## DUPLICATION

- **[Drifting|Extract|Leave] <cluster name>** — <file:lines> ↔ <file:lines> (…) · <N> tokens
  - **Target:** <shared helper/base/service and where it lives> (or why leave)

## PATTERN DEVIATIONS

### Census

| Concern | Norm | Variants (count) | Δ since last run |
| ------- | ---- | ---------------- | ---------------- |

### Outliers

- **<file>:<line>** — does X; norm is Y (<N>/<M>) — **Cost:** … — **Fix:** …

## PERFORMANCE (should fix)

…

## CLEAN CODE (nice to have)

…

## POSITIVE PATTERNS

- <terse list>

## Coverage gaps in this audit

- <units or slices skipped or partially audited, why>
```

Then append **one line** to `references/analysis-log.md`:

```markdown
| <YYYY-MM-DD> | <section> | <SHA..HEAD or full> @ <HEAD short sha> | <N bugs / N stab / N sec / N dup / N pat / N perf / N clean> | [findings/<file>.md](findings/<file>.md) |
```

Keep the row to that shape — the log is an index, not a place for findings prose.

The chat output is the executive summary only — point the user to the findings file for detail. Do not start fixing anything unless the user asks.

## Rules

- **Never hardcode the unit list.** Always derive from `dotnet sln list` + the repo layout. New projects and apps must be audited automatically.
- **Always read docs first.** Compare against _current_ official documentation, not cached knowledge.
- **File:line on every finding.** No vague "consider improving X."
- **Prioritize bugs > stability > security/integrity > drifting duplicates > pattern deviations > performance > clean code > style.** A drifting duplicate (one copy got a fix the other didn't) is a bug.
- **The norm is this repo's majority practice**, not generic advice — see `references/patterns.md` → Method.
- **Be concrete.** Show the problematic snippet and the recommended fix.
- **Acknowledge good patterns.** Not just a fault-finding exercise.
- **Don't duplicate `docs/CLAUDE.md` rules.** If a convention is already documented, only flag it when violated.
- **Audit linked source once.** Files compiled into the tools by `<Compile Include="..\..\Street Rod AC\...">` belong to the game slice.
- **Respect deliberate decisions.** The project's memory and docs record choices that look odd out of context (running SLRR's compiled scripts in a VM instead of porting them, carbs of different packs never merged, re-implementing AC's FMOD DSP plugins). Flag how they are implemented, not that they were chosen. Opponent skill never goes below 90 (`Opponent.MinSkill`): AC's AI drives too badly under that. A doc that says otherwise is the thing to fix.
- **No interactive prompts in autonomous mode.** Default to `security` and proceed.
- **Cap subagent output.** Slices × 40 findings × 1 sentence is readable; unbounded prose is not.

## When NOT to use

- Single-file review or "check this branch / PR" — use `/code-review` (or `/security-review` for pending changes).
- Quick lookup of one library's docs — use `WebFetch` directly.
- Verifying a specific known issue — read the file with `Read`.

## Section-specific scope

- `security` → `references/security-checklist.md`
- `stability` → `references/stability-checklist.md`
- `patterns` → `references/patterns.md`
- `duplication` → `references/stack-and-docs.md` → DUPLICATION, and `scripts/find_duplicates.py`
- `clean-code`, `xaml`, `test-coverage`, `performance`, `dependencies` → `references/stack-and-docs.md`

`quality` and `all` run their sections together (sharing Phases 0–3 and the slice agents) and write one combined findings file.
