using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>Parts an event gives as its prize, picked for the car that won it</summary>
public static class PrizeParts
{
    public const string CamshaftGroup = "Camshafts";

    /// <summary>
    /// The best camshaft for <paramref name="engine"/>: the dearest one that goes where its camshaft sits, dearer than
    /// the one it has, and how
    /// many it takes (one for each camshaft the engine has in that kind of place, two heads with a cam each). Not
    /// the one it has already. Null when the engine has no camshaft, or nothing better fits.
    /// </summary>
    public static (PartDefinition Cam, int Count)? BestCamshaft(PartsCatalog catalog, PartInstance? engine)
    {
        if (engine == null) return null;

        // Each mounted camshaft with the part it sits on: that part's slot is where a new one goes
        var mounted = engine.SelfAndDescendants()
            .SelectMany(parent => parent.Children.Select(child => (Parent: parent, Child: child)))
            .Where(p => catalog.Get(p.Child.DefinitionId) is { } d && PartKinds.GroupOf(d) == CamshaftGroup)
            .ToList();
        if (mounted.Count == 0) return null;

        var (parent, cam) = mounted[0];
        if (catalog.Get(parent.DefinitionId) is not { } parentDefinition) return null;
        if (parentDefinition.Slots.FirstOrDefault(s => s.Id == cam.ParentSlot) is not { } slot) return null;

        var current = catalog.Get(cam.DefinitionId);
        var best = catalog.FindMountable(parentDefinition, slot)
            .Select(m => m.Part)
            .Where(p => PartKinds.GroupOf(p) == CamshaftGroup && !ReferenceEquals(p, current))
            .Where(p => current == null || PartPricing.NewPrice(p) > PartPricing.NewPrice(current))
            .OrderByDescending(PartPricing.NewPrice)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (best == null) return null;

        var count = mounted.Count(m => catalog.Get(m.Parent.DefinitionId) == parentDefinition);
        return (best, Math.Max(1, count));
    }
}
