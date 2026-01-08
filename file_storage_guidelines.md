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
