# Catalog System

## Purpose
Import AC content into normalized catalog. Game logic reads catalog, never AC files directly.

## Where It Lives
`%AppData%\StreetRodAC\Catalog\catalog.db` (`CatalogDatabase.DefaultPath`), apart from the saves. An older
`Saves\catalog.db` (and its side files) is moved there once, under the catalog lock; if both exist the old one is
renamed `catalog.db.old`. One opener at a time: a wait longer than 60 s is an error (`TimeoutException`), not a frozen
window, and the same thread opening twice gets an exception instead of a deadlock. A catalog.db that cannot be opened
is set aside as `catalog.db.bad` and a new one made (a file in use is not taken for corruption).

## Import Boundary
- Only import system reads AC installation
- No Screen/ViewModel touches AC files
- Catalog is single source of truth for content

## Import Pipeline

### 1. Discovery
- Enumerate `content/cars/` directories
- Validate minimum files: `ui/ui_car.json`
- Extract folder name as stable ID
- Left out, so they never reach a lot, a rival or the main screen, and are marked Legacy if they were ever in the
  catalog. Both stay in AC's cars folder:
  - police cars (every livery marked `street_corsa_police`, see `PoliceCars`);
  - cars with an **encrypted model** (`EncryptedCars`). Some mods scramble the KN5's vertex normals, which only CSP
    undoes, so the car races fine in AC but draws as shattered glass in the game's viewers. The tell: normals that
    average a cosine of about 0 against their own faces, with half facing backwards (real models average about 0.95).
    The answer is cached per model file (path, size, time) in `%AppData%\StreetRodAC\encrypted_cars.json`, so only
    the first scan reads the models (about 7.5 s for 167 cars).

### 2. Materialization
- Create `CarDefinition` in catalog
- ID = folder name (no GUIDs)
- Idempotent: same input = same output
- ui_car.json read leniently through `AcCarUi` (comments, trailing commas, a year as 1969, 1969.0 or "1969")
- The change hash covers the ui_car.json text and the skin folder names
- One read of the catalog and one batched upsert, off the UI thread

### 3. Retirement
- An Active car the scan did not find is marked Legacy (`ImportCarsAsync`; `IncrementalUpdateAsync` is the same call)
- Only after a scan that worked: no cars folder, or a listing that failed, retires nothing
- A car that is there but whose ui_car.json does not read counts as seen and is not retired

### 4. Profile Generation
- Create `CarProfile` if missing
- Calculate `BasePrice` from power/weight (`AcSpecs`, culture-free; a weight outside 300-5000 kg counts as no specs)
- Calculate `DealerPrecedence` from year/brand/source
- A `Generated` profile whose `DefinitionHash` no longer matches the car's `ContentHash` is regenerated (base price,
  precedence, hash, date only; the stock engine and the rest are kept). Manual and Imported profiles are left alone.

## Car States

| Status | Meaning |
|--------|---------|
| Active | Present in AC install |
| Legacy | Missing from AC, referenced by saves |
| Broken | Invalid metadata |

## Key Services

| Service | Interface | Purpose |
|---------|-----------|---------|
| ContentCatalogRepository | IContentCatalogRepository | CRUD for definitions |
| CarProfileRepository | ICarProfileRepository | CRUD for profiles |
| CarProfileService | ICarProfileService | Generate default profiles |

## Profile Generation Algorithm
- BasePrice: `(BHP / Weight_kg) × 1000` + adjustments
- DealerPrecedence: Common cars 0.7-1.0, Exotic 0.1-0.3

## Rules
- Never delete catalog entries automatically (a car that is gone is retired to Legacy, not deleted)
- Legacy cars stay for save compatibility
- Profiles persist across AC updates
- No random identifiers

## Files
- `Services/Catalog/CatalogDatabase.cs`
- `Services/Catalog/CarImportService.cs`
- `Services/Catalog/InstalledCars.cs`
- `Services/Catalog/ContentCatalogRepository.cs`
- `Services/Catalog/CarProfileRepository.cs`
- `Services/Catalog/CarProfileService.cs`
- `Models/Catalog/CarDefinition.cs`
- `Models/Catalog/CarProfile.cs`
