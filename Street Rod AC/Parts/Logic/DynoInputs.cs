using Street_Rod_AC.Parts.Scripting;

namespace Street_Rod_AC.Parts.Logic;

/// <summary>
/// Everything the part scripts hand to the engine simulation, as the block collects it from its parts into a
/// DynoData object. Names and units are the scripts' own.
/// </summary>
public sealed record DynoInputs
{
    public int Cylinders { get; init; }

    /// <summary>Metres</summary>
    public double Bore { get; init; }

    /// <summary>Metres</summary>
    public double Stroke { get; init; }

    /// <summary>Volume left above the piston at top dead centre, cubic metres per cylinder</summary>
    public double ClearanceVolume { get; init; }

    /// <summary>How well a cylinder breathes through its valves at low and at full lift, 0..1</summary>
    public double IntakeMin { get; init; }
    public double IntakeMax { get; init; }
    public double ExhaustMin { get; init; }
    public double ExhaustMax { get; init; }

    /// <summary>Valve events as a fraction of the 720 degree cycle</summary>
    public double IntakeOpen { get; init; }
    public double IntakeClose { get; init; }
    public double ExhaustOpen { get; init; }
    public double ExhaustClose { get; init; }

    /// <summary>Spark as a fraction of the cycle (0.5 = top dead centre): from Min, moving by Inc between Rpm0 and Rpm1</summary>
    public double SparkMin { get; init; }
    public double SparkInc { get; init; }
    public double SparkRpm0 { get; init; }
    public double SparkRpm1 { get; init; }

    /// <summary>Seconds the mixture takes to burn</summary>
    public double BurnTime { get; init; }

    public double RpmLimit { get; init; }

    /// <summary>Highest speed the weakest rotating part survives; the curve is computed up to here</summary>
    public double MaxRpm { get; init; }

    /// <summary>Heat lost to the cylinder walls, percent per second</summary>
    public double HeatLoss { get; init; }

    /// <summary>Air to fuel, by mass</summary>
    public double MixtureRatio { get; init; }

    /// <summary>Heat released per kg of mixture, J</summary>
    public double MixtureHeat { get; init; }

    /// <summary>What the fuel system and the air filter can deliver, kg/s; 0 = no limit</summary>
    public double MaxFuelFlow { get; init; }
    public double MaxAirFlow { get; init; }

    /// <summary>Forced induction: peak boost in bar over atmosphere and where the charger works best</summary>
    public double BoostMax { get; init; }
    public double BoostWastegate { get; init; }
    public double TurboRpmMultiplier { get; init; }
    public double TurboRpmOptimum { get; init; }
    public double TurboRpmRange { get; init; }

    public static DynoInputs From(ScriptObject data) => new()
    {
        Cylinders = (int)data.Number("cylinders"),
        Bore = data.Number("bore"),
        Stroke = data.Number("stroke"),
        ClearanceVolume = data.Number("Vmin"),
        IntakeMin = data.Number("in_min"),
        IntakeMax = data.Number("in_max"),
        ExhaustMin = data.Number("out_min"),
        ExhaustMax = data.Number("out_max"),
        IntakeOpen = data.Number("time_in_open"),
        IntakeClose = data.Number("time_in_close"),
        ExhaustOpen = data.Number("time_out_open"),
        ExhaustClose = data.Number("time_out_close"),
        SparkMin = data.Number("time_spark_min"),
        SparkInc = data.Number("time_spark_inc"),
        SparkRpm0 = data.Number("time_spark_RPM0"),
        SparkRpm1 = data.Number("time_spark_RPM1"),
        BurnTime = data.Number("time_burn"),
        RpmLimit = data.Number("RPM_limit"),
        MaxRpm = data.Number("maxRPM"),
        HeatLoss = data.Number("T_loss"),
        MixtureRatio = data.Number("mixture_ratio"),
        MixtureHeat = data.Number("mixture_H"),
        MaxFuelFlow = data.Number("max_fuel_consumption"),
        MaxAirFlow = data.Number("max_air_consumption"),
        BoostMax = data.Number("P_turbo_max"),
        BoostWastegate = data.Number("P_turbo_waste"),
        TurboRpmMultiplier = data.Number("rpm_turbo_mul"),
        TurboRpmOptimum = data.Number("rpm_turbo_opt"),
        TurboRpmRange = data.Number("rpm_turbo_range")
    };
}
