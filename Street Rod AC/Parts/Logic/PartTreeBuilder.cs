namespace Street_Rod_AC.Parts.Logic;

/// <summary>An assembly put together from a list of parts, with whatever did not find a place</summary>
public sealed record PartTree(InstalledPart Root, IReadOnlyList<PartDefinition> Unplaced, IReadOnlyList<string> Missing);

/// <summary>
/// Turns a flat list of parts (an engine build) into the tree the slots dictate: each part goes on the first free
/// slot of an already placed part that it attaches to.
/// </summary>
public static class PartTreeBuilder
{
    private const string BlockClass = "Block";

    public static PartTree? BuildEngine(PartsCatalog catalog, EngineBuild build) =>
        Build(catalog, build.Parts.Where(p => p.Part != null).Select(p => p.Part!), BlockClass,
            build.Parts.Where(p => p.Part == null).Select(p => p.Source));

    /// <param name="rootClass">Simple class name of the part everything hangs off</param>
    public static PartTree? Build(PartsCatalog catalog, IEnumerable<string> partIds, string rootClass, IEnumerable<string>? unresolved = null)
    {
        var missing = new List<string>(unresolved ?? Array.Empty<string>());
        var pending = new List<PartDefinition>();
        foreach (var id in partIds)
        {
            if (catalog.Get(id) is { } part) pending.Add(part);
            else missing.Add(id);
        }

        var rootDefinition = pending.FirstOrDefault(p => p.ClassChain.Any(c => c.EndsWith("." + rootClass, StringComparison.Ordinal)));
        if (rootDefinition == null) return null;

        pending.Remove(rootDefinition);
        var root = new InstalledPart(rootDefinition);
        var placed = new List<InstalledPart> { root };

        // Parts deeper in the tree can only go on once their parent is there
        for (var progress = true; progress && pending.Count > 0;)
        {
            progress = false;
            foreach (var part in pending.ToList())
            {
                if (!TryMount(catalog, part, placed)) continue;

                pending.Remove(part);
                progress = true;
            }
        }

        return new PartTree(root, pending, missing);
    }

    private static bool TryMount(PartsCatalog catalog, PartDefinition part, List<InstalledPart> placed)
    {
        foreach (var slot in part.Slots)
        {
            foreach (var parent in placed)
            {
                // Free slots only: not taken by a child, and not the one the parent itself hangs by
                var parentSlot = parent.Definition.Slots.FirstOrDefault(s =>
                    s.Id != parent.OwnSlot && !parent.Children.ContainsKey(s.Id) && catalog.CanMate(part, slot, parent.Definition, s));
                if (parentSlot == null) continue;

                var child = new InstalledPart(part);
                parent.Mount(parentSlot.Id, child, slot.Id);
                placed.Add(child);
                return true;
            }
        }

        return false;
    }
}
