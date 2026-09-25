using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Services.Catalog;

namespace StreetRodAC.Tests;

public class CarProfileServiceTests
{
    private readonly MemoryCatalog _catalog = new();
    private readonly MemoryProfiles _profiles = new();
    private readonly CarProfileService _service;

    public CarProfileServiceTests()
    {
        TestLogging.SilenceLogging();
        _service = new CarProfileService(_catalog, _profiles);
    }

    private static CarDefinition Car(string? bhp, string? weight, string brand = "Plymouth", int? year = null,
        ContentSource source = ContentSource.Unknown, string id = "car") => new()
    {
        Id = id,
        Name = "Test",
        Brand = brand,
        Year = year,
        Source = source,
        ContentHash = "hash",
        Specs = bhp == null && weight == null ? null : new CarSpecsData { Bhp = bhp, Weight = weight }
    };

    [Theory]
    [InlineData("70bhp", "1000kg", 7000)] // an economy car: 70 hp per tonne
    [InlineData("100bhp", "1400kg", 7200)]
    [InlineData("150bhp", "1000kg", 18900)] // a muscle car
    [InlineData("350bhp", "1000kg", 56700)] // a supercar
    [InlineData("60bhp", "1200kg", 5000)] // below the floor
    public void Power_to_weight_in_hp_per_tonne_prices_the_car(string bhp, string weight, int expected)
    {
        Assert.Equal(expected, _service.CalculateBasePrice(Car(bhp, weight)));
    }

    [Fact]
    public void Brand_year_and_source_scale_the_price()
    {
        // 187.5 hp/t, a 1960s classic (1.2) from a mod (0.9)
        Assert.Equal(27200m, _service.CalculateBasePrice(Car("300bhp", "1600kg", "Chevrolet", 1969, ContentSource.Mod)));

        // 357 hp/t, a Ferrari (2.5) of the 2000s from Kunos (1.2)
        Assert.Equal(174700m, _service.CalculateBasePrice(Car("500bhp", "1400kg", "Ferrari", 2005, ContentSource.Kunos)));
    }

    [Fact]
    public void Quicker_is_dearer()
    {
        var prices = new[] { 80, 120, 160, 200, 300, 400 }.Select(hp => _service.CalculateBasePrice(Car($"{hp}bhp", "1000kg"))).ToList();
        Assert.Equal(prices.OrderBy(p => p), prices);
        Assert.Equal(prices.Count, prices.Distinct().Count());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("300bhp", null)]
    [InlineData("300bhp", "90kg")] // not believable
    public void A_car_without_specs_is_priced_at_the_default(string? bhp, string? weight)
    {
        Assert.Equal(15000m, _service.CalculateBasePrice(Car(bhp, weight)));
    }

    [Fact]
    public void Precedence_tiers_by_hp_per_tonne()
    {
        Assert.Equal(0.5f, _service.CalculatePrecedence(Car("150bhp", "1000kg")), 3); // neither
        Assert.Equal(0.7f, _service.CalculatePrecedence(Car("60bhp", "1200kg")), 3); // economy
        Assert.Equal(0.3f, _service.CalculatePrecedence(Car("500bhp", "1000kg")), 3); // exotic
        Assert.Equal(0.5f, _service.CalculatePrecedence(Car(null, null)), 3); // no specs: no tier
    }

    [Fact]
    public async Task Generated_profiles_of_an_older_reading_are_priced_again_and_hand_edited_ones_are_left()
    {
        _catalog.UpsertCar(Car("150bhp", "1000kg", id: "generated"));
        _catalog.UpsertCar(Car("150bhp", "1000kg", id: "manual"));
        _profiles.UpsertProfile(new CarProfile { CarDefinitionId = "generated", BasePrice = 5000m, Source = ProfileDataSource.Generated, DefinitionHash = "hash|specs2" });
        _profiles.UpsertProfile(new CarProfile { CarDefinitionId = "manual", BasePrice = 1234m, Source = ProfileDataSource.Manual, DefinitionHash = "hash|specs2" });

        await _service.EnsureProfilesExistAsync();

        Assert.Equal(18900m, _profiles.Profiles["generated"].BasePrice);
        Assert.Equal(1234m, _profiles.Profiles["manual"].BasePrice);
        Assert.Equal("hash|specs2", _profiles.Profiles["manual"].DefinitionHash);
    }
}
