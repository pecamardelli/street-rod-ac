# Engine sounds

A folder here is a sound. Drop one in and the game has it; nothing else to edit.

```
Assets\Sounds\
  sounds.json                 pins and facts about installed cars (optional)
  ford_smallblock_289\        one sound
    ford_smallblock_289.bank  the FMOD bank, as its author built it
    GUIDs.txt                 its GUID lines, as they came with the bank
    sound.json                what engine it suits (optional)
```

The bank and the GUIDs are what an Assetto Corsa car ships under `sfx`. Copy them from any car, mod or Kunos:
the game rewrites the GUID lines for whichever car races on the sound, and only keeps the lines the bank can
answer, so a GUIDs.txt that carries a hundred other cars is fine. A Kunos car has no GUIDs.txt of its own; take
the lines from `content\sfx\GUIDs.txt` (all of it works).

`sound.json`, every field optional:

```json
{
  "name": "Ford 289 small block",
  "donor_id": "ks_ford_gt40",
  "bank": "ks_ford_gt40.bank",
  "cylinders": 8,
  "family": "ford",
  "rpm_max": 7000,
  "tags": ["small block", "open exhaust"]
}
```

- `donor_id` is the car id the GUID lines name (`event:/cars/<donor_id>/engine_ext`). Without it the bank's
  file name is taken, so a bank copied under its own name needs nothing; a renamed bank whose GUIDs give an engine
  to one car only takes that car. A sound whose GUIDs have no engine for its donor is left out (it would race
  silent), and the log says so.
- `bank` names the bank file when the folder holds more than one.
- `cylinders`, `family` (`gm`, `ford`, `mopar`) and `rpm_max` (the rev limiter of the car the bank was made
  for) are what the matcher goes by. A sound that says nothing fits anything, a little less well.

Every installed car's own bank is a sound as well, harvested at start-up: the same bytes under several cars are one
sound, its rev ceiling the highest limiter among them, its cylinders and family those of the engine the parts would
give the car. `sounds.json` corrects the harvest per car and pins a block to a sound:

```json
{
  "pins": { "engines/chrysler/_Engine_block_426_XEMI": "mopar_hemi_426" },
  "cars": {
    "chevy_c10hotrod": { "cylinders": 8, "family": "gm", "rpm_max": 6500 },
    "MRC_DODGE_CHARGER_68_RESTOMOD": { "exclude": true }
  }
}
```

A pin names a folder here, or `car:<car id>` for the bank an installed car ships: any car that ships those bytes
will do, whichever one the harvest happened to name the sound after. A pin to a sound nobody has is logged and
the block goes back to the matcher.

`exclude` keeps a car's bank out of the library altogether: a modern engine under a period body, a bank that
does not play right.

`EngineBench <parts folder> sounds <AC cars folder>` prints the library and the choice for every engine build.

## Loudness in the garage

Banks are mixed to their authors' taste, so the garage and the dealer lots even them out when they start an engine:
each bank is played once through a silent FMOD mixer and measured (K-weighted) over a grid of rpm and throttle, and
at every point the engine is turned up or down to the median of all 65 banks of the install
(`Audio/EngineLoudness.cs`). The figures are kept per bank in `%AppData%\StreetRodAC\engine_levels.json`; delete it
to have everything measured again. Races play the banks as they are.
