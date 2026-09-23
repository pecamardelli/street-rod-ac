# Dealer Map and Dealer Lot

Two screens that replace "pick a dealer from a dropdown" with going somewhere and looking at the cars.

```
Garage (hub) ──▶ DealerMap ──▶ DealerLot ──▶ buy ──▶ stays on the lot
                                    └──▶ back to the map
```

The garage stays the hub. The map is somewhere you go from it, not a new top level.

## DealerMap

A crop of `Assets/Images/Backgrounds/los_angeles_map.jpg` with a pin per dealer. Hovering a pin shows what
the lot has and how far away it is; clicking drives out to it.

Pin positions are stored in `dealers.json` as fractions of the **whole** map image. The screen shows only
part of that image, and `DealerMapScreenViewModel` holds the crop rectangle (`CropX/Y/Width/Height`) and
converts. Moving the crop therefore does not mean re-measuring every dealer.

`Helpers/FractionPanel` places the pins: children are laid out at a fraction of the panel's size, so a pin
stays on its bit of the map at any window size.

## DealerLot

`Controls/DealerLotViewport3D` renders the dealer's showroom with its cars standing in it. There are two
camera states and no free roam:

| View | Camera | What the screen shows |
|------|--------|-----------------------|
| Lot | orbits the middle of the lot, wide | Every car; hovering names one, clicking picks it |
| Car | orbits that car, close | The info card, with Buy |

`SelectedIndex` is the whole of it: `-1` is the lot view, anything else is that car. The viewport sets it
itself on a click and a two-way binding carries it to the screen, so there is no code-behind.

The camera eases between the two rather than cutting. Alpha is steered round to the car's front
three-quarter until the player drags, at which point the camera is theirs and the steering stops.

### Data

`Assets/Dealers/dealers.json`, read by `Services/Dealers/DealerCatalog`. It holds the map position, the
showroom, how much stock the lot carries, and the kind of car it deals in. **There are no parking bays to
author** — see below.

The save keeps only a dealer's `Id`, `Name` and `Region` (`DealerLocation`). Everything else lives in the
file and is merged over the save on load, so a new field reaches saves that predate it, and a dealer dropped
from the file goes away. **Do not add map or scene fields to `DealerLocation`** — that is what this split is
for.

### Lots are laid out to fit the room

`Assets/Dealers/showrooms.json` carries each showroom's floor extent and wall radius, **measured off the
.kn5 rather than guessed**:

| Showroom | Floor | Walls | Holds |
|---|---|---|---|
| `Hangar` | 28.9 x 28.9 m | 14.4 m | 10 |
| `showroom` | 28.0 x 28.0 m | 14.0 m | 10 |
| `industrial` | 80.2 x 82.6 m | 39.0 m | 20 |
| `studio_white` | 79.4 x 78.2 m | 38.9 m | 20 |

Bays are spaced for a 5.8 m saloon with its doors opening: 4.6 m along a row, 10 m between rows. That is
what a 28 m room costs — ten cars, not the eighteen it holds if they are packed. Neither size fits in one
shot; the lot is swung round the way it would be walked.

`beach` was measured and dropped: a sand flat with a Victorian pier does not read as a Los Angeles car lot.

`LotLayout.Build` works the bays out from the room and the number of cars: rows facing each other across an
aisle, kept square enough for one camera position to take in, and never wider than the walls.
`LotLayout.CameraRadiusFor` then places the camera far enough back to see the lot and **inside the walls**,
because a distance that frames an 80 m yard puts the camera through the wall of a 28 m shed.

A dealer's `stockHigh` is kept at or under its showroom's `capacity`, so everything a lot sells is standing
on it and nothing can only be read about.

**Measuring a new showroom:** run `Modding/Tools/ac_track_tools/kn5bounds.py` (in the AC modding repo) over
the model and paste what `--json` prints:

```
python kn5bounds.py --json --id Hangar "C:/GAMES/Street Rod AC/content/showroom/Hangar/hangar.kn5"
```

Do not guess — a lot that overruns the walls puts cars outside the room, and a preview picture will not tell
you a 28 m shed from an 80 m yard. That tool's bay-spacing constants mirror `LotLayout.cs`; change one and
change the other.

### Stock matches the lot

Each dealer is **filled to its own target**. It used to spawn a market from every installed car and then cut
it to size with `.Take(n)` — which kept whichever cars came first in the catalog and starved every dealer
whose kind of car came later. That is what left two lots empty and put a hundred cars on a third.

A dealer claims a slice of the market (`priceBandLow`/`priceBandHigh`) measured as the **share of installed
cars it is dearer than**, not as dollars — one half-million-dollar car in the install would otherwise push
everything else into the bottom tenth and leave the smart showroom bare. The bands overlap on purpose and
must together cover 0 to 1.

`conditionCenter` then shifts the condition roll toward that dealer's standard, except for one car in twelve
which ignores it — the trade-in nobody looked at properly. Without that every lot holds the same spread and
there is no reason to drive anywhere.

The daily refresh tops each lot back up to its own target, for the same reason.

## Things worth knowing before changing this

- **Re-apply `LocalMatrix` after `SetCarAsync`.** A slot keeps a matrix set while it had no car and gives
  that stale one to the next car put in it. `Place()` does this and calls `ResetCarBoundingBox()`.
- **Load cars one at a time.** `SetCarAsync` ends with real device calls made from a worker thread without
  taking the renderer's lock; two at once race each other and the drawing.
- **LOD saves triangles, not memory.** A car always loads LOD A's textures whatever LOD is shown, and most
  installed mods ship LOD A only. The guard is `CarByteBudget`, measured from the .kn5 on disk — models here
  run from 11 MB to over 400 MB, so a fixed car count means nothing.
- **A car's own node never reports a mesh hit** (`RenderableList.CheckIntersection` returns null). Picking
  goes through the slot's bounding box, which is also cheaper and easier to aim at.
- **Shadows and reflections are worked out from `MainSlot` only.** The car nearest the middle of the lot goes
  there, so the quality falls off evenly. The renderer also only *listens* to that slot, so nothing marks the
  shadows dirty when a car lands in any other one — `GarageRenderer.RefreshShadows()` is called once the lot
  is stocked, or the last car to arrive stands there without a shadow.
- **Only sell cars that are installed.** The catalog outlives the install: a car deleted from `content/cars`
  stays on its books. `BuildPool` checks the folder exists. Without that check the dearest lots came out
  empty, because the cars that had been cleared out were the expensive ones.
- The lot is covered until every car is standing in it (`IsLotReady`), then the cover fades and the viewport
  fades up whole. Watching an empty room fill one car at a time is not worth showing.
- An empty `Cars` list with no showroom is how a lot ends when you leave the screen — bindings clear one at a
  time. That is not a failure and must not be reported as one.
- Selling a car reuses the standing scene and swaps only the cars that moved; the showroom, which can be a
  few hundred MB, is not read again.
- `ModelHeadingOffset` in the viewport is the knob if every car on every lot faces the same wrong way.
- The camera eases with a per-second settle rate rather than a per-frame lerp, so it moves the same way at
  any frame rate. `CarViewport3D` does the same, and the first car of a session is still placed at once —
  only later moves are eased.
- The map is held at the shape of its crop by `Helpers/AspectPanel`, which hands the picture and the pin
  layer the same rectangle. Stretching the map to fill the window would crop it by an unknown amount and
  take every pin off its place.

## Files

- `Screens/DealerMap/` — map screen
- `Screens/DealerLot/` — lot screen
- `Controls/DealerLotViewport3D.cs` — the 3D lot
- `Controls/SharedTextureBridge.cs` — DX11-to-WPF presentation, shared with `CarViewport3D`
- `Helpers/FractionPanel.cs` — fractional layout for the pins
- `Helpers/AspectPanel.cs` — holds the map and its pins to one shape
- `Services/Dealers/LotLayout.cs` — works the bays out from the room
- `Assets/Dealers/showrooms.json` — measured showroom floors
- `Services/Dealers/` — dealer definitions
- `Services/Market/CarPurchaseService.cs` — buying, shared with the listings screen
- `Assets/Dealers/dealers.json` — the data
