using System.Globalization;
using Street_Rod_AC.Dialogs.Timeslip;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;
using Street_Rod_AC.Services.Configuration;
using Street_Rod_AC.Services.Configuration.Models;
using Street_Rod_AC.Services.Time;

namespace StreetRodAC.Tests;

/// <summary>
/// Damage as real as AC allows: what a race reports lands on the parts, goes back into the next race, keeps a
/// broken car home, and comes off in the repair bay.
/// </summary>
public sealed class CarConditionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    // ---- A car with a small engine and running gear, groups by part id ----

    private static readonly Dictionary<string, string> GroupsById = new()
    {
        ["block"] = "Engine blocks",
        ["crank"] = "Crankshafts",
        ["rod"] = "Connecting rods",
        ["piston"] = "Pistons",
        ["cam"] = "Camshafts",
        ["carb"] = "Carburettors and injection",
        ["gearbox"] = CarCondition.TransmissionGroup,
        ["rim"] = PartKinds.Rims,
        ["tyre"] = PartKinds.Tyres,
        ["brake"] = PartKinds.Brakes,
        ["spring"] = PartKinds.Springs,
        ["shock"] = PartKinds.Shocks
    };

    private static string? GroupOf(string id) => GroupsById.GetValueOrDefault(id);

    private static Car NewCar()
    {
        var block = new PartInstance("block") { ParentSlot = PartInstance.CarEngineSlot };
        foreach (var (id, slot) in new[] { ("crank", 10), ("rod", 11), ("piston", 12), ("cam", 13), ("carb", 14), ("gearbox", 15) })
            block.Children.Add(new PartInstance(id) { ParentSlot = slot });

        var car = new Car("car_a") { HasPartsAssigned = true, HasRunningGearAssigned = true };
        car.Parts.Add(block);
        for (var i = 0; i < RunningGear.Corners; i++)
        {
            var rim = new PartInstance("rim") { ParentSlot = RunningGear.WheelSlot(i) };
            rim.Children.Add(new PartInstance("tyre") { ParentSlot = RunningGear.TyreSlotOnRim });
            car.Parts.Add(rim);
            car.Parts.Add(new PartInstance("brake") { ParentSlot = RunningGear.BrakeSlot(i) });
            car.Parts.Add(new PartInstance("spring") { ParentSlot = RunningGear.SpringSlot(i) });
            car.Parts.Add(new PartInstance("shock") { ParentSlot = RunningGear.ShockSlot(i) });
        }

        return car;
    }

    private static PartInstance Part(Car car, string id) => car.Parts.SelectMany(p => p.SelfAndDescendants()).First(p => p.DefinitionId == id);

    private static RaceCarCondition Report(double[]? body = null, double? life = null, double? gearbox = null,
        double tyreWear = 0, double[]? bend = null, int blown = -1) => new()
    {
        BodyDamageKmh = (body ?? new double[4]).ToList(),
        EngineLife = life,
        GearboxDamage = gearbox,
        Wheels = Enumerable.Range(0, 4).Select(i => new WheelCondition
        {
            TyreWear = tyreWear,
            TyreBlown = i == blown,
            SuspensionDamage = bend?[i] ?? 0
        }).ToList()
    };

    // ---- After a race ----

    [Fact]
    public void A_new_car_goes_in_pristine()
    {
        var start = CarCondition.StartState(NewCar(), GroupOf);

        Assert.True(start.IsPristine);
        Assert.Equal(1000, start.EngineLife);
        Assert.Empty(CarCondition.WhyCannotRace(NewCar(), GroupOf));
    }

    [Fact]
    public void The_body_keeps_the_worst_of_what_it_had_and_what_AC_reports()
    {
        var car = NewCar();
        car.BodyDamageKmh = new double[] { 30, 0, 0, 0 };

        // AC started with the car's 30 at the front: it reports the whole of it, plus a new hit on the left.
        // A start that did not take reports less at the front than the car had; the car's own stands.
        var report = CarCondition.ApplyRace(car, Report(body: new double[] { 12, 0, 25.8, 0 }), 1, GroupOf);

        Assert.Equal(new double[] { 30, 0, 25.8, 0 }, car.BodyDamageKmh);
        Assert.Contains(report, l => l.Contains("left side") && l.Contains("26 km/h"));
        Assert.DoesNotContain(report, l => l.Contains("front"));
        Assert.Equal(1 - 55.8 / CarCondition.TotaledKmh, car.BodyCondition, 6);
    }

    [Fact]
    public void A_200_kmh_wreck_totals_the_car_and_keeps_it_home()
    {
        var car = NewCar();

        var report = CarCondition.ApplyRace(car, Report(body: new double[] { 0, 0, 205, 205 }, life: -100), 2.5, GroupOf);

        Assert.True(CarCondition.IsTotaled(car));
        Assert.Contains("The body is wrecked: the car is totaled.", report);
        Assert.Contains("Engine: a connecting rod let go. It needs a rebuild before it runs again.", report);
        var problems = CarCondition.WhyCannotRace(car, GroupOf);
        Assert.Contains(problems, p => p.Contains("totaled"));
        Assert.Contains(problems, p => p.Contains("engine is blown"));
    }

    [Fact]
    public void Engine_damage_lands_on_the_weakest_rotating_part_and_comes_back_as_the_next_start()
    {
        var car = NewCar();
        Part(car, "rod").Tear = 0.8;
        Part(car, "piston").Tear = 0.9;

        var report = CarCondition.ApplyRace(car, Report(life: 640), 0.4, GroupOf);

        // The race took the engine from 800 to 640: the worn rod gave, and took all of it
        Assert.Equal(0.64, Part(car, "rod").Tear, 6);
        // The rest of the rotating assembly took a share of the 0.16 with it
        var share = 0.16 * CarCondition.SharedEngineDamage;
        Assert.Equal(0.9 - share, Part(car, "piston").Tear, 6);
        foreach (var id in new[] { "crank", "cam" }) Assert.Equal(1 - share, Part(car, id).Tear, 6);
        // What does not spin is not hurt by an over-rev
        Assert.Equal(1.0, Part(car, "block").Tear);
        Assert.Equal(1.0, Part(car, "carb").Tear);
        Assert.Contains("Engine: the connecting rods took a beating (80% → 64%).", report);

        Assert.Equal(640, CarCondition.StartState(car, GroupOf).EngineLife, 6);
    }

    [Fact]
    public void When_the_rotating_parts_are_as_worn_the_rods_give_first_then_the_pistons()
    {
        var car = NewCar();
        Assert.Equal("rod", CarCondition.WeakestRotatingPart(car, GroupOf)!.DefinitionId);

        Part(car, "rod").Tear = 0.95;
        Part(car, "piston").Tear = 0.7;
        Part(car, "cam").Tear = 0.7;
        Assert.Equal("piston", CarCondition.WeakestRotatingPart(car, GroupOf)!.DefinitionId);

        var report = CarCondition.ApplyRace(car, Report(life: 0), 0.4, GroupOf);
        Assert.Equal(0, Part(car, "piston").Tear);
        Assert.True(Part(car, "cam").Tear > 0);
        Assert.Contains("Engine: a piston let go. It needs a rebuild before it runs again.", report);
        Assert.Equal(0, CarCondition.EngineLife(car, GroupOf));
    }

    [Fact]
    public void An_engine_AC_left_alone_is_not_healed_by_its_report()
    {
        var car = NewCar();
        Part(car, "piston").Tear = 0.5;

        // A start that did not take: AC reports a new engine's life
        CarCondition.ApplyRace(car, Report(life: 1000), 0.4, GroupOf);

        Assert.Equal(0.5, Part(car, "piston").Tear);
    }

    [Fact]
    public void A_light_over_rev_wears_the_engine_race_after_race()
    {
        var car = NewCar();

        // 0.3 of AC's 1000 a race: each goes into the next start, so they add up
        CarCondition.ApplyRace(car, Report(life: 999.7), 0.4, GroupOf);
        var start = CarCondition.StartState(car, GroupOf).EngineLife;
        Assert.Equal(999.7, start, 6);
        CarCondition.ApplyRace(car, Report(life: start - 0.3), 0.4, GroupOf);

        Assert.Equal(999.4, CarCondition.EngineLife(car, GroupOf), 6);
    }

    [Fact]
    public void An_engine_without_rotating_parts_keeps_its_life_on_the_block()
    {
        var car = NewCar();
        car.Engine!.Children.RemoveAll(p => p.DefinitionId is "crank" or "rod" or "piston" or "cam");

        var report = CarCondition.ApplyRace(car, Report(life: 0), 0.4, GroupOf);

        Assert.Equal(0, Part(car, "block").Tear);
        Assert.Equal(0, CarCondition.EngineLife(car, GroupOf));
        Assert.Contains("Engine: the bottom end let go. It needs a rebuild before it runs again.", report);
        Assert.Contains(CarCondition.WhyCannotRace(car, GroupOf), p => p.Contains("engine is blown"));
    }

    [Fact]
    public void The_gearbox_takes_this_races_damage_on_top_of_what_it_had()
    {
        var car = NewCar();
        Part(car, "gearbox").Tear = 0.5;

        var report = CarCondition.ApplyRace(car, Report(gearbox: 0.45), 0.4, GroupOf);

        Assert.Equal(0.05, Part(car, "gearbox").Tear, 6);
        Assert.Contains("Gearbox: it is wrecked.", report);
        Assert.Contains(CarCondition.WhyCannotRace(car, GroupOf), p => p.Contains("gearbox"));
        Assert.Equal(0.95, CarCondition.StartState(car, GroupOf).GearboxWear, 6);
    }

    [Fact]
    public void A_bent_steering_rod_bends_that_corners_spring_and_shock()
    {
        var car = NewCar();

        var report = CarCondition.ApplyRace(car, Report(bend: new[] { 0.025, 0, 0, 0 }), 0.4, GroupOf);

        var front = RunningGear.Mounted(car.Parts)[0];
        Assert.Equal(0.5, front.Spring!.Tear, 6);
        Assert.Equal(0.5, front.Shock!.Tear, 6);
        Assert.Equal(1.0, RunningGear.Mounted(car.Parts)[1].Spring!.Tear);
        Assert.Contains("Suspension: the front left corner is bent.", report);
        Assert.Equal(new[] { 0.5, 0, 0, 0 }, CarCondition.StartState(car, GroupOf).SuspensionBend);
    }

    [Fact]
    public void Tyres_lose_the_tread_AC_wore_and_a_blown_one_keeps_the_car_home()
    {
        var car = NewCar();

        CarCondition.ApplyRace(car, Report(tyreWear: 0.02, blown: 3), 1.7, GroupOf);

        var corners = RunningGear.Mounted(car.Parts);
        Assert.All(corners, c => Assert.Equal(0.98, c.Tyre!.Wear, 6));
        Assert.Equal(0.0, corners[3].Tyre!.Tear);
        Assert.Contains(CarCondition.WhyCannotRace(car, GroupOf), p => p.Contains("rear right tyre"));
        Assert.True(car.TireCondition < 0.98);
    }

    [Fact]
    public void Driving_wears_the_engine_a_little_by_the_km()
    {
        var car = NewCar();

        CarCondition.ApplyRace(car, Report(), 400, GroupOf);

        Assert.Equal(1 - 400 * CarCondition.MileageWearPerKm, Part(car, "block").Wear, 9);
        Assert.Equal(1.0, RunningGear.Mounted(car.Parts)[0].Brake!.Wear);
    }

    [Fact]
    public void A_harder_game_wears_the_engine_faster()
    {
        var car = NewCar();

        CarCondition.ApplyRace(car, Report(), 400, GroupOf, wearMultiplier: 1.5);

        Assert.Equal(1 - 600 * CarCondition.MileageWearPerKm, Part(car, "block").Wear, 9);
    }

    [Fact]
    public void An_easy_game_turns_AC_damage_down()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var service = new IniModificationService(cfg, _temp.Combine("AcRestore"));

        Assert.True(service.ApplyIntent(new DragRaceIntent
        {
            PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O",
            DamagePercent = GameRules.For(Difficulty.Easy).RaceDamagePercent
        }));

        Assert.Contains("DAMAGE=60", File.ReadAllLines(Path.Combine(cfg, "assists.ini")));
    }

    [Fact]
    public void The_cars_figures_follow_its_parts()
    {
        var car = NewCar();

        CarCondition.ApplyRace(car, Report(life: 500, gearbox: 0.3), 0, GroupOf);

        Assert.Equal(0.5, car.EngineHealth, 6);
        Assert.Equal(0.7, car.TransmissionHealth, 6);
        Assert.Equal(1.0, car.BodyCondition);
    }

    [Fact]
    public void Nonsense_in_the_report_changes_nothing()
    {
        var car = NewCar();

        CarCondition.ApplyRace(car, new RaceCarCondition
        {
            BodyDamageKmh = new List<double> { double.NaN, double.PositiveInfinity, -50, 0 },
            EngineLife = double.NaN,
            GearboxDamage = double.NegativeInfinity,
            Wheels = new List<WheelCondition> { new() { TyreWear = double.NaN, SuspensionDamage = -1 } }
        }, double.NaN, GroupOf);

        Assert.All(car.BodyDamageKmh, z => Assert.Equal(0, z));
        Assert.All(car.Parts.SelectMany(p => p.SelfAndDescendants()), p =>
        {
            Assert.Equal(1.0, p.Wear);
            Assert.Equal(1.0, p.Tear);
        });
    }

    [Fact]
    public void A_car_without_parts_takes_the_report_on_its_own_figures()
    {
        var car = new Car("car_a") { EngineHealth = 0.9, TransmissionHealth = 1, TireCondition = 1, BodyCondition = 1 };

        CarCondition.ApplyRace(car, Report(body: new double[] { 50, 0, 0, 0 }, life: 300, gearbox: 0.2, tyreWear: 0.1), 1, groupOf: null);

        Assert.Equal(0.3, car.EngineHealth, 6);
        Assert.Equal(0.8, car.TransmissionHealth, 6);
        Assert.Equal(0.9, car.TireCondition, 6);
        Assert.Equal(0.75, car.BodyCondition, 6);
        Assert.Equal(300, CarCondition.StartState(car, null).EngineLife, 6);
    }

    // ---- Into the next race ----

    [Fact]
    public void The_start_goes_into_race_ini_in_AC_number_format_whatever_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var keys = new RaceStartState
            {
                BodyKmh = new[] { 25.8, 0, 0, 1 },
                EngineLife = 640.25,
                GearboxWear = 0.125,
                SuspensionBend = new[] { 0.5, 0, 0, 0 }
            }.IniKeys(1).ToDictionary(k => k.Key, k => k.Value);

            Assert.Equal("25.8,0.0,0.0,1.0", keys["CAR_1_BODY"]);
            Assert.Equal("640.3", keys["CAR_1_ENGINE_LIFE"]);
            Assert.Equal("0.125", keys["CAR_1_GEARBOX"]);
            Assert.Equal("0.500,0.000,0.000,0.000", keys["CAR_1_SUSPENSION"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Race_ini_carries_each_cars_start_under_STREET_ROD()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var service = new IniModificationService(cfg, _temp.Combine("AcRestore"));

        Assert.True(service.ApplyIntent(new DragRaceIntent
        {
            PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O",
            PlayerStart = new RaceStartState { EngineLife = 500 },
            OpponentStart = new RaceStartState { BodyKmh = new double[] { 10, 0, 0, 0 } }
        }));

        var lines = File.ReadAllLines(Path.Combine(cfg, "race.ini"));
        var section = lines.SkipWhile(l => l != "[STREET_ROD]").ToList();
        Assert.Contains("CAR_0_ENGINE_LIFE=500.0", section);
        Assert.Contains("CAR_1_BODY=10.0,0.0,0.0,0.0", section);
        Assert.Contains("CAR_1_ENGINE_LIFE=1000.0", section);
    }

    [Fact]
    public void A_bracket_race_tells_the_mode_both_dial_ins_whatever_the_culture()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var service = new IniModificationService(cfg, _temp.Combine("AcRestore"));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(service.ApplyIntent(new DragRaceIntent
            {
                PlayerCarId = "a", OpponentCarId = "b", PlayerName = "P", OpponentName = "O",
                Bracket = new BracketSetup(12.4, 13.05, 0.2, 0.06)
            }));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        var section = File.ReadAllLines(Path.Combine(cfg, "race.ini")).SkipWhile(l => l != "[STREET_ROD]").ToList();
        Assert.Contains("RACE_TYPE=DRAG", section);
        Assert.Contains("DIAL_IN=12.40,13.05", section);
        Assert.Contains("BRACKET_RIVAL=0.200,0.060", section);
    }

    [Fact]
    public void A_test_and_tune_is_the_player_alone_on_the_strip()
    {
        var cfg = _temp.Combine("cfg");
        Directory.CreateDirectory(cfg);
        var service = new IniModificationService(cfg, _temp.Combine("AcRestore"));

        Assert.True(service.ApplyIntent(new DragRaceIntent
        {
            PlayerCarId = "a", PlayerName = "P", TunePasses = 6,
            PlayerStart = new RaceStartState { EngineLife = 700 },
            OpponentStart = new RaceStartState { EngineLife = 500 }
        }));

        var lines = File.ReadAllLines(Path.Combine(cfg, "race.ini"));
        Assert.Contains("CARS=1", lines);
        Assert.DoesNotContain("[CAR_1]", lines);
        Assert.Contains($"LAPS={IniModificationService.TuneLaps}", lines);
        var section = lines.SkipWhile(l => l != "[STREET_ROD]").ToList();
        Assert.Contains("RACE_TYPE=TUNE", section);
        Assert.Contains("TUNE_PASSES=6", section);
        Assert.Contains("CAR_0_ENGINE_LIFE=700.0", section);
        Assert.DoesNotContain(section, l => l.StartsWith("CAR_1_") || l.StartsWith("DIAL_IN"));
    }

    [Fact]
    public void A_bent_axle_toes_out_and_a_worn_gearbox_shifts_slower_in_the_cars_data()
    {
        var files = new Dictionary<string, string>
        {
            ["suspensions.ini"] = "[FRONT]\r\nTOE_OUT=0.00010\r\n[REAR]\r\nTOE_OUT=-0.000100\r\n",
            ["drivetrain.ini"] = "[GEARBOX]\r\nCHANGE_UP_TIME=400\r\nCHANGE_DN_TIME=420\r\nVALID_SHIFT_RPM_WINDOW=600\r\n"
        };

        var changed = AcDamageData.Generate(new RaceStartState
        {
            GearboxWear = 0.5,
            SuspensionBend = new[] { 0.5, 0, 0, 0 }
        }, files.GetValueOrDefault);

        var suspensions = new IniText(changed["suspensions.ini"]);
        // The front axle takes the mean of its corners: a quarter of AC's 0.05 m
        Assert.Equal(0.0001 + 0.0125, suspensions.GetNumber("FRONT", "TOE_OUT")!.Value, 6);
        Assert.Equal(-0.0001, suspensions.GetNumber("REAR", "TOE_OUT")!.Value, 6);

        var drivetrain = new IniText(changed["drivetrain.ini"]);
        Assert.Equal(800, drivetrain.GetNumber("GEARBOX", "CHANGE_UP_TIME"));
        Assert.Equal(840, drivetrain.GetNumber("GEARBOX", "CHANGE_DN_TIME"));
        Assert.Equal(420, drivetrain.GetNumber("GEARBOX", "VALID_SHIFT_RPM_WINDOW"));
    }

    [Fact]
    public void A_car_in_good_shape_changes_no_data()
    {
        var changed = AcDamageData.Generate(new RaceStartState { BodyKmh = new double[] { 40, 0, 0, 0 }, EngineLife = 300 },
            _ => "[FRONT]\r\nTOE_OUT=0.0001\r\n[GEARBOX]\r\nCHANGE_UP_TIME=400\r\n");

        // Body and engine go in through the mode, not the data
        Assert.Empty(changed);
    }

    // ---- The repair bay ----

    [Fact]
    public void Body_work_is_charged_by_the_hit_and_straightens_the_body()
    {
        var car = new Car("car_a") { BodyDamageKmh = new double[] { 30, 0, 0, 0 } };

        var job = Assert.Single(RepairShop.Jobs(car, catalog: null));

        Assert.Equal(RepairShop.Labour + 30 * RepairShop.BodyCostPerKmh, job.Cost);
        Assert.Equal(GameAction.GarageWorkMinor, job.Time);
        job.Apply();
        Assert.Equal(0, CarCondition.BodyTotal(car));
        Assert.Equal(1.0, car.BodyCondition);
    }

    [Fact]
    public void A_totaled_body_is_big_work()
    {
        var car = new Car("car_a") { BodyDamageKmh = new double[] { 0, 0, 205, 205 } };

        var job = Assert.Single(RepairShop.Jobs(car, catalog: null));

        Assert.StartsWith("Totaled", job.Detail);
        Assert.Equal(GameAction.GarageWorkMajor, job.Time);
        job.Apply();
        Assert.Empty(CarCondition.WhyCannotRace(car, null));
    }

    [Fact]
    public void A_car_in_good_shape_has_nothing_to_repair()
    {
        Assert.Empty(RepairShop.Jobs(NewCar(), catalog: null));
    }

    private static readonly Lazy<PartsCatalog> Catalog = new(() => PartsCatalog.Load(RepoPaths.PartsFolder));

    /// <summary>A real Mopar 340 in a car, its tree as the factory puts one in</summary>
    private static Car CarWithRealEngine(PartsCatalog catalog)
    {
        var build = catalog.EngineBuilds.Single(b => b.Id.EndsWith("chrysler-v8-pack/chrysler-la-340-275-hp", StringComparison.Ordinal));
        var tree = PartTreeBuilder.BuildEngine(catalog, build)!;
        var car = new Car("car_a") { HasPartsAssigned = true };
        car.Parts.Add(PartTrees.FromInstalled(tree.Root, PartInstance.CarEngineSlot));
        return car;
    }

    [Fact]
    public void A_real_engine_blown_in_a_race_needs_a_rebuild_that_puts_it_right()
    {
        var catalog = Catalog.Value;
        var car = CarWithRealEngine(catalog);
        var groupOf = CarCondition.Groups(catalog);
        Assert.NotEmpty(CarCondition.RotatingParts(car, groupOf));
        Assert.NotNull(CarCondition.Transmission(car, groupOf));

        CarCondition.ApplyRace(car, new RaceCarCondition { EngineLife = -50, GearboxDamage = 0.3 }, 0.4, groupOf);
        Assert.Contains("the engine is blown", CarCondition.WhyCannotRace(car, groupOf));

        var jobs = RepairShop.Jobs(car, catalog);
        var engine = Assert.Single(jobs, j => j.Name == "Engine rebuild");
        var gearbox = Assert.Single(jobs, j => j.Name == "Gearbox rebuild");
        Assert.StartsWith("Blown: the ", engine.Detail);
        Assert.EndsWith(" let go", engine.Detail);
        Assert.True(engine.Cost > gearbox.Cost);
        Assert.Equal(GameAction.GarageWorkMajor, engine.Time);
        Assert.Equal(GameAction.GarageWorkMinor, gearbox.Time);

        engine.Apply();
        gearbox.Apply();
        Assert.Empty(CarCondition.WhyCannotRace(car, groupOf));
        Assert.Equal(1000, CarCondition.EngineLife(car, groupOf));
        Assert.Empty(RepairShop.Jobs(car, catalog));
    }

    // ---- The timeslip ----

    [Fact]
    public void A_timeslip_shows_a_dash_for_a_mark_the_car_never_reached()
    {
        var rows = TimeslipDialogViewModel.BuildRows(
        [
            new TimeslipLane("Me", new Timeslip { ReactionSeconds = 0.4567, QuarterMileSeconds = 13.9, QuarterMileMph = 101.456 }),
            new TimeslipLane("Him", null)
        ]);

        Assert.Equal("R/T", rows[0].Label);
        Assert.Equal(["0.457", "-"], rows[0].Values);
        Assert.Equal(["13.900", "-"], rows.Single(r => r.Label == "1/4 ET").Values);
        Assert.Equal(["101.46", "-"], rows.Single(r => r.Label == "1/4 MPH").Values);
        Assert.Equal("-", rows.Single(r => r.Label == "60'").Values[0]);
        Assert.DoesNotContain(rows, r => r.Label == "DIAL");
    }

    [Fact]
    public void A_bracket_slip_starts_with_the_dial_ins_and_marks_a_red_light()
    {
        var rows = TimeslipDialogViewModel.BuildRows(
        [
            new TimeslipLane("Me", new Timeslip { ReactionSeconds = -0.052, RedLight = true }, 12.4),
            new TimeslipLane("Him", new Timeslip { ReactionSeconds = 0.31 }, 13.05)
        ]);

        Assert.Equal("DIAL", rows[0].Label);
        Assert.Equal(["12.40", "13.05"], rows[0].Values);
        Assert.Equal(["-0.052 RL", "0.310"], rows.Single(r => r.Label == "R/T").Values);
    }
}
