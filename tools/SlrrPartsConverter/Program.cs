using System.Diagnostics;
using Newtonsoft.Json;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Slrr;

namespace Street_Rod_AC;

/// <summary>
/// Converts the parts of a Street Legal Racing: Redline install into the game's own part format:
/// one folder per rpk with a pack.json (part definitions, slots, attach graph) and a KN5 model per part.
/// </summary>
public static class Program
{
    private const string PartsFolder = "parts";
    private const string BaseRpk = "parts.rpk";
    private const string BasePackId = "stock";
    private const string SlotRoleSuffix = "_slot_ID";
    private const string ConstantsFile = "script_constants.json";

    private sealed record SourcePart(SlrrRpk Rpk, SlrrRpkEntry Entry, string ConfigFile, string? ScriptPath, string Id, string Name);

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: SlrrPartsConverter <SLRR folder> <output folder> [pack filter]");
            Console.WriteLine(@"  e.g. SlrrPartsConverter ""D:\Games\SLRR"" ""C:\Games\AC\content\parts"" engines/Mopar");
            return 1;
        }

        var game = new SlrrGame(args[0]);
        var scripts = new SlrrScriptEvaluator(game);
        var output = args[1];
        var filter = args.Length > 2 ? args[2] : null;

        var partsRoot = Path.Combine(game.Root, PartsFolder);
        if (!Directory.Exists(partsRoot))
        {
            Console.WriteLine($"No '{PartsFolder}' folder in {game.Root}");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();

        // First pass: give every part an id, so slots can refer to parts of any pack
        var packs = new List<(string Id, SlrrRpk Rpk, List<SourcePart> Parts)>();
        var partIds = new Dictionary<(SlrrRpk, int), string>();

        // The base game keeps its stock parts (running gear, accessories, neons) in an rpk next to the parts folder
        var baseRpk = Path.Combine(game.Root, BaseRpk);
        var files = Directory.EnumerateFiles(partsRoot, "*.rpk", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        if (File.Exists(baseRpk)) files.Insert(0, baseRpk);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(game.Root, file);
            var rpk = game.GetRpk(relativePath);
            if (rpk == null) continue;

            var packId = file == baseRpk
                ? BasePackId
                : Path.ChangeExtension(Path.GetRelativePath(partsRoot, file), null).Replace('\\', '/');
            var parts = CollectParts(game, rpk, packId);
            if (parts.Count == 0) continue;

            packs.Add((packId, rpk, parts));
            foreach (var part in parts) partIds[(part.Rpk, part.Entry.TypeId)] = part.Id;
        }

        Console.WriteLine($"Found {partIds.Count} parts in {packs.Count} packs");

        // Second pass: models and definitions
        var converted = 0;
        var withoutModel = 0;
        var failures = new List<string>();

        foreach (var (packId, rpk, parts) in packs)
        {
            if (filter != null && !packId.Equals(filter, StringComparison.OrdinalIgnoreCase)) continue;

            var packFolder = Path.Combine(output, packId.Replace('/', Path.DirectorySeparatorChar));
            if (!PrepareFolder(packFolder))
            {
                Console.WriteLine($"Skipped {packId}: {packFolder} exists and is not a converted pack");
                continue;
            }

            var pack = new PartPack { Id = packId, Source = rpk.RelativePath };
            var models = new Dictionary<string, string?>();
            var texturePrefix = packId.Replace('/', '_').ToLowerInvariant() + "__";

            foreach (var source in parts)
            {
                try
                {
                    var definition = Convert(game, scripts, source, partIds, packFolder, texturePrefix, models);
                    pack.Parts.Add(definition);

                    converted++;
                    if (definition.Model == null) withoutModel++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{source.Id}: {ex.Message}");
                }
            }

            File.WriteAllText(Path.Combine(packFolder, PartPack.FileName), JsonConvert.SerializeObject(pack, Formatting.Indented));
            Console.WriteLine($"  {packId,-45} {pack.Parts.Count,4} parts, {models.Values.Count(m => m != null),4} models");
        }

        // Constants of the shared script classes (fuel types, price factors...), for the game's part logic.
        // A filtered run has not seen them all.
        if (filter == null)
            File.WriteAllText(Path.Combine(output, ConstantsFile), JsonConvert.SerializeObject(scripts.Constants, Formatting.Indented));

        Console.WriteLine();
        Console.WriteLine($"Converted {converted} parts ({withoutModel} without a model) in {stopwatch.Elapsed.TotalSeconds:0.0} s");
        if (failures.Count > 0)
        {
            Console.WriteLine($"{failures.Count} failed:");
            foreach (var failure in failures) Console.WriteLine("  " + failure);
        }

        return 0;
    }

    private static List<SourcePart> CollectParts(SlrrGame game, SlrrRpk rpk, string packId)
    {
        var parts = new List<SourcePart>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in rpk.Entries.Values)
        {
            var configPath = entry.Lines.FirstOrDefault(l => l.Length > 2 && l[0] == "native" && l[1] == "part")?[2];
            if (configPath == null) continue;

            var configFile = Path.Combine(game.Root, configPath);
            if (!File.Exists(configFile)) continue;

            // Several entries may share one cfg; the type id keeps their names apart
            var name = Path.GetFileNameWithoutExtension(configPath);
            if (!usedNames.Add(name))
            {
                name = $"{name}_{entry.TypeId:X4}";
                usedNames.Add(name);
            }

            parts.Add(new SourcePart(rpk, entry, configFile, entry.FirstValue("script"), $"{packId}/{name}", name));
        }

        return parts;
    }

    private static PartDefinition Convert(SlrrGame game, SlrrScriptEvaluator scripts, SourcePart source,
        Dictionary<(SlrrRpk, int), string> partIds, string packFolder, string texturePrefix, Dictionary<string, string?> models)
    {
        var config = SlrrPartConfig.Load(source.ConfigFile);

        var scriptFile = source.ScriptPath == null ? null : Path.Combine(game.Root, source.ScriptPath);
        var script = scriptFile != null && File.Exists(scriptFile) ? scripts.Evaluate(scriptFile) : null;

        // Fields like crankshaft_slot_ID name the slot a kind of part goes on; 0 = the part has no such slot
        var properties = script?.Properties ?? new Dictionary<string, object>();
        var slotRoles = properties
            .Where(p => p.Key.EndsWith(SlotRoleSuffix, StringComparison.Ordinal) && p.Value is long)
            .ToDictionary(p => p.Key[..^SlotRoleSuffix.Length], p => (int)(long)p.Value);
        foreach (var role in slotRoles.Keys) properties.Remove(role + SlotRoleSuffix);

        return new PartDefinition
        {
            Id = source.Id,
            Name = source.Name,
            DisplayName = script?.DisplayName,
            BaseClass = script?.BaseClass,
            ClassChain = script?.ClassChain ?? new List<string>(),
            Properties = properties,
            Derived = script?.Derived ?? new Dictionary<string, object>(),
            SlotRoles = slotRoles.Where(r => r.Value > 0).ToDictionary(r => r.Key, r => r.Value),
            StockParts = (script?.StockParts ?? new List<SlrrStockPart>()).Select(StockReference).ToList(),
            RequiredSlots = (script?.RequiredSlots ?? new List<SlrrSlotRule>())
                .Select(r => new PartSlotRule { Slot = r.Slot, Message = r.Message }).ToList(),
            Categories = game.Categories(source.Rpk, source.Entry),
            Model = ConvertModel(game, source, config, packFolder, texturePrefix, models),
            Mass = config.Mass,
            Config = config.Other,
            SourceTypeId = source.Entry.TypeId,
            SourceScript = source.ScriptPath,
            Slots = config.Slots.Select(slot => new PartSlot
            {
                Id = slot.Id,
                Name = slot.Name,
                Position = new[] { slot.Position.X, slot.Position.Y, slot.Position.Z },
                Rotation = new[] { slot.YawPitchRoll.X, slot.YawPitchRoll.Y, slot.YawPitchRoll.Z },
                DamageMode = slot.DamageMode,
                AttachesTo = slot.AttachesTo.Select(r => Reference(r.PartId, r.SlotId)).ToList(),
                CompatibleWith = slot.CompatibleWith.Select(r => Reference(r.PartId, r.SlotId)).ToList()
            }).ToList()
        };

        // Scripts name the rpk outright, so their ids need no external table
        PartStockReference StockReference(SlrrStockPart stock)
        {
            var rpk = game.GetRpk(stock.Rpk);
            return new PartStockReference
            {
                Part = rpk != null && partIds.TryGetValue((rpk, stock.TypeId), out var id) ? id : null,
                Name = stock.Name,
                Conditional = stock.Conditional,
                Source = $"{stock.Rpk}#0x{stock.TypeId:X4}"
            };
        }

        PartSlotReference Reference(int partId, int slotId)
        {
            var (targetRpk, target) = game.Resolve(source.Rpk, partId);
            return new PartSlotReference
            {
                Part = targetRpk != null && target != null && partIds.TryGetValue((targetRpk, target.TypeId), out var id) ? id : null,
                Slot = slotId,
                Source = SlrrGame.Describe(source.Rpk, partId)
            };
        }
    }

    /// <summary>
    /// Writes the part's model and returns its file name: the most detailed LOD that resolves to a mesh,
    /// plus every render outside the LOD set. Renders that are not meshes (exhaust smoke, lights) drop out.
    /// Parts that draw the same meshes with the same textures share one model file.
    /// </summary>
    private static string? ConvertModel(SlrrGame game, SourcePart source, SlrrPartConfig config, string packFolder,
        string texturePrefix, Dictionary<string, string?> models)
    {
        var resolved = new List<(SlrrRender Render, string MeshFile, List<string?> TextureFiles)>();
        foreach (var render in config.Renders)
        {
            var (renderRpk, entry) = game.Resolve(source.Rpk, render.Id);
            if (renderRpk == null || entry == null) continue;

            var meshFile = game.SourceFile(renderRpk, entry.Ids("mesh").FirstOrDefault(-1));
            if (meshFile == null) continue;

            resolved.Add((render, meshFile, entry.Ids("texture").Select(id => game.SourceFile(renderRpk, id)).ToList()));
        }

        var bestLod = resolved.Max(r => r.Render.Lod);
        var selected = resolved.Where(r => r.Render.Lod == null || r.Render.Lod == bestLod).ToList();
        if (selected.Count == 0) return null;

        var key = string.Join('|', selected.Select(r =>
            $"{r.MeshFile}:{string.Join(',', r.TextureFiles)}:{r.Render.Position}:{r.Render.YawPitchRoll}")).ToLowerInvariant();
        if (models.TryGetValue(key, out var existing)) return existing;

        var pieces = selected.Select(r => new SlrrKn5Builder.Piece(SlrrMesh.Load(r.MeshFile), r.TextureFiles, r.Render.Matrix));
        var kn5 = SlrrKn5Builder.Build(source.Name, pieces, texturePrefix);
        if (kn5.RootNode.Children.Count == 0) return models[key] = null;

        var model = source.Name + ".kn5";
        kn5.Save(Path.Combine(packFolder, model));
        return models[key] = model;
    }

    /// <summary>Empties a previously converted pack folder; refuses to touch anything else</summary>
    private static bool PrepareFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            var isPack = File.Exists(Path.Combine(folder, PartPack.FileName));
            var isEmpty = !Directory.EnumerateFileSystemEntries(folder).Any();
            if (!isPack && !isEmpty) return false;

            foreach (var file in Directory.EnumerateFiles(folder, "*.kn5")) File.Delete(file);
        }

        Directory.CreateDirectory(folder);
        return true;
    }
}
