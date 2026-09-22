using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>
/// Keeps saved part trees in step with the catalog. When a pack is replaced by a later release its parts go
/// by new ids (<see cref="PartsCatalog.CurrentId"/>), and the new release may have renumbered slots: the oil
/// pan that hung by slot 9 now hangs by slot 10. Saved parts get their current ids, and every joint the
/// catalog no longer agrees with is made again the way the workbench would.
/// </summary>
public static class SavedParts
{
    /// <param name="loose">Gets the parts that fit nowhere on their parent any more, with what is on them</param>
    /// <returns>True when the tree changed</returns>
    public static bool BringUpToDate(PartsCatalog catalog, PartInstance root, List<PartInstance> loose)
    {
        if (root.SelfAndDescendants().All(p => catalog.CurrentId(p.DefinitionId) == p.DefinitionId)) return false;

        Renew(catalog, root, loose);
        return true;
    }

    /// <param name="parent">What <paramref name="part"/> hangs on, when it hangs on something</param>
    private static void Renew(PartsCatalog catalog, PartInstance part, List<PartInstance> loose, PartInstance? parent = null)
    {
        part.DefinitionId = catalog.CurrentId(part.DefinitionId);

        var children = part.Children.ToList();
        foreach (var child in children) child.DefinitionId = catalog.CurrentId(child.DefinitionId);

        // Parts the catalog does not know stay as they are: PartTrees leaves them out, and they may come back
        if (catalog.Get(part.DefinitionId) is { } definition)
        {
            // Joints that still hold keep their slots; the others take what is free after that, on the same part
            // or one up (an air cleaner that sat on a set of carburettors sits over the manifold's row of them now)
            foreach (var child in children.Where(child => !Holds(catalog, definition, part, child)).ToList())
            {
                part.Children.Remove(child);
                if (Reseat(catalog, definition, part, child)) continue;
                if (parent != null && catalog.Get(parent.DefinitionId) is { } parentDefinition && Reseat(catalog, parentDefinition, parent, child)) continue;

                child.ParentSlot = 0;
                child.OwnSlot = 0;
                loose.Add(child);
            }
        }

        // A part knows the slot it hangs by now, so its own joints can be looked at; one that came off
        // still carries parts of its own
        foreach (var child in children) Renew(catalog, child, loose, part);
    }

    private static bool Holds(PartsCatalog catalog, PartDefinition definition, PartInstance part, PartInstance child)
    {
        if (catalog.Get(child.DefinitionId) is not { } childDefinition) return true;

        var slot = definition.Slots.FirstOrDefault(s => s.Id == child.ParentSlot);
        var own = childDefinition.Slots.FirstOrDefault(s => s.Id == child.OwnSlot);
        return slot != null && own != null && slot.Id != part.OwnSlot && catalog.CanMate(childDefinition, own, definition, slot);
    }

    private static bool Reseat(PartsCatalog catalog, PartDefinition definition, PartInstance part, PartInstance child)
    {
        var childDefinition = catalog.Get(child.DefinitionId)!;
        var childSlots = childDefinition.Slots.Where(s => child.Children.All(c => c.ParentSlot != s.Id)).ToList();

        foreach (var slot in definition.Slots)
        {
            if (slot.Id == part.OwnSlot || part.Children.Any(c => c.ParentSlot == slot.Id)) continue;

            var own = childSlots.FirstOrDefault(s => catalog.CanMate(childDefinition, s, definition, slot));
            if (own == null) continue;

            child.ParentSlot = slot.Id;
            child.OwnSlot = own.Id;
            part.Children.Add(child);
            return true;
        }

        return false;
    }
}
