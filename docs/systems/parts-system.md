# Parts System

Car parts come from Street Legal Racing: Redline (SLRR): models, mounting slots **and behaviour**. The game does not
reimplement SLRR's part logic, it runs it: the compiled part scripts of the SLRR install are executed by a small VM,
against the player's own assembly of parts. Only what SLRR itself kept in native code is written in C#: the engine
simulation and, new here, the export to Assetto Corsa physics data.

Scope: mechanical parts only: engine, transmission and running gear (tyres, rims, brakes, springs, shocks). Car bodies
stay Assetto Corsa's.

```
SLRR install ──SlrrPartsConverter──▶ Assets\Parts\                         (one-off, offline)
                                       <kind>\<pack>\pack.json + *.kn5      part definitions + models
                                       engine_builds.json                   complete engines as part lists
                                       script_constants.json                statics of the shared script classes
                                       _scripts\...\*.class                 the compiled scripts the parts need

Assets\Parts ──PartsCatalog──▶ PartTreeBuilder ──▶ PartScriptRuntime ──▶ EngineDyno ──▶ EngineReport ──▶ AcEngineData
                definitions     build → tree        scripts run on it     torque curve    verdict+figures   AC data files

Car.Parts ──RunningGear.Mounted──▶ AcRunningGearData ─┐
                                                       ├──▶ AcCarBuild ──▶ RaceCarDataService ──▶ CarDataOverlay ──▶ acs.exe
Car.Engine ──EngineEvaluator──▶ AcEngineData ──────────┘     all files      per car of a race     apply, race, restore
```

The converted content lives in the repo, `Street Rod AC\Assets\Parts` (187 MB), and is copied next to the exe at build;
`AppSettings.PartsPath` reads it from there. It is made from a commercial game and community mods: the repo stays
private.

## Why a VM instead of a port

- 242 mod part classes override `updatevariables()` with their own slot-specific logic; a port of the shared classes
  would ignore them.
- The install's *shared* classes are modded too (`Part.tHUF2USD` divides by 281 instead of 211, used price factor 0.4
  instead of 0.7, an extra hydrogen fuel). The released Java sources (2.2.1) are good for reading, not for values.
- The scripts are simple (field math, slot lookups, a few loops), and the converter needed an interpreter anyway to
  read part values out of class files that ship without sources.

## Converter (`tools/SlrrPartsConverter`)

`SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>] [--replace <old pack>=<new pack>] [--drop <part id pattern>,...] [--rename <rpk pack>[:<selector>]=<pack id>,...] [--merge <part id pattern>=<part id>[*<count>],...] [--model <part id pattern>=<part id>,...] [--fit <part id pattern>:<slot>=<fitting>[+<fitting>],...] [--shift <part id pattern>:<slot>=<dx>/<dy>/<dz>,...] [--single <part id pattern>=<count>@<spacing>,...] [--pads <part id pattern>:<slot>=<fitting>*<count>@<spacing>@<dx>/<dy>/<dz>[@<air fitting>],...] [--name <part id pattern>=<display name>,...] [--shifts <slot_shifts.json kept for good>] [--absorb <slot_shifts.json written by the game>,...] [--previous <earlier conversion>] [--twin <old part id>=<new part id or ->,...] [--measure <part id pattern>,...]`

The content in use is made with `tools/convert-parts.ps1` (both Chrysler packs are installed in the SLRR folder, see
"Replacing a pack"). It comes out as:

```
engines/chrysler, gm, ford, ford_six   the engine packs (GM has Pontiac and Cadillac too; ford_six is the Falcon's engine)
engines/generic                        universal-fit induction parts out of the engine packs: aftermarket carburettors,
                                       air cleaners, scoops, roots blowers (see "Parts that fit any engine")
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
the base game's `parts.rpk`), and several rpks may feed one pack (`engines/generic` takes parts of three engine
packs; part names must not clash); a replaced or replacing pack takes the whole rpk with it. Every other option names
packs by their new names. The old ids go into `part_aliases.json` like those of a replaced pack, so saves made before
keep working (see "Replacing a pack"): a part picked out by a selector is aliased both from the rpk's name and from
the pack the rest of the rpk goes to (`engines/chrysler/Air_cleaner_HOLLEY` → `engines/generic/Air_cleaner_HOLLEY`).
A full run removes converted packs it no longer produces (renamed, replaced or dropped whole); the catalog would
otherwise load both.

`stock` (the base game's `parts.rpk`) holds 96 scriptless entries without a model: the roots that every mod declares
its fit against (`stock/Wheel` is what every rim and tyre mounts by, `stock/ExhaustTip` every muffler, `stock/Brake`,
`stock/Spring_0051`... the suspensions). They are routed next to what needs them (`rims/stock`, `tyres/stock`,
`exhaust/stock`, `brakes/stock`, `suspension/stock`, `engines/stock`) and never go on sale: the shop only lists
scripted parts, plus what the engine builds use (batteries).

`--drop` leaves parts out altogether (`*` matches anything in the id). Engine builds around a dropped part are left out
as well. A save that holds such a part: see `BringUpToDate` under "Replacing a pack". What is dropped, for a game set
in 1960s America and mechanical parts only: the fictional and modern engine packs whole (Baiern/Emer OHC sixes,
Einvagen/Duhen/Ishima fours, the OHC V6 pack, the Buick LC2 turbo V6, SLRR's own MC/Prime/SuperDuty OHC V8s), Dexter's
Dodge and Chevrolet engines and his drag block (his pack is replaced by the user's Ford V8s, but a replaced pack's parts
are paired with the new one's, and a Duster with a Ford 292 is no Duster), two 2000s crate blocks (BluePrint 360, GM
Performance Parts 427), SL Tuners' `wheels.rpk` (142 of its 151 rims are 17"-21"), everything that is body or
interior: `interior` (seats, steering wheels), `wings`, and the base game's neons, plates, woofers and body-part roots
(routed to `body/stock`, then dropped), Dexter's 2-bar "Super Blower", and the Ford V8 pack's scriptless dress-up and
nitrous bits (a part without a script pairs with anything). The Ford V8s (`engines/ford`, rpk `ford_v8s`) are the
user's own too (Nov 2016, `MODS ENGINES\OTHER MODS ENGINES\ford_v8s`): 260, 292, 302, 312, 332, 351 Boss, 390, 429
Cobra Jet and 460 with their period boxes (Borg-Warner T10/Super T10/T18, Ford HED/HEG 3-speeds), a Mopar-pack clone
by mesh (all 283 identical to Chrysler 4.5's) with Ford scripts and 80 Ford textures, and the 2010 Mopar slot
numbering (carburettor 7, blower pad 9); its aftermarket carburettors and Edelbrock blower are universal-fit like
the other packs' (see "Parts that fit any engine"), its air filters keep their Ford decals and stay Ford parts. The
Ford six (`ford_six`, rpk `ford_l6`) is the user's own Ford Falcon 188/221/221 SP
pack (Dec 2016, `MODS ENGINES\OTHER MODS ENGINES\ford_l6`): a pushrod engine with 47 meshes they modelled (block,
heads, manifolds, carbs, timing set, pump, distributor, radiator...) and the Baiern/Emer parts it was grown from still
inside, dropped here. It replaces the Ford 221 pack (`fordi6_data`, Nov 2016, Baiern DOHC meshes with Ford scripts)
that car scripts and build notes name, see "Replacing a pack"; the 2012 Baiern reskin that stood in before both
("ford falcon block") is archived next to them, its ids answered through "An earlier conversion". Its blocks extend
`Block_Inline_OHV`, a class SLRR never shipped: see "Script VM", stand-in classes.

`--merge` leaves parts out too, but names the part that stands in for each: builds, saves (through
`part_aliases.json`), stock-part lists and the attach lines of other parts that named a merged part get the stand-in,
and the stand-in inherits the merged part's fit (its attach and compatible lines are grafted onto the stand-in's slot
of the same id; a part that mounts by a single slot mounts by it whatever the number, Ford's air cleaners hang by 12
where Chrysler's hang by 11). Used for the same part twice with the same script values (the Chrysler pack's plain
Holley 4-bbl next to its "street" one) and for **transmissions from after the 1960s**, which builds and saves swap for
the period box of the same engine: TKO 500/600 → A833; Tremec T-56 and TKO, Richmond 5- and 6-speeds → Richmond Super
T-10; TH-700-R4 → TH-400; TH-200-4R → TH-180C; 4L30-E → TH-125C; the Ford V8s' Tremec T45/T56 → Borg-Warner Super
T10. The Cadillac 500's TH-125C is a 1980s box kept because it is the 500's only one. The converter prints a merge
whose stand-in lacks a slot the merged part fitted by.

Carburettors of different packs are **not** merged even when they are the same product: the carburettor script sets
the engine's mixture (the Chrysler pack runs 8:1 on gasoline, GM's Dominators 9-14:1 on fuel type 3, Dexter's Fords
ran 12.5-13.5:1) and its fuel cap, and every pack's engines were rated with their own. A trial that merged them re-tuned
whole engines (the GM 427 fell from 659 to 419 hp). Nor are the two GM Weiand 8-71s merged: same mesh, different
boost.

### Parts that fit any engine (`--fit`, `--model`, `engines/generic`)

An aftermarket carburettor bolts to a flange pattern, not a brand; an air cleaner sits on the carburettor's air horn;
a roots blower on a blower manifold. SLRR packs know nothing of each other, so their attach lines lock every such
part to its own pack (three Holley four-barrels, each fitting one make). `--fit` gives a slot a **standard fitting**
(`fits` in pack.json): `carb:2bbl`, `carb:4bbl`, `carb:2x4`, `carb:3x2` on carburettor bases, `air:single` and
`air:inline` (a 2x4 or 3x2 set) on air cleaners and scoops, `blower:roots` on blowers. The slots that **take** a
fitting (`takes`) are found from the attach lines: every slot a fitted slot attaches to, written on either side, so a
pack's own carburettors tell which of its manifold pads are 4-bbl pads. The game (`PartsCatalog.CanMate`,
`FindMountable`) mates two slots by a shared fitting as well as by name, through stand-ins on either side (a blower's
carburettor pad that stands in for a dual-quad manifold pad takes what that pad takes). A pad no fitted part of its
own pack names gets its fitting by rule (`takes:air:single` on the Chrysler 2-bbl's air horn, which only knows
Chrysler's factory cleaners). Slot numbering is the engine framework's (manifold pad 7, carburettor base 10, its air
horn 11, blower pad 9, drive belt 15).

**Slot conventions differ between packs**, which cancels within a pack and shows where parts of two packs meet.
Measured on the meshes (`--shift` rules in `convert-parts.ps1`): the Chrysler pack, and Dexter's Ford pads, put the
carburettor slot 6.5 cm above the carburettor's base and the manifold pad 6 cm above the flange; GM puts both at the
flange; GM's stock single carburettors are modelled 18 cm ahead of their origin and its single-carburettor pads sit
18 cm back to match. Untreated, a GM carburettor rode 7 cm above a Chrysler pad and a Chrysler one sank 7 cm into a
GM manifold. `--shift` moves a slot in its part's space; both sides of a pack's joint move by the same amount, so
nothing moves within the pack: Chrysler/Ford carburettor slots and pads come down to the flange (borrowed Chrysler
models carry Chrysler geometry and come down too, and so do the crossram carburettors and manifolds, since the
crossram pads take any four-barrel), GM's stock single carburettors and pads go forward to the centre. Blowers needed nothing. Checked
with harness close-ups: a Chrysler Holley and Edelbrock cleaner on a GM 327, GM's stock carburettor with a K&N on a
340, the Chrysler 2-bbl on GM's 2-bbl manifold, the borrowed-model Dominator with a Summit filter on a Hemi, a
Summit scoop on a 340, the Chrysler Weiand on a GM 8-71 manifold and the GM one on a 440, Demon dual quads and the
Holley 650 on Dexter's Ford 302, the GTO Tri-Power filters on the borrowed tri-power; and every pack's own
carburettor on its own manifold, unchanged.

`--measure <part id pattern>,...` (`convert-parts.ps1 -Measure`) is how a convention is read before a fitting lets
two packs meet: it converts nothing and prints every matching part's slots next to the bounds of its meshes, in the
model's space and before any shift. The user's Ford V8 pack (2026-09-22) measured as a 2010 Mopar clone by cfg as
well as by mesh: its carburettors' slot 7 at the carburettor's centre, its pads 2 cm over the manifold top, its
Edelbrock blower's pad 9 where Chrysler's is - the same figures on the shared meshes, so the same 6.2 cm down to the
flange for its carburettors (routed to `engines/generic`, drawn with the Chrysler models: their scripts run 13.5:1
where Chrysler's run 8:1, so they are parts of their own, not merges), its pads and the blower's pad. Its air filters
already carried Chrysler's offsets on the shared meshes within 1.5 cm, except three that sat at their mesh centre (the
K&N and Edelbrock 2x4 ovals, the Motorcraft 2x4 scoop) and got the Chrysler part's offset by `--shift`.

Factory carburettors and air cleaners (Carter AVS, GM's stock 2/4-bbl and Quadrajet-style cleaners, the Mopar pie
tins, Six Pack cleaners, the Shaker, the GTO Tri-Power and Corvette air boxes) keep their attach lines and stay with
their brand: a used car tuned by `EngineFactory` gets aftermarket parts of any make, never another make's factory
part. Kits stay too: the Paxton and Edelbrock E-Force superchargers, the Hemi crossram carburettors. Blowers bring
their own pack's drive belt (belts are positioned in the blower's frame, and reach the crank pulley of any V8 near
enough).

The universal-fit parts are routed into `engines/generic` (the shop and the catalog never read pack ids, only the
folder and the ids change). The GM and Ford packs' carburettors are crude blocks next to the Chrysler pack's;
`--model` draws a part with **another part's model**, its own script untouched: the model is converted again under
the borrower's name (into the borrower's pack folder) and the borrower's slots take the donor's positions, since slot
positions are in the model's space (a part that mounts by a single slot takes the donor's mounting slot whatever its
number: GM carburettors hang by 10, the Carter by 12). The borrowers: GM's Holley 2-bbl, 1050 Dominator,
"hardcore" 1050, 750 Dominator and blower 2-bbl, the Ford V8 pack's two Holley streets, two Edelbrocks and Holley
dual-quad set, and GM's factory carburettors, which get the Carter AVS (a factory carburettor's looks rather than
another Holley's). GM's injection
(the Weiand methanol stacks, the '63 fuelie rail, the Holley rails) is decent and GM-only and keeps its own models.

### Placement mode in the garage (F5, `SlotShifts`)

A tool for fitting the converted parts by eye, not gameplay. With a part picked in the workbench, **F5** turns
placement mode on: the arrows move the part across and fore-aft, PgUp/PgDn up and down, in the *engine's* axes
(right, up, towards the radiator), whatever the camera does; a step is 5 mm, Ctrl 1 mm, Shift 2 cm. **Tab** switches
between the part's own slot and the pad the part sits on (a carburettor wrong on one manifold is the pad's fault, one
wrong everywhere is its own). A part's own slot is the part's wherever it goes, so nudging it moves every copy: a part
that is one of several of the same on its parent (the carburettors of a dual quad or Six Pack, the filters on them)
starts on its pad instead, which is that place's alone. **R** takes the slot back to where the packs put it, F5 or
Esc ends the mode; a readout in the part card says what is being moved and by how much so far. `CarViewport3D` turns the step into a move of a slot in that slot's
part's space (the part's own mounting slot moves the other way, since the part hangs so that its slot lands on the
pad), `PartsCatalog.ShiftSlot` changes the part in place, and the parts are laid out again without rebuilding the
model (`GarageRenderer.PlacePart`).

Every step is written to `slot_shifts.json` next to the content the game runs on (part id → slot id → offset, metres,
the part's axes, the sum of every nudge). The catalog applies the file when it loads, so the fit sticks between runs;
`convert-parts.ps1` folds such files (from the output folder and the build folders, `--absorb`) into
**`tools/slot_shifts.json`**, kept for good and applied last on every conversion, and takes the game's files away,
so the packs stay the truth and nothing is applied twice. The kept file is plain enough to edit by hand.

### Every carburettor part is one carburettor (`--single`, `--pads`, the shared air slot)

SLRR sells a dual-quad or a Six Pack as one part with one model of two or three carburettors, on one manifold pad.
The user wants one carburettor per part. So:

- **Pads.** `--pads` turns a pad that took a set into one pad per carburettor, in a row along the engine axis about
  where the pad was (the pad keeps its id for the middle one, or the rear one of a pair - the pad the manifold script
  reads; the others are 300, 301, ...), plus slot **311** (`PartSlot.SharedAirSlot`) over the row for an air cleaner
  that spans the set, placed where the set's own air-horn slot was (offset per rule; Chrysler four-barrel sets
  (-0.043, 0.108, -0.040), Six Packs (0.003, 0.073, -0.006), crossrams (-0.043, 0.146, -0.040); every pack's pads use
  the Chrysler offsets and GM's ovals are shifted to match). Pads that stood in for a set pad (the GM 427 tunnel ram,
  the GTO and Corvette Tri-Power intakes, GM blowers) need rules of their own: stand-ins are followed at run time,
  not by the converter.
- **Sets.** A set whose single carburettor exists with the same script values is `--merge`d into it `*count` (the
  Edelbrock and HOLLE Six Pack sets, GM's stock dual quad and tri-power); one with values of its own is kept and its
  model **sliced** (`--single`, `SlrrKn5Slicer`): every triangle goes with the item whose nominal centre is nearest
  (items evenly spaced about the model's centre), one item stays and is moved onto the origin; the slots stay as
  they are, since the Chrysler pack's slot offsets are the same for a set and a single. Sliced: the Dominator and King
  Demon pairs, the Road Demon Six Pack, the three Hemi crossram sets (which keep their fuel figures and fit only
  crossram manifolds, by the `carb:crossram` fitting), GM's 750 Dominator pair (14:1 where its 1050 runs 9:1) and
  blower 2x2, Dexter's dual sets, and two air-filter rows (GM's dual round filter, the GTO Tri-Power box). `--name`
  gives them names that no longer say 2x4. Either way a build that named the set gets **count** of the single, one
  per pad (`multiplicity`, keyed by every source id that resolved to the set, twins of the replaced Mopar pack
  included), and the fit of a merged set is grafted onto its single.
- **Air cleaners over a row** (ovals, Six Pack cleaners, the Shaker, scoops for pairs) named the set's air-horn slot;
  the converter moves those lines onto slot 311 of every pad the set sat on (`Repoint`), and the aftermarket ones get
  `air:2x4`/`air:3x2` fittings that the 311 slots take. Single cleaners still sit on a carburettor's own horn
  (`air:single`; the carburettors sliced out of sets, whose horns only the set's cleaner named, get it by rule). GM's
  carburettor scripts *require* a cleaner on their own horn, so `PartScriptRuntime` answers `partOnSlot(horn)` of a
  carburettor whose horn is empty with the part on its parent's slot 311: a carburettor in a row breathes through the
  cleaner over the row. Only a horn (a slot that takes an `air:` fitting, `PartSlot.TakesAir`) is answered that way; a
  nitrous slot stays empty. `SavedParts.BringUpToDate` looks at every joint on load, not only when an id changed (a
  sliced set keeps its id and loses its horn's fit), and tries a joint one part up when it no longer holds on the
  same part, so a saved oval on a set moves over the manifold's row; the set itself becomes one carburettor in a
  save (the other pads wait for the shop).

Checked: every build's power unchanged except the GTO 389 family (348 → 341 hp: GM's tri-power ran 12.5:1, its 2-bbl
runs 12.0), one GM 427 build whose dual-quad set never found the tunnel ram's stand-in pad now runs (161 → 542 hp);
`renew` of all engines of the previous content: nothing comes off; `bench` round trips on 10 builds; harness
close-ups of the Six Pack under its factory cleaner, two Dominators with a round filter each on the 427 tunnel ram,
the Tri-Power's three filters on three 2-bbls, two King Demons under the Edelbrock oval on a 340, two Dominators and
a K&N on the Weiand blower, two GM Dominators under a Summit oval on a 283 (runs: the shared cleaner counts for
both), GM's factory carburettor in Carter clothes on a 327.

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

**Engine kits** (`origin` "kit", `SlrrEngineBuilds.FromKits`): the `Set` classes a pack's rpk names, whose `build()`
puts parts in the inventory (`SlrrScriptEvaluator.Kit` runs it against an inventory of no class and notes every
`insertItem`). Where a mod's author wrote complete engines that way they are builds like the others: the user's Ford
six (`kit_188`, `kit_221`, `kit_221_SP`) and V8 packs (one kit per displacement), GM's crate engines, Chrysler's
`Kit_318_block`... A kit without a block is an upgrade (a blower with its manifold), no engine; one whose parts a car
or notes build already lists (with a battery on top, say) adds nothing and is left out, so a rated notes build wins
over the kit it was written from.

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
it is read but not converted, and its output folder is removed. The same way, the user's Ford six (`ford_l6`)
replaced their Ford 221 pack (`fordi6_data`) and their Ford V8s (`ford_v8s`) replaced Dexter's `DEXTERV8s`
(2026-09-22): three replacements, each with the old pack still installed.

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

Where the matcher has nothing to go on, `--twin <old part id>=<new part id>` writes the pair by hand, and
`--twin <old part id>=-` says the new release does without the part. The Ford six needed 16: the old pack was a DOHC
reskin, the new one is the pushrod engine, so its exhaust camshafts and camshaft bearing bridge have no twin, the
intake camshafts are the single camshafts, the drive belt is the timing chain, and the Sprint and racing head,
manifold and header have their look-alikes but the matcher settled for the stock ones. A rule naming a part that is
not there stops the run. The old builds still do not run on the new pack: its blocks demand a timing cover, fuel pump,
distributor, coil, water pump, radiator and starter that the old pack never had, so the car builds and old saves of
the Ford six come up to date but "missing the timing cover" (`renew` says so); the pack's own kits are the builds that
run, see "Engine builds".

### An earlier conversion (`--previous`, `EarlierConversion`)

A release of a mod that keeps its rpk but renames its files (the Ford six: `Baiern_Kraftwerk_2_5_block.cfg` became
`Ford_188_Block.cfg`, same resource `0x41`) changes part ids without there being two packs to pair. Part ids are the
cfg names, but the SLRR game knows a part by its rpk resource id alone, and the converted parts keep it
(`source_type_id`, the pack's `source` says the rpk). `--previous <folder>` names an earlier conversion, normally the
content the game runs on: `convert-parts.ps1` passes the repo's `Assets\Parts` whatever folder the run writes to, so a
scratch run sees the same. Read before anything is written, it adds to `part_aliases.json`:

- every part it had whose id the run no longer produces, pointed at the part its resource is now (a pack fed by
  several rpks is tried against each; two answering differently is nobody's part), unless a rule already aliases it;
- its own aliases, kept while they still lead to a part through the aliases as they end up (the game follows chains
  of up to 8).

So the ids ratchet: an alias once written stays as long as its target exists, and a save made with any earlier content
loads. Checked the way a replaced pack is: `EngineBench <new parts> renew <old parts>` after the Ford six swap brought
all 12 engines of the old pack up to date, nothing came off.

## Script VM (`Parts/Scripting`)

- `ScriptClass`: the "TUFA" class file. Sections CONS (pool), FILD, MTHD, TREE. Code is postfix expression trees
  with a source line per node; see the bytecode reference at the end.
- `ScriptClassLoader`: finds classes under a root laid out like SLRR. The `scripts` folder may sit at any level of the
  package path; classes next to the referring class win (cars share one package across folders). **Stand-in
  classes**: a class a mod extends that the game never shipped is made from the one its author copied
  (`ScriptClass.DerivedAs`: the pool's names swapped, methods that only made sense for the original left out).
  `Block_Inline_OHV` (the Ford six's blocks) is `Block_Vee_OHV` less the second cylinder head; its author's source sits
  in the SLRR notes, never compiled, as does their `OHV_CylinderHead` that would accept it - so an object of a stand-in
  also passes `instanceof` the class it was copied from (`ScriptClass.CopiedFrom`), which is what the stock head asks.
  A compiled class file, once there, wins over the stand-in.
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

**Running gear** (`Parts/Cars/RunningGear`, `RunningGearFactory`). Every car also carries its wheels: on the car's own
slots as the chassis script numbers them (rim 101+i, brake 111+i, shock 301+i, spring 311+i for corner i: 0-1 front,
2-3 rear, even numbers left), the tyre on the rim's slot 2. `Car.HasRunningGearAssigned` works like `HasPartsAssigned`:
a car without it gets its factory set the first time it is looked at (`EnsureParts`), worn like its tyres. The factory
set is matched to what the car's own Assetto Corsa data says (`AcCarSpecs`, read from the car's `tyres.ini`,
`brakes.ini`, `suspensions.ini`, `car.ini`, folder or `data.acd`): the tyre nearest in width (one tyre for the car
unless it is staggered by 3 cm or more, the plainest compound of that width), the plainest rim that takes it, and the
brake, spring and shock nearest to the car's figures, which is the smallest the catalog has more often than not, since
the source game's parts are all stiffer and stronger than a road car of 1970 (so what the shop sells is an upgrade).
The choice is deterministic: it is also the baseline the export measures against. A tyre only goes on a rim of the
same diameter whose width it takes (`RunningGear.TyreFitsRim`, the tyre script's own rule); rims, brakes, springs and
shocks go on any free corner (`Workbench.FindPlaces`). What each part does to the car is read off its script fields
with the framework's formulas: brake torque = clamp force × calipers × pad-on-disc friction × disc radius, less with
wear; spring rate; bump damping and rebound (gas, oil, gas-oil); tyre width, radius, rim radius, grip (less as it
wears), load capacity, rolling resistance, pressure; rim offset and mass. Sway bars and suspension arms are inert in
the source game (their physics calls are commented out) and stay out of the shop. In the garage the running gear is
drawn at the hubs (`CarPartsLayout`, `CarViewport3D.Gear`) and picked, taken off and put back like engine parts.
Check: `EngineBench <parts> gear <AC cars folder>`.

**Threads.** The game state and the save file belong to the UI thread. Whatever takes long (loading the catalog,
putting engines together for ads, market listings and cars without parts) runs on a worker thread on objects nobody
else has yet, and the result goes into the game state back on the calling thread (`RefreshAdsAsync`,
`EnsurePartsAsync`, `UsedCarMarketService.RefreshMarketAsync`). Two scheduled tasks therefore hand the day back before
they are done (`MarketRefreshTask`, `PartsAdsRefreshTask`); they are registered last, so the race simulation and the
event generation are finished first, and whoever saves after spending time awaits `SpendTimeAsync` before saving.
`ICarPartsService.IsAvailable` never throws: parts that cannot be read count as no parts, for the session.

## Assetto Corsa export (`Parts/Export`)

What a car's parts make of it goes into the car's own data files for a race, by changing them as little as possible
(`IniText` keeps comments, order and untouched lines; a section that occurs twice, a second tyre compound, is reached
by occurrence). `AcCarBuild.Generate(catalog, CarBuild, readFile)` puts it together: the engine's files, then the
running gear's over them, and a list of `Problems` that keep the car from being driven (no engine, an engine that does
not run, a corner without a wheel, brake, spring or shock). `AcCarData.Open(carDirectory)` reads a car's data whether
it is a folder or `data.acd` (`AcdFile` reads the packed form; the key is worked out from the folder name the way the
game does it, checked against every packed car of both installs on this machine).

**Engine and transmission** (`AcEngineData`): the curve **replaces** the car's. The file says what the dyno says.

| File | Changes |
|---|---|
| `power.lut` | Flywheel torque, exactly (no drivetrain loss taken off: `DrivetrainEfficiency` = 1) |
| `engine.ini` | `INERTIA`, `LIMITER`, `MINIMUM`; `COAST_REF` = 12 Nm per litre at the limiter × the build's friction over a stock oil pan's (`engine_friction_fwd`, 0.0002 is the median of the builds, clamped ½..2: a racing pan lets the engine spin freer); `DAMAGE/RPM_THRESHOLD` = what the weakest rotating part survives; the car's own `TURBO_n` removed |
| `engine.ini [TURBO_0]` | Only with a turbocharger part: the game's own turbo (lag, gauge, boost damage) fitted to how much more the boosted curve makes than the same engine without its charger: `MAX_BOOST` = `WASTEGATE` = the peak gain, `REFERENCE_RPM` where 95% of it is reached, `GAMMA` by least squares on the way up. The lut is divided by the boost the game adds back, so the torque with the throttle open stays the dyno's. A supercharger is crank-driven and has no lag: it stays baked into the curve |
| `drivetrain.ini` | `GEARS` count/ratios/reverse/final, `DIFFERENTIAL` lock; `CLUTCH/MAX_TORQUE` = the clutch part's clamp figure (`maxF`) × 2.6 Nm, set so every factory engine's clutch holds it (the big-block packs put a 300 behind 770 Nm): a built engine outgrows a stock clutch and it slips; `AUTO_SHIFTER` `UP`/`DOWN` when the car has them. `TRACTION` is left alone: the body decides |
| `ai.ini` | Shift points: `UP` near peak power below the limiter; `DOWN` at most 90% of where the widest gear step lands after an upshift, so wide-ratio boxes do not hunt |
| `setup.ini` | Gear ratio selectors removed; they would override the transmission |
| `car.ini` | `TOTALMASS` moves by what the engine weighs more or less than the car's factory build (`FactoryEngineMass`) |

A car without `engine.ini` or `drivetrain.ini` throws (`FileNotFoundException`) instead of producing skeleton files; a
build without a limiter (`RPM_limit` 0) is limited at the end of its curve.

Blowers: the dyno's boost peaks where the charger script says it works best. The script hands over its working band
in engine speeds multiplied by the square of the drive ratio, so `EngineDyno.Boost` compares it with the engine speed
times that square (it compared it with the speed times the ratio before, which put a 4:1 roots blower's peak at
10,600 rpm and made it behave like a big turbo: 55% boost at 3,000 rpm and rising). No build of `engine_builds.json`
is blown, so the rated table did not move; a mounted blower now makes its torque low down.

**Running gear** (`AcRunningGearData`): the car's figures are **scaled**, never replaced. Whatever units the source
game and Assetto Corsa think in, the car's author set its numbers for its factory parts; what is mounted moves each
number by its ratio to the factory part, averaged over the two corners of the axle. A car on its factory parts comes
out byte for byte (a factor of one leaves the line alone; a file without an edit is not written), a worn car a little
weaker (brake torque `0.2 + 0.8 √wear`, grip `0.85 + 0.15 wear`).

| File | Scaled by mounted / factory |
|---|---|
| `tyres.ini`, every `FRONT*`/`REAR*` compound | `WIDTH`, `RADIUS`, `RIM_RADIUS` (and `ANGULAR_INERTIA` by radius²), `DX_REF`/`DY_REF`/`DX0`/`DY0` by grip, `FZ0` by load capacity, `ROLLING_RESISTANCE_0/1`, `PRESSURE_STATIC`/`PRESSURE_IDEAL` |
| `brakes.ini` | `MAX_TORQUE` and `FRONT_SHARE` from the front and rear torques scaled separately; `HANDBRAKE_TORQUE` by the rear |
| `suspensions.ini` | `SPRING_RATE` (or a coil-over's `RATE`), `DAMP_BUMP`/`DAMP_FAST_BUMP`, `DAMP_REBOUND`/`DAMP_FAST_REBOUND`; `TRACK` moves by twice the rim offset difference, `HUB_MASS` by the unsprung mass difference |
| `car.ini` | `TOTALMASS` by the running gear's mass difference |

What has no lever on either side stays a gate: batteries, alternators, water pumps, radiators, distributors and the
like are plain `Part`s in the source game (no physics fields) and Assetto Corsa has no battery or cooling model (CSP
only synthesises gauge channels). They matter through `required_slots`: without them the engine does not run, and a car
whose engine does not run does not race.

## Apply and restore (`Services/CarDataOverlay`, `Services/Race/RaceCarDataService`)

The rule for every change to the Assetto Corsa install: the originals are kept aside first, the change is the
smallest that does the job, and whatever happens the originals go back. `CarDataOverlay.Apply(carId, files)` keeps the
car's originals under `%APPDATA%\StreetRodAC\AcRestore\<car>\files` with a `manifest.json` (which files, whether
each existed), written before anything in the install changes, then writes the files into the car's `data` folder.
A car that ships packed (`data.acd`, no folder: the GT500) gets the whole of its data unpacked into a `data` folder
for the race, since the game reads the folder when there is one, and the folder removed after. `Restore`/`RestoreAll`
put everything back and delete the kept copies; the launcher calls `RestoreAll` in its `finally`, and `App.OnStartup`
calls it too, for what a crash or a power cut left behind.

**Sound** (`Parts/Export/AcCarSound`, the overlay's sfx section). Assetto Corsa hangs a sound on a car folder: it opens
`sfx\<car>.bank` by file name and finds `event:/cars/<car>/engine_ext` and the rest by GUID through the car's
`sfx\GUIDs.txt` (a Kunos car has no file of its own and is in the install's `content\sfx\GUIDs.txt`). The GUIDs inside
a bank are its author's and never change when the bank is reused, so a sound is a bank plus its GUID lines, and it
plays under any car once the lines are written for that car's id. `CarSound(BankPath, GuidsText, DonorId)` is one;
`AcCarSound.FromCar(carDirectory, masterGuids)` reads a car's own, and `GuidsFor(carId)` writes the lines for another
car: buses, VCAs and snapshots as they are, the `common` bank and the donor's, the donor's events and the ones outside
any car (collisions, surfaces), everything else dropped, since mod authors ship the whole master file with a hundred
other cars in it more often than not. `CarBuildResult.Sound` carries the choice; `CarDataOverlay.Apply(carId, files,
sound)` moves the car's own bank and GUIDs into `sfx\_streetrod_keep` (a rename, whatever their size), hard-links the
new bank in under the car's name (a copy only when the two are not on one volume), writes the GUIDs, and the manifest
says so (`Sfx`: whether the folder, the bank and the GUIDs existed). `Restore` deletes the link and moves the originals
back; every step checks what is there, so a restore cut short finishes the next time. Every installed car has
`engine_ext` and `engine_int`; four lack `limiter` (`AcCarSound.MissingEngineEvents`). Nothing sets the sound yet:
the sound library and the matcher come next.

The diner prepares the data before a race (`RaceCarDataService.Prepare(car)`: the engine on the dyno, the running gear
against the factory's, for the player's car and the opponent's, each on its own parts). A player's car with problems
does not race ("Your car is not going anywhere: no brake front right"); an opponent's car with problems races as its
author made it. The files ride on the `LaunchIntent` (`CarData`) and the launcher applies them after the race config
and before `acs.exe`. Two cars of one model share one folder: the diner skips the opponent's pass and it drives the
player's data (the launcher refuses a second `Apply` to a car changed for the same race, in case). A manifest an earlier
race could not restore is put back before the car's data is changed again. A car without parts (no catalog, an older
save) races on the data its author gave it.

Check: `EngineBench <parts> car <AC car folder> <build id> [output folder]` writes every file the car's parts change,
with the factory running gear mounted (so only the engine files differ).

## Bench (`tools/EngineBench`)

```
EngineBench <parts folder> list | rated | all | inputs | show <build id>
EngineBench <parts folder> export <build id> <car data folder> <output folder>
EngineBench <parts folder> cars <AC cars folder>       factory engine suggested for every car, with the runners-up
EngineBench <parts folder> tune <build id> [count]     engines a used car of that build may turn up with
EngineBench <parts folder> bench <build id>            every part comes off and has to find its way back
EngineBench <parts folder> renew <older parts folder>  engines as a save made with an older conversion holds them, brought up to date
EngineBench <parts folder> gear <AC cars folder>       factory running gear chosen for every car, against its own data
EngineBench <parts folder> car <AC car folder> <build id> [output folder]   every data file the car's parts change
EngineBench <parts folder> sound <AC car folder> <car id> [output folder]   the car's sound written for another car id, with the engine events it lacks
```

The overlay's sfx section was checked with a dry-run console (a mirror of a car's `data` and `sfx`, the folder hashed
before and after): sound plus data, sound only with a crash and `RestoreAll` from a fresh instance, a restore cut
short, a car without `GUIDs.txt`, the packed GT500, and a mirror on another volume for the copy fallback.

## Not done yet

- Two cars of one model in a race share one data folder: the opponent drives the player's data. A clone of the car
  folder for the race (with the sound bank's GUIDs renamed, as Content Manager does) would give each its own; the
  overlay's sfx section is the half of that clone already done.
- Engine sounds: the overlay swaps them, but nothing chooses one yet. Next: a sound library under `Assets\Sounds`
  (a folder per sound: bank, GUIDs with a placeholder id, `sound.json`), harvesting installed cars' banks as
  candidates (deduplicated by a checksum of the first 16 KB, rev ceiling from their `engine.ini`), and a matcher on
  the engine block (cylinders, family, rev range, deterministic among equals) that sets `CarBuildResult.Sound`.
- Working on an engine outside a car (an engine stand): on the shelf an assembly can be taken apart, but parts only
  go together on a car.
- Wear from mileage; tuning UI (the scripts' `buildTuningMenu` is not used, fields are set directly).
- Nitrous: the parts exist and gate on their slots, but neither the dyno nor Assetto Corsa has a model for it.
- The running gear's placement in the garage is an estimate from the hubs (springs and shocks 24 cm inboard); a
  car-level part cannot be nudged with F5, only parts on a parent.
- Camber and toe: the suspension arm parts carry them but the source game never applies them, so neither does the export.

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
