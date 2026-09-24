using System.Text;
using Street_Rod_AC.Services.Configuration;
using Street_Rod_AC.Services.Configuration.Models;

namespace StreetRodAC.Tests;

/// <summary>AcConfigBackup and IniModificationService on a fake Documents\Assetto Corsa\cfg in a temp folder</summary>
public sealed class AcConfigBackupTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly TempDir _temp = new();
    private readonly string _cfg;
    private readonly string _restore;

    public AcConfigBackupTests()
    {
        _cfg = _temp.Combine("cfg");
        _restore = _temp.Combine("AcRestore");
        Directory.CreateDirectory(_cfg);
    }

    public void Dispose() => _temp.Dispose();

    private AcConfigBackup Backup() => new(_cfg, _restore);

    private string CfgFile(string name) => Path.Combine(_cfg, name);

    private string KeepFolder => Path.Combine(_restore, AcConfigBackup.FolderName);

    [Fact]
    public void Keep_write_restore_gives_back_the_original_bytes()
    {
        var original = (byte[])[.. Bom, .. Encoding.Latin1.GetBytes("[RACE]\r\nMODEL=é\r\n; comment\n")];
        var race = _temp.Bytes(Path.Combine("cfg", "race.ini"), original);
        var backup = Backup();

        backup.Keep(race);
        File.WriteAllText(race, "ours");
        Assert.True(backup.HasPending);

        Assert.Equal(1, backup.RestoreAll());
        Assert.Equal(original, File.ReadAllBytes(race));
        Assert.False(backup.HasPending);
        Assert.False(Directory.Exists(KeepFolder));
    }

    [Fact]
    public void A_file_that_did_not_exist_is_deleted_again()
    {
        var race = CfgFile("race.ini");
        var backup = Backup();

        backup.Keep(race);
        File.WriteAllText(race, "ours");

        Assert.Equal(1, backup.RestoreAll());
        Assert.False(File.Exists(race));
    }

    [Fact]
    public void Keeping_twice_keeps_the_users_file_not_ours()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        var backup = Backup();

        backup.Keep(race);
        File.WriteAllText(race, "ours, first launch");
        backup.Keep(race);
        File.WriteAllText(race, "ours, second launch");

        backup.RestoreAll();
        Assert.Equal("user's own", File.ReadAllText(race));
    }

    [Fact]
    public void RestoreAll_is_idempotent()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        var backup = Backup();
        backup.Keep(race);
        File.WriteAllText(race, "ours");

        Assert.Equal(1, backup.RestoreAll());
        Assert.Equal(0, backup.RestoreAll());
        Assert.Equal(0, Backup().RestoreAll());
        Assert.Equal("user's own", File.ReadAllText(race));
    }

    [Fact]
    public void A_read_only_file_goes_back()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        var backup = Backup();
        backup.Keep(race);
        File.WriteAllText(race, "ours");
        File.SetAttributes(race, FileAttributes.ReadOnly);

        Assert.Equal(1, backup.RestoreAll());
        Assert.Equal("user's own", File.ReadAllText(race));
    }

    [Fact]
    public void A_file_outside_cfg_is_never_kept()
    {
        var outside = _temp.File("elsewhere.ini", "x");
        Assert.Throws<InvalidOperationException>(() => Backup().Keep(outside));
        Assert.Throws<InvalidOperationException>(() => Backup().Keep(Path.Combine(_cfg, "..", "elsewhere.ini")));
        Assert.False(Directory.Exists(KeepFolder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"Files\":[{\"Path\":")]
    [InlineData("[]")]
    public void An_unreadable_manifest_restores_nothing_deletes_nothing_and_is_set_aside(string manifest)
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        var backup = Backup();
        backup.Keep(race);
        File.WriteAllText(race, "ours");
        var kept = Directory.GetFiles(KeepFolder).Single(f => !f.EndsWith("ini-manifest.json"));
        var keptName = Path.GetFileName(kept);
        File.WriteAllText(Path.Combine(KeepFolder, "ini-manifest.json"), manifest);

        Assert.Equal(0, Backup().RestoreAll());

        Assert.Equal("ours", File.ReadAllText(race));
        Assert.False(Directory.Exists(KeepFolder));
        var aside = Directory.GetDirectories(_restore, AcConfigBackup.FolderName + ".unreadable-*").Single();
        Assert.Equal("user's own", File.ReadAllText(Path.Combine(aside, keptName)));
    }

    [Fact]
    public void A_manifest_whose_file_list_is_null_is_set_aside_like_any_unreadable_one()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        Backup().Keep(race);
        File.WriteAllText(Path.Combine(KeepFolder, "ini-manifest.json"), "{\"Files\":null}");

        var restored = Backup().RestoreAll();

        Assert.Equal(0, restored);
        Assert.Single(Directory.GetDirectories(_restore, AcConfigBackup.FolderName + ".unreadable-*"));
    }

    [Fact]
    public void A_manifest_entry_pointing_outside_cfg_is_not_written()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's own");
        var outside = _temp.File("victim.txt", "victim");
        var backup = Backup();
        backup.Keep(race);
        var manifestPath = Path.Combine(KeepFolder, "ini-manifest.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace(
            Newtonsoft.Json.JsonConvert.ToString(Path.GetFullPath(race)).Trim('"'),
            Newtonsoft.Json.JsonConvert.ToString(outside).Trim('"')));

        Assert.Equal(0, Backup().RestoreAll());
        Assert.Equal("victim", File.ReadAllText(outside));
        Assert.True(Backup().HasPending);
    }

    [Fact]
    public void One_file_that_cannot_go_back_does_not_stop_the_others()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "race own");
        var showroom = _temp.File(Path.Combine("cfg", "showroom_start.ini"), "showroom own");
        var backup = Backup();
        backup.Keep(race);
        backup.Keep(showroom);
        File.WriteAllText(race, "ours");
        File.WriteAllText(showroom, "ours");

        int restored;
        using (new FileStream(race, FileMode.Open, FileAccess.Read, FileShare.None))
            restored = backup.RestoreAll();

        Assert.Equal(1, restored);
        Assert.Equal("showroom own", File.ReadAllText(showroom));
        Assert.True(backup.HasPending);

        Assert.Equal(1, backup.RestoreAll());
        Assert.Equal("race own", File.ReadAllText(race));
        Assert.False(backup.HasPending);
    }

    [Fact]
    public void Two_files_of_one_name_in_different_cfg_folders_keep_their_own_originals_after_a_partial_restore()
    {
        var top = _temp.File(Path.Combine("cfg", "race.ini"), "top own");
        var sub = _temp.File(Path.Combine("cfg", "sub", "race.ini"), "sub own");
        var backup = Backup();
        backup.Keep(top);   // kept as 0_race.ini
        backup.Keep(sub);   // kept as 1_race.ini
        File.WriteAllText(top, "ours");
        File.WriteAllText(sub, "ours");

        // Only the top one goes back; the sub one stays in the manifest as 1_race.ini
        using (new FileStream(sub, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(1, backup.RestoreAll());

        // The next launch keeps the top one again: one entry in the manifest, so it is named 1_race.ini too
        File.WriteAllText(top, "top own, second time");
        backup.Keep(top);
        File.WriteAllText(top, "ours again");

        backup.RestoreAll();
        Assert.Equal("top own, second time", File.ReadAllText(top));
        Assert.Equal("sub own", File.ReadAllText(sub));
    }

    // ---- IniModificationService, which writes through the backup ----

    private IniModificationService Service() => new(_cfg, _restore);

    [Fact]
    public void A_showroom_edit_changes_only_its_lines_and_keeps_bom_comments_and_ansi_bytes()
    {
        var text = "; showroom settings\r\n[SHOWROOM]\r\nCAR=old_car\r\nSKIN=old_skin\r\nSELECTED_SKIN=old_skin\r\nTRACK=showroom\r\n\r\n[OTHER]\r\nNAME=José ; driver\r\n";
        var original = (byte[])[.. Bom, .. Encoding.Latin1.GetBytes(text)];
        var file = _temp.Bytes(Path.Combine("cfg", "showroom_start.ini"), original);

        Assert.True(Service().ApplyIntent(new ShowroomIntent("new_car", "new_skin", "studio")));

        var written = File.ReadAllBytes(file);
        Assert.Equal(Bom, written[..3]);
        var after = Encoding.Latin1.GetString(written, 3, written.Length - 3);
        Assert.Equal(text.Replace("old_car", "new_car").Replace("old_skin", "new_skin").Replace("TRACK=showroom", "TRACK=studio"), after);

        Assert.Equal(1, Service().RestoreAll());
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Fact]
    public void Applying_the_same_showroom_values_again_writes_nothing()
    {
        var file = _temp.File(Path.Combine("cfg", "showroom_start.ini"), "[SHOWROOM]\r\nCAR=a\r\nSKIN=b\r\nSELECTED_SKIN=b\r\nTRACK=showroom\r\n");
        var service = Service();

        Assert.True(service.ApplyIntent(new ShowroomIntent("a", "b")));

        Assert.False(service.HasPendingRestore);
        Assert.Equal("[SHOWROOM]\r\nCAR=a\r\nSKIN=b\r\nSELECTED_SKIN=b\r\nTRACK=showroom\r\n", File.ReadAllText(file));
    }

    [Fact]
    public void Applying_the_same_values_to_a_line_with_a_comment_writes_nothing()
    {
        var content = "[SHOWROOM]\r\nCAR=a ; the car\r\nSKIN=b\r\nSELECTED_SKIN=b\r\nTRACK=showroom\r\n";
        var file = _temp.File(Path.Combine("cfg", "showroom_start.ini"), content);
        var service = Service();

        Assert.True(service.ApplyIntent(new ShowroomIntent("a", "b")));

        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void A_drag_race_ini_carries_the_context_and_a_driver_name_cannot_open_a_section()
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's race.ini");
        var context = Guid.NewGuid();
        var intent = new DragRaceIntent
        {
            PlayerCarId = "car_a", PlayerSkin = "s", PlayerName = "Bob\r\n[REMOTE]\r\nACTIVE=1",
            OpponentCarId = "car_b", OpponentSkin = "s", OpponentName = "Al;ice", ContextId = context
        };

        Assert.True(Service().ApplyIntent(intent));

        var lines = File.ReadAllLines(race);
        Assert.Single(lines, l => l == "[REMOTE]");            // the one AppendTailSections writes
        Assert.DoesNotContain("ACTIVE=1", lines);
        Assert.Contains("DRIVER_NAME=Bob  (REMOTE)  ACTIVE-1", lines);
        Assert.Contains("DRIVER_NAME=Al,ice", lines);
        Assert.Contains("[STREET_ROD]", lines);
        Assert.Contains($"CONTEXT_ID={context:D}", lines);

        // race.ini, and the assists file the race's damage went into (there was none: it goes again)
        Assert.Equal(2, Service().RestoreAll());
        Assert.Equal("user's race.ini", File.ReadAllText(race));
        Assert.False(File.Exists(CfgFile("assists.ini")));
    }

    [Fact]
    public void A_race_runs_in_the_race_mode_with_damage_on_and_the_players_assists_come_back()
    {
        _temp.File(Path.Combine("cfg", "race.ini"), "user's race.ini");
        const string assists = "[ASSISTS]\r\nABS=2\r\nVISUALDAMAGE=100\r\nDAMAGE=0\r\nFUEL_RATE=0\r\nTYRE_WEAR=0\r\n";
        var assistsFile = _temp.File(Path.Combine("cfg", "assists.ini"), assists);

        Assert.True(Service().ApplyIntent(new DragRaceIntent { PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O" }));

        var race = File.ReadAllLines(CfgFile("race.ini"));
        Assert.Contains($"__CM_CUSTOM_MODE={IniModificationService.RaceModeId}", race);
        // A drag race is a one-lap race the mode controls: not AC's drag session, no AC jump-start penalty
        Assert.Contains("TYPE=3", race);
        Assert.DoesNotContain("TYPE=7", race);
        Assert.Contains("LAPS=1", race);
        Assert.Contains("JUMP_START_PENALTY=0", race);
        Assert.Contains("RACE_TYPE=DRAG", race);
        Assert.DoesNotContain(race, l => l.StartsWith("MODE=") || l.StartsWith("__CM_NEW_MODE_USED"));

        var raced = File.ReadAllLines(assistsFile);
        Assert.Contains("DAMAGE=100", raced);
        Assert.Contains("TYRE_WEAR=1", raced);
        Assert.Contains("ABS=2", raced);             // the player's own assists stay
        Assert.Contains("VISUALDAMAGE=100", raced);
        Assert.Contains("FUEL_RATE=0", raced);

        Assert.Equal(2, Service().RestoreAll());
        Assert.Equal(assists, File.ReadAllText(assistsFile));
    }

    [Theory]
    [InlineData("car=x")]
    [InlineData("car\r\n[X]")]
    [InlineData("car;x")]
    [InlineData("[car]")]
    public void A_car_id_that_would_change_the_ini_fails_the_launch_and_leaves_the_file(string carId)
    {
        var race = _temp.File(Path.Combine("cfg", "race.ini"), "user's race.ini");
        var intent = new DragRaceIntent { PlayerCarId = carId, OpponentCarId = "b", PlayerName = "P", OpponentName = "O" };

        Assert.False(Service().ApplyIntent(intent));
        Assert.Equal("user's race.ini", File.ReadAllText(race));
        Assert.False(Service().HasPendingRestore);
    }
}
