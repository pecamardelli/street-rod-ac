using Street_Rod_AC.Helpers;
using Street_Rod_AC.Services.Configuration;

namespace StreetRodAC.Tests;

/// <summary>
/// What never blocks a race: an unreadable INI manifest whose keep folder cannot be moved aside (a kept file held open
/// by a scanner), and SafeFile's clean-up of temp files a write cut short left
/// </summary>
public sealed class AcConfigBackupSetAsideTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _cfg;
    private readonly string _restore;

    public AcConfigBackupSetAsideTests()
    {
        _cfg = _temp.Combine("cfg");
        _restore = _temp.Combine("AcRestore");
        Directory.CreateDirectory(_cfg);
    }

    public void Dispose() => _temp.Dispose();

    private string KeepFolder => Path.Combine(_restore, AcConfigBackup.FolderName);

    [Fact]
    public void An_unreadable_manifest_whose_folder_cannot_move_is_copied_aside_and_the_race_goes_on()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        new AcConfigBackup(_cfg, _restore).Keep(race);
        File.WriteAllText(race, "ours");
        var kept = Directory.GetFiles(KeepFolder).Single(f => !f.EndsWith("ini-manifest.json"));
        File.WriteAllText(Path.Combine(KeepFolder, "ini-manifest.json"), "null");

        string aside;
        var assists = _temp.File(Path.Combine("cfg", "assists.ini"), "assists");
        var backup = new AcConfigBackup(_cfg, _restore);
        // Held open without delete sharing, as a scanner would: the folder cannot be renamed
        using (new FileStream(kept, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // The next race keeps its file all the same
            backup.Keep(assists);

            aside = Directory.GetDirectories(_restore, AcConfigBackup.FolderName + ".unreadable-*").Single();
        }

        Assert.Equal("user's own", File.ReadAllText(Path.Combine(aside, Path.GetFileName(kept))));
        File.WriteAllText(assists, "ours");
        Assert.Equal(1, backup.RestoreAll());
        Assert.Equal("assists", File.ReadAllText(assists));
        Assert.Equal("ours", File.ReadAllText(race));
    }

    [Fact]
    public void SweepTemps_removes_only_old_files_named_as_SafeFile_names_them()
    {
        var folder = _temp.Combine("sweep");
        var old = _temp.File(Path.Combine("sweep", $".race.ini.{Guid.NewGuid():N}.tmp"), "cut short");
        var fresh = _temp.File(Path.Combine("sweep", $".race.ini.{Guid.NewGuid():N}.tmp"), "being written");
        var notOurs = _temp.File(Path.Combine("sweep", ".race.ini.not-a-guid.tmp"), "x");
        var plain = _temp.File(Path.Combine("sweep", "cache.tmp"), "x");
        var nested = _temp.File(Path.Combine("sweep", "car", $".manifest.json.{Guid.NewGuid():N}.tmp"), "x");
        foreach (var file in new[] { old, notOurs, plain, nested })
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(1, SafeFile.SweepTemps(folder));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(notOurs));
        Assert.True(File.Exists(plain));
        Assert.True(File.Exists(nested));

        Assert.Equal(1, SafeFile.SweepTemps(folder, recursive: true));
        Assert.False(File.Exists(nested));
        Assert.Equal(0, SafeFile.SweepTemps(_temp.Combine("missing")));
    }

    [Fact]
    public async Task A_write_waits_out_a_reader_that_holds_the_file_for_a_moment()
    {
        var path = _temp.File("race.ini", "old");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = Task.Run(async () =>
        {
            await Task.Delay(60, TestContext.Current.CancellationToken);
            reader.Dispose();
        }, TestContext.Current.CancellationToken);

        SafeFile.WriteAllText(path, "new");
        await release;

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_temp.Path, "*.tmp"));
    }
}
