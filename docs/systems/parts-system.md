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

`SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>] [--replace <old pack>=<new pack>] [--drop <part id pattern>,...] [--rename <rpk pack>[:<selector>]=<pack id>,...]`

The content in use is made with `tools/convert-parts.ps1` (both Chrysler packs are installed in the SLRR folder, see
"Replacing a pack"). It comes out as:

```
engines/chrysler, gm, ford, ford_six   the engine packs (GM has Pontiac and Cadillac too; ford_six is the Falcon's engine)
engines/stock                          the base game's engine bits: batteries, N2O, and the engine-part roots
rims/mopar, falcon, hudson, opala, goodyear_eagle, stock
tyres/mopar, camaro69, goodyear_eagle, stock
exhaust/mufflers, stock
suspension/stock                       suspensions, springs, shocks, sway bars (named after SLRR's own cars: the only running gear there is)
brakes/stock
```

`--rename` names a pack after what it holds instead of the mod's file name: the pack id is the folder and the first
part of every part id, so `engines/chrysler/_Engine_block_340` where the rpk is `Chrysler_V8_pak`. With a selector
(`wheels:Tyre=tyres/sl_tuners`, `stock:bodypart=body/stock`, `stock:BodyPart*=body/stock`) only the parts it picks
out go there: those descending from a script class of that name, filed under a category of that name, or named so
themselves (`*` for anything). Selector rules apply in the order given, first match wins, the rest of the rpk goes
where its plain rename says. One rpk may so become several packs (rims and tyres out of `wheels.rpk`, six packs out of
the base game's `parts.rpk`); a replaced or replacing pack takes the whole rpk with it. Every other option names packs
by their new names. The old ids go into `part_aliases.json` like those of a replaced pack, so saves made before keep
working (see "Replacing a pack"). A full run removes converted packs it no longer produces (renamed, replaced or
dropped whole); the catalog would otherwise load both.

`stock` (the base game's `parts.rpk`) holds 96 scriptless entries without a model: the roots that every mod declares
its fit against (`stock/Wheel` is what every rim and tyre mounts by, `stock/ExhaustTip` every muffler, `stock/Brake`,
`stock/Spring_0051`... the suspensions). They are routed next to what needs them (`rims/stock`, `tyres/stock`,
`exhaust/stock`, `brakes/stock`, `suspension/stock`, `engines/stock`) and never go on sale: the shop only lists
scripted parts, plus what the engine builds use (batteries).

`--drop` leaves parts out altogether (`*` matches anything in the id). Engine builds around a dropped part are left out
as well. A save that holds such a part: see `BringUpToDate` under "Replacing a pack". What is dropped, for a game set
in 1960s America and mechanical parts only: the fictional and modern engine packs whole (Baiern/Emer OHC sixes,
Einvagen/Duhen/Ishima fours, the OHC V6 pack, the Buick LC2 turbo V6, SLRR's own MC/Prime/SuperDuty OHC V8s), Dexter's
Dodge and Chevrolet engines (one generic block mesh with numbers on it; the Chrysler and GM packs do them properly) and
its fictional drag block, two 2000s crate blocks (BluePrint 360, GM Performance Parts 427), SL Tuners' `wheels.rpk`
(142 of its 151 rims are 17"-21"), and everything that is body or interior: `interior` (seats, steering wheels),
`wings`, and the base game's neons, plates, woofers and body-part roots (routed to `body/stock`, then dropped). The
Ford six (`ford_six`) is a reskin of the Baiern OHC six but stands on its own: its parts attach to each other, not to
Baiern parts.

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
engine model is checked against. A title names and rates only the first list under it. Prose inside a list
(`// heads`) is a comment: after a comment the numbering counts on, after a title it starts over.

A run with a pack filter adds that pack's script classes to `_scripts`; only a full run replaces the folder and
writes `engine_builds.json`, `script_constants.json` and `part_aliases.json`.

### Meshes with too many triangles (`SlrrMeshSimplifier`)

Parts are modelled by hand with a few thousand triangles, the most detailed with some ten thousand. A mesh far
beyond that (Fireful0's hood scoops and air cleaner in the Chrysler pack: 25,000 to 90,000) came out of a CAD program
and carries triangles its shape does not need. `SlrrKn5Builder` hands anything above 20,000 to the simplifier, which
collapses edges in the order of the error they cause (quadric error metric) down to 10,000 or until a vertex would
end up more than about a millimetre from the surfaces it stands in for. Vertices only ever move onto neighbours, so
normals and texture coordinates are untouched; a vertex on a texture seam, a material border or a rim only moves along
it; a triangle that would fold over is left alone; a mesh modelled with both sides is worked on as one side and
doubled again. Checked with harness screenshots of the scoop and the air cleaner before and after: a few hundred
pixels differ. The converter prints every mesh it touched.

### Replacing a pack (`--replace`, `SlrrPackTwins`)

A later release of a mod takes the place of the one it grew out of: `engines/Mopar` (MagnumForce, 2010) was replaced
by `engines/chrysler` (Chrysler V8 Pack 4.5 Reboot, rpk `Chrysler_V8_pak`: the same meshes, 108 more parts, 4 more
blocks). Car scripts and build notes keep naming the old rpk, so the old pack **stays installed in the SLRR folder**;
it is read but not converted, and its output folder is removed.

Nothing carries over by id or name: the new release renames the files, renumbers the rpk and some slots (oil pan
9 → 10, carburettor 7 → 10), rebalances the scripts and says "small/big block" where the old one said "340/440".
`SlrrPackTwins` pairs every old part with its twin by, in order of weight: **mating** (a twin bolts onto the twins of
what the old part bolted onto; repeated until it settles, blocks and crankshafts anchor it because they carry their
displacement in both packs), mesh geometry (hash of positions), words and numbers of the names, slot ids, textures,
script class. The converter prints the pairs that are in doubt (another candidate as good, or a different mesh).

Every reference to an old part (car builds, notes, stock parts, attach lines of other packs) then resolves to the
twin, and `part_aliases.json` (old id → new id) is written for the game:

- `PartsCatalog.Get` answers old ids through the aliases; `CurrentId` gives the id a part goes by now.
- `SavedParts.BringUpToDate` (run over the whole save by `CarPartsService.BringUpToDate` when a game is loaded)
  gives saved parts their current ids and makes every joint again that the catalog no longer agrees with, the way
  the workbench would. A part that fits nowhere on its parent any more comes off: onto its owner's shelf, or out
  of the offer for cars and parts that are for sale. A car whose engine block the catalog no longer has at all
  (dropped parts) loses the engine and gets its factory engine again the next time it is looked at, worn like the
  car; loose parts, listings and ads made of such parts leave the save.

Checked with `EngineBench <new parts> renew <old parts>`: all 37 engines of the old pack come up to date and run; the
only part that comes off is the Hemi fan some 383 builds wore, which 4.5 no longer lets bolt to a 383 pump. The same
check after the packs were renamed (2026-09-21): all 132 engines of the previous conversion come up to date, nothing
comes off.

## Script VM (`Parts/Scripting`)

- `ScriptClass`: the "TUFA" class file. Sections CONS (pool), FILD, MTHD, TREE. Code is postfix expression trees
  with a source line per node; see the bytecode reference at the end.
- `ScriptClassLoader`: finds classes under a root laid out like SLRR. The `scripts` folder may sit at any level of the
  package path; classes next to the referring class win (cars share one package across folders).
- `ScriptVm`: objects, virtual calls, `super`, statics, arrays, casts, `instanceof`, loops, short-circuit logic.
  Natives go to an `IScriptHost`. What the host does not provide is **unknown**; code behind unknown conditions is
  walked without taking effect. `partOnSlot(n)` with no game around returns an unknown tagged with slot `n`, which is
  how `if (!p) return "msg"` turns into a `required_slots` rule.
  - The uncertain region runs from the undecidable `if` to the end of its branches. A loop around it returns to
    certain code and decides the `if` anew on every round.
  - `FirstAlternativeWins` (converter only): part lists filled inside undecidable branches are kept, first one wins.
    The game leaves it off; there, uncertain code changes nothing.
- Declared types count (`ScriptTypes`): `int n = maxRPM / 250` drops the fraction, a `float` field, parameter, array
  element or return value set from an int literal divides as a float from there on, `(int)`/`(float)` casts convert.
  Unset elements of `new float[n]` read as 0 (null for objects), outside the array as unknown.
- Statics belong to the class: initializers run once per VM, instances see the values. The holder exists before its
  initializers run, so `static Foo instance = new Foo()` ends; construction depth carries through initializers.
- Method parameters are numbered backwards (last parameter = local 1, local 0 = this).
- One `ScriptClassLoader` per catalog (`PartsCatalog.Scripts`, thread-safe): classes are read and parsed once.

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
  final, drive type, diff lock, mass, value. A part whose class file is not in `_scripts` is reported as that, since
  to the other scripts it would just look like a missing part. One verdict of the scripts is put right: with no
  carburettor or injection the compression limit they derive from the fuel is 0 and they complain about compression
  ("not more than 0.0:1"); the report says that nothing feeds the engine instead.
- Numbers in `properties`/`derived` come back from JSON as `long` or `double`; read them with `PartDefinition.Number`.

Model check: `EngineBench <parts> rated`. Currently mean error +4%, mean absolute 15% over 15 builds
(Mopar 340 Six Pack: 310 hp @ 5750, 446 Nm @ 2750; factory 290 hp, 468 Nm).

## Cars and their parts (`Parts/Cars`, `Services/Parts`)

Every car in the game owns a tree of parts; what is on the tree is what the garage draws, what the dyno measures and
(next) what Assetto Corsa gets to drive.

**Save model.** `PartInstance` (in `Models/GameState`) is a part as a save file holds it: catalog id, wear, tear, tuning,
the slot it is mounted on, the slot it is mounted by, and its children. `Car.Parts` are the parts that sit on the car
itself, by car slot (`PartInstance.CarEngineSlot` = 1, the scripts' own number; running gear will use 101+). A loose part
keeps whatever was on it, so the player's shelf (`Player.Parts`) and the ads hold sub-assemblies as readily as single
parts, a complete engine included. `PartTrees` goes between `PartInstance` and the runtime's `InstalledPart`
(`LiveTree` keeps the way back). `Car.HasPartsAssigned` tells a car that never had parts (an older save) from one whose
engine was pulled; `ICarPartsService.EnsureParts` gives the former its factory engine, worn like the car, the first time
the car is looked at (`EnsurePartsAsync` from a screen: the engine is built on a worker thread, the car changed on the
caller's).

**Factory engine.** `CarProfile.StockEngineBuildId` names an engine build of `engine_builds.json`. It is suggested by
`StockEngineMatcher` and can be overruled per car in the Car Catalog Editor (options come best match first;
`StockEngineIsManual` keeps suggestions away afterwards, also when the picked build leaves the catalog: the car then
runs on the best match until somebody picks again). Suggestions are written with `ICarProfileRepository.UpdateProfile`,
a read-change-write in one visit to the database, and the editor saves the same way, so neither overwrites what the
other stored in the meantime. The matcher scores the builds that run
(`EngineBuildIndex`: every build on the dyno once, about a second, kept for the session): same corporate family
(`MakeFamilies`: GM, Ford, Mopar... from the words of the name; a block belongs to the make whose cars it came in),
words the names share (a 427 is a 427), and how close the power comes to the car's own `bhp`. With the parts there are,
some cars get the nearest relative rather than their engine (no slant six: the Valiant gets a 318).
Check: `EngineBench <parts> cars <AC cars folder>`.

**Cars that have been around** (`EngineFactory.CreateTuned`). Used car listings carry their actual engine
(`UsedCarListing.Parts`, moved onto the car when it is bought) and about a third of them have been worked on, only the
way somebody plausibly would have: an engine of the same family with 5-50% more power, and bolt-on parts (air filter,
carburettor, manifold, headers, camshaft, flywheel, clutch) picked from what really goes on that very slot
(`PartsCatalog.FindMountable`), whose own children find their place on the new part. Every change has to leave an engine
that runs, makes no less power, stays under 1.6x the factory power, and is worth doing (more power or a dearer part).
A Hemi never ends up in a Chevrolet. Half of what the changes are worth shows in the asking price.
Check: `EngineBench <parts> tune <build id> [count]`.

**Workbench** (`Workbench`). A part comes off with everything that is mounted on it and goes back on the same way.
`FindPlaces` lists the free slots a loose part mates with (`CanMate`), blocks only ever go in the empty engine bay, and
kinds that never stack (head on head, block on block: copy-paste in the mods' configs) are refused. The list
(`PartKinds.NeverStacks`) was checked against every engine build: intake manifolds are *not* on it, 23 builds mount
theirs on a converter plate that is a manifold itself.
Check: `EngineBench <parts> bench <build id>` takes every part of a build off and has it find its way back.

**What fits a slot.** `PartsCatalog.FindMountable(parent, slot)` answers `CanMate` for the whole catalog from an index
built on first use: slots by the slots they name, and slots by the slots they stand in for.

**Garage.** The Parts toggle opens the workbench (`PartsWorkbenchViewModel` + `PartsWorkbenchView` around the 3D view):
- the engine sheet: block, power, torque, displacement and compression, gearbox, weight, and when it does not run the
  part scripts' own words for why;
- parts are picked **in the 3D view**: the part under the pointer glows and is named, a click selects it (orange glow,
  card with condition and worth, "Take it off"). Hidden parts are reached the way they are in a workshop: take off what
  is in the way;
- the shelf: picking a loose part shows it, glowing green, in every place it fits; a click on one mounts it.
  "Take apart" splits what is mounted on a shelf part off it (a manifold from its carburettor), piece by piece.
Every change is saved and costs game time (5 minutes a part, 30 at most).

Nothing pops. The cards and the navigation tiles they replace slide and fade through `Helpers/Reveal`
(`Reveal.IsShown` in place of a Visibility binding, `OffsetX/Y` for where an element comes from). Parts move too: a tree
is made anew after every change, so `InstalledPart.InstanceId` (the saved part's id) is how the viewport tells what left
and what arrived. What left lifts off the old model while the new one is put together (`GarageRenderer.MoveParts`: the
part nodes' `LocalMatrix` travels 0.45 m away from the middle of what stays, shrinking, 0.55 s), then the models are
swapped and what arrived flies in the same way backwards. A car change swaps without any of it.

`CarViewport3D` draws what it is given: `PartsCatalog`, `Engine` (an `InstalledPart` tree, a new one after every
change), `Candidates` (`MountCandidate`: a loose tree, the part and slot it would go on) and `SelectedPart`; it raises
`PartClicked` and `CandidateClicked`. `PartAssembler.Assemble` places a tree by its slots, `BuildModel` merges it into
one KN5 with a node per part (`NodeName(i)`, `AssemblyModel.Nodes[i]`), which is how a pick finds its part.
A part without a model (a handful of fuel rails and injectors) goes into the model as a small grey box
(`PlaceholderMesh`): parts are mounted and taken off by clicking them, so every part needs something to click.
The model on screen lags behind the tree while parts fly off and the next model is built, so whatever comes out of a
click is checked against what counts now: the workbench finds a clicked part again by its `InstanceId` in the current
tree, and a clicked place only counts if it belongs to the `Candidates` of the shelf part that is picked (the viewport
drops the places of the part picked before at once).
`GarageRenderer` holds two part models (mounted, candidates), picks by ray (`Pick`: bounding box first, then triangles,
about 0.2 ms; a place for a loose part wins over a mounted part in front of it, the places glow through everything
and some sit inside another part) and draws the glows: depth of the thing alone, `EffectPpOutline`, then scaled to a tint with a
blend-factor pass, one buffer per glow (car, candidates, selected, hovered).

**Shop** (`PartsShopService`, the Used Parts screen of the newspaper). Three pages: used parts from the ads
(`NewspaperAds.Parts`, 25-40 ads turned over daily by `PartsAdsRefreshTask`, now and then a complete engine), new parts
by mail order (the engine packs plus whatever else the builds use), and selling from the shelf. Filters: kind
(`PartKinds.GroupOf`, from the script classes), name, and "only what fits my car" (`FindFittingParts`: everything that
goes on any slot of the selected car's tree, taken slots included). New price is the script's `value`; a used part is
worth `value x tear x (0.3 + 0.7 x wear)` as in SLRR, ads ask around 60% of that, a trade-in pays 40%.
`AppSettings.PartsPriceScale` (0.2) brings the scripts' early-2000s dollars to the game's 1970 ones.

`catalog.db` is opened per call and exclusively; with parts work running in the background the two catalog repositories
now take turns through `CatalogDatabase.Open` (a semaphore, given back when the connection is disposed, whatever
happens while closing; a call must not open the database again while it has it open).

**Threads.** The game state and the save file belong to the UI thread. Whatever takes long (loading the catalog,
putting engines together for ads, market listings and cars without parts) runs on a worker thread on objects nobody
else has yet, and the result goes into the game state back on the calling thread (`RefreshAdsAsync`,
`EnsurePartsAsync`, `UsedCarMarketService.RefreshMarketAsync`). Two scheduled tasks therefore hand the day back before
they are done (`MarketRefreshTask`, `PartsAdsRefreshTask`); they are registered last, so the race simulation and the
event generation are finished first, and whoever saves after spending time awaits `SpendTimeAsync` before saving.
`ICarPartsService.IsAvailable` never throws: parts that cannot be read count as no parts, for the session.

## Assetto Corsa export (`Parts/Export`)

`AcEngineData.Generate(report, readFile)` returns the data files an engine build changes, made from the car's own
files with minimal edits (`IniText` keeps comments and order):

| File | Changes |
|---|---|
| `power.lut` | Flywheel torque × drivetrain efficiency (0.87 default; AC has no drivetrain loss of its own) |
| `engine.ini` | `INERTIA`, `LIMITER`, `MINIMUM`, `COAST_REF`, `DAMAGE/RPM_THRESHOLD` = what the weakest rotating part survives; `TURBO_n` removed (boost is in the curve) |
| `drivetrain.ini` | `GEARS` count/ratios/reverse/final, `DIFFERENTIAL` lock, clutch torque raised if needed, `AUTO_SHIFTER` `UP`/`DOWN` when the car has them. `TRACTION` is left alone: the body decides |
| `ai.ini` | Shift points: `UP` near peak power below the limiter; `DOWN` at most 90% of where the widest gear step lands after an upshift, so wide-ratio boxes do not hunt |
| `setup.ini` | Gear ratio selectors removed; they would override the transmission |

A car without `engine.ini` or `drivetrain.ini` throws (`FileNotFoundException`) instead of producing skeleton files; a
build without a limiter (`RPM_limit` 0) is limited at the end of its curve.

`AcCarDataReader.ForCar(dir)` reads a car's data whether folder or `data.acd`. **Nothing writes into the AC install
yet**: applying the files for a race and restoring them afterwards (the rule for every AC change) is the next step.

## Bench (`tools/EngineBench`)

```
EngineBench <parts folder> list | rated | all | inputs | show <build id>
EngineBench <parts folder> export <build id> <car data folder> <output folder>
EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car, with the runners-up
EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with
EngineBench <parts folder> bench <build id>            every part comes off and has to find its way back
EngineBench <parts folder> renew <older parts folder>  engines as a save made with an older conversion holds them, brought up to date
```

## Not done yet

- Applying exported data for a race, with restore. Open question for that step: a factory build does not make
  exactly the power of the AC car it stands for (the matcher picks the nearest engine, the dyno is within ~15%), so
  either the parts' curve replaces the car's, or the car's own curve is scaled by tuned/factory from the dyno.
- Opponents' cars get their parts lazily (`EnsureParts`) but nothing uses them yet.
- Working on an engine outside a car (an engine stand): on the shelf an assembly can be taken apart, but parts only
  go together on a car.
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
