using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Xunit.Abstractions;

namespace StreetRodAC.Tests;

/// <summary>The rivals' tuning on the real parts and the real dyno</summary>
public class EngineTunerTests(ITestOutputHelper output)
{
    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));
    private static readonly Lazy<EngineBuildIndex> Builds = new(() => EngineBuildIndex.Create(Catalog.Value));

    private static RatedBuild Build(string idEnd) => Builds.Value.Runnable.Single(b => b.Build.Id.EndsWith(idEnd, StringComparison.Ordinal));

    [Theory]
    [InlineData("chrysler-v8-pack/chrysler-la-340-275-hp")]
    [InlineData("script-ford-l6/ford-221-132-hp")]
    public void Money_buys_power_that_the_dyno_confirms(string buildId)
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build(buildId), 0.8, new Random(1))!;
        var before = stock.Report.Dyno!.MaxPowerHp;

        var started = DateTime.Now;
        var result = EngineTuner.TuneUp(catalog, Builds.Value, PartTrees.Clone(stock.Root, keepIds: true), 1_000_000m, 1.0, new Random(7));
        output.WriteLine($"{buildId}: {(DateTime.Now - started).TotalMilliseconds:0} ms");

        Assert.NotNull(result);
        foreach (var upgrade in result!.Upgrades)
            output.WriteLine($"  {upgrade.Group}: {upgrade.Change}, {upgrade.PowerBefore:0} -> {upgrade.PowerAfter:0} hp, ${upgrade.Cost} (trade-in ${upgrade.TradeIn})");

        Assert.True(result.Report.Runs);
        Assert.True(result.Report.Dyno!.MaxPowerHp >= before * (1 + EngineTuner.MinGain));
        Assert.All(result.Upgrades, u => Assert.True(u.PowerAfter >= u.PowerBefore * (1 + EngineTuner.MinGain)));
        Assert.Equal(result.Upgrades.Count, result.Upgrades.Select(u => u.Group).Distinct().Count());
    }

    [Fact]
    public void No_money_no_upgrade()
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build("chrysler-v8-pack/chrysler-la-340-275-hp"), 0.8, new Random(1))!;

        Assert.Null(EngineTuner.TuneUp(catalog, Builds.Value, stock.Root, 0m, 1.0, new Random(7)));
    }

    [Fact]
    public void The_upgrades_stay_within_the_budget()
    {
        var catalog = Catalog.Value;
        var stock = EngineFactory.CreateStock(catalog, Build("chrysler-v8-pack/chrysler-la-340-275-hp"), 0.8, new Random(1))!;

        foreach (var budget in new[] { 50m, 200m, 800m })
        {
            var result = EngineTuner.TuneUp(catalog, Builds.Value, PartTrees.Clone(stock.Root, keepIds: true), budget, 1.0, new Random(3));
            output.WriteLine($"${budget}: {(result == null ? "nothing" : string.Join(", ", result.Upgrades.Select(u => $"{u.Change} ${u.Cost}")))}");
            if (result != null) Assert.True(result.Cost <= budget);
        }
    }
}
