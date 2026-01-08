# PROJECT ARCHITECTURE & GUIDELINES

**Project Goal:** "Assetto Rod" - A Street Rod-style career mode manager for Assetto Corsa.
**Tech Stack:** .NET 10, WPF (MVVM), LiteDB, CommunityToolkit.Mvvm.

---

## 1. HIGH-LEVEL ARCHITECTURE

The application follows a strict **MVVM (Model-View-ViewModel)** pattern.

- **UI Layer:** WPF XAML. No logic in code-behind.
- **ViewModel Layer:** Manages state and commands. Uses `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm.
- **Service Layer:** Handles business logic (Physics calculation, File I/O, AC process launching).
- **Data Layer:** LiteDB repositories.

---

## 2. DATA STORAGE STRATEGY (LiteDB)

We use a **Dual-Database Strategy** to separate static game rules from dynamic player progress.

### A. The Catalog (Static Data)

- **File:** `Data/Catalog.db` (Read-Only during gameplay).
- **Purpose:** Stores the definitions of all cars, parts, and compatibility rules.
- **Schema Concept Example:**

  ```csharp
  // Represents a car model exists in AC (e.g., "ks_ferrari_488")
  public record CarDefinition(
      string Id,              // AC folder name
      string Name,            // Display Name
      string Brand,
      int BasePrice,
      EngineBayType BayType,  // Logic for engine swaps
      List<string> CompatibleTags
  );

  // Represents a purchasable part
  public record PartDefinition(
      string Id,              // "turbo_stage_3"
      PartType Type,          // Turbo, ECU, Tires, Piston
      double PowerModifier,   // +15% HP
      double WeightModifier,  // +2kg
      double ReliabilityDrop, // -5% Reliability
      int Price
  );
  ```

### B. The Save State (Dynamic Data)

- **File:** `Saves/Save_01.db` (Read/Write).
- **Purpose:** Stores the player's specific progress, garage, and unique car instances.
- **Schema Concept Example:**

  ```csharp
  // The Root Object for a Save File
  public class GameState
  {
      public int Day { get; set; }
      public decimal Money { get; set; }
      public List<CarInstance> Garage { get; set; }
      public List<CarInstance> UsedCarMarket { get; set; }
  }

  // A specific car entity owned by the player or AI
  public class CarInstance
  {
      public Guid InstanceId { get; set; }
      public string DefinitionId { get; set; } // FK to Catalog

      // Mutable Mechanics (Street Rod style)
      public double OdometerKM { get; set; }
      public double EngineHealth { get; set; } // 0.0 to 1.0
      public double BodyCondition { get; set; }

      // Installed Inventory
      public List<InstalledPart> Parts { get; set; }
  }

  public class InstalledPart
  {
      public string PartId { get; set; } // FK to Catalog
      public double WearLevel { get; set; }
  }
  ```

---

## 3. IMPLEMENTATION RECOMMENDATIONS

### A. Repository Pattern

Implement repositories for clean data access abstraction:

```csharp
public interface IGameStateRepository
{
    GameState Load(string saveName);
    void Save(GameState state, string saveName);
    bool Exists(string saveName);
    void Delete(string saveName);
    List<string> ListSaves();
}

public interface ICatalogRepository
{
    List<CarDefinition> GetAllCars();
    CarDefinition GetCar(string id);
    List<PartDefinition> GetAllParts();
    List<PartDefinition> GetPartsForCar(string carId);
}
```

### B. Unit of Work for Transactions

Use LiteDB transactions for save operations:

```csharp
using (var transaction = db.BeginTrans())
{
    try
    {
        // Multiple operations
        gameStateCollection.Update(state);
        eventLogCollection.Insert(new Event(...));

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

### C. Backup/Autosave Strategy

- **Autosave**: Every N in-game days or after major events (race finish, car purchase)
- **Backup**: Keep last 3 save files (e.g., `Save_01.db`, `Save_01.backup1.db`, `Save_01.backup2.db`)
- **Cloud sync ready**: Single file design makes Steam Cloud/OneDrive integration easy

### D. Data Validation Layer

Validate foreign keys and constraints before persisting:

```csharp
public class SaveValidator
{
    private readonly ICatalogRepository _catalog;

    public ValidationResult Validate(GameState state)
    {
        // Ensure all DefinitionIds exist in catalog
        foreach (var car in state.Garage)
        {
            if (_catalog.GetCar(car.DefinitionId) == null)
                return ValidationResult.Error($"Car {car.DefinitionId} not found in catalog");

            foreach (var part in car.Parts)
            {
                if (_catalog.GetPart(part.PartId) == null)
                    return ValidationResult.Error($"Part {part.PartId} not found in catalog");
            }
        }

        return ValidationResult.Success();
    }
}
```

### E. Catalog Versioning

Handle catalog updates gracefully:

```csharp
public class CatalogMetadata
{
    public int Version { get; set; }
    public DateTime LastModified { get; set; }
}

// In save file, track catalog version used
public class GameState
{
    public int CatalogVersion { get; set; }
    // ... rest of properties
}
```

Migration strategy:
- If save.CatalogVersion < current catalog version, run migration
- If part/car removed from catalog, mark as "Legacy" instead of breaking save
- Show warning to player: "Some parts are no longer available"

### F. Performance Optimization

**Caching Strategy:**
- Cache catalog in memory (read-only, load once on startup)
- Cache computed stats on `CarInstance` (recalc only when parts change)
- Use LiteDB indexes on frequently queried fields

```csharp
public class CarInstance
{
    // ... existing properties

    [BsonIgnore] // Don't persist, calculate on load
    public double TotalHP { get; private set; }

    [BsonIgnore]
    public double TotalWeight { get; private set; }

    public void RecalculateStats(ICatalogRepository catalog)
    {
        var baseCar = catalog.GetCar(DefinitionId);
        TotalHP = baseCar.BaseHP;
        TotalWeight = baseCar.BaseWeight;

        foreach (var part in Parts)
        {
            var partDef = catalog.GetPart(part.PartId);
            TotalHP *= (1 + partDef.PowerModifier);
            TotalWeight += partDef.WeightModifier;
        }
    }
}
```

### G. Future-Proofing

Design for expansion:

```csharp
// Extensible event system
public class GameEvent
{
    public Guid Id { get; set; }
    public int Day { get; set; }
    public EventType Type { get; set; } // RaceWin, CarPurchase, PartInstalled
    public Dictionary<string, object> Data { get; set; } // Flexible payload
}

// Achievement tracking
public class Achievement
{
    public string Id { get; set; }
    public bool Unlocked { get; set; }
    public DateTime? UnlockedDate { get; set; }
}
```

---
