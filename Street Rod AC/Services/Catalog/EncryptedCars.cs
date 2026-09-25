using System.IO;
using System.Numerics;
using System.Text.Json;
using AcTools.Kn5File;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Finds the cars whose model is encrypted. Some mods ship a KN5 with its vertex normals scrambled, which CSP undoes
    /// in AC and nothing else does: the car races fine, but every viewer of the game (garage, dealer lot, main screen)
    /// draws it as shattered glass. <see cref="CarImportService"/> leaves such a car out of the catalog, the way it
    /// leaves out police cars, and it stays in AC's cars folder.
    ///
    /// The tell is the normals against their own triangles. In a real model a vertex normal points out of the faces it
    /// belongs to: across a car, the cosine between them averages about 0.95, and even a model with flipped faces
    /// keeps it well away from zero. Scrambled normals point anywhere, so the cosine averages zero and exactly half
    /// of them face backwards. Measured over the install on 2026-09-25: the eight encrypted cars sit at 0.000 ± 0.003
    /// with 0.498–0.501 backwards; the lowest real car is 0.27 (and a flipped one −0.17 with 0.59 backwards).
    ///
    /// Reading a model is seconds for the whole install, so the answer is kept per model file (path, size, time) in
    /// %AppData%/StreetRodAC/encrypted_cars.json and a car is only looked at again when its model changes.
    /// </summary>
    public static class EncryptedCars
    {
        private const int CacheVersion = 1;

        // The average cosine and the share facing backwards that only scrambled normals give
        private const double MaxMeanCosine = 0.05;
        private const double MinBackShare = 0.45;
        private const double MaxBackShare = 0.55;

        // Enough triangles to be sure of either answer; a model is not read past this
        private const int TriangleSample = 200_000;

        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger(LogCategory.Import);
        private static readonly object Lock = new();
        private static Dictionary<string, Entry>? _cache;
        private static bool _dirty;

        private sealed record Entry(long Size, long WriteTicks, bool Encrypted);

        private sealed record CacheFile(int Version, Dictionary<string, Entry> Models);

        public static string CachePath => Path.Combine(AppSettings.AppDataPath, "encrypted_cars.json");

        /// <summary>
        /// The car's model is encrypted. False when it cannot be told (no model, a model that will not read): a car
        /// is only left out on evidence.
        /// </summary>
        public static bool IsEncrypted(string carFolder)
        {
            var model = CarModelFiles.MainModel(carFolder);
            if (model == null) return false;

            // Read once, here: the model may be gone by now (a mod being replaced while the import runs)
            long size, writeTicks;
            try
            {
                var info = new FileInfo(model);
                size = info.Length;
                writeTicks = info.LastWriteTimeUtc.Ticks;
            }
            catch (Exception)
            {
                return false;
            }

            lock (Lock)
            {
                var cache = LoadCache();
                if (cache.TryGetValue(model, out var known) && known.Size == size && known.WriteTicks == writeTicks)
                    return known.Encrypted;

                bool encrypted;
                try
                {
                    encrypted = Looks(Kn5.FromFile(model, SkippingTextureLoader.Instance, SkippingMaterialLoader.Instance));
                }
                catch (Exception ex)
                {
                    // Kept as not encrypted, so a model that will not read is not read again on every start-up
                    Logger.Warning("Could not read {Model} to tell if it is encrypted: {Error}", model, ex.Message);
                    encrypted = false;
                }

                cache[model] = new Entry(size, writeTicks, encrypted);
                _dirty = true;
                if (encrypted) Logger.Information("{Car} has an encrypted model: left out of the game", Path.GetFileName(carFolder));
                return encrypted;
            }
        }

        /// <summary>Writes what was learnt this scan, if anything was. Called once at the end of an import.</summary>
        public static void SaveCache()
        {
            lock (Lock)
            {
                if (!_dirty || _cache == null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    Helpers.SafeFile.WriteAllText(CachePath, JsonSerializer.Serialize(new CacheFile(CacheVersion, _cache)));
                    _dirty = false;
                }
                catch (Exception ex)
                {
                    // Only costs the next start-up a second look
                    Logger.Warning("Could not save {Path}: {Error}", CachePath, ex.Message);
                }
            }
        }

        private static bool Looks(IKn5 model)
        {
            var normals = new NormalAgreement();
            foreach (var node in model.Nodes)
            {
                if (node.NodeClass == Kn5NodeClass.Base || node.Vertices is not { Length: > 0 } vertices || node.Indices is not { Length: >= 3 } indices)
                    continue;

                for (var i = 0; i + 2 < indices.Length && normals.Triangles < TriangleSample; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;

                    normals.Add(
                        V(vertices[a].Position), V(vertices[b].Position), V(vertices[c].Position),
                        V(vertices[a].Normal), V(vertices[b].Normal), V(vertices[c].Normal));
                }

                if (normals.Triangles >= TriangleSample) break;
            }

            return normals.LooksScrambled;
        }

        private static Vector3 V(AcTools.Numerics.Vec3 v) => new(v.X, v.Y, v.Z);

        private static Dictionary<string, Entry> LoadCache()
        {
            if (_cache != null) return _cache;

            try
            {
                if (File.Exists(CachePath)
                    && JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath)) is { Version: CacheVersion, Models: { } models })
                {
                    return _cache = new Dictionary<string, Entry>(models, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Could not read {Path}, looking at every model again: {Error}", CachePath, ex.Message);
            }

            return _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>How well a model's vertex normals agree with its faces</summary>
        public sealed class NormalAgreement
        {
            private double _cosineSum;
            private long _count;
            private long _backwards;

            public int Triangles { get; private set; }

            /// <summary>The cosine between vertex normal and face normal, averaged over every corner</summary>
            public double MeanCosine => _count == 0 ? 1 : _cosineSum / _count;

            /// <summary>The share of corners whose normal faces away from its triangle</summary>
            public double BackShare => _count == 0 ? 0 : (double)_backwards / _count;

            /// <summary>Normals pointing anywhere: what an encrypted model looks like, and no real one does</summary>
            public bool LooksScrambled => _count >= 300
                                          && Math.Abs(MeanCosine) < MaxMeanCosine
                                          && BackShare is > MinBackShare and < MaxBackShare;

            public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                var face = Vector3.Cross(b - a, c - a);
                var length = face.Length();
                if (length < 1e-9f) return;

                face /= length;
                Triangles++;
                Corner(na, face);
                Corner(nb, face);
                Corner(nc, face);
            }

            private void Corner(Vector3 normal, Vector3 face)
            {
                var cosine = Vector3.Dot(normal, face);
                if (!float.IsFinite(cosine)) return;

                _cosineSum += cosine;
                _count++;
                if (cosine < 0) _backwards++;
            }
        }
    }
}
