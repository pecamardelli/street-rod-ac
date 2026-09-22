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
showroom, the parking bays, and the kind of stock the dealer keeps.

The save keeps only a dealer's `Id`, `Name` and `Region` (`DealerLocation`). Everything else lives in the
file and is merged over the save on load, so a new field reaches saves that predate it, and a dealer dropped
from the file goes away. **Do not add map or scene fields to `DealerLocation`** — that is what this split is
for.

### Stock matches the lot

`UsedCarMarketService` used to hand each listing to `dealers[random]`. It now places a car by where its
price sits in the market: a dealer claims a slice (`priceBandLow`/`priceBandHigh`) and a standard of car
(`conditionCenter`, two parts the dealer to one part the roll, so a gem on the dirt lot is still possible).

Without this the map is decoration — every lot would hold the same spread of cars.

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
  there, so the quality falls off evenly.
- Selling a car reuses the standing scene and swaps only the cars that moved; the showroom, which can be a
  few hundred MB, is not read again.
- `ModelHeadingOffset` in the viewport is the knob if every car on every lot faces the same wrong way. Bay
  headings in the JSON are for when one car is wrong.

## Files

- `Screens/DealerMap/` — map screen
- `Screens/DealerLot/` — lot screen
- `Controls/DealerLotViewport3D.cs` — the 3D lot
- `Controls/SharedTextureBridge.cs` — DX11-to-WPF presentation, shared with `CarViewport3D`
- `Helpers/FractionPanel.cs` — fractional layout for the pins
- `Services/Dealers/` — dealer definitions
- `Services/Market/CarPurchaseService.cs` — buying, shared with the listings screen
- `Assets/Dealers/dealers.json` — the data
