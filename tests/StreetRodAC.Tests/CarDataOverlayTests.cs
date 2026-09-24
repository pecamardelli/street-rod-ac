using System.Text;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services;

namespace StreetRodAC.Tests;

/// <summary>
/// The overlay on a fake install in a temp folder: content\cars\&lt;car&gt; and an AcRestore folder of its own.
/// </summary>
public sealed class CarDataOverlayTests : IDisposable
{
    private const string Car = "sr_test_car";

    // Bytes no text encoding round-trips by accident: Latin-1 accents, a NUL, a lone CR
    private static readonly byte[] EngineBytes = [.. Encoding.Latin1.GetBytes("[HEADER]\r\nVERSION=1 ; é à\r\n"), 0x00, 0x0D, 0xFF];
    private static readonly byte[] CarIniBytes = Encoding.ASCII.GetBytes("[INFO]\nSCREEN_NAME=Test\n");
    private static readonly byte[] BankBytes = [1, 2, 3, 4, 5];
    private const string OwnGuids = "{11111111-1111-1111-1111-111111111111} bank:/sr_test_car\r\n";

    private readonly TempDir _temp = new();
    private readonly string _cars;
    private readonly string _keep;

    public CarDataOverlayTests()
    {
        _cars = _temp.Combine("ac", "content", "cars");
        _keep = _temp.Combine("AcRestore");
        Directory.CreateDirectory(_cars);
    }

    public void Dispose() => _temp.Dispose();

    private string CarFolder(string id = Car) => Path.Combine(_cars, id);

    private string MakeCar(string id = Car, bool withSound = false)
    {
        var folder = CarFolder(id);
        _temp.Bytes(Path.Combine(folder, "data", "engine.ini"), EngineBytes);
        _temp.Bytes(Path.Combine(folder, "data", "car.ini"), CarIniBytes);
        _temp.File(Path.Combine(folder, "ui", "ui_car.json"), "{\"name\":\"Test\"}");
        if (withSound)
        {
            _temp.Bytes(Path.Combine(folder, "sfx", id + ".bank"), BankBytes);
            _temp.File(Path.Combine(folder, "sfx", "GUIDs.txt"), OwnGuids);
        }

        return folder;
    }

    private CarSound DonorSound()
    {
        var bank = _temp.Bytes(Path.Combine("library", "donor", "sfx", "donor.bank"), [9, 9, 9, 9]);
        return new CarSound(bank, "{22222222-2222-2222-2222-222222222222} bank:/donor\r\n{33333333-3333-3333-3333-333333333333} event:/cars/donor/engine_ext\r\n", "donor");
    }

    private CarDataOverlay Overlay() => new(_cars, _keep);

    private static Dictionary<string, string> RaceFiles() => new()
    {
        ["engine.ini"] = "[HEADER]\r\nVERSION=2\r\n",
        ["drivetrain.ini"] = "[GEARS]\r\nCOUNT=4\r\n"
    };

    private static Dictionary<string, byte[]> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static void AssertSameTree(Dictionary<string, byte[]> expected, string folder)
    {
        var actual = Snapshot(folder);
        Assert.Equal(expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var (name, bytes) in expected) Assert.Equal(bytes, actual[name]);
    }

    [Fact]
    public void Apply_then_Restore_gives_back_the_car_byte_for_byte()
    {
        var folder = MakeCar(withSound: true);
        var before = Snapshot(folder);
        var overlay = Overlay();

        Assert.True(overlay.Apply(Car, RaceFiles(), DonorSound()));
        Assert.True(overlay.IsApplied(Car));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));
        Assert.True(File.Exists(Path.Combine(folder, "data", "drivetrain.ini")));
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(Path.Combine(folder, "sfx", Car + ".bank")));

        Assert.True(overlay.Restore(Car));

        AssertSameTree(before, folder);
        Assert.False(overlay.IsApplied(Car));
        Assert.False(Directory.Exists(Path.Combine(_keep, Car)));
        Assert.False(Directory.Exists(Path.Combine(folder, "sfx", CarDataOverlay.SfxKeepFolder)));
    }

    [Fact]
    public void Restore_without_a_manifest_is_nothing_to_do()
    {
        MakeCar();
        Assert.False(Overlay().Restore(Car));
    }

    [Fact]
    public void A_second_Apply_to_the_same_car_in_one_race_is_refused_and_the_first_stays()
    {
        var folder = MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceFiles()));
        Assert.False(overlay.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "other" }));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));

        overlay.Restore(Car);
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(folder, "data", "engine.ini")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("   ")]
    [InlineData("{\"CarId\":\"sr_test_car\",\"Files\":[{\"Name\":\"engine.ini\",\"Exi")]
    public void An_unreadable_manifest_throws_and_keeps_the_originals(string manifest)
    {
        var folder = MakeCar();
        Assert.True(Overlay().Apply(Car, RaceFiles()));
        var manifestPath = Path.Combine(_keep, Car, "manifest.json");
        File.WriteAllText(manifestPath, manifest);

        Assert.Throws<InvalidDataException>(() => Overlay().Restore(Car));

        // The kept original is still there, and the race data stays in place for a person to sort out
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(_keep, Car, "files", "engine.ini")));
        Assert.True(File.Exists(manifestPath));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));
    }

    [Fact]
    public void A_manifest_whose_file_list_is_null_throws_without_deleting()
    {
        MakeCar();
        Assert.True(Overlay().Apply(Car, RaceFiles()));
        File.WriteAllText(Path.Combine(_keep, Car, "manifest.json"), "{\"CarId\":\"sr_test_car\",\"Files\":null}");

        // Whatever it throws, the originals must stay
        Assert.ThrowsAny<Exception>(() => Overlay().Restore(Car));
        Assert.True(File.Exists(Path.Combine(_keep, Car, "files", "engine.ini")));
    }

    [Fact]
    public void A_manifest_whose_file_list_is_null_is_reported_as_unreadable()
    {
        MakeCar();
        Assert.True(Overlay().Apply(Car, RaceFiles()));
        File.WriteAllText(Path.Combine(_keep, Car, "manifest.json"), "{\"CarId\":\"sr_test_car\",\"Files\":null}");

        Assert.Throws<InvalidDataException>(() => Overlay().Restore(Car));
    }

    [Fact]
    public void A_read_only_data_file_is_changed_and_comes_back_read_only()
    {
        var folder = MakeCar();
        var engine = Path.Combine(folder, "data", "engine.ini");
        File.SetAttributes(engine, FileAttributes.ReadOnly);
        var overlay = Overlay();

        Assert.True(overlay.Apply(Car, RaceFiles()));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(engine));

        Assert.True(overlay.Restore(Car));
        Assert.Equal(EngineBytes, File.ReadAllBytes(engine));
        Assert.True(File.GetAttributes(engine).HasFlag(FileAttributes.ReadOnly));
        Assert.False(Directory.Exists(Path.Combine(_keep, Car)));
    }

    [Fact]
    public void A_kept_file_that_is_gone_does_not_stop_the_other_files_or_the_sound()
    {
        var folder = MakeCar(withSound: true);
        var overlay = Overlay();
        var files = new Dictionary<string, string> { ["engine.ini"] = "race engine", ["car.ini"] = "race car" };
        Assert.True(overlay.Apply(Car, files, DonorSound()));

        File.Delete(Path.Combine(_keep, Car, "files", "engine.ini"));

        Assert.True(overlay.Restore(Car));
        Assert.Equal(CarIniBytes, File.ReadAllBytes(Path.Combine(folder, "data", "car.ini")));
        Assert.Equal(BankBytes, File.ReadAllBytes(Path.Combine(folder, "sfx", Car + ".bank")));
        Assert.Equal(OwnGuids, File.ReadAllText(Path.Combine(folder, "sfx", "GUIDs.txt")));
        // Lost for good: the race version stays, and the car is no longer locked by a manifest
        Assert.Equal("race engine", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));
        Assert.False(overlay.IsApplied(Car));
    }

    [Fact]
    public void A_failing_step_keeps_the_manifest_for_it_and_the_others_go_back()
    {
        var folder = MakeCar();
        var overlay = Overlay();
        var files = new Dictionary<string, string> { ["engine.ini"] = "race engine", ["car.ini"] = "race car" };
        Assert.True(overlay.Apply(Car, files));

        // car.ini cannot be written while it is held open without sharing
        var carIni = Path.Combine(folder, "data", "car.ini");
        using (new FileStream(carIni, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<AggregateException>(() => overlay.Restore(Car));
        }

        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(folder, "data", "engine.ini")));
        Assert.True(overlay.IsApplied(Car));
        var manifest = File.ReadAllText(Path.Combine(_keep, Car, "manifest.json"));
        Assert.Contains("car.ini", manifest);
        Assert.DoesNotContain("engine.ini", manifest);

        // The next try finishes it
        Assert.True(Overlay().Restore(Car));
        Assert.Equal(CarIniBytes, File.ReadAllBytes(carIni));
        Assert.False(Directory.Exists(Path.Combine(_keep, Car)));
    }

    [Fact]
    public void A_file_the_car_did_not_have_is_deleted_again()
    {
        var folder = MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceFiles()));
        Assert.True(overlay.Restore(Car));
        Assert.False(File.Exists(Path.Combine(folder, "data", "drivetrain.ini")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..\\..")]
    [InlineData("C:\\x")]
    [InlineData("C:")]
    public void Ids_that_are_not_one_folder_name_are_refused_before_anything_is_deleted(string id)
    {
        MakeCar();
        var sentinelKeep = _temp.File(Path.Combine("AcRestore", "sentinel.txt"), "keep me");
        var sentinelCars = _temp.File(Path.Combine("ac", "content", "sentinel.txt"), "keep me");
        var overlay = Overlay();

        Assert.ThrowsAny<ArgumentException>(() => overlay.Apply(id, RaceFiles()));
        Assert.ThrowsAny<ArgumentException>(() => overlay.Restore(id));
        Assert.False(overlay.IsApplied(id));
        Assert.False(overlay.RemoveClone(id));

        Assert.True(File.Exists(sentinelKeep));
        Assert.True(File.Exists(sentinelCars));
        Assert.True(Directory.Exists(CarFolder()));
    }

    [Fact]
    public void A_data_file_name_that_is_a_path_is_refused()
    {
        MakeCar();
        var overlay = Overlay();
        Assert.ThrowsAny<Exception>(() => overlay.Apply(Car, new Dictionary<string, string> { ["..\\..\\evil.ini"] = "x" }));
        Assert.False(File.Exists(Path.Combine(_cars, "evil.ini")));
    }

    [Fact]
    public void RestoreIfLeftover_puts_back_what_an_earlier_run_left_and_not_this_runs()
    {
        if (AcProcesses.AnyRunning()) return; // the method rightly waits for AC; nothing to check on this machine now

        var folder = MakeCar();
        var earlier = Overlay();
        Assert.True(earlier.Apply(Car, RaceFiles()));

        // The same instance: changed for the race being prepared, left alone
        Assert.False(earlier.RestoreIfLeftover(Car));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));

        // A new start of the game: it is a leftover
        var next = Overlay();
        Assert.True(next.RestoreIfLeftover(Car));
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(folder, "data", "engine.ini")));
        Assert.False(next.RestoreIfLeftover(Car));
    }

    [Fact]
    public void RestoreAll_restores_every_car_and_skips_the_ini_backup_folder()
    {
        MakeCar("car_a");
        MakeCar("car_b");
        var overlay = Overlay();
        Assert.True(overlay.Apply("car_a", RaceFiles()));
        Assert.True(overlay.Apply("car_b", RaceFiles()));
        Directory.CreateDirectory(Path.Combine(_keep, "~cfg"));
        _temp.File(Path.Combine("AcRestore", "~cfg", "ini-manifest.json"), "{\"Files\":[]}");

        Assert.Equal(2, Overlay().RestoreAll());
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(CarFolder("car_a"), "data", "engine.ini")));
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(CarFolder("car_b"), "data", "engine.ini")));
        Assert.True(File.Exists(Path.Combine(_keep, "~cfg", "ini-manifest.json")));
    }

    [Fact]
    public void A_manifest_naming_another_folder_puts_back_the_cars_own_folder()
    {
        var folder = MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceFiles()));

        // Someone edited CarFolder to point outside any cars folder
        var manifestPath = Path.Combine(_keep, Car, "manifest.json");
        var elsewhere = _temp.Combine("elsewhere", Car);
        Directory.CreateDirectory(Path.Combine(elsewhere, "data"));
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace(
            Newtonsoft.Json.JsonConvert.ToString(Path.GetFullPath(folder)).Trim('"'),
            Newtonsoft.Json.JsonConvert.ToString(elsewhere).Trim('"')));

        Assert.True(Overlay().Restore(Car));
        Assert.Equal(EngineBytes, File.ReadAllBytes(Path.Combine(folder, "data", "engine.ini")));
        Assert.Empty(Directory.GetFiles(Path.Combine(elsewhere, "data")));
    }

    /// <summary>A packed car: data.acd holding engine.ini and car.ini, no data folder</summary>
    private string MakePackedCar(string id)
    {
        var folder = CarFolder(id);
        var key = Encoding.ASCII.GetBytes(AcdFile.KeyFor(id));
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        foreach (var (name, content) in new[] { ("engine.ini", EngineBytes), ("car.ini", CarIniBytes) })
        {
            w.Write(name.Length);
            w.Write(Encoding.ASCII.GetBytes(name));
            w.Write(content.Length);
            for (var i = 0; i < content.Length; i++) w.Write((int)(byte)(content[i] + key[i % key.Length]));
        }

        _temp.Bytes(Path.Combine(folder, AcdFile.FileName), data.ToArray());
        _temp.File(Path.Combine(folder, "ui", "ui_car.json"), "{}");
        return folder;
    }

    [Fact]
    public void A_packed_car_is_unpacked_for_the_race_and_the_folder_goes_after()
    {
        const string packed = "sr_packed_car";
        var folder = MakePackedCar(packed);
        var before = Snapshot(folder);
        var overlay = Overlay();

        Assert.True(overlay.Apply(packed, RaceFiles()));
        Assert.Equal(CarIniBytes, File.ReadAllBytes(Path.Combine(folder, "data", "car.ini")));
        Assert.Equal("[HEADER]\r\nVERSION=2\r\n", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));

        Assert.True(overlay.Restore(packed));
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        AssertSameTree(before, folder);
    }

    [Fact]
    public void A_clone_of_a_packed_car_gets_the_unpacked_data()
    {
        const string packed = "sr_packed_car";
        MakePackedCar(packed);
        var clone = AcCarFolder.CloneIdFor(packed);

        Overlay().CreateClone(packed, clone, new Dictionary<string, string> { ["engine.ini"] = "clone" }, null, MasterGuids());

        Assert.Equal(CarIniBytes, File.ReadAllBytes(Path.Combine(CarFolder(clone), "data", "car.ini")));
        Assert.False(File.Exists(Path.Combine(CarFolder(clone), AcdFile.FileName)));
        Assert.Equal(1, Overlay().RemoveClones());
    }

    [Fact]
    public void A_sound_on_a_car_without_an_sfx_folder_leaves_no_folder_behind()
    {
        var folder = MakeCar();
        var before = Snapshot(folder);
        var overlay = Overlay();

        Assert.True(overlay.Apply(Car, new Dictionary<string, string>(), DonorSound()));
        Assert.True(File.Exists(Path.Combine(folder, "sfx", Car + ".bank")));
        Assert.Contains("event:/cars/" + Car + "/engine_ext", File.ReadAllText(Path.Combine(folder, "sfx", "GUIDs.txt")));

        Assert.True(overlay.Restore(Car));
        Assert.False(Directory.Exists(Path.Combine(folder, "sfx")));
        AssertSameTree(before, folder);
        // The donor's bank was linked, not moved: it is still there
        Assert.Equal(new byte[] { 9, 9, 9, 9 }, File.ReadAllBytes(DonorSound().BankPath));
    }

    [Fact]
    public void A_sound_whose_bank_is_gone_leaves_the_car_on_its_own()
    {
        var folder = MakeCar(withSound: true);
        var before = Snapshot(folder);
        var gone = new CarSound(_temp.Combine("nowhere", "gone.bank"), "", "gone");

        Assert.True(Overlay().Apply(Car, new Dictionary<string, string>(), gone));
        Assert.False(Overlay().IsApplied(Car));
        AssertSameTree(before, folder);
    }

    [Fact]
    public void The_next_race_after_a_crash_restores_the_leftover_first_and_its_own_restore_gives_the_original()
    {
        var folder = MakeCar(withSound: true);
        var before = Snapshot(folder);

        // A race whose app died: the manifest and the race data are left behind
        Assert.True(Overlay().Apply(Car, RaceFiles(), DonorSound()));

        // The next start races the same car on other data
        var next = Overlay();
        Assert.True(next.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "second race" }, DonorSound()));
        Assert.Equal("second race", File.ReadAllText(Path.Combine(folder, "data", "engine.ini")));
        Assert.False(File.Exists(Path.Combine(folder, "data", "drivetrain.ini")));

        Assert.True(next.Restore(Car));
        AssertSameTree(before, folder);
    }

    // ---- Clones ----

    private string MasterGuids() => _temp.Combine("no-master-guids.txt");

    [Fact]
    public void A_clone_is_made_and_removed_and_the_car_is_untouched()
    {
        var folder = MakeCar(withSound: true);
        var before = Snapshot(folder);
        var overlay = Overlay();
        var clone = AcCarFolder.CloneIdFor(Car);

        overlay.CreateClone(Car, clone, new Dictionary<string, string> { ["engine.ini"] = "clone engine" }, null, MasterGuids());

        var cloneFolder = CarFolder(clone);
        Assert.True(File.Exists(Path.Combine(cloneFolder, AcCarFolder.CloneMarker)));
        Assert.Equal("clone engine", File.ReadAllText(Path.Combine(cloneFolder, "data", "engine.ini")));
        Assert.Equal(CarIniBytes, File.ReadAllBytes(Path.Combine(cloneFolder, "data", "car.ini")));
        Assert.True(File.Exists(Path.Combine(cloneFolder, "sfx", clone + ".bank")));
        AssertSameTree(before, folder);

        Assert.Equal(1, overlay.RemoveClones());
        Assert.False(Directory.Exists(cloneFolder));
        AssertSameTree(before, folder);
    }

    [Fact]
    public void RemoveClones_deletes_only_folders_with_marker_suffix_and_the_cars_parent()
    {
        MakeCar();
        // Marker, no suffix: a user's car
        var noSuffix = MakeCar("users_copy");
        File.WriteAllText(Path.Combine(noSuffix, AcCarFolder.CloneMarker), "{}");
        // Suffix, no marker: not ours either
        var noMarker = MakeCar("other" + AcCarFolder.CloneSuffix);
        // Only the suffix as a name
        var bare = CarFolder(AcCarFolder.CloneSuffix);
        Directory.CreateDirectory(bare);
        File.WriteAllText(Path.Combine(bare, AcCarFolder.CloneMarker), "{}");
        // Ours
        var ours = CarFolder("x" + AcCarFolder.CloneSuffix);
        Directory.CreateDirectory(Path.Combine(ours, "data"));
        File.WriteAllText(Path.Combine(ours, AcCarFolder.CloneMarker), "{}");
        File.WriteAllText(Path.Combine(ours, "data", "engine.ini"), "x");

        Assert.Equal(1, Overlay().RemoveClones());

        Assert.False(Directory.Exists(ours));
        Assert.True(Directory.Exists(noSuffix));
        Assert.True(Directory.Exists(noMarker));
        Assert.True(Directory.Exists(bare));
        Assert.False(Overlay().RemoveClone("users_copy"));
    }

    [Fact]
    public void A_clone_id_without_the_suffix_or_over_a_real_car_is_refused()
    {
        MakeCar();
        var other = MakeCar("real_car" + AcCarFolder.CloneSuffix);
        var overlay = Overlay();

        Assert.Throws<ArgumentException>(() => overlay.CreateClone(Car, "plain_name", new Dictionary<string, string>(), null, MasterGuids()));
        Assert.Throws<IOException>(() => overlay.CreateClone(Car, "real_car" + AcCarFolder.CloneSuffix, new Dictionary<string, string>(), null, MasterGuids()));
        Assert.True(File.Exists(Path.Combine(other, "data", "engine.ini")));
    }

    [Fact]
    public void A_clone_of_a_car_changed_for_this_race_is_refused()
    {
        MakeCar();
        var overlay = Overlay();
        Assert.True(overlay.Apply(Car, RaceFiles()));
        Assert.Throws<InvalidOperationException>(() =>
            overlay.CreateClone(Car, AcCarFolder.CloneIdFor(Car), new Dictionary<string, string>(), null, MasterGuids()));
    }
}
