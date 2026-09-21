using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// A free spot a loose part can go on: a slot of a mounted part, or of the car itself when
/// <see cref="Parent"/> is null. <see cref="OwnSlot"/> is the slot of the loose part that meets it.
/// </summary>
public sealed record MountPlace(PartInstance? Parent, int ParentSlot, int OwnSlot);

/// <summary>
/// Taking parts off a car and putting them on. A part comes off with everything that is mounted on it and
/// goes back on the same way; what fits where is the catalog's say (<see cref="PartsCatalog.CanMate"/>).
/// </summary>
public static class Workbench
{
    /// <param name="carParts">The parts mounted on the car itself (Car.Parts)</param>
    public static List<MountPlace> FindPlaces(PartsCatalog catalog, List<PartInstance> carParts, PartInstance loose)
    {
        var places = new List<MountPlace>();
        if (catalog.Get(loose.DefinitionId) is not { } looseDefinition) return places;

        // An engine block goes in the engine bay and nowhere else
        if (PartKinds.IsBlock(looseDefinition))
        {
            if (carParts.All(p => p.ParentSlot != PartInstance.CarEngineSlot)) places.Add(new MountPlace(null, PartInstance.CarEngineSlot, 0));
            return places;
        }

        var looseGroup = PartKinds.GroupOf(looseDefinition);
        var looseSlots = looseDefinition.Slots.Where(s => loose.Children.All(c => c.ParentSlot != s.Id)).ToList();

        foreach (var parent in carParts.SelectMany(p => p.SelfAndDescendants()))
        {
            if (catalog.Get(parent.DefinitionId) is not { } parentDefinition) continue;
            if (PartKinds.NeverStacks(looseGroup) && PartKinds.GroupOf(parentDefinition) == looseGroup) continue;

            foreach (var slot in parentDefinition.Slots)
            {
                if (slot.Id == parent.OwnSlot || parent.Children.Any(c => c.ParentSlot == slot.Id)) continue;

                var own = looseSlots.FirstOrDefault(s => catalog.CanMate(looseDefinition, s, parentDefinition, slot));
                if (own != null) places.Add(new MountPlace(parent, slot.Id, own.Id));
            }
        }

        return places;
    }

    public static void Mount(List<PartInstance> carParts, MountPlace place, PartInstance loose)
    {
        loose.ParentSlot = place.ParentSlot;
        loose.OwnSlot = place.OwnSlot;
        (place.Parent?.Children ?? carParts).Add(loose);
    }

    /// <summary>Takes a part off, with whatever is on it. False when the part is not on the car.</summary>
    public static bool Remove(List<PartInstance> carParts, PartInstance part)
    {
        var siblings = carParts.Contains(part)
            ? carParts
            : carParts.Select(root => PartTrees.FindParent(root, part)).FirstOrDefault(parent => parent != null)?.Children;
        if (siblings == null) return false;

        siblings.Remove(part);
        part.ParentSlot = 0;
        part.OwnSlot = 0;
        return true;
    }
}
