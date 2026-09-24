using System.Reflection;
using System.Runtime.CompilerServices;
using Street_Rod_AC.Logging;

namespace StreetRodAC.Tests;

/// <summary>
/// Runs before any test. The game's classes take their logger from <see cref="AppLoggerFactory"/>, which throws
/// until <c>Initialize()</c> has run, and <c>Initialize()</c> writes log files under %AppData%\StreetRodAC\Logs.
/// The tests must never touch the user's folders, so the factory is marked initialized with Serilog's default
/// (silent) logger instead. See the report: this is the "needs a seam" for AppLoggerFactory.
/// </summary>
internal static class TestLogging
{
    [ModuleInitializer]
    internal static void SilenceLogging()
    {
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().CreateLogger();
        var initialized = typeof(AppLoggerFactory).GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Static)
                          ?? throw new InvalidOperationException("AppLoggerFactory._isInitialized is gone: the test logging hook needs updating");
        initialized.SetValue(null, true);
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
