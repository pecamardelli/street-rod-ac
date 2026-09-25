using System.Text;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services;

namespace StreetRodAC.Tests;

/// <summary>
/// The overlay against what it cannot trust or what a crash leaves: a mod's data.acd naming paths, data file names
/// that are paths, a race copy cut short before its marker, copies left in an install the settings no longer name
/// </summary>
public sealed class CarDataOverlayHardeningTests : IDisposable
{
    private const string Car = "sr_test_car";

    private readonly TempDir _temp = new();
    private readonly string _cars;
    private readonly string _keep;

    public CarDataOverlayHardeningTests()
    {
        _cars = _temp.Combine("ac", "content", "cars");
        _keep = _temp.Combine("AcRestore");
        Directory.CreateDirectory(_cars);
    }

    public void Dispose() => _temp.Dispose();

    private CarDataOverlay Overlay(string? cars = null) => new(cars ?? _cars, _keep);

    private string CarFolder(string id, string? cars = null) => Path.Combine(cars ?? _cars, id);

    private string MasterGuids() => _temp.File(Path.Combine("ac", "content", "sfx", "GUIDs.txt"), "");

    private string MakeCar(string id = Car, string? cars = null)
    {
        var folder = CarFolder(id, cars);
        _temp.File(Path.Combine(folder, "data", "engine.ini"), "[HEADER]\r\nVERSION=1\r\n");
        _temp.File(Path.Combine(folder, "ui", "ui_car.json"), "{}");
        return folder;
    }

    /// <summary>A packed car whose data.acd holds the given entries, in AC's format</summary>
    private string MakePackedCar(string id, params (string Name, string Content)[] entries)
    {
        var folder = CarFolder(id);
        var key = Encoding.ASCII.GetBytes(AcdFile.KeyFor(id));
        using var data = new MemoryStream();
        var w = new BinaryWriter(data);
        foreach (var (name, text) in entries)
        {
            var content = Encoding.ASCII.GetBytes(text);
            w.Write(name.Length);
            w.Write(Encoding.ASCII.GetBytes(name));
            w.Write(content.Length);
            for (var i = 0; i < content.Length; i++) w.Write((int)(byte)(content[i] + key[i % key.Length]));
        }

        _temp.Bytes(Path.Combine(folder, AcdFile.FileName), data.ToArray());
        _temp.File(Path.Combine(folder, "ui", "ui_car.json"), "{}");
        return folder;
    }

    /// <summary>Every file under the temp folder, so a write anywhere outside the car shows</summary>
    private HashSet<string> AllFiles() =>
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("..\\evil.ini")]
    [InlineData("..\\..\\evil.ini")]
    [InlineData("sub\\evil.ini")]
    public void A_data_acd_entry_that_is_a_path_refuses_the_archive_and_writes_nothing(string badName)
    {
        const string packed = "sr_packed_car";
        var folder = MakePackedCar(packed, ("engine.ini", "[HEADER]"), (badName, "pwned"));
        var overlay = Overlay();

        Assert.Throws<InvalidDataException>(() => overlay.Apply(packed, new Dictionary<string, string> { ["engine.ini"] = "race" }));

        Assert.False(File.Exists(Path.Combine(folder, "evil.ini")));
        Assert.False(File.Exists(Path.Combine(_cars, "evil.ini")));
        Assert.DoesNotContain(AllFiles(), f => Path.GetFileName(f).Equals("evil.ini", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(folder, "data", "engine.ini")));

        // The restore takes back what the refused race had started
        overlay.RestoreAll();
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
    }

    [Fact]
    public void A_clone_of_a_car_whose_data_acd_names_a_path_is_refused()
    {
        const string packed = "sr_packed_car";
        MakePackedCar(packed, ("..\\evil.ini", "pwned"));
        var clone = AcCarFolder.CloneIdFor(packed);

        Assert.Throws<InvalidDataException>(() => Overlay().CreateClone(packed, clone, new Dictionary<string, string>(), null, MasterGuids()));

        Assert.DoesNotContain(AllFiles(), f => Path.GetFileName(f).Equals("evil.ini", StringComparison.OrdinalIgnoreCase));
        Overlay().RemoveClones();
        Assert.False(Directory.Exists(CarFolder(clone)));
    }

    [Theory]
    [InlineData("..\\x.lut")]
    [InlineData("..\\..\\x.lut")]
    public void A_clone_data_file_name_that_is_a_path_is_refused_before_the_copy_is_made(string badName)
    {
        MakeCar();
        var clone = AcCarFolder.CloneIdFor(Car);
        var guids = MasterGuids();
        var before = AllFiles();

        Assert.Throws<InvalidDataException>(() =>
            Overlay().CreateClone(Car, clone, new Dictionary<string, string> { [badName] = "x" }, null, guids));

        Assert.DoesNotContain(AllFiles(), f => Path.GetFileName(f).Equals("x.lut", StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(CarFolder(clone)));
        Assert.Subset(before, AllFiles()); // nothing new anywhere
    }

    [Fact]
    public void A_copy_cut_short_before_its_marker_is_taken_for_ours()
    {
        MakeCar();
        var clone = AcCarFolder.CloneIdFor(Car);

        // A kill between the folder and the marker: an empty folder named as a copy
        Directory.CreateDirectory(CarFolder(clone));

        // The next race's copy goes over it instead of failing as "a car of the install"
        Overlay().CreateClone(Car, clone, new Dictionary<string, string> { ["engine.ini"] = "clone" }, null, MasterGuids());
        Assert.True(AcCarFolder.IsClone(CarFolder(clone)));

        // And the start-up clean-up takes an empty one away
        Overlay().RemoveClones();
        Directory.CreateDirectory(CarFolder(clone));
        Assert.Equal(1, Overlay().RemoveClones());
        Assert.False(Directory.Exists(CarFolder(clone)));
    }

    [Fact]
    public void A_copy_named_as_one_with_content_and_no_marker_is_still_left_alone()
    {
        MakeCar();
        var other = MakeCar(AcCarFolder.CloneIdFor(Car));

        Assert.Throws<IOException>(() => Overlay().CreateClone(Car, AcCarFolder.CloneIdFor(Car), new Dictionary<string, string>(), null, MasterGuids()));
        Assert.Equal(0, Overlay().RemoveClones());
        Assert.True(File.Exists(Path.Combine(other, "data", "engine.ini")));
    }

    [Fact]
    public void Copies_left_in_an_install_the_settings_no_longer_name_are_removed()
    {
        var oldCars = _temp.Combine("old_ac", "content", "cars");
        MakeCar(Car, oldCars);
        var clone = AcCarFolder.CloneIdFor(Car);

        // Made on the old install; the app died before its restore
        Overlay(oldCars).CreateClone(Car, clone, new Dictionary<string, string> { ["engine.ini"] = "clone" }, null, MasterGuids());
        Assert.True(Directory.Exists(CarFolder(clone, oldCars)));

        // The AC folder in the settings changed: the new install's overlay still finds it
        Directory.CreateDirectory(_cars);
        Assert.Equal(1, Overlay().RemoveClones());

        Assert.False(Directory.Exists(CarFolder(clone, oldCars)));
        Assert.True(File.Exists(Path.Combine(CarFolder(Car, oldCars), "data", "engine.ini")));
        Assert.False(File.Exists(Path.Combine(_keep, "clones.json")));
    }

    [Fact]
    public void A_recorded_folder_that_is_no_longer_a_copy_is_left_alone()
    {
        var oldCars = _temp.Combine("old_ac", "content", "cars");
        MakeCar(Car, oldCars);
        var clone = AcCarFolder.CloneIdFor(Car);
        Overlay(oldCars).CreateClone(Car, clone, new Dictionary<string, string>(), null, MasterGuids());

        // Somebody kept the copy as a car of their own: the marker is gone, the files stay
        File.Delete(Path.Combine(CarFolder(clone, oldCars), AcCarFolder.CloneMarker));

        Assert.Equal(0, Overlay().RemoveClones());
        Assert.True(File.Exists(Path.Combine(CarFolder(clone, oldCars), "data", "engine.ini")));
    }

    [Fact]
    public void A_restored_read_only_file_leaves_no_temp_file_and_is_read_only_again()
    {
        var folder = MakeCar();
        var engine = Path.Combine(folder, "data", "engine.ini");
        File.SetAttributes(engine, FileAttributes.ReadOnly);
        var overlay = Overlay();

        Assert.True(overlay.Apply(Car, new Dictionary<string, string> { ["engine.ini"] = "race" }));
        Assert.True(overlay.Restore(Car));

        Assert.Equal("[HEADER]\r\nVERSION=1\r\n", File.ReadAllText(engine));
        Assert.True(File.GetAttributes(engine).HasFlag(FileAttributes.ReadOnly));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "data"), "*.tmp"));
    }
}
