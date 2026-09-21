using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts.Cars;

/// <summary>A saved part tree brought together with the catalog, and the way back from each part to its saved self</summary>
public sealed class LiveTree
{
    public LiveTree(InstalledPart root, Dictionary<InstalledPart, PartInstance> saved, List<PartInstance> unknown)
    {
        Root = root;
        Saved = saved;
        Unknown = unknown;
    }

    public InstalledPart Root { get; }

    public IReadOnlyDictionary<InstalledPart, PartInstance> Saved { get; }

    /// <summary>Saved parts the catalog no longer has (a pack was removed); they and what is on them are left out</summary>
    public IReadOnlyList<PartInstance> Unknown { get; }

    public PartTree AsPartTree() => new(Root, Array.Empty<PartDefinition>(), Array.Empty<string>());
}

/// <summary>
/// Goes between the two shapes of a part tree: <see cref="PartInstance"/> is what a save file holds,
/// <see cref="InstalledPart"/> is what the part scripts and the 3D view work on.
/// </summary>
public static class PartTrees
{
    public static LiveTree? ToInstalled(PartsCatalog catalog, PartInstance root)
    {
        if (catalog.Get(root.DefinitionId) is not { } rootDefinition) return null;

        var saved = new Dictionary<InstalledPart, PartInstance>();
        var unknown = new List<PartInstance>();
        var installedRoot = Create(rootDefinition, root);
        AddChildren(installedRoot, root);
        return new LiveTree(installedRoot, saved, unknown);

        InstalledPart Create(PartDefinition definition, PartInstance instance)
        {
            var installed = new InstalledPart(definition) { InstanceId = instance.InstanceId, Wear = instance.Wear, Tear = instance.Tear };
            foreach (var (field, value) in instance.Tuning) installed.Tuning[field] = value;
            saved[installed] = instance;
            return installed;
        }

        void AddChildren(InstalledPart parent, PartInstance instance)
        {
            foreach (var child in instance.Children)
            {
                if (catalog.Get(child.DefinitionId) is not { } definition)
                {
                    unknown.Add(child);
                    continue;
                }

                var installed = Create(definition, child);
                parent.Mount(child.ParentSlot, installed, child.OwnSlot);
                AddChildren(installed, child);
            }
        }
    }

    /// <param name="parentSlot">Where the root goes: a slot of the car, or 0 for a loose part</param>
    public static PartInstance FromInstalled(InstalledPart root, int parentSlot = 0)
    {
        var instance = Copy(root);
        instance.ParentSlot = parentSlot;
        instance.OwnSlot = 0;
        return instance;

        static PartInstance Copy(InstalledPart part)
        {
            var copy = new PartInstance(part.Definition.Id)
            {
                Wear = part.Wear,
                Tear = part.Tear,
                ParentSlot = part.ParentSlot,
                OwnSlot = part.OwnSlot,
                Tuning = new Dictionary<string, double>(part.Tuning)
            };
            foreach (var child in part.Children.Values) copy.Children.Add(Copy(child));
            return copy;
        }
    }

    /// <summary>A copy that shares nothing with the original; the parts get ids of their own</summary>
    public static PartInstance Clone(PartInstance part, bool keepIds = false)
    {
        var copy = new PartInstance(part.DefinitionId)
        {
            Wear = part.Wear,
            Tear = part.Tear,
            ParentSlot = part.ParentSlot,
            OwnSlot = part.OwnSlot,
            Tuning = new Dictionary<string, double>(part.Tuning)
        };
        if (keepIds) copy.InstanceId = part.InstanceId;
        foreach (var child in part.Children) copy.Children.Add(Clone(child, keepIds));
        return copy;
    }

    /// <summary>The part another part is mounted on; null for a root or a part that is not in the tree</summary>
    public static PartInstance? FindParent(PartInstance root, PartInstance part) =>
        root.SelfAndDescendants().FirstOrDefault(p => p.Children.Contains(part));

    /// <summary>Same parts in the same places, whatever their condition</summary>
    public static bool SameBuild(PartInstance a, PartInstance b)
    {
        if (!a.DefinitionId.Equals(b.DefinitionId, StringComparison.OrdinalIgnoreCase) || a.Children.Count != b.Children.Count) return false;

        foreach (var child in a.Children)
        {
            var other = b.Children.FirstOrDefault(c => c.ParentSlot == child.ParentSlot);
            if (other == null || !SameBuild(child, other)) return false;
        }

        return true;
    }
}
