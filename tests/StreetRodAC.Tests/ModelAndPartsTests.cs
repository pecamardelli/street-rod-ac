using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Services.Opponents;

namespace StreetRodAC.Tests;

public class RacerCollectionTests
{
    [Fact]
    public void Two_racers_of_one_name_both_stay_under_different_names()
    {
        var racers = new RacerCollection();
        var first = new Opponent("Ace", 30, Gender.Male, 95, 50);
        var second = new Opponent("Ace", 40, Gender.Female, 92, 20) { Status = RacerStatus.Inactive };
        var third = new Opponent("Ace", 50, Gender.Male, 91, 10);

        racers.AddRacer(first);
        racers.AddRacer(second);
        racers.AddRacer(third);

        Assert.Equal("Ace", first.Name);
        Assert.Equal("Ace (2)", second.Name);
        Assert.Equal("Ace (3)", third.Name);
        Assert.Equal(3, racers.TotalCount);
        Assert.Same(second, racers.Find("Ace (2)"));
    }

    [Fact]
    public void Adding_the_same_racer_again_keeps_its_name()
    {
        var racers = new RacerCollection();
        var ace = new Opponent("Ace", 30, Gender.Male, 95, 50);
        racers.AddRacer(ace);
        racers.AddRacer(ace);
        Assert.Equal("Ace", ace.Name);
        Assert.Equal(1, racers.TotalCount);
    }
}

public class OpponentAIAdapterTests
{
    [Theory]
    [InlineData(50, -5, 90, 0)]
    [InlineData(95, 50, 95, 50)]
    [InlineData(150, 150, 100, 100)]
    [InlineData(int.MinValue, int.MaxValue, 90, 100)]
    public void Skill_and_aggression_reach_race_ini_in_range(int skill, int aggression, int level, int aiAggression)
    {
        var opponent = new Opponent("X", 30, Gender.Male, 95, 50) { Skill = skill, Aggression = aggression };
        var ai = OpponentAIAdapter.ToAssettoCorsaAI(opponent);
        Assert.Equal(level, ai.AILevel);
        Assert.Equal(aiAggression, ai.AIAggression);
        Assert.Equal(skill is >= 90 and <= 100 && aggression is >= 0 and <= 100, OpponentAIAdapter.ValidateOpponent(opponent));
    }
}

public sealed class SlotShiftsTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_truncated_file_is_a_problem_and_is_never_written_over()
    {
        var path = _temp.File(SlotShifts.FileName, "{\"a/b\": {\"1\": [0.1, 0.2");
        var before = File.ReadAllBytes(path);

        var shifts = SlotShifts.Load(_temp.Path);

        Assert.NotNull(shifts.Problem);
        Assert.True(shifts.IsEmpty);
        Assert.False(shifts.Add("a/b", 1, [0.1f, 0, 0]));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Entries_that_are_not_three_finite_numbers_are_skipped()
    {
        _temp.File(SlotShifts.FileName, "{\"a/b\":{\"1\":null,\"2\":[1,2],\"3\":[1,2,3],\"4\":[1,\"NaN\",3]},\"c/d\":null}");

        var shifts = SlotShifts.Load(_temp.Path);

        Assert.Null(shifts.Problem);
        var only = Assert.Single(shifts.All);
        Assert.Equal(("a/b", 3), (only.PartId, only.SlotId));
        Assert.Equal(new float[] { 1, 2, 3 }, only.Offset);
    }

    [Fact]
    public void An_added_shift_is_written_and_reads_back_and_a_zero_one_removes_the_file()
    {
        var shifts = SlotShifts.Load(_temp.Path);
        Assert.True(shifts.Add("a/b", 7, [0.5f, 0, -0.25f]));

        var again = SlotShifts.Load(_temp.Path);
        Assert.Equal(new[] { 0.5f, 0, -0.25f }, again.Of("a/b", 7));

        Assert.True(again.Add("a/b", 7, [-0.5f, 0, 0.25f]));
        Assert.False(File.Exists(Path.Combine(_temp.Path, SlotShifts.FileName)));
    }
}
