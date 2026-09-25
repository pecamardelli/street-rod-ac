using System.Runtime.CompilerServices;
using Street_Rod_AC.Logging;

namespace StreetRodAC.Tests;

/// <summary>
/// Runs before any test. The game's classes take their logger from <see cref="AppLoggerFactory"/>, which throws
/// until <c>Initialize()</c> has run, and <c>Initialize()</c> writes log files under %AppData%\StreetRodAC\Logs.
/// The tests must never touch the user's folders, so the factory is set up on Serilog's default (silent) logger
/// instead, through the seam made for it.
///
/// LiteDB's shared mapper builds a type's mapping the first time it meets it, and two tests meeting the same type
/// at once can see it half built. The types the saves are made of are mapped here, once, before any test runs.
/// </summary>
internal static class TestLogging
{
    [ModuleInitializer]
    internal static void SilenceLogging()
    {
        AppLoggerFactory.InitializeSilent();

        LiteDB.BsonMapper.Global.ToDocument(new Street_Rod_AC.Models.Race.ProcessedRaceSession());
        LiteDB.BsonMapper.Global.ToDocument(new Street_Rod_AC.Models.GameState.GameState());
    }
}

/// <summary>
/// A fresh folder under the system temp folder for one test, deleted with everything in it (read-only files
/// included) when the test ends. Every filesystem test works in one of these and nowhere else.
/// </summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreetRodAC.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes a file (making its folder), returns its full path</summary>
    public string File(string relative, string content) => Bytes(relative, System.Text.Encoding.UTF8.GetBytes(content));

    public string Bytes(string relative, byte[] content)
    {
        var full = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
            // A handle still open (LiteDB closing late): the temp folder is the OS's to clean
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Where the repository is, found from this source file (the test output lives outside the tree)</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string GameProject => Path.Combine(Root, "Street Rod AC");

    public static string PartsFolder => Path.Combine(GameProject, "Assets", "Parts");

    private static string FindRoot([CallerFilePath] string here = "")
    {
        for (var dir = Path.GetDirectoryName(here); dir != null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Street Rod AC.slnx"))) return dir;
        }

        throw new DirectoryNotFoundException($"No Street Rod AC.slnx above {here}");
    }
}
