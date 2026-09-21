using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Scripting;
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
    private const string NotesOption = "--notes";
    private const string ReplaceOption = "--replace";
    private const string DropOption = "--drop";
    private const string RenameOption = "--rename";

    private sealed record SourcePart(SlrrRpk Rpk, SlrrRpkEntry Entry, string ConfigFile, string? ScriptPath, string Id, string Name);

    public static int Main(string[] args)
    {
        // --notes <folder>: text files with engine builds written down as stock_parts_list_E lines
        // --replace <old pack>=<new pack>: the old pack stays out, what names its parts gets their twins of the new one
        // --drop <part id pattern>: parts left out altogether (a pack's take on engines another pack does better)
        // --rename <rpk pack>=<pack id>: the pack goes by a name of our own (the mod's file name says nothing to a player)
        string? notes = null;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var drops = new List<Regex>();
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == NotesOption && i + 1 < args.Length) notes = args[++i];
            else if (args[i] == DropOption && i + 1 < args.Length)
            {
                drops.AddRange(args[++i].Split(',').Select(p =>
                    new Regex("^" + Regex.Escape(p.Trim()).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled)));
            }
            else if ((args[i] == ReplaceOption || args[i] == RenameOption) && i + 1 < args.Length)
            {
                var option = args[i];
                foreach (var pair in args[++i].Split(',').Select(p => p.Split('=')))
                {
                    if (pair.Length != 2)
                    {
                        Console.WriteLine(option == ReplaceOption
                            ? $"{ReplaceOption} takes <old pack>=<new pack>, e.g. engines/Mopar=engines/Chrysler_V8_pak"
                            : $"{RenameOption} takes <rpk pack>=<pack id>, e.g. engines/Chrysler_V8_pak=engines/chrysler");
                        return 1;
                    }

                    (option == ReplaceOption ? replacements : renames)[pair[0].Trim()] = pair[1].Trim();
                }
            }
            else positional.Add(args[i]);
        }

        if (positional.Count < 2)
        {
            Console.WriteLine("Usage: SlrrPartsConverter <SLRR folder> <output folder> [pack filter] [--notes <folder>] " +
                              "[--replace <old pack>=<new pack>] [--drop <part id pattern>,...] [--rename <rpk pack>=<pack id>,...]");
            Console.WriteLine(@"  e.g. SlrrPartsConverter ""D:\Games\SLRR"" ""C:\Games\AC\content\parts"" engines/Mopar");
            return 1;
        }

        // Packs are named after the rpk they come from unless renamed; every option names them by what they are called here
        foreach (var (old, replacement) in replacements.ToList()) replacements[old] = renames.GetValueOrDefault(replacement, replacement);

        var game = new SlrrGame(positional[0]);
        var scripts = new SlrrScriptEvaluator(game);
        var output = positional[1];
        var filter = positional.Count > 2 ? positional[2] : null;

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
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Ids parts went by before, for saves made then: a pack renamed since, or replaced by a later release
        var aliases = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The base game keeps its stock parts (running gear, accessories, neons) in an rpk next to the parts folder
        var baseRpk = Path.Combine(game.Root, BaseRpk);
        var files = Directory.EnumerateFiles(partsRoot, "*.rpk", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        if (File.Exists(baseRpk)) files.Insert(0, baseRpk);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(game.Root, file);
            var rpk = game.GetRpk(relativePath);
            if (rpk == null) continue;

            var rpkId = file == baseRpk
                ? BasePackId
                : Path.ChangeExtension(Path.GetRelativePath(partsRoot, file), null).Replace('\\', '/');
            var packId = renames.GetValueOrDefault(rpkId, rpkId);
            var parts = CollectParts(game, rpk, packId);
            foreach (var part in parts.Where(part => drops.Any(d => d.IsMatch(part.Id))).ToList())
            {
                parts.Remove(part);
                dropped.Add(SlrrGame.Describe(rpk, part.Entry.TypeId));
            }
            if (parts.Count == 0) continue;

            packs.Add((packId, rpk, parts));
            foreach (var part in parts)
            {
                partIds[(part.Rpk, part.Entry.TypeId)] = part.Id;
                if (packId != rpkId) aliases[$"{rpkId}/{part.Name}"] = part.Id;
            }
        }

        Console.WriteLine($"Found {partIds.Count} parts in {packs.Count} packs" + (dropped.Count > 0 ? $", {dropped.Count} dropped" : ""));

        foreach (var (oldId, newId) in replacements)
        {
            var oldPack = packs.FirstOrDefault(p => p.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase));
            var newPack = packs.FirstOrDefault(p => p.Id.Equals(newId, StringComparison.OrdinalIgnoreCase));
            if (oldPack.Parts == null || newPack.Parts == null)
            {
                Console.WriteLine($"Cannot replace {oldId} with {newId}: {(oldPack.Parts == null ? oldId : newId)} is not among the packs");
                return 1;
            }

            Console.WriteLine($"Replacing {oldPack.Id} with {newPack.Id}");
            foreach (var (from, to) in ReplacePack(game, oldPack.Parts, newPack.Parts, partIds)) aliases[from] = to;

            packs.Remove(oldPack);
        }

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

        // The game runs the part scripts itself. A filtered run adds the classes of its packs to what is there;
        // parts without their classes would be parts nobody finds on their slot.
        var copied = CopyScripts(game, scripts, Path.Combine(output, PartScripts.Folder), filter == null);
        Console.WriteLine($"  {copied} script classes");

        // Constants of the shared script classes (fuel types, price factors...), for the game's part logic.
        // A filtered run has not seen them all.
        if (filter == null)
        {
            // Packs converted before under a name no longer produced (renamed, replaced, dropped whole) would be
            // loaded next to the current ones
            var current = packs.Select(p => Path.GetFullPath(Path.Combine(output, p.Id.Replace('/', Path.DirectorySeparatorChar))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in Directory.EnumerateFiles(output, PartPack.FileName, SearchOption.AllDirectories)
                         .Select(f => Path.GetFullPath(Path.GetDirectoryName(f)!)).Where(f => !current.Contains(f)).ToList())
            {
                RemovePack(stale);
                Console.WriteLine($"  removed {Path.GetRelativePath(output, stale)}: no longer converted");
            }

            File.WriteAllText(Path.Combine(output, ConstantsFile), JsonConvert.SerializeObject(scripts.Constants, Formatting.Indented));
            File.WriteAllText(Path.Combine(output, PartPack.AliasesFileName), JsonConvert.SerializeObject(aliases, Formatting.Indented));

            var engineBuilds = new SlrrEngineBuilds(game, partIds);
            // Cars get an evaluator of their own: their classes are of no use to the game
            var builds = engineBuilds.FromCars(new SlrrScriptEvaluator(game));
            if (notes != null && Directory.Exists(notes)) builds.AddRange(engineBuilds.FromNotes(notes));

            // An engine around a part that was dropped on purpose is no engine the game should offer
            var left = builds.RemoveAll(b => b.Parts.Any(p => p.Part == null && dropped.Contains(p.Source)));
            if (left > 0) Console.WriteLine($"  {left} engine builds left out, they use dropped parts");

            File.WriteAllText(Path.Combine(output, EngineBuild.FileName), JsonConvert.SerializeObject(builds, Formatting.Indented));

            Console.WriteLine($"  {builds.Count} engine builds ({builds.Count(b => b.RatedPower != null)} with a rated power, " +
                              $"{builds.Count(b => b.Parts.All(p => p.Part != null))} fully resolved)");
        }

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
            RequiredSlots = (script?.RequiredSlots ?? new List<ScriptSlotRule>())
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
        var selected = SelectRenders(game, source, config);
        if (selected.Count == 0) return null;

        var key = string.Join('|', selected.Select(r =>
            $"{r.MeshFile}:{string.Join(',', r.TextureFiles)}:{r.Render.Position}:{r.Render.YawPitchRoll}")).ToLowerInvariant();
        if (models.TryGetValue(key, out var existing)) return existing;

        var pieces = selected.Select(r => new SlrrKn5Builder.Piece(SlrrMesh.Load(r.MeshFile), r.TextureFiles, r.Render.Matrix));
        var kn5 = SlrrKn5Builder.Build(source.Name, pieces, texturePrefix, simplified => Console.WriteLine($"    {simplified}"));
        if (kn5.RootNode.Children.Count == 0) return models[key] = null;

        var model = source.Name + ".kn5";
        kn5.Save(Path.Combine(packFolder, model));
        return models[key] = model;
    }

    /// <summary>What a part draws: the most detailed LOD that resolves to a mesh, plus every render outside the LOD set</summary>
    private static List<(SlrrRender Render, string MeshFile, List<string?> TextureFiles)> SelectRenders(SlrrGame game, SourcePart source, SlrrPartConfig config)
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
        return resolved.Where(r => r.Render.Lod == null || r.Render.Lod == bestLod).ToList();
    }

    /// <summary>
    /// Points every part of a replaced pack at the part that takes its place, so that whatever names the old
    /// pack (car scripts, build notes, other packs) ends up with a part of the new one. Old parts without a
    /// twin stay unresolved. Returns the pairs by part id, for the game to read saves made with the old pack.
    /// </summary>
    private static SortedDictionary<string, string> ReplacePack(SlrrGame game, List<SourcePart> oldParts, List<SourcePart> newParts,
        Dictionary<(SlrrRpk, int), string> partIds)
    {
        // An evaluator of its own: the classes of a pack that is left out are of no use to the game
        var scripts = new SlrrScriptEvaluator(game);
        var geometry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var twins = SlrrPackTwins.Match(oldParts.Select(Traits).ToList(), newParts.Select(Traits).ToList());

        var aliases = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, twin) in oldParts.Zip(twins))
        {
            if (twin.New == null) partIds.Remove((source.Rpk, source.Entry.TypeId));
            else partIds[(source.Rpk, source.Entry.TypeId)] = aliases[source.Id] = twin.New.Id;
        }

        Console.WriteLine($"  {twins.Count(t => t.New != null)} of {twins.Count} parts have a twin ({twins.Count(t => t.New != null && t.Doubt != null)} in doubt)");
        foreach (var twin in twins.Where(t => t.New == null || t.Doubt != null))
        {
            Console.WriteLine(twin.New == null
                ? $"    {twin.Old.Name,-40} no twin ({twin.Old.DisplayName})"
                : $"    {twin.Old.Name,-40} -> {twin.New.Name,-44} {twin.Doubt}");
        }

        return aliases;

        SlrrPartTraits Traits(SourcePart source)
        {
            var config = SlrrPartConfig.Load(source.ConfigFile);
            var scriptFile = source.ScriptPath == null ? null : Path.Combine(game.Root, source.ScriptPath);
            var script = scriptFile != null && File.Exists(scriptFile) ? scripts.Evaluate(scriptFile) : null;
            var renders = SelectRenders(game, source, config);

            var traits = new SlrrPartTraits
            {
                Id = source.Id,
                Name = source.Name,
                DisplayName = script?.DisplayName,
                BaseClass = script?.BaseClass,
                Slots = config.Slots.Select(s => s.Id).ToHashSet(),
                Geometry = renders.Select(r => GeometryOf(r.MeshFile)).ToHashSet(),
                Textures = renders.SelectMany(r => r.TextureFiles).Where(t => t != null)
                    .Select(t => Path.GetFileNameWithoutExtension(t!).ToLowerInvariant()).ToHashSet()
            };

            foreach (var (partId, _) in config.Slots.SelectMany(s => s.AttachesTo))
            {
                var (targetRpk, target) = game.Resolve(source.Rpk, partId);
                if (targetRpk != null && target != null && partIds.TryGetValue((targetRpk, target.TypeId), out var id)) traits.Neighbours.Add(id);
            }

            return traits;
        }

        string GeometryOf(string meshFile) =>
            geometry.TryGetValue(meshFile, out var hash) ? hash : geometry[meshFile] = SlrrPackTwins.GeometryHash(SlrrMesh.Load(meshFile));
    }

    /// <summary>
    /// The game runs the part scripts itself, so it gets the classes: everything the evaluation touched plus the
    /// shared part classes, in the folder layout class lookup depends on.
    /// </summary>
    /// <param name="replace">Whether the classes are all there are: what was in the folder before goes</param>
    private static int CopyScripts(SlrrGame game, SlrrScriptEvaluator scripts, string target, bool replace)
    {
        var files = new HashSet<string>(scripts.UsedClassFiles, StringComparer.OrdinalIgnoreCase);
        var shared = Path.Combine(game.Root, PartsFolder, "scripts");
        if (Directory.Exists(shared)) files.UnionWith(Directory.EnumerateFiles(shared, "*.class", SearchOption.AllDirectories));

        if (replace && Directory.Exists(target)) Directory.Delete(target, true);

        var root = Path.GetFullPath(game.Root);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(file));
            if (relative.StartsWith("..", StringComparison.Ordinal)) continue;

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }

        return files.Count;
    }

    /// <summary>Takes a converted pack out of the output; a folder that is anything else is left alone</summary>
    private static void RemovePack(string folder)
    {
        var packFile = Path.Combine(folder, PartPack.FileName);
        if (!File.Exists(packFile)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.kn5")) File.Delete(file);
        File.Delete(packFile);
        if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
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
