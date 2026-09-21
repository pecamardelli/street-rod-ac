# Parts System

Car parts come from Street Legal Racing: Redline (SLRR): models, mounting slots **and behaviour**. The game does not
reimplement SLRR's part logic, it runs it: the compiled part scripts of the SLRR install are executed by a small VM,
against the player's own assembly of parts. Only what SLRR itself kept in native code is written in C#: the engine
simulation and, new here, the export to Assetto Corsa physics data.

Scope: mechanical parts only (engine, transmission; running gear next). Car bodies stay Assetto Corsa's.

```
SLRR install ──SlrrPartsConverter──▶ content\parts\                        (one-off, offline)
                                       <pack>\pack.json + *.kn5             part definitions + models
                                       engine_builds.json                   complete engines as part lists
                                       script_constants.json                statics of the shared script classes
                                       _scripts\...\*.class                 the compiled scripts the parts need

content\parts ──PartsCatalog──▶ PartTreeBuilder ──▶ PartScriptRuntime ──▶ EngineDyno ──▶ EngineReport ──▶ AcEngineData
                 definitions      build → tree        scripts run on it     torque curve    verdict+figures   AC data files
```

Converted assets are from a commercial game and community mods: personal use only, never in the repo.

## Why a VM instead of a port

- 242 mod part classes override `updatevariables()` with their own slot-specific logic; a port of the shared classes
  would ignore them.
- The install's *shared* classes are modded too (`Part.tHUF2USD` divides by 281 instead of 211, used price factor 0.4
  instead of 0.7, an extra hydrogen fuel). The released Java sources (2.2.1) are good for reading, not for values.
- The scripts are simple (field math, slot lookups, a few loops), and the converter needed an interpreter anyway to
  read part values out of class files that ship without sources.

## Converter (`tools/SlrrPartsConverter`)

`SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>]`

Per part, from its compiled script (run with no game around, see `SlrrScriptEvaluator`):

| pack.json field | Meaning |
|---|---|
| `class_chain` | Script classes above the part, nearest first (`...Block_Vee_OHV`, `...Block_Vee`, `...Block`, ..., `Part`) |
| `properties` | Fields after construction, script names and units: `bore`/`stroke` mm, `Vmin` cc, `value` USD, `max_wear` m, `ratio[0..7]` ([7] = reverse), `end_ratio`... |
| `slot_roles` | From `*_slot_ID` fields: which slot takes what (`crankshaft` → 8) |
| `required_slots` | Slots that must be filled for the part to work, with the script's own message. Found by running `isDynoable`/`isDriveable` with unknown parts |
| `stock_parts` | What `addStockParts()` mounts |
| `derived` | What `updatevariables()` computes on a lone new part; reference values |

`engine_builds.json`: every car chassis script's `stock_parts_list_E` (+ stage 1/2 kits), plus the lists found in the
text files given with `--notes`. A note title like `MOPAR 340 Six Pack 290 hp` also gives `rated_power`, which the
engine model is checked against.

## Script VM (`Parts/Scripting`)

- `ScriptClass`: the "TUFA" class file. Sections CONS (pool), FILD, MTHD, TREE. Code is postfix expression trees
  with a source line per node; see the bytecode reference at the end.
- `ScriptClassLoader`: finds classes under a root laid out like SLRR. The `scripts` folder may sit at any level of the
  package path; classes next to the referring class win (cars share one package across folders).
- `ScriptVm`: objects, virtual calls, `super`, statics, arrays, casts, `instanceof`, loops, short-circuit logic.
  Natives go to an `IScriptHost`. What the host does not provide is **unknown**; code behind unknown conditions is
  walked without taking effect. `partOnSlot(n)` with no game around returns an unknown tagged with slot `n`, which is
  how `if (!p) return "msg"` turns into a `required_slots` rule.
- Method parameters are numbered backwards (last parameter = local 1, local 0 = this).

## Runtime (`Parts/Logic`)

- `InstalledPart`: a part in an assembly: definition, wear/tear, tuning overrides by script field
  (`RPM_limit`, `mixture_ratio`, `advance`, `ratio[2]`...), parent and children by slot.
- `PartTreeBuilder`: list of parts → tree. Two slots mate when either names the other in an `attach` line, directly
  or through a slot it is `compatible` with (`PartsCatalog.CanMate`). Parts that find no place are reported, not lost.
- `PartScriptRuntime`: one script object per installed part on a stand-in `Chassis`; natives (`partOnSlot`,
  `slotIDOnSlot`, `getSlots`, `getSlotID`, `getWear`, `getTear`, `getCarRef`) answered from the tree. The slot a part
  hangs by leads back to its parent, as in SLRR. `Chassis.updatevariables()` then runs unchanged: the block collects
  bore, stroke, chamber volume, valve flow, cam timing, spark, fuel and air limits from its parts into a `DynoData`,
  the transmission leaves gears, final drive, drive type and diff lock on the chassis.
- `EngineDyno`: replaces the native `DynoData.calcDyno`. Mean value model per 250 rpm. Energy follows the scripts'
  figures: mixture flow × `mixture_H` × ideal air cycle efficiency `1 − CR^−0.4`, no friction taken off (builds limited
  by their fuel system land within 5% of their rated power that way). Breathing is first-order filling/emptying through
  the valves, fitted to the rated builds. Boost is baked into the curve.
- `EngineEvaluator` → `EngineReport`: runs or not and why (the scripts' words), curve, idle, limiter, inertia, gears,
  final, drive type, diff lock, mass, value.

Model check: `EngineBench <parts> rated`. Currently mean error +4%, mean absolute 15% over 15 builds
(Mopar 340 Six Pack: 310 hp @ 5750, 446 Nm @ 2750; factory 290 hp, 468 Nm).

## Assetto Corsa export (`Parts/Export`)

`AcEngineData.Generate(report, readFile)` returns the data files an engine build changes, made from the car's own
files with minimal edits (`IniText` keeps comments and order):

| File | Changes |
|---|---|
| `power.lut` | Flywheel torque × drivetrain efficiency (0.87 default; AC has no drivetrain loss of its own) |
| `engine.ini` | `INERTIA`, `LIMITER`, `MINIMUM`, `COAST_REF`, `DAMAGE/RPM_THRESHOLD` = what the weakest rotating part survives; `TURBO_n` removed (boost is in the curve) |
| `drivetrain.ini` | `GEARS` count/ratios/reverse/final, `DIFFERENTIAL` lock, clutch torque raised if needed. `TRACTION` is left alone: the body decides |
| `ai.ini` | Shift points |
| `setup.ini` | Gear ratio selectors removed; they would override the transmission |

`AcCarDataReader.ForCar(dir)` reads a car's data whether folder or `data.acd`. **Nothing writes into the AC install
yet**: applying the files for a race and restoring them afterwards (the rule for every AC change) is the next step.

## Bench (`tools/EngineBench`)

```
EngineBench <parts folder> list | rated | all | inputs | show <build id>
EngineBench <parts folder> export <build id> <car data folder> <output folder>
```

## Not done yet

- Hooking a player car to a part tree (save model, garage UI for mounting parts, shop).
- Applying exported data for a race, with restore.
- Running gear (tyres, brakes, suspension): same VM, different natives (`WheelRef`).
- Wear from mileage; tuning UI (the scripts' `buildTuningMenu` is not used, fields are set directly).
- Turbo lag (boost is static in the curve) and car mass change from the engine's mass.

## Bytecode reference

Worked out from the install's 7,589 class files (all parse to the last byte) and checked against the 1,402 classes
that have Java sources: 5,775 constructor values of 886 parts match.

File: `TUFA`, 12 byte header, then sections `tag(4) size(4) payload`: `CONS`, `FILD` (optional), `MTHD`, `CLSS`, `TREE`.

- `CONS`: count, then entries `kind(4)`: 0 string (length, bytes, NUL) · 3 resource (rpk path string, type id) ·
  4 class (name string) · 5 member (class, name-and-type) · 7 name-and-type (name string, signature string).
  Entry 0 = class name, entry 2 = base class name. Numbers are inline in the code, not in the pool.
- `FILD`, `MTHD`: two groups each (static, then instance): count + entries. Field = flags, name, signature,
  initializer tree (−1 = none). Method = flags, name, signature, tree, locals. Flags: 8 static, 0x40 native.
- `TREE`: count, then per tree an instruction count and instructions `op(1) line(2) [operand(4)]`.
  Operand present for: 01 02 03 06 07 08 09 0A 0B 0D 0E 11 12 14 15 16 17 19 1A 1B 1C 25 26 27.

| Op | Meaning |
|---|---|
| 01 n | local n (value or assignment target) |
| 02 n, 06 t, 2C … 2D | declare local n of type t, with initializer between 2C and 2D |
| 09 / 0A / 0B / 0C / 0E | string / float / int / null / resource constant (each literal is preceded by `07 28`) |
| 1B m | field (member constant); 19 c = class as a value; 1C s = field named s of the object on the stack |
| 20 | array element: `[index] [array] 20`; as a target: `[value] [index] [array] 20 [08 35]` |
| 11 k | an object path of k−1 elements follows, then the member (`11 1` = implicit this) |
| 27 n, 24, then 1A m / 12 s | call with n arguments: member constant / by name. 25 s = call on the previous result. 26 s = `super.s(...)`. 21 = `new`, 23 = `super(...)`, 22 = `this(...)` |
| 15 n | if false, jump to index+n. 14 n = jump (negative = loop; the else-jump sits right before an if's target) |
| 04 / 05 | `&&` / `||` short-circuit guard, followed by the 14 that skips the right operand |
| 28 / 29 | return value / return. 10 = end of a field initializer. 2A = end of method |
| 07 k | operator: 1 `\|\|` 2 `&&` 3 `\|` 5 `&` 6 `!=` 7 `==` 8 `>=` 9 `<=` 10 `>` 11 `<` 13 `>>` 14 `<<` 15 `-` 16 `+` 17 `%` 18 `/` 19 `*` 20 `~` 21 `!` 22 negate 23/24 `x++`/`x--` 25/26 `++x`/`--x` 27 call result 28 literal 30 new 31 new array 32 instanceof 33 cast 35 assignment as a value. 30/32/33 are followed by their type (`06` or `19`) |
| 08 k | statement: 35 `=` 36 `*=` 37 `/=` 39 `+=` 40 `-=` 46 `\|=` 23 `x++` 24 `x--` 27 expression 34 constructor call |

Parameters are numbered from the last one back: last parameter = local 1, local 0 = `this` (static: last = local 0).
