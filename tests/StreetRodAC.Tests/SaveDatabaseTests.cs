using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Services.Race;
using Street_Rod_AC.Services.Storage;

namespace StreetRodAC.Tests;

/// <summary>GameStateRepository and the race session repository on a SaveDatabase in a temp folder</summary>
public sealed class SaveDatabaseTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly SaveDatabase _database;
    private readonly GameStateRepository _repository;

    public SaveDatabaseTests()
    {
        _database = new SaveDatabase(_temp.Combine("Saves"));
        _repository = new GameStateRepository(null, _database);
    }

    public void Dispose()
    {
        _repository.Dispose();
        _temp.Dispose();
    }

    private string Saves => _database.SavesDirectory;

    [Theory]
    [InlineData("Bob", true)]
    [InlineData("Bob Smith", true)]
    [InlineData("catalog", false)]
    [InlineData("CATALOG", false)]
    [InlineData("Bob-log", false)]
    [InlineData("Bob-TMP", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("..\\evil", false)]
    [InlineData("a/b", false)]
    [InlineData("C:\\x", false)]
    public void Save_names(string? name, bool valid) => Assert.Equal(valid, SaveDatabase.IsValidSaveName(name));

    [Fact]
    public void ListSaves_shows_only_real_saves()
    {
        _repository.CreateNew("Alice", "Alice");
        _repository.CreateNew("Bob", "Bob");
        _repository.Save(_repository.Load("Bob")!, "Bob"); // makes Bob.db.backup1
        foreach (var junk in new[] { "catalog.db", "Alice-log.db", "Bob-tmp.db", "Catalog.db" })
            System.IO.File.WriteAllText(Path.Combine(Saves, junk), "x");

        var saves = _repository.ListSaves();

        Assert.Equal(["Alice", "Bob"], saves.OrderBy(s => s));
        Assert.False(_repository.Exists("catalog"));
        Assert.False(_repository.Exists("Alice-log"));
        Assert.True(_repository.Exists("Alice"));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("catalog")]
    [InlineData("x-log")]
    [InlineData("")]
    public void An_invalid_save_name_is_refused_before_anything_is_written(string name)
    {
        var state = GameState.CreateNew("P");
        Assert.ThrowsAny<ArgumentException>(() => _repository.CreateNew(name, "P"));
        Assert.ThrowsAny<Exception>(() => _repository.Save(state, name));
        Assert.ThrowsAny<ArgumentException>(() => _repository.Load(name));
        Assert.ThrowsAny<ArgumentException>(() => _repository.Delete(name));
        Assert.False(System.IO.File.Exists(Path.Combine(_temp.Path, "evil.db")));
        Assert.Empty(Directory.GetFiles(Saves));
    }

    [Fact]
    public void Save_and_Load_round_trip_and_Load_does_not_stamp_the_date()
    {
        var created = _repository.CreateNew("Carol", "Carol");
        created.Player.Money = 1234m;
        created.PendingRace = new RaceContext { OpponentName = "Rival", CashWager = 50m, EventInstanceId = Guid.NewGuid() };
        _repository.Save(created, "Carol");
        var saved = created.LastPlayedDate;

        var loaded = _repository.Load("Carol")!;

        Assert.Equal("Carol", loaded.SaveName);
        Assert.Equal(1234m, loaded.Player.Money);
        Assert.Equal(created.PendingRace.ContextId, loaded.PendingRace!.ContextId);
        Assert.Equal(created.PendingRace.EventInstanceId, loaded.PendingRace.EventInstanceId);
        Assert.Equal(saved, loaded.LastPlayedDate, TimeSpan.FromMilliseconds(1));
        Assert.Null(_repository.Load("Nobody"));
    }

    [Fact]
    public void A_save_with_fields_the_game_no_longer_has_still_loads()
    {
        _repository.Save(_repository.CreateNew("Dora", "Dora"), "Dora");

        // UsedCars, UsedParts and NewspaperAds.Cars were in every save until they were taken out, never filled
        _database.Use("Dora", db =>
        {
            var games = db.GetCollection("gamestate");
            var doc = games.FindById(1);
            doc["UsedCars"] = new LiteDB.BsonArray();
            doc["UsedParts"] = new LiteDB.BsonArray();
            doc["NewspaperAds"].AsDocument["Cars"] = new LiteDB.BsonArray();
            return games.Update(doc);
        });

        var loaded = _repository.Load("Dora");

        Assert.NotNull(loaded);
        Assert.Equal("Dora", loaded!.Player.Name);
    }

    [Fact]
    public void Backups_rotate_while_the_save_stays_open()
    {
        var state = _repository.CreateNew("Dan", "Dan");
        for (var i = 0; i < 4; i++)
        {
            state.Player.Money = i;
            _repository.Save(state, "Dan");
        }

        // Once per session: the copy from before this session's first write, not pushed out by the writes after it
        Assert.True(System.IO.File.Exists(Path.Combine(Saves, "Dan.db.backup1")));
        Assert.False(System.IO.File.Exists(Path.Combine(Saves, "Dan.db.backup2")));
        using (var backup = new LiteDB.LiteDatabase(new LiteDB.ConnectionString { Filename = Path.Combine(Saves, "Dan.db.backup1"), ReadOnly = true }))
            Assert.Equal(Player.StartingMoney, backup.GetCollection("gamestate").FindById(1)["Player"]["Money"].AsDecimal);
        Assert.Equal(3m, _repository.Load("Dan")!.Player.Money);
    }

    [Fact]
    public void Delete_removes_the_save_its_side_files_and_backups()
    {
        _repository.CreateNew("Other", "Other");
        var state = _repository.CreateNew("Eve", "Eve");
        _repository.Save(state, "Eve");
        _repository.Save(state, "Eve");
        // Eve is the open save (LiteDB may hold its own Eve-log.db journal); a leftover rebuild file sits next to it
        System.IO.File.WriteAllText(Path.Combine(Saves, "Eve-tmp.db"), "x");
        Assert.NotEmpty(Directory.GetFiles(Saves, "Eve.db.backup*"));

        _repository.Delete("Eve");

        Assert.Empty(Directory.GetFiles(Saves, "Eve*"));
        Assert.True(_repository.Exists("Other"));
        Assert.Equal(["Other"], _repository.ListSaves());
    }

    [Fact]
    public async Task The_state_and_the_session_record_commit_together_or_not_at_all()
    {
        var sessions = new RaceSessionRepository(_database);
        var state = _repository.CreateNew("Fay", "Fay");

        state.Player.Money = 777m;
        var ex = Assert.Throws<InvalidOperationException>(() => _repository.Save(state, "Fay", db =>
        {
            sessions.Save(db, new ProcessedRaceSession { SessionId = "s1", RaceContextId = Guid.NewGuid() });
            throw new IOException("the record could not be written");
        }));
        Assert.IsType<IOException>(ex.InnerException);
        Assert.NotEqual(777m, _repository.Load("Fay")!.Player.Money);
        Assert.False(await sessions.IsProcessedAsync("Fay", "s1"));

        var context = Guid.NewGuid();
        _repository.Save(state, "Fay", db => sessions.Save(db, new ProcessedRaceSession { SessionId = "s2", RaceContextId = context }));
        Assert.Equal(777m, _repository.Load("Fay")!.Player.Money);
        Assert.True(await sessions.IsProcessedAsync("Fay", "s2"));
        Assert.True(await sessions.IsContextSettledAsync("Fay", context));
        Assert.False(await sessions.IsContextSettledAsync("Fay", Guid.NewGuid()));
    }

    [Fact]
    public void Switching_between_saves_keeps_each_ones_data()
    {
        var a = _repository.CreateNew("Gus", "Gus");
        var b = _repository.CreateNew("Hal", "Hal");
        a.Player.Money = 1m;
        b.Player.Money = 2m;
        _repository.Save(a, "Gus");
        _repository.Save(b, "Hal");

        Assert.Equal(1m, _repository.Load("Gus")!.Player.Money);
        Assert.Equal(2m, _repository.Load("Hal")!.Player.Money);
        Assert.Equal(1m, _repository.Load("Gus")!.Player.Money);
    }

    [Fact]
    public void After_dispose_the_database_refuses_work()
    {
        _repository.CreateNew("Ivy", "Ivy");
        _repository.Dispose();
        _repository.Dispose(); // twice is fine
        Assert.ThrowsAny<ObjectDisposedException>(() => _database.Use("Ivy", db => db.GetCollectionNames().ToList()));
    }
}
