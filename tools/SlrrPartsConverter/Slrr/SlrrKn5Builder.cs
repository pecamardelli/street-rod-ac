using System.IO;
using System.Numerics;
using AcTools.Kn5File;
using AcTools.Numerics;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Turns an SLRR part mesh into a KN5 so the AcTools renderer can draw it
/// with the same lighting, shadows and post-processing as the cars.
/// </summary>
public static class SlrrKn5Builder
{
    private const int MaxVerticesPerMesh = ushort.MaxValue;

    // Parts are made by hand with a few thousand triangles, the most detailed ones with some ten thousand. A mesh far
    // beyond that came out of a CAD program or a subdivision modifier and carries triangles its shape does not need.
    // Those go, as long as no vertex ends up further than about a millimetre from the surfaces it stands in for.
    private const int SimplifyAbove = 20000;
    private const int SimplifyTo = 10000;
    private const double SimplifyMaxError = 1e-6;

    /// <summary>A mesh of the part with the textures of its render and its offset inside the part</summary>
    public sealed record Piece(SlrrMesh Mesh, IReadOnlyList<string?> TextureFiles, Matrix4x4 Offset);

    /// <param name="texturePrefix">Makes texture names unique across packs, so part models can be merged later</param>
    /// <param name="report">Told about every mesh that had triangles taken out</param>
    public static IKn5 Build(string name, IEnumerable<Piece> pieces, string texturePrefix, Action<string>? report = null)
    {
        var kn5 = Kn5.CreateEmpty();
        kn5.RootNode.Name = name;

        foreach (var piece in pieces)
        {
            var subMeshes = piece.Mesh.SubMeshes.Where(s => s.Vertices.Length > 0 && s.Indices.Length >= 3).Select(Weld).ToList();

            var triangles = subMeshes.Sum(s => s.Indices.Length / 3);
            if (triangles > SimplifyAbove)
            {
                subMeshes = SlrrMeshSimplifier.Simplify(subMeshes, SimplifyTo, SimplifyMaxError);
                report?.Invoke($"{name}: {triangles} -> {subMeshes.Sum(s => s.Indices.Length / 3)} triangles");
            }

            foreach (var sub in subMeshes)
            {
                if (sub.Indices.Length < 3) continue;

                var materialId = AddMaterial(kn5, $"{name}_{kn5.Materials.Count}", sub, piece.TextureFiles, texturePrefix);
                foreach (var (vertices, indices) in Split(sub))
                {
                    var nodeName = $"{name}_{kn5.RootNode.Children.Count}";
                    kn5.RootNode.Children.Add(CreateMeshNode(nodeName, vertices, indices, materialId, piece.Offset));
                }
            }
        }

        return kn5;
    }

    private static uint AddMaterial(IKn5 kn5, string name, SlrrSubMesh sub, IReadOnlyList<string?> textureFiles, string texturePrefix)
    {
        var textureFile = sub.DiffuseTextureIndex >= 0 && sub.DiffuseTextureIndex < textureFiles.Count
            ? textureFiles[sub.DiffuseTextureIndex]
            : null;
        var texture = textureFile != null ? AddTextureFile(kn5, textureFile, texturePrefix) : AddColorTexture(kn5, sub.DiffuseColor);

        // SLRR fakes metal with an additive environment map (its textures for cast parts are nearly black),
        // so those materials get the scene reflections instead. Without a diffuse texture it is plain chrome.
        var reflective = sub.ReflectionTextureIndex >= 0;
        var chrome = reflective && textureFile == null;

        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = reflective ? "ksPerPixelReflection" : "ksPerPixel",
            ShaderProperties = new[]
            {
                Property("ksAmbient", 0.45f),
                Property("ksDiffuse", 0.5f),
                Property("ksSpecular", reflective ? 0.9f : 0.25f),
                Property("ksSpecularEXP", Math.Clamp(sub.Glossiness * (reflective ? 4f : 1.5f), 8f, 200f)),
                Property("fresnelC", chrome ? 0.6f : 0.12f),
                Property("fresnelEXP", 3f),
                Property("fresnelMaxLevel", chrome ? 1f : 0.45f),
                Property("isAdditive", 1f)
            },
            TextureMappings = new[]
            {
                new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = texture, Slot = 0 }
            }
        };

        return (uint)(kn5.Materials.Count - 1);
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    private static string AddTextureFile(IKn5 kn5, string filename, string prefix)
    {
        var key = prefix + Path.GetFileName(filename).ToLowerInvariant();
        if (!kn5.TexturesData.ContainsKey(key))
        {
            AddTexture(kn5, key, File.ReadAllBytes(filename));
        }
        return key;
    }

    private static string AddColorTexture(IKn5 kn5, Vector3 color)
    {
        var r = (byte)(Math.Clamp(color.X, 0f, 1f) * 255f);
        var g = (byte)(Math.Clamp(color.Y, 0f, 1f) * 255f);
        var b = (byte)(Math.Clamp(color.Z, 0f, 1f) * 255f);

        var key = $"color_{r:x2}{g:x2}{b:x2}.bmp";
        if (!kn5.TexturesData.ContainsKey(key))
        {
            AddTexture(kn5, key, CreateSolidBitmap(r, g, b));
        }
        return key;
    }

    private static void AddTexture(IKn5 kn5, string key, byte[] data)
    {
        kn5.Textures[key] = new Kn5Texture { Name = key, Active = true, Length = data.Length };
        kn5.TexturesData[key] = data;
    }

    /// <summary>Smallest BMP the texture loader accepts: 2x2 pixels, 24 bpp, rows padded to 4 bytes</summary>
    private static byte[] CreateSolidBitmap(byte r, byte g, byte b)
    {
        const int headerSize = 54;
        const int rowSize = 8;

        var data = new byte[headerSize + rowSize * 2];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.GetBytes(data.Length).CopyTo(data, 2);
        BitConverter.GetBytes(headerSize).CopyTo(data, 10);
        BitConverter.GetBytes(40).CopyTo(data, 14);
        BitConverter.GetBytes(2).CopyTo(data, 18);
        BitConverter.GetBytes(2).CopyTo(data, 22);
        BitConverter.GetBytes((short)1).CopyTo(data, 26);
        BitConverter.GetBytes((short)24).CopyTo(data, 28);
        BitConverter.GetBytes(rowSize * 2).CopyTo(data, 34);

        for (var row = 0; row < 2; row++)
        {
            for (var pixel = 0; pixel < 2; pixel++)
            {
                var offset = headerSize + row * rowSize + pixel * 3;
                data[offset] = b;
                data[offset + 1] = g;
                data[offset + 2] = r;
            }
        }

        return data;
    }

    /// <summary>
    /// Merges identical vertices. Version 3 meshes store three vertices per triangle,
    /// so this typically shrinks them several times over.
    /// </summary>
    private static SlrrSubMesh Weld(SlrrSubMesh sub)
    {
        var lookup = new Dictionary<(Vector3, Vector3, Vector2), int>(sub.Vertices.Length);
        var vertices = new List<SlrrVertex>(sub.Vertices.Length);
        var indices = new int[sub.Indices.Length];

        for (var i = 0; i < indices.Length; i++)
        {
            var source = sub.Indices[i];
            if (source < 0 || source >= sub.Vertices.Length)
            {
                // Broken triangle: collapse it onto a valid vertex so it renders as nothing
                indices[i] = 0;
                continue;
            }

            var vertex = sub.Vertices[source];
            var key = (vertex.Position, vertex.Normal, vertex.Uv);
            if (!lookup.TryGetValue(key, out var target))
            {
                target = vertices.Count;
                lookup[key] = target;
                vertices.Add(vertex);
            }
            indices[i] = target;
        }

        if (vertices.Count == 0) return sub;

        return new SlrrSubMesh
        {
            MaterialName = sub.MaterialName,
            DiffuseTextureIndex = sub.DiffuseTextureIndex,
            ReflectionTextureIndex = sub.ReflectionTextureIndex,
            DiffuseColor = sub.DiffuseColor,
            Glossiness = sub.Glossiness,
            Vertices = vertices.ToArray(),
            Indices = indices
        };
    }

    /// <summary>KN5 indices are 16-bit, so bigger meshes are cut into pieces that fit</summary>
    private static IEnumerable<(SlrrVertex[] Vertices, ushort[] Indices)> Split(SlrrSubMesh sub)
    {
        if (sub.Vertices.Length <= MaxVerticesPerMesh)
        {
            yield return (sub.Vertices, Array.ConvertAll(sub.Indices, i => (ushort)i));
            yield break;
        }

        var vertices = new List<SlrrVertex>();
        var indices = new List<ushort>();
        var remap = new Dictionary<int, ushort>();

        for (var i = 0; i + 2 < sub.Indices.Length; i += 3)
        {
            if (vertices.Count + 3 > MaxVerticesPerMesh)
            {
                yield return (vertices.ToArray(), indices.ToArray());
                vertices.Clear();
                indices.Clear();
                remap.Clear();
            }

            for (var j = 0; j < 3; j++)
            {
                var source = sub.Indices[i + j];
                if (!remap.TryGetValue(source, out var target))
                {
                    target = (ushort)vertices.Count;
                    remap[source] = target;
                    vertices.Add(sub.Vertices[source]);
                }
                indices.Add(target);
            }
        }

        if (indices.Count > 0) yield return (vertices.ToArray(), indices.ToArray());
    }

    /// <param name="offset">Baked into the vertices, so part models stay a flat list of meshes</param>
    private static Kn5Node CreateMeshNode(string name, SlrrVertex[] vertices, ushort[] indices, uint materialId, Matrix4x4 offset)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var converted = new Kn5Node.Vertex[vertices.Length];

        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (!offset.IsIdentity)
            {
                v.Position = Vector3.Transform(v.Position, offset);
                v.Normal = Vector3.TransformNormal(v.Normal, offset);
            }

            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
            converted[i] = new Kn5Node.Vertex(
                new Vec3(v.Position.X, v.Position.Y, v.Position.Z),
                new Vec3(v.Normal.X, v.Normal.Y, v.Normal.Z),
                new Vec2(v.Uv.X, v.Uv.Y),
                new Vec3(1f, 0f, 0f));
        }

        var center = (min + max) / 2f;
        return new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = new List<Kn5Node>(),
            CastShadows = true,
            IsVisible = true,
            IsRenderable = true,
            Vertices = converted,
            Indices = indices,
            MaterialId = materialId,
            BoundingSphereCenter = new Vec3(center.X, center.Y, center.Z),
            BoundingSphereRadius = (max - min).Length() / 2f
        };
    }
}
