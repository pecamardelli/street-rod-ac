using AcTools.Kn5File;
using AcTools.Numerics;

namespace Street_Rod_AC.Parts;

/// <summary>
/// A small box for the parts that come without a model (fuel rails and injectors that the source game never
/// shows). Parts are picked and mounted by clicking them, so every part needs something to click.
/// </summary>
internal static class PlaceholderMesh
{
    public const float HalfSize = 0.035f;

    private const string MaterialName = "placeholder";
    private const string TextureName = "placeholder_grey.bmp";

    /// <summary>The box as a mesh node of the model, around the part's own origin; its material is added to the model once</summary>
    public static Kn5Node Create(IKn5 kn5, string name)
    {
        var materialId = MaterialId(kn5);

        // Four corners per side, so that every side gets its own normal
        var sides = new (Vec3 Normal, Vec3 U, Vec3 V)[]
        {
            (new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1)),
            (new Vec3(-1, 0, 0), new Vec3(0, 0, 1), new Vec3(0, 1, 0)),
            (new Vec3(0, 1, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0)),
            (new Vec3(0, -1, 0), new Vec3(1, 0, 0), new Vec3(0, 0, 1)),
            (new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0)),
            (new Vec3(0, 0, -1), new Vec3(0, 1, 0), new Vec3(1, 0, 0))
        };

        var vertices = new List<Kn5Node.Vertex>();
        var indices = new List<ushort>();
        foreach (var (normal, u, v) in sides)
        {
            var first = (ushort)vertices.Count;
            foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var position = new Vec3(
                    (normal.X + u.X * a + v.X * b) * HalfSize,
                    (normal.Y + u.Y * a + v.Y * b) * HalfSize,
                    (normal.Z + u.Z * a + v.Z * b) * HalfSize);
                vertices.Add(new Kn5Node.Vertex(position, normal, new Vec2((a + 1) / 2, (b + 1) / 2), u));
            }

            // Both ways round: the box shows whichever way the renderer culls
            var (second, third, fourth) = ((ushort)(first + 1), (ushort)(first + 2), (ushort)(first + 3));
            indices.AddRange(new[] { first, second, third, first, third, fourth });
            indices.AddRange(new[] { first, third, second, first, fourth, third });
        }

        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = new List<Kn5Node>(),
            CastShadows = true,
            IsVisible = true,
            IsRenderable = true,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            MaterialId = materialId,
            BoundingSphereCenter = new Vec3(0, 0, 0),
            BoundingSphereRadius = HalfSize * MathF.Sqrt(3)
        };
    }

    private static uint MaterialId(IKn5 kn5)
    {
        var index = 0u;
        foreach (var name in kn5.Materials.Keys)
        {
            if (name == MaterialName) return index;
            index++;
        }

        var texture = Kn5Bitmap.Solid(0x9A, 0x9A, 0x9A);
        kn5.Textures[TextureName] = new Kn5Texture { Name = TextureName, Active = true, Length = texture.Length };
        kn5.TexturesData[TextureName] = texture;

        kn5.Materials[MaterialName] = new Kn5Material
        {
            Name = MaterialName,
            ShaderName = "ksPerPixel",
            ShaderProperties = new[]
            {
                new Kn5Material.ShaderProperty { Name = "ksAmbient", ValueA = 0.45f },
                new Kn5Material.ShaderProperty { Name = "ksDiffuse", ValueA = 0.5f },
                new Kn5Material.ShaderProperty { Name = "ksSpecular", ValueA = 0.25f },
                new Kn5Material.ShaderProperty { Name = "ksSpecularEXP", ValueA = 30f }
            },
            TextureMappings = new[]
            {
                new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = TextureName, Slot = 0 }
            }
        };

        return (uint)(kn5.Materials.Count - 1);
    }
}
