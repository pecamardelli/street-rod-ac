using System.IO;
using System.Numerics;
using System.Text;

namespace Street_Rod_AC.Slrr;

public struct SlrrVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
}

/// <summary>One material's worth of geometry inside an SCX file</summary>
public sealed class SlrrSubMesh
{
    public string MaterialName = string.Empty;

    /// <summary>Index into the owning render's texture list, -1 if untextured</summary>
    public int DiffuseTextureIndex = -1;

    /// <summary>Index of the environment/reflection texture, -1 if the material is not reflective</summary>
    public int ReflectionTextureIndex = -1;

    public Vector3 DiffuseColor = new(0.6f);
    public float Glossiness = 16f;

    public SlrrVertex[] Vertices = Array.Empty<SlrrVertex>();
    public int[] Indices = Array.Empty<int>();
}

/// <summary>
/// SLRR .scx mesh ("INVO" container). Version 3 is a flat list of material/vertex/index blocks with
/// fixed 64-byte vertices; version 4 is a chunk table with a flexible vertex layout.
/// Positions are converted from centimetres to metres on load.
///
/// Counts, offsets and sizes come from the file (mods are made by anyone), so each is checked against what is left of
/// it before anything is allocated or read: a damaged or crafted mesh is an <see cref="InvalidDataException"/> for
/// its part, never an allocation of gigabytes or a silently degenerate model.
/// </summary>
public sealed class SlrrMesh
{
    private const float UnitsToMetres = 0.01f;

    // Version 3
    private const int V3VertexSize = 64;
    private const int V3TriangleSize = 12;
    private const int V3MaterialTexturesOffset = 0x2C;
    private const int V3MaterialReflectionSlot = 3;
    private const int MaterialNameSize = 32;

    /// <summary>A version 3 material block reaches at least past its texture slots and holds its name at its end</summary>
    private const int V3MaterialMinSize = V3MaterialTexturesOffset + (V3MaterialReflectionSlot + 1) * 2;

    // Version 4 chunk types
    private const int ChunkMaterial = 0;
    private const int ChunkVertices = 4;
    private const int ChunkIndices = 5;

    // Version 4 vertex layout flags, in storage order
    private const uint FlagPosition = 0x1;
    private const uint FlagBlendWeight = 0x4;
    private const uint FlagNormal = 0x40;
    private const uint FlagUv = 0x200;

    // Version 4 material entry kinds (high byte of the entry key)
    private const int EntryValue = 0;
    private const int EntryFloat = 1;
    private const int EntryTexture = 6;
    private const int EntryName = 8;
    private const int TextureSlotDiffuse = 0;
    private const int TextureSlotReflection = 3;

    public List<SlrrSubMesh> SubMeshes { get; } = new();

    public static SlrrMesh Load(string filename)
    {
        var data = File.ReadAllBytes(filename);
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "INVO")
            throw new InvalidDataException($"Not an SCX file: {filename}");

        var version = BitConverter.ToInt32(data, 4);
        return version switch
        {
            3 => LoadV3(data),
            4 => LoadV4(data),
            _ => throw new InvalidDataException($"Unsupported SCX version {version}: {filename}")
        };
    }

    private static SlrrMesh LoadV3(byte[] data)
    {
        var mesh = new SlrrMesh();
        var position = 8;

        while (position + 4 <= data.Length)
        {
            var materialSize = BitConverter.ToInt32(data, position);
            if (materialSize <= 0) break;

            // The material block holds the colour, the texture slots and, at its end, the name; the vertex count follows it
            if (materialSize < V3MaterialMinSize || (long)position + materialSize + 4 > data.Length)
                throw new InvalidDataException($"SCX material block of {materialSize} bytes at {position} does not fit the file");

            var sub = new SlrrSubMesh
            {
                DiffuseColor = new Vector3(
                    BitConverter.ToSingle(data, position + 4),
                    BitConverter.ToSingle(data, position + 8),
                    BitConverter.ToSingle(data, position + 12)),
                Glossiness = BitConverter.ToSingle(data, position + 36),
                DiffuseTextureIndex = BitConverter.ToInt16(data, position + V3MaterialTexturesOffset),
                ReflectionTextureIndex = BitConverter.ToInt16(data, position + V3MaterialTexturesOffset + V3MaterialReflectionSlot * 2),
                MaterialName = ReadName(data, position + materialSize - MaterialNameSize)
            };
            position += materialSize;

            var vertexCount = BitConverter.ToInt32(data, position);
            position += 4;
            // The triangle count follows the vertices
            if (vertexCount < 0 || vertexCount > (data.Length - position - 4) / V3VertexSize)
                throw new InvalidDataException($"SCX vertex count {vertexCount} at {position - 4} does not fit the file");

            sub.Vertices = new SlrrVertex[vertexCount];
            for (var i = 0; i < vertexCount; i++, position += V3VertexSize)
            {
                sub.Vertices[i] = new SlrrVertex
                {
                    Position = ReadVector3(data, position) * UnitsToMetres,
                    Normal = ReadVector3(data, position + 12),
                    Uv = new Vector2(BitConverter.ToSingle(data, position + 24), BitConverter.ToSingle(data, position + 28))
                };
            }

            var triangleCount = BitConverter.ToInt32(data, position);
            position += 4;
            if (triangleCount < 0 || triangleCount > (data.Length - position) / V3TriangleSize)
                throw new InvalidDataException($"SCX triangle count {triangleCount} at {position - 4} does not fit the file");

            sub.Indices = new int[checked(triangleCount * 3)];
            for (var i = 0; i < sub.Indices.Length; i++, position += 4)
            {
                sub.Indices[i] = BitConverter.ToInt32(data, position);
            }

            mesh.SubMeshes.Add(sub);
        }

        return mesh;
    }

    private static SlrrMesh LoadV4(byte[] data)
    {
        var mesh = new SlrrMesh();
        var chunkCount = BitConverter.ToInt32(data, 8);
        if (chunkCount < 0 || chunkCount > (data.Length - 12) / 8)
            throw new InvalidDataException($"SCX chunk count {chunkCount} does not fit the file");

        SlrrSubMesh? sub = null;

        for (var i = 0; i < chunkCount; i++)
        {
            var type = BitConverter.ToInt32(data, 12 + i * 8);
            var offset = BitConverter.ToInt32(data, 16 + i * 8);
            if (offset < 0 || (long)offset + 8 > data.Length)
                throw new InvalidDataException($"SCX chunk {i} at {offset} lies outside the file");

            var size = BitConverter.ToInt32(data, offset + 4);
            if (size < 0 || (long)offset + size > data.Length)
                throw new InvalidDataException($"SCX chunk {i} of {size} bytes at {offset} does not fit the file");

            switch (type)
            {
                case ChunkMaterial:
                    sub = ReadV4Material(data, offset, offset + size);
                    mesh.SubMeshes.Add(sub);
                    break;

                case ChunkVertices when sub != null:
                    ReadV4Vertices(data, offset, size, sub);
                    break;

                case ChunkIndices when sub != null:
                    if (size < 12) throw new InvalidDataException($"SCX index chunk of {size} bytes at {offset} is too small");

                    var indexCount = BitConverter.ToInt32(data, offset + 8);
                    if (indexCount < 0 || indexCount > (size - 12) / 2)
                        throw new InvalidDataException($"SCX index count {indexCount} at {offset} does not fit its chunk of {size} bytes");

                    sub.Indices = new int[indexCount];
                    for (var j = 0; j < indexCount; j++)
                    {
                        sub.Indices[j] = BitConverter.ToUInt16(data, offset + 12 + j * 2);
                    }
                    break;
            }
        }

        return mesh;
    }

    private static SlrrSubMesh ReadV4Material(byte[] data, int offset, int end)
    {
        var sub = new SlrrSubMesh();
        var position = offset + 20;

        while (position + 4 <= end)
        {
            var key = BitConverter.ToUInt32(data, position);
            var kind = (int)(key >> 24);
            var index = (int)(key & 0xFFFFFF);
            position += 4;

            // An entry's payload may run past the chunk's end (a name at the very end does), never past the file's
            var payload = kind switch
            {
                EntryValue or EntryFloat => 4,
                EntryTexture => 28,
                EntryName => MaterialNameSize,
                _ => 0
            };
            if ((long)position + payload > data.Length)
                throw new InvalidDataException($"SCX material entry at {position - 4} runs past the end of the file");

            switch (kind)
            {
                case EntryValue:
                    if (index == 0)
                    {
                        sub.DiffuseColor = new Vector3(data[position], data[position + 1], data[position + 2]) / 255f;
                    }
                    position += 4;
                    break;

                case EntryFloat:
                    if (index == 0) sub.Glossiness = BitConverter.ToSingle(data, position);
                    position += 4;
                    break;

                case EntryTexture:
                    var textureIndex = BitConverter.ToInt32(data, position);
                    if (index == TextureSlotDiffuse) sub.DiffuseTextureIndex = textureIndex;
                    if (index == TextureSlotReflection) sub.ReflectionTextureIndex = textureIndex;
                    position += 28;
                    break;

                case EntryName:
                    sub.MaterialName = ReadName(data, position);
                    position += MaterialNameSize;
                    break;

                default:
                    // Unknown entry: its size is unknown too, so stop reading this material
                    return sub;
            }
        }

        return sub;
    }

    private static void ReadV4Vertices(byte[] data, int offset, int size, SlrrSubMesh sub)
    {
        var count = BitConverter.ToInt32(data, offset + 8);
        var flags = BitConverter.ToUInt32(data, offset + 12);
        if (count <= 0 || (flags & FlagPosition) == 0) return;

        if (size < 16) throw new InvalidDataException($"SCX vertex chunk of {size} bytes at {offset} is too small");

        var stride = (size - 16) / count;
        var normalOffset = 12 + ((flags & FlagBlendWeight) != 0 ? 4 : 0);
        var uvOffset = normalOffset + ((flags & FlagNormal) != 0 ? 12 : 0);

        // A stride below what the layout needs reads one vertex's bytes as the next one's (0 reads the same bytes for
        // all of them): a degenerate model, not a mesh
        var minimumStride = uvOffset + ((flags & FlagUv) != 0 ? 8 : 0);
        if (stride < minimumStride || 16 + (long)count * stride > size)
            throw new InvalidDataException($"SCX vertex chunk at {offset}: {count} vertices of at least {minimumStride} bytes do not fit {size} bytes");

        sub.Vertices = new SlrrVertex[count];
        for (var i = 0; i < count; i++)
        {
            var position = offset + 16 + i * stride;
            sub.Vertices[i] = new SlrrVertex
            {
                Position = ReadVector3(data, position) * UnitsToMetres,
                Normal = (flags & FlagNormal) != 0 ? ReadVector3(data, position + normalOffset) : Vector3.UnitY,
                Uv = (flags & FlagUv) != 0
                    ? new Vector2(BitConverter.ToSingle(data, position + uvOffset), BitConverter.ToSingle(data, position + uvOffset + 4))
                    : Vector2.Zero
            };
        }
    }

    private static Vector3 ReadVector3(byte[] data, int offset) => new(
        BitConverter.ToSingle(data, offset),
        BitConverter.ToSingle(data, offset + 4),
        BitConverter.ToSingle(data, offset + 8));

    private static string ReadName(byte[] data, int offset)
    {
        var length = 0;
        while (length < MaterialNameSize && data[offset + length] != 0) length++;
        return Encoding.Latin1.GetString(data, offset, length);
    }
}
