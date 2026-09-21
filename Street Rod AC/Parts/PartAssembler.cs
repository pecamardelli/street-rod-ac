using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts;

/// <summary>
/// A part with its place in an assembly, relative to the assembly's root part.
/// <see cref="Source"/> is the installed part it stands for, when it stands for one.
/// </summary>
public sealed record PlacedPart(PartDefinition Part, Matrix4x4 World, InstalledPart? Source = null);

/// <summary>
/// An assembly merged into one model, with its bounds in the model's own space.
/// <see cref="Nodes"/> are the parts that made it into the model: the i-th is the node <see cref="PartAssembler.NodeName"/>(i).
/// A part without a model of its own that stands for an installed part is in it as a small box, so it can be clicked.
/// </summary>
public sealed record AssemblyModel(IKn5 Kn5, Vector3 Min, Vector3 Max, IReadOnlyList<PlacedPart> Nodes);

/// <summary>
/// Puts parts together by their slots and merges their models into a single KN5 for the renderer
/// </summary>
public static class PartAssembler
{
    private const int MaxDepth = 8;

    /// <summary>Places the parts of a real assembly: every part goes where the slot it is mounted on puts it</summary>
    public static List<PlacedPart> Assemble(InstalledPart root, Matrix4x4 rootWorld)
    {
        var result = new List<PlacedPart>();
        Place(root, rootWorld, 0);
        return result;

        void Place(InstalledPart part, Matrix4x4 world, int depth)
        {
            result.Add(new PlacedPart(part.Definition, world, part));
            if (depth >= MaxDepth) return;

            foreach (var (slotId, child) in part.Children)
            {
                if (ChildWorld(part.Definition, slotId, child.Definition, child.OwnSlot, world) is { } childWorld)
                    Place(child, childWorld, depth + 1);
            }
        }
    }

    /// <summary>Where a part ends up when one of its slots is brought onto a slot of a placed part; null when either slot is not there</summary>
    public static Matrix4x4? ChildWorld(PartDefinition parent, int parentSlotId, PartDefinition child, int childSlotId, Matrix4x4 parentWorld)
    {
        var parentSlot = parent.Slots.FirstOrDefault(s => s.Id == parentSlotId);
        var childSlot = child.Slots.FirstOrDefault(s => s.Id == childSlotId);
        if (parentSlot == null || childSlot == null || !Matrix4x4.Invert(SlotMatrix(childSlot), out var childSlotInverse)) return null;

        return childSlotInverse * SlotMatrix(parentSlot) * parentWorld;
    }

    /// <summary>Name of the node a part gets in a merged model</summary>
    public static string NodeName(int index) => $"part{index}";

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
        var nodes = new List<PlacedPart>();
        var index = 0;

        foreach (var placed in parts)
        {
            var modelPath = catalog.GetModelPath(placed.Part);
            if (modelPath == null || !File.Exists(modelPath))
            {
                if (placed.Source != null) AddPlaceholder(placed);
                continue;
            }

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

            var partNode = Kn5Node.CreateBaseNode(NodeName(nodes.Count));
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

            if (partNode.Children.Count > 0)
            {
                kn5.RootNode.Children.Add(partNode);
                nodes.Add(placed);
            }

            index++;
        }

        if (kn5.RootNode.Children.Count == 0)
            throw new InvalidOperationException($"No part of '{name}' has a model");

        return new AssemblyModel(kn5, min, max, nodes);

        void AddPlaceholder(PlacedPart placed)
        {
            var partNode = Kn5Node.CreateBaseNode(NodeName(nodes.Count));
            partNode.Transform = ToMat4x4(placed.World);
            partNode.Children.Add(PlaceholderMesh.Create(kn5, $"{partNode.Name}_box"));
            kn5.RootNode.Children.Add(partNode);
            nodes.Add(placed);

            var centre = placed.World.Translation;
            min = Vector3.Min(min, centre - new Vector3(PlaceholderMesh.HalfSize));
            max = Vector3.Max(max, centre + new Vector3(PlaceholderMesh.HalfSize));
        }
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
