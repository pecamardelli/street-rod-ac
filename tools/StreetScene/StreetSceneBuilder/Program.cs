using System.Globalization;
using AcTools.Kn5File;
using AcTools.Numerics;
using Newtonsoft.Json.Linq;

namespace StreetSceneBuilder;

/// <summary>
/// Builds the street scene the Cruise screen is drawn in: one KN5 a light of the day (street_day.kn5, street_dusk.kn5,
/// street_night.kn5) out of the pictures prepare_street.py made from a panorama.
///
/// The panorama is projected from the point it was taken at, the capture height above the middle of the scene, onto a
/// ground and a dome: a floor disc near the middle, which is lit and takes the cars' shadows; a ground ring out to the
/// dome; and a hemisphere standing on the ring. Seen from the capture point the three meet without a seam, and the
/// street looks as photographed; from the driver's seat, a little lower, near enough.
///
///   StreetSceneBuilder &lt;prepared folder&gt; &lt;out folder&gt; [--dome-radius 35] [--flip]
/// </summary>
internal static class Program
{
    private static readonly string[] Lights = ["day", "dusk", "night"];

    // Around the scene; enough that the curved street stays smooth
    private const int Segments = 256;
    private const int FloorRings = 48;
    private const int GroundRings = 24;
    private const int DomeRings = 64;

    private const int MaxVerticesPerMesh = ushort.MaxValue;

    private static int Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        if (positional.Count < 2)
        {
            Console.Error.WriteLine("StreetSceneBuilder <prepared folder> <out folder> [--dome-radius 35] [--flip]");
            return 1;
        }

        var prepared = positional[0];
        var output = positional[1];
        var domeRadius = Option(args, "--dome-radius") ?? 35f;
        var flip = args.Contains("--flip");

        var light = JObject.Parse(File.ReadAllText(Path.Combine(prepared, "light.json")));
        var height = light.Value<float>("captureHeight");
        var floorHalf = light.Value<float>("floorHalfSize");
        if (floorHalf >= domeRadius)
        {
            Console.Error.WriteLine($"The floor ({floorHalf} m) must end inside the dome ({domeRadius} m)");
            return 1;
        }

        Directory.CreateDirectory(output);
        var floor = FloorDisc(floorHalf);
        var ground = GroundRing(floorHalf, domeRadius, height);
        var dome = Dome(domeRadius, height);

        foreach (var name in Lights)
        {
            var kn5 = Kn5.CreateEmpty();
            kn5.RootNode.Name = "street_" + name;

            var floorTexture = AddTexture(kn5, Path.Combine(prepared, $"floor_{name}.jpg"));
            var skyTexture = AddTexture(kn5, Path.Combine(prepared, $"sky_{name}.jpg"));

            // The floor is the photo lit by the scene: mostly its own light, so it looks as shot, with enough of the
            // key light that a car's shadow darkens it. The dome is the photo and nothing else.
            var floorMaterial = AddMaterial(kn5, "street_floor", floorTexture, ambient: 0.25f, diffuse: 0.35f, emissive: 0.55f);
            var skyMaterial = AddMaterial(kn5, "street_sky", skyTexture, ambient: 0f, diffuse: 0f, emissive: 1f);

            AddMesh(kn5, "FLOOR", floor, floorMaterial, flip);
            AddMesh(kn5, "GROUND", ground, skyMaterial, flip);
            AddMesh(kn5, "DOME", dome, skyMaterial, flip);

            var path = Path.Combine(output, $"street_{name}.kn5");
            kn5.Save(path);
            Console.WriteLine($"{path}: {new FileInfo(path).Length / 1024} KB");
        }

        return 0;
    }

    private static float? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? float.Parse(args[i + 1], CultureInfo.InvariantCulture) : null;
    }

    private sealed record Mesh(List<Kn5Node.Vertex> Vertices, List<int> Indices);

    /// <summary>Where the panorama sees a direction: the same mapping as prepare_street.py</summary>
    private static float PanoramaV(float dx, float dy, float dz) =>
        0.5f - MathF.Asin(Math.Clamp(dy / MathF.Sqrt(dx * dx + dy * dy + dz * dz), -1f, 1f)) / MathF.PI;

    private static float Azimuth(int column) => MathF.PI - 2f * MathF.PI * column / Segments;

    /// <summary>
    /// A grid of rings round the middle, <see cref="Segments"/> + 1 columns so the panorama's seam gets vertices of
    /// its own on both sides. A column's azimuth is its place in the panorama: u = column / segments.
    /// </summary>
    private static Mesh Rings(int rings, Func<int, int, (Vec3 Position, Vec3 Normal, Vec2 Uv)> vertex)
    {
        var mesh = new Mesh([], []);
        for (var ring = 0; ring <= rings; ring++)
        {
            for (var column = 0; column <= Segments; column++)
            {
                var (p, n, uv) = vertex(ring, column);
                mesh.Vertices.Add(new Kn5Node.Vertex(p, n, uv, new Vec3(1f, 0f, 0f)));
            }
        }

        var stride = Segments + 1;
        for (var ring = 0; ring < rings; ring++)
        {
            for (var column = 0; column < Segments; column++)
            {
                var a = ring * stride + column;
                var b = a + 1;
                var c = a + stride;
                var d = c + 1;
                mesh.Indices.AddRange([a, b, c, b, d, c]);
            }
        }

        return mesh;
    }

    /// <summary>The floor near the middle, drawn with the picture of the ground seen from above</summary>
    private static Mesh FloorDisc(float half) => Rings(FloorRings, (ring, column) =>
    {
        // Rings closer together near the middle, where the cars are
        var t = (float)ring / FloorRings;
        var r = half * t * t;
        var a = Azimuth(column);
        var x = r * MathF.Sin(a);
        var z = r * MathF.Cos(a);
        var uv = new Vec2((x + half) / (2f * half), (half - z) / (2f * half));
        return (new Vec3(x, 0f, z), new Vec3(0f, 1f, 0f), uv);
    });

    /// <summary>The ground from the floor out to the dome, drawn with the panorama itself</summary>
    private static Mesh GroundRing(float from, float to, float height) => Rings(GroundRings, (ring, column) =>
    {
        var r = from + (to - from) * ring / GroundRings;
        var a = Azimuth(column);
        var uv = new Vec2((float)column / Segments, PanoramaV(r, -height, 0f));
        return (new Vec3(r * MathF.Sin(a), 0f, r * MathF.Cos(a)), new Vec3(0f, 1f, 0f), uv);
    });

    /// <summary>The hemisphere standing on the ground ring, facing in</summary>
    private static Mesh Dome(float radius, float height) => Rings(DomeRings, (ring, column) =>
    {
        var elevation = MathF.PI / 2f * ring / DomeRings;
        var a = Azimuth(column);
        var horizontal = radius * MathF.Cos(elevation);
        var y = radius * MathF.Sin(elevation);
        var p = new Vec3(horizontal * MathF.Sin(a), y, horizontal * MathF.Cos(a));
        var inward = new Vec3(-MathF.Cos(elevation) * MathF.Sin(a), -MathF.Sin(elevation), -MathF.Cos(elevation) * MathF.Cos(a));
        var uv = new Vec2((float)column / Segments, PanoramaV(horizontal, y - height, 0f));
        return (p, inward, uv);
    });

    private static string AddTexture(IKn5 kn5, string file)
    {
        var data = File.ReadAllBytes(file);
        var key = Path.GetFileName(file).ToLowerInvariant();
        kn5.Textures[key] = new Kn5Texture { Name = key, Active = true, Length = data.Length };
        kn5.TexturesData[key] = data;
        return key;
    }

    private static uint AddMaterial(IKn5 kn5, string name, string texture, float ambient, float diffuse, float emissive)
    {
        kn5.Materials[name] = new Kn5Material
        {
            Name = name,
            ShaderName = "ksPerPixel",
            ShaderProperties =
            [
                Property("ksAmbient", ambient),
                Property("ksDiffuse", diffuse),
                Property("ksSpecular", 0f),
                Property("ksSpecularEXP", 1f),
                new Kn5Material.ShaderProperty { Name = "ksEmissive", ValueC = new Vec3(emissive, emissive, emissive) },
                Property("ksAlphaRef", 0f)
            ],
            TextureMappings = [new Kn5Material.TextureMapping { Name = "txDiffuse", Texture = texture, Slot = 0 }]
        };
        return (uint)(kn5.Materials.Count - 1);
    }

    private static Kn5Material.ShaderProperty Property(string name, float value) => new() { Name = name, ValueA = value };

    private static void AddMesh(IKn5 kn5, string name, Mesh mesh, uint material, bool flip)
    {
        if (mesh.Vertices.Count > MaxVerticesPerMesh) throw new InvalidOperationException($"{name}: too many vertices");

        var indices = mesh.Indices.Select(i => (ushort)i).ToArray();
        if (flip)
        {
            for (var i = 0; i + 2 < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
        }

        var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = new Vec3(Math.Min(min.X, v.Position.X), Math.Min(min.Y, v.Position.Y), Math.Min(min.Z, v.Position.Z));
            max = new Vec3(Math.Max(max.X, v.Position.X), Math.Max(max.Y, v.Position.Y), Math.Max(max.Z, v.Position.Z));
        }

        var size = new Vec3(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
        kn5.RootNode.Children.Add(new Kn5Node
        {
            NodeClass = Kn5NodeClass.Mesh,
            Name = name,
            Active = true,
            Children = [],
            // The street throws no shadows: the sun's are in the photo already
            CastShadows = false,
            IsVisible = true,
            IsRenderable = true,
            Vertices = mesh.Vertices.ToArray(),
            Indices = indices,
            MaterialId = material,
            BoundingSphereCenter = new Vec3((min.X + max.X) / 2f, (min.Y + max.Y) / 2f, (min.Z + max.Z) / 2f),
            BoundingSphereRadius = MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z) / 2f
        });
    }
}
