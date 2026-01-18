# Catalog System

## Purpose
Import AC content into normalized catalog. Game logic reads catalog, never AC files directly.

## Import Boundary
- Only import system reads AC installation
- No Screen/ViewModel touches AC files
- Catalog is single source of truth for content

## Import Pipeline

### 1. Discovery
- Enumerate `content/cars/` directories
- Validate minimum files: `ui/ui_car.json`
- Extract folder name as stable ID

### 2. Materialization
- Create `CarDefinition` in catalog
- ID = folder name (no GUIDs)
- Idempotent: same input = same output

### 3. Profile Generation
- Create `CarProfile` if missing
- Calculate `BasePrice` from power/weight
- Calculate `DealerPrecedence` from year/brand/source

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
- Never delete catalog entries automatically
- Legacy cars stay for save compatibility
- Profiles persist across AC updates
- No random identifiers

## Files
- `Services/Catalog/ContentCatalogRepository.cs`
- `Services/Catalog/CarProfileRepository.cs`
- `Services/Catalog/CarProfileService.cs`
- `Models/Catalog/CarDefinition.cs`
- `Models/Catalog/CarProfile.cs`
