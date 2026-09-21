using System.Numerics;

namespace Street_Rod_AC.Slrr;

/// <summary>
/// Takes triangles out of a mesh that has far more than its shape needs (a hood scoop exported from a CAD
/// program with 90,000 of them), by collapsing edges in the order of the error they cause (quadric error
/// metric, Garland and Heckbert). A vertex only ever moves onto a neighbour, so what is left are vertices of
/// the original with their normals and texture coordinates untouched.
///
/// The shape is followed by position, across materials and texture seams: a vertex on a seam (several
/// normals or texture coordinates at one position, or the rim of an open mesh) only moves along that seam,
/// a vertex where seams meet stays. Nothing tears open and no texture creeps over an edge.
/// </summary>
public static class SlrrMeshSimplifier
{
    // A triangle that turns further than this from where it faced is a fold, not a simplification
    private const float MinFacingAfter = 0.2f;

    // How much more leaving the line of a seam costs than leaving a surface
    private const double SeamWeight = 4;

    /// <param name="subMeshes">Welded: a vertex that is used with the same normal and texture coordinates is one vertex</param>
    /// <param name="targetTriangles">Where to stop</param>
    /// <param name="maxError">
    /// Where to stop even if the target has not been reached: the sum of the squared distances (square metres)
    /// of a vertex to the planes of the triangles it stands in for
    /// </param>
    public static List<SlrrSubMesh> Simplify(IReadOnlyList<SlrrSubMesh> subMeshes, int targetTriangles, double maxError)
    {
        var mesh = new Work(subMeshes);
        mesh.Run(targetTriangles, maxError);
        return mesh.Result();
    }

    private sealed class Work
    {
        private readonly IReadOnlyList<SlrrSubMesh> _subMeshes;
        private readonly int[] _wedgeOffset;

        private readonly List<Vector3> _positions = new();
        private readonly List<int[]> _corners = new();      // per triangle: position of each corner
        private readonly List<int[]> _wedges = new();       // per triangle: vertex (with its normal and uv) of each corner
        private readonly List<int> _subMeshOf = new();
        private readonly List<bool> _alive = new();

        // Thin parts are often modelled with both sides (the renderer only draws the one facing the camera):
        // every triangle a second time, turned over. One side is worked on, the other is made from it again.
        private readonly List<bool> _twoSided = new();
        private int _aliveCount;

        private List<int>[] _trianglesAt = Array.Empty<List<int>>();
        private Quadric[] _quadrics = Array.Empty<Quadric>();
        private int[] _version = Array.Empty<int>();

        public Work(IReadOnlyList<SlrrSubMesh> subMeshes)
        {
            _subMeshes = subMeshes;
            _wedgeOffset = new int[subMeshes.Count];

            var positionIds = new Dictionary<Vector3, int>();
            var facings = new Dictionary<(int, int, int), int>();
            var offset = 0;
            for (var s = 0; s < subMeshes.Count; s++)
            {
                var sub = subMeshes[s];
                _wedgeOffset[s] = offset;
                offset += sub.Vertices.Length;

                for (var i = 0; i + 2 < sub.Indices.Length; i += 3)
                {
                    var corners = new int[3];
                    var wedges = new int[3];
                    for (var j = 0; j < 3; j++)
                    {
                        var position = sub.Vertices[sub.Indices[i + j]].Position;
                        if (!positionIds.TryGetValue(position, out var id))
                        {
                            positionIds[position] = id = _positions.Count;
                            _positions.Add(position);
                        }

                        corners[j] = id;
                        wedges[j] = _wedgeOffset[s] + sub.Indices[i + j];
                    }

                    // Triangles without a surface say nothing about the shape and only get in the way
                    if (corners[0] == corners[1] || corners[1] == corners[2] || corners[0] == corners[2]) continue;

                    // The other side of a triangle that is already there: it comes back when the work is done
                    var first = Array.IndexOf(corners, corners.Min());
                    var facing = (corners[first], corners[(first + 1) % 3], corners[(first + 2) % 3]);
                    if (facings.TryGetValue((facing.Item1, facing.Item3, facing.Item2), out var front) && _subMeshOf[front] == s)
                    {
                        _twoSided[front] = true;
                        continue;
                    }

                    facings.TryAdd(facing, _corners.Count);
                    _corners.Add(corners);
                    _wedges.Add(wedges);
                    _subMeshOf.Add(s);
                    _alive.Add(true);
                    _twoSided.Add(false);
                }
            }

            _aliveCount = _twoSided.Sum(Sides);
        }

        private static int Sides(bool twoSided) => twoSided ? 2 : 1;

        public void Run(int targetTriangles, double maxError)
        {
            if (_aliveCount <= targetTriangles) return;

            _trianglesAt = new List<int>[_positions.Count];
            for (var i = 0; i < _trianglesAt.Length; i++) _trianglesAt[i] = new List<int>();
            for (var t = 0; t < _corners.Count; t++)
            {
                foreach (var corner in _corners[t]) _trianglesAt[corner].Add(t);
            }

            _version = new int[_positions.Count];
            _quadrics = new Quadric[_positions.Count];
            AddSurfaces();
            AddSeams();

            var queue = new PriorityQueue<(int Keep, int Remove, int KeepVersion, int RemoveVersion), double>();
            for (var v = 0; v < _positions.Count; v++) Push(queue, v);

            // The cheapest edge first. An entry made before either end changed is out of date and skipped; the
            // edges of the end that stays are queued anew after every collapse, refused ones among them.
            while (_aliveCount > targetTriangles && queue.TryDequeue(out var edge, out var cost))
            {
                if (cost > maxError) break;
                if (_version[edge.Keep] != edge.KeepVersion || _version[edge.Remove] != edge.RemoveVersion) continue;
                if (!Collapse(edge.Keep, edge.Remove)) continue;

                Push(queue, edge.Keep);
            }
        }

        public List<SlrrSubMesh> Result()
        {
            var result = new List<SlrrSubMesh>();
            for (var s = 0; s < _subMeshes.Count; s++)
            {
                var source = _subMeshes[s];
                var remap = new Dictionary<(int Vertex, bool Back), int>();
                var vertices = new List<SlrrVertex>();
                var indices = new List<int>();

                for (var t = 0; t < _corners.Count; t++)
                {
                    if (!_alive[t] || _subMeshOf[t] != s) continue;

                    Add(_wedges[t][0], _wedges[t][1], _wedges[t][2], false);
                    if (_twoSided[t]) Add(_wedges[t][0], _wedges[t][2], _wedges[t][1], true);
                }

                void Add(int a, int b, int c, bool back)
                {
                    foreach (var wedge in new[] { a, b, c })
                    {
                        var vertex = wedge - _wedgeOffset[s];
                        if (!remap.TryGetValue((vertex, back), out var index))
                        {
                            remap[(vertex, back)] = index = vertices.Count;

                            var copy = source.Vertices[vertex];
                            if (back) copy.Normal = -copy.Normal;
                            vertices.Add(copy);
                        }

                        indices.Add(index);
                    }
                }

                result.Add(new SlrrSubMesh
                {
                    MaterialName = source.MaterialName,
                    DiffuseTextureIndex = source.DiffuseTextureIndex,
                    ReflectionTextureIndex = source.ReflectionTextureIndex,
                    DiffuseColor = source.DiffuseColor,
                    Glossiness = source.Glossiness,
                    Vertices = vertices.ToArray(),
                    Indices = indices.ToArray()
                });
            }

            return result;
        }

        private void AddSurfaces()
        {
            for (var t = 0; t < _corners.Count; t++)
            {
                if (Normal(t) is not { } normal) continue;

                var plane = Quadric.OfPlane(normal, _positions[_corners[t][0]], 1);
                foreach (var corner in _corners[t]) _quadrics[corner].Add(plane);
            }
        }

        /// <summary>
        /// Along a seam or a rim a vertex is held to the line as well: a plane through the edge, square to the
        /// triangle, for either end. Without it the outline of an open mesh would be free to shrink.
        /// </summary>
        private void AddSeams()
        {
            for (var t = 0; t < _corners.Count; t++)
            {
                if (Normal(t) is not { } normal) continue;

                for (var j = 0; j < 3; j++)
                {
                    var a = _corners[t][j];
                    var b = _corners[t][(j + 1) % 3];
                    if (!IsSeam(a, b)) continue;

                    var across = Vector3.Cross(_positions[b] - _positions[a], normal);
                    if (across.LengthSquared() < 1e-20f) continue;

                    var plane = Quadric.OfPlane(Vector3.Normalize(across), _positions[a], SeamWeight);
                    _quadrics[a].Add(plane);
                    _quadrics[b].Add(plane);
                }
            }
        }

        /// <summary>An edge with anything but two triangles on it, or whose two triangles do not share their vertices</summary>
        private bool IsSeam(int a, int b)
        {
            var first = -1;
            var count = 0;
            foreach (var t in _trianglesAt[a])
            {
                if (!_alive[t] || Array.IndexOf(_corners[t], b) < 0) continue;

                count++;
                if (first < 0) first = t;
                else if (WedgeAt(t, a) != WedgeAt(first, a) || WedgeAt(t, b) != WedgeAt(first, b)) return true;
            }

            return count != 2;
        }

        private void Push(PriorityQueue<(int, int, int, int), double> queue, int vertex)
        {
            foreach (var neighbour in Neighbours(vertex))
            {
                var position = _quadrics[vertex];
                position.Add(_quadrics[neighbour]);

                // Either end may stay; which ones are allowed to is found out when their turn comes
                queue.Enqueue((vertex, neighbour, _version[vertex], _version[neighbour]), position.Error(_positions[vertex]));
                queue.Enqueue((neighbour, vertex, _version[neighbour], _version[vertex]), position.Error(_positions[neighbour]));
            }
        }

        private HashSet<int> Neighbours(int vertex)
        {
            var result = new HashSet<int>();
            foreach (var t in _trianglesAt[vertex])
            {
                if (!_alive[t]) continue;

                foreach (var corner in _corners[t])
                {
                    if (corner != vertex) result.Add(corner);
                }
            }

            return result;
        }

        /// <summary>Moves <paramref name="remove"/> onto <paramref name="keep"/>, if the mesh stays in one piece and keeps its looks</summary>
        private bool Collapse(int keep, int remove)
        {
            var around = _trianglesAt[remove].Where(t => _alive[t]).ToList();
            var shared = around.Where(t => Array.IndexOf(_corners[t], keep) >= 0).ToList();
            if (shared.Count is 0 or > 2) return false;

            // The vertices of the removed end hand over to those of the end that stays, as the triangles on the
            // edge pair them. One that is not on the edge belongs to a seam the edge does not follow.
            var handOver = new Dictionary<int, int>();
            foreach (var t in shared)
            {
                var from = WedgeAt(t, remove);
                var to = WedgeAt(t, keep);
                if (handOver.TryGetValue(from, out var other) && other != to) return false;

                handOver[from] = to;
            }

            if (around.Any(t => !handOver.ContainsKey(WedgeAt(t, remove)))) return false;

            // A vertex on a rim moves along the rim only
            var timesSeen = new Dictionary<int, int>();
            foreach (var t in around)
            {
                foreach (var corner in _corners[t])
                {
                    if (corner != remove) timesSeen[corner] = timesSeen.GetValueOrDefault(corner) + 1;
                }
            }

            if (shared.Count == 2 && timesSeen.Values.Any(times => times == 1)) return false;

            // Both ends may have no more neighbours in common than the triangles on the edge account for,
            // or two sheets of the mesh end up on top of each other
            var keepNeighbours = Neighbours(keep);
            if (timesSeen.Keys.Count(keepNeighbours.Contains) != shared.Count) return false;

            foreach (var t in around)
            {
                if (shared.Contains(t) || Normal(t) is not { } before) continue;

                var moved = _corners[t].Select(c => _positions[c == remove ? keep : c]).ToArray();
                var after = Vector3.Cross(moved[1] - moved[0], moved[2] - moved[0]);
                if (after.LengthSquared() < 1e-20f || Vector3.Dot(before, Vector3.Normalize(after)) < MinFacingAfter) return false;
            }

            foreach (var t in around)
            {
                if (shared.Contains(t))
                {
                    _alive[t] = false;
                    _aliveCount -= Sides(_twoSided[t]);
                    continue;
                }

                var corner = Array.IndexOf(_corners[t], remove);
                _corners[t][corner] = keep;
                _wedges[t][corner] = handOver[_wedges[t][corner]];
            }

            _trianglesAt[keep] = _trianglesAt[keep].Concat(around).Where(t => _alive[t]).Distinct().ToList();
            _trianglesAt[remove].Clear();
            _quadrics[keep].Add(_quadrics[remove]);

            // Whatever was queued for either end, and for the neighbours towards them, is out of date
            _version[keep]++;
            _version[remove]++;
            return true;
        }

        private int WedgeAt(int triangle, int position) => _wedges[triangle][Array.IndexOf(_corners[triangle], position)];

        private Vector3? Normal(int triangle)
        {
            var corners = _corners[triangle];
            var normal = Vector3.Cross(_positions[corners[1]] - _positions[corners[0]], _positions[corners[2]] - _positions[corners[0]]);
            return normal.LengthSquared() < 1e-20f ? null : Vector3.Normalize(normal);
        }
    }

    /// <summary>Sum of squared distances to a set of planes, as the ten numbers of a symmetric 4x4 matrix</summary>
    private struct Quadric
    {
        private double _xx, _xy, _xz, _xw, _yy, _yz, _yw, _zz, _zw, _ww;

        public static Quadric OfPlane(Vector3 normal, Vector3 point, double weight)
        {
            double x = normal.X, y = normal.Y, z = normal.Z;
            var w = -(x * point.X + y * point.Y + z * point.Z);
            return new Quadric
            {
                _xx = weight * x * x, _xy = weight * x * y, _xz = weight * x * z, _xw = weight * x * w,
                _yy = weight * y * y, _yz = weight * y * z, _yw = weight * y * w,
                _zz = weight * z * z, _zw = weight * z * w,
                _ww = weight * w * w
            };
        }

        public void Add(Quadric other)
        {
            _xx += other._xx; _xy += other._xy; _xz += other._xz; _xw += other._xw;
            _yy += other._yy; _yz += other._yz; _yw += other._yw;
            _zz += other._zz; _zw += other._zw;
            _ww += other._ww;
        }

        public readonly double Error(Vector3 point)
        {
            double x = point.X, y = point.Y, z = point.Z;
            return Math.Max(0, _xx * x * x + 2 * _xy * x * y + 2 * _xz * x * z + 2 * _xw * x
                               + _yy * y * y + 2 * _yz * y * z + 2 * _yw * y
                               + _zz * z * z + 2 * _zw * z
                               + _ww);
        }
    }
}
