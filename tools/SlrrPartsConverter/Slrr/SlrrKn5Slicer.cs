using AcTools.Kn5File;
using AcTools.Numerics;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Keeps one of the identical items a model draws in a row along the engine axis (a dual-quad set of carburettors,
/// a Six Pack, a tri-power air box): every triangle goes with the item whose nominal centre is nearest, one item
/// stays and is moved onto the model's origin. Items are taken as evenly spaced about the model's centre.
/// </summary>
public static class SlrrKn5Slicer
{
    /// <param name="count">Items in the row</param>
    /// <param name="spacing">Distance between neighbouring items, metres</param>
    /// <returns>How far along the axis the kept item was, before it was moved onto the origin</returns>
    public static float KeepOne(IKn5 kn5, int count, float spacing)
    {
        var meshes = kn5.RootNode.Children.Where(n => n.NodeClass == Kn5NodeClass.Mesh).ToList();
        if (count < 2 || meshes.Count == 0) return 0;

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var vertex in meshes.SelectMany(m => m.Vertices))
        {
            min = Math.Min(min, vertex.Position.Z);
            max = Math.Max(max, vertex.Position.Z);
        }

        // The item kept is the middle one, or the rearmost of a pair: the one a build's first carburettor pad takes
        var centre = (min + max) / 2;
        var kept = (count - 1) / 2;
        var keptZ = centre + (kept - (count - 1) / 2f) * spacing;
        int Nearest(float z) => (int)Math.Clamp(Math.Round((z - centre) / spacing + (count - 1) / 2f), 0, count - 1);

        foreach (var mesh in meshes)
        {
            var indices = new List<ushort>(mesh.Indices.Length);
            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                var z = (mesh.Vertices[mesh.Indices[i]].Position.Z + mesh.Vertices[mesh.Indices[i + 1]].Position.Z + mesh.Vertices[mesh.Indices[i + 2]].Position.Z) / 3;
                if (Nearest(z) != kept) continue;

                indices.Add(mesh.Indices[i]);
                indices.Add(mesh.Indices[i + 1]);
                indices.Add(mesh.Indices[i + 2]);
            }

            if (indices.Count == 0)
            {
                kn5.RootNode.Children.Remove(mesh);
                continue;
            }

            // Vertices the kept triangles use, moved onto the origin
            var used = new Dictionary<ushort, ushort>();
            var vertices = new List<Kn5Node.Vertex>();
            var boundsMin = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            var boundsMax = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            for (var i = 0; i < indices.Count; i++)
            {
                if (!used.TryGetValue(indices[i], out var index))
                {
                    var vertex = mesh.Vertices[indices[i]];
                    var position = new Vec3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z - keptZ);
                    used[indices[i]] = index = (ushort)vertices.Count;
                    vertices.Add(new Kn5Node.Vertex(position, vertex.Normal, vertex.Tex, vertex.Tangent));
                    boundsMin = new Vec3(Math.Min(boundsMin.X, position.X), Math.Min(boundsMin.Y, position.Y), Math.Min(boundsMin.Z, position.Z));
                    boundsMax = new Vec3(Math.Max(boundsMax.X, position.X), Math.Max(boundsMax.Y, position.Y), Math.Max(boundsMax.Z, position.Z));
                }

                indices[i] = index;
            }

            mesh.Vertices = vertices.ToArray();
            mesh.Indices = indices.ToArray();
            mesh.BoundingSphereCenter = new Vec3((boundsMin.X + boundsMax.X) / 2, (boundsMin.Y + boundsMax.Y) / 2, (boundsMin.Z + boundsMax.Z) / 2);
            var extent = new Vec3(boundsMax.X - boundsMin.X, boundsMax.Y - boundsMin.Y, boundsMax.Z - boundsMin.Z);
            mesh.BoundingSphereRadius = MathF.Sqrt(extent.X * extent.X + extent.Y * extent.Y + extent.Z * extent.Z) / 2;
        }

        return keptZ;
    }
}
