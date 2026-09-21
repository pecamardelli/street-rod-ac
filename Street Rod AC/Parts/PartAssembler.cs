using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace Street_Rod_AC.Parts;

/// <summary>A part with its place in an assembly, relative to the assembly's root part</summary>
public sealed record PlacedPart(PartDefinition Part, Matrix4x4 World);

/// <summary>An assembly merged into one model, with its bounds in the model's own space</summary>
public sealed record AssemblyModel(IKn5 Kn5, Vector3 Min, Vector3 Max);

/// <summary>
/// Puts parts together by their slots and merges their models into a single KN5 for the renderer
/// </summary>
public static class PartAssembler
{
    private const int MaxDepth = 8;

    /// <summary>
    /// Assembles a part with a stock-looking choice for every slot. Stands in for a real build
    /// (the parts actually installed on a car) until the game tracks those.
    /// </summary>
    public static List<PlacedPart> AssembleDefault(PartsCatalog catalog, string rootId)
    {
        var root = catalog.Get(rootId) ?? throw new InvalidOperationException($"Part '{rootId}' is not in the catalog");

        var result = new List<PlacedPart>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Place(root, Matrix4x4.Identity, 0);
        return result;

        void Place(PartDefinition part, Matrix4x4 world, int depth)
        {
            if (!placed.Add(part.Id)) return;

            result.Add(new PlacedPart(part, world));
            if (depth >= MaxDepth) return;

            foreach (var slot in part.Slots)
            {
                var choice = catalog.GetMountable(part, slot)
                    .Where(c => !placed.Contains(c.Part.Id) && !IsReceivingSlot(catalog, c.Part, c.Slot))
                    // Prefer the variant made for this very part over generic or aftermarket ones
                    .OrderByDescending(c => c.Part.Name.StartsWith(root.Name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (choice.Part == null) continue;

                // Bring the child's mounting slot onto the parent's slot
                if (!Matrix4x4.Invert(SlotMatrix(choice.Slot), out var childSlotInverse)) continue;
                Place(choice.Part, childSlotInverse * SlotMatrix(slot) * world, depth + 1);
            }
        }
    }

    /// <summary>
    /// Some part configs copy the attach lines of their siblings onto slots that in fact receive parts
    /// (one engine block "mounting" on another block's alternator slot). Those are not real mounts.
    /// </summary>
    private static bool IsReceivingSlot(PartsCatalog catalog, PartDefinition part, PartSlot slot) =>
        catalog.GetMountable(part, slot).Count > 0;

    public static Matrix4x4 SlotMatrix(PartSlot slot) =>
        Matrix4x4.CreateFromYawPitchRoll(slot.Rotation[0], slot.Rotation[1], slot.Rotation[2]) *
        Matrix4x4.CreateTranslation(slot.Position[0], slot.Position[1], slot.Position[2]);

    public static AssemblyModel BuildModel(PartsCatalog catalog, string name, IEnumerable<PlacedPart> parts)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var models = new Dictionary<string, IKn5>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var placed in parts)
        {
            var modelPath = catalog.GetModelPath(placed.Part);
            if (modelPath == null || !File.Exists(modelPath)) continue;

            if (!models.TryGetValue(modelPath, out var model)) models[modelPath] = model = Kn5.FromFile(modelPath);

            // Texture names are unique across packs, so equal names mean equal content
            foreach (var (textureName, texture) in model.Textures)
            {
                kn5.Textures[textureName] = texture;
                kn5.TexturesData[textureName] = model.TexturesData[textureName];
            }

            // Materials are per placed part: the same model may appear more than once
            var materialIds = new Dictionary<uint, uint>();
            var sourceMaterialId = 0u;
            foreach (var material in model.Materials.Values)
            {
                var clone = (Kn5Material)material.Clone();
                clone.Name = $"{index}:{material.Name}";
                kn5.Materials[clone.Name] = clone;
                materialIds[sourceMaterialId++] = (uint)(kn5.Materials.Count - 1);
            }

            var partNode = Kn5Node.CreateBaseNode(placed.Part.Id);
            partNode.Transform = ToMat4x4(placed.World);

            foreach (var mesh in model.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh))
            {
                partNode.Children.Add(CloneMesh(mesh, materialIds.GetValueOrDefault(mesh.MaterialId)));

                foreach (var vertex in mesh.Vertices)
                {
                    var position = Vector3.Transform(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), placed.World);
                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }

            if (partNode.Children.Count > 0) kn5.RootNode.Children.Add(partNode);
            index++;
        }

        if (kn5.RootNode.Children.Count == 0)
            throw new InvalidOperationException($"No part of '{name}' has a model");

        return new AssemblyModel(kn5, min, max);
    }

    /// <summary>Geometry arrays are shared with the source, only the material binding differs</summary>
    private static Kn5Node CloneMesh(Kn5Node mesh, uint materialId) => new()
    {
        NodeClass = Kn5NodeClass.Mesh,
        Name = mesh.Name,
        Active = true,
        Children = new List<Kn5Node>(),
        CastShadows = mesh.CastShadows,
        IsVisible = mesh.IsVisible,
        IsTransparent = mesh.IsTransparent,
        IsRenderable = mesh.IsRenderable,
        Vertices = mesh.Vertices,
        Indices = mesh.Indices,
        MaterialId = materialId,
        Layer = mesh.Layer,
        LodIn = mesh.LodIn,
        LodOut = mesh.LodOut,
        BoundingSphereCenter = mesh.BoundingSphereCenter,
        BoundingSphereRadius = mesh.BoundingSphereRadius
    };

    private static Mat4x4 ToMat4x4(Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
