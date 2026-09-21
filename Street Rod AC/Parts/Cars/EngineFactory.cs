using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>An engine made for a car: the parts, what they come to, and what was changed from the factory build</summary>
public sealed record BuiltEngine(PartInstance Root, EngineReport Report, IReadOnlyList<string> Changes)
{
    public bool IsModified => Changes.Count > 0;
}

/// <summary>
/// Makes the engine a car shows up with. The factory build is the starting point; cars that have been around
/// may have been worked on, and then only the way somebody plausibly would have: a bigger engine of the same
/// family, or bolt-on parts that fit this very engine. Every change has to leave an engine that runs, makes
/// no less power than before and does not run away from what the car left the factory with.
/// </summary>
public static class EngineFactory
{
    private const double WearNoise = 0.08;
    private const double NewerPartBonus = 0.15;

    private const double EngineSwapChance = 0.25;
    private const double SwapMinPower = 1.05;
    private const double SwapMaxPower = 1.5;
    private const double MaxPowerOverStock = 1.6;
    private const double MinPowerGain = 1.005;
    private const int MaxBoltOns = 4;
    private const int AttemptsPerBoltOn = 6;

    // What people change on an engine without taking it apart
    private static readonly HashSet<string> BoltOnGroups = new()
    {
        "Air filters", "Carburettors and injection", "Intake manifolds", "Exhaust", "Camshafts", "Flywheels and clutches"
    };

    /// <param name="condition">0..1, how worn the car is; every part ends up around it</param>
    public static BuiltEngine? CreateStock(PartsCatalog catalog, RatedBuild build, double condition, Random random)
    {
        var root = Assemble(catalog, build);
        if (root == null) return null;

        ApplyWear(root, condition, random);
        return new BuiltEngine(root, Evaluate(catalog, root)!, Array.Empty<string>());
    }

    /// <param name="tuneLevel">0 = as it left the factory, 1 = worked on as much as it gets</param>
    public static BuiltEngine? CreateTuned(PartsCatalog catalog, EngineBuildIndex index, RatedBuild stock, double condition,
        double tuneLevel, Random random)
    {
        var changes = new List<string>();
        var build = stock;

        if (random.NextDouble() < EngineSwapChance * tuneLevel)
        {
            var bigger = index.Runnable
                .Where(b => b.Family.Length > 0 && b.Family == stock.Family && b.Build.Id != stock.Build.Id
                            && b.PowerHp >= stock.PowerHp * SwapMinPower && b.PowerHp <= stock.PowerHp * SwapMaxPower)
                .ToList();
            if (bigger.Count > 0)
            {
                build = bigger[random.Next(bigger.Count)];
                changes.Add(build.BlockId.Equals(stock.BlockId, StringComparison.OrdinalIgnoreCase)
                    ? $"engine rebuilt like the one of a {build.Build.Name}"
                    : $"engine of a {build.Build.Name} swapped in");
            }
        }

        var root = Assemble(catalog, build);
        if (root == null) return null;

        ApplyWear(root, condition, random);
        var report = Evaluate(catalog, root)!;

        var boltOns = (int)Math.Round(tuneLevel * MaxBoltOns * random.NextDouble());
        for (var i = 0; i < boltOns; i++)
        {
            for (var attempt = 0; attempt < AttemptsPerBoltOn; attempt++)
            {
                var swapped = TryBoltOn(catalog, root, report, stock.PowerHp * MaxPowerOverStock, condition, random);
                if (swapped == null) continue;

                report = swapped.Value.Report;
                changes.Add(swapped.Value.Change);
                break;
            }
        }

        return new BuiltEngine(root, report, changes);
    }

    /// <summary>Runs a saved engine through the part scripts and the dyno; null when its block is not in the catalog</summary>
    public static EngineReport? Evaluate(PartsCatalog catalog, PartInstance root) =>
        PartTrees.ToInstalled(catalog, root) is { } live ? EngineEvaluator.Evaluate(catalog, live.AsPartTree()) : null;

    private static PartInstance? Assemble(PartsCatalog catalog, RatedBuild build) =>
        PartTreeBuilder.BuildEngine(catalog, build.Build) is { } tree ? PartTrees.FromInstalled(tree.Root, PartInstance.CarEngineSlot) : null;

    private static void ApplyWear(PartInstance root, double condition, Random random)
    {
        foreach (var part in root.SelfAndDescendants()) part.Wear = WearAround(condition, random);
    }

    private static double WearAround(double condition, Random random) =>
        Math.Clamp(condition + (random.NextDouble() * 2 - 1) * WearNoise, 0.05, 1.0);

    /// <summary>Swaps one bolt-on part for another that goes in its place; leaves the engine alone when it does not work out</summary>
    private static (EngineReport Report, string Change)? TryBoltOn(PartsCatalog catalog, PartInstance root, EngineReport before,
        double maxPower, double condition, Random random)
    {
        var swappable = root.SelfAndDescendants()
            .Where(p => !ReferenceEquals(p, root) && catalog.Get(p.DefinitionId) is { } d && BoltOnGroups.Contains(PartKinds.GroupOf(d)))
            .ToList();
        if (swappable.Count == 0) return null;

        var old = swappable[random.Next(swappable.Count)];
        var parent = PartTrees.FindParent(root, old)!;
        var oldDefinition = catalog.Get(old.DefinitionId)!;
        var parentDefinition = catalog.Get(parent.DefinitionId);
        var parentSlot = parentDefinition?.Slots.FirstOrDefault(s => s.Id == old.ParentSlot);
        if (parentDefinition == null || parentSlot == null) return null;

        var group = PartKinds.GroupOf(oldDefinition);
        var alternatives = catalog.FindMountable(parentDefinition, parentSlot)
            .Where(c => !ReferenceEquals(c.Part, oldDefinition) && PartKinds.GroupOf(c.Part) == group)
            .ToList();
        if (alternatives.Count == 0) return null;

        var (newDefinition, newOwnSlot) = alternatives[random.Next(alternatives.Count)];
        var replacement = new PartInstance(newDefinition.Id)
        {
            ParentSlot = old.ParentSlot,
            OwnSlot = newOwnSlot.Id,
            Wear = Math.Clamp(WearAround(condition, random) + NewerPartBonus, 0.05, 1.0)
        };

        // Whatever was on the old part has to find its place on the new one
        foreach (var child in old.Children)
        {
            var childDefinition = catalog.Get(child.DefinitionId);
            var childSlot = childDefinition?.Slots.FirstOrDefault(s => s.Id == child.OwnSlot);
            var target = childSlot == null
                ? null
                : newDefinition.Slots
                    .Where(s => s.Id != replacement.OwnSlot && replacement.Children.All(c => c.ParentSlot != s.Id))
                    .OrderByDescending(s => s.Id == child.ParentSlot)
                    .FirstOrDefault(s => catalog.CanMate(childDefinition!, childSlot, newDefinition, s));
            if (target == null) return null;

            var moved = PartTrees.Clone(child, keepIds: true);
            moved.ParentSlot = target.Id;
            replacement.Children.Add(moved);
        }

        var index = parent.Children.IndexOf(old);
        parent.Children[index] = replacement;

        var after = Evaluate(catalog, root);
        // Worth doing: more power, or at least a better part for the same power (a clutch makes none)
        var powerBefore = before.Dyno?.MaxPowerHp ?? 0;
        var improved = after is { Runs: true } && after.Dyno!.MaxPowerHp >= powerBefore && after.Dyno.MaxPowerHp <= maxPower
                       && (after.Dyno.MaxPowerHp > powerBefore * MinPowerGain || PartPricing.NewPrice(newDefinition) > PartPricing.NewPrice(oldDefinition));
        if (!improved)
        {
            parent.Children[index] = old;
            return null;
        }

        return (after!, newDefinition.DisplayName ?? newDefinition.Name);
    }
}
