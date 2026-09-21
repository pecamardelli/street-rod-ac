using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Street_Rod_AC.Slrr;

/// <summary>What tells a part apart from the others of its pack, whatever it is called or numbered</summary>
public sealed class SlrrPartTraits
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public string? BaseClass { get; init; }
    public required HashSet<int> Slots { get; init; }

    /// <summary>A hash per mesh the part draws: the shape itself, not the file it is kept in</summary>
    public required HashSet<string> Geometry { get; init; }

    /// <summary>Texture file names without extension (a later release may ship the same picture as dds)</summary>
    public required HashSet<string> Textures { get; init; }

    /// <summary>Ids of the parts of the same pack this one is joined to by an attach line, from either side</summary>
    public HashSet<string> Neighbours { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A part of a replaced pack with the part that takes its place; <see cref="Doubt"/> says why a pair is worth a look</summary>
public sealed record SlrrTwin(SlrrPartTraits Old, SlrrPartTraits? New, double Score, string? Doubt);

/// <summary>
/// Pairs the parts of a pack with those of the pack that replaces it. Later releases of a mod rename the files,
/// renumber the rpk and some slots and rebalance the scripts, so neither ids nor names nor values carry over.
/// The shape does: the same mesh is the same part, and the words of its name settle which one when a mesh is
/// shared (a 340 and a 383 crankshaft).
/// </summary>
public static class SlrrPackTwins
{
    private const double GeometryWeight = 10;
    private const double NameWeight = 6;
    private const double TextureWeight = 2;
    private const double ClassWeight = 2;
    private const double SlotsWeight = 3;
    private const double NumbersWeight = 4;
    // More than looks and textures together: a part that does not bolt on where the old one did is no twin of it
    private const double MatingWeight = 14;
    private const int MatingRounds = 6;
    private const double NumberClash = 8;

    // Below this nothing ties the two parts together but a few common words
    private const double MinScore = 6;
    private const double MinLead = 0.5;

    private const string PlainPartClass = "Part";

    private static readonly Regex Word = new("[a-z]+|[0-9]+", RegexOptions.Compiled);

    public static List<SlrrTwin> Match(IReadOnlyList<SlrrPartTraits> oldParts, IReadOnlyList<SlrrPartTraits> newParts)
    {
        // What a part looks like and is called comes first. Then the joints have their say, round after round:
        // the old pack tells a 440 clutch from a 340 one by name, the new one calls them "big" and "small block",
        // but the one that bolts to the twin of the 440 flywheel is the 440 clutch. Blocks and crankshafts carry
        // their displacement in both packs, and from them the right answer spreads outwards.
        LinkBothWays(oldParts);
        LinkBothWays(newParts);

        var newTokens = newParts.ToDictionary(p => p, Tokens);
        var own = oldParts.ToDictionary(old => old, old =>
        {
            var tokens = Tokens(old);
            return newParts.ToDictionary(candidate => candidate, candidate => Score(old, tokens, candidate, newTokens[candidate]));
        });

        var twins = Pick(oldParts, own, null);
        for (var round = 0; round < MatingRounds; round++)
        {
            var next = Pick(oldParts, own, twins);
            var settled = next.Zip(twins).All(pair => pair.First.New == pair.Second.New);
            twins = next;
            if (settled) break;
        }

        return twins;
    }

    /// <summary>An attach line is written on one side of a joint, either one</summary>
    private static void LinkBothWays(IReadOnlyList<SlrrPartTraits> parts)
    {
        var byId = parts.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            foreach (var neighbour in part.Neighbours.ToList())
            {
                if (byId.TryGetValue(neighbour, out var other)) other.Neighbours.Add(part.Id);
            }
        }
    }

    /// <param name="previous">Twins of the round before, in the order of <paramref name="oldParts"/>; null in the first round</param>
    private static List<SlrrTwin> Pick(IReadOnlyList<SlrrPartTraits> oldParts, Dictionary<SlrrPartTraits, Dictionary<SlrrPartTraits, double>> own,
        List<SlrrTwin>? previous)
    {
        var twinIds = previous?.Where(t => t.New != null).ToDictionary(t => t.Old.Id, t => t.New!.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<SlrrTwin>();

        foreach (var old in oldParts)
        {
            var expected = twinIds == null
                ? new HashSet<string>()
                : old.Neighbours.Select(n => twinIds.GetValueOrDefault(n)).Where(id => id != null).Select(id => id!).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var ranked = own[old]
                .Select(c => (Part: c.Key, Score: c.Value + (expected.Count == 0 ? 0 : MatingWeight * expected.Count(c.Key.Neighbours.Contains) / expected.Count)))
                .OrderByDescending(c => c.Score)
                .Take(2)
                .ToList();

            if (ranked.Count == 0 || ranked[0].Score < MinScore)
            {
                result.Add(new SlrrTwin(old, null, ranked.Count == 0 ? 0 : ranked[0].Score, null));
                continue;
            }

            var best = ranked[0];
            var doubt = ranked.Count > 1 && best.Score - ranked[1].Score < MinLead ? $"as good: {ranked[1].Part.Name}"
                : !old.Geometry.SetEquals(best.Part.Geometry) ? "other mesh"
                : null;
            result.Add(new SlrrTwin(old, best.Part, best.Score, doubt));
        }

        return result;
    }

    /// <summary>Hash of a mesh's vertex positions and triangles; materials and texture slots stay out of it</summary>
    public static string GeometryHash(SlrrMesh mesh)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var sub in mesh.SubMeshes)
        {
            foreach (var vertex in sub.Vertices)
            {
                hash.AppendData(BitConverter.GetBytes(vertex.Position.X));
                hash.AppendData(BitConverter.GetBytes(vertex.Position.Y));
                hash.AppendData(BitConverter.GetBytes(vertex.Position.Z));
            }

            hash.AppendData(BitConverter.GetBytes(sub.Indices.Length));
        }

        return System.Convert.ToHexString(hash.GetHashAndReset(), 0, 8);
    }

    private static double Score(SlrrPartTraits old, HashSet<string> oldTokens, SlrrPartTraits candidate, HashSet<string> candidateTokens)
    {
        var score = GeometryWeight * Overlap(old.Geometry, candidate.Geometry)
                    + TextureWeight * Overlap(old.Textures, candidate.Textures)
                    + NameWeight * Overlap(oldTokens, candidateTokens);

        // Some slots get renumbered between releases (the oil pan's went from 9 to 10), for every part of a kind
        // alike; where they were kept they tell a belt (14) from the pulley it used to come with (55)
        score += SlotsWeight * Overlap(old.Slots, candidate.Slots);

        // A pack may turn a plain part into one with a script of its own kind (pulleys); anything else is another part
        var oldClass = SimpleName(old.BaseClass);
        var newClass = SimpleName(candidate.BaseClass);
        if (oldClass == newClass) score += ClassWeight;
        else if (oldClass != PlainPartClass && newClass != PlainPartClass) score -= GeometryWeight;

        // Numbers say more than words: a "3-speed" is not the "4-speed" that shares all its other words, and
        // "340" against "383" is the same casting but not the same part
        var oldNumbers = oldTokens.Where(IsNumber).ToHashSet();
        var newNumbers = candidateTokens.Where(IsNumber).ToHashSet();
        score += NumbersWeight * Overlap(oldNumbers, newNumbers);
        if (oldNumbers.Any(IsSize) && newNumbers.Any(IsSize) && !oldNumbers.Where(IsSize).Any(newNumbers.Contains)) score -= NumberClash;

        return score;
    }

    private static double Overlap<T>(HashSet<T> a, HashSet<T> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;

        var common = a.Count(b.Contains);
        return (double)common / (a.Count + b.Count - common);
    }

    /// <summary>Words and numbers of the part's names: "4BRL Carburetors" and "4 brl carburetor" come out the same</summary>
    private static HashSet<string> Tokens(SlrrPartTraits part) =>
        Word.Matches($"{part.DisplayName} {part.Name}".ToLowerInvariant())
            .Select(m => m.Value.Length > 3 ? m.Value.TrimEnd('s') : m.Value)
            .ToHashSet();

    private static bool IsNumber(string token) => char.IsDigit(token[0]);

    /// <summary>Three digits and more: a displacement or a flow rate rather than a count</summary>
    private static bool IsSize(string token) => token.Length >= 3 && IsNumber(token);

    private static string SimpleName(string? className) =>
        className == null ? PlainPartClass : className[(className.LastIndexOf('.') + 1)..];
}
