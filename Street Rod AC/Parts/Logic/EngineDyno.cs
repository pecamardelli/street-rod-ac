namespace Street_Rod_AC.Parts.Logic;

/// <summary>Torque over engine speed, with the figures people quote</summary>
public sealed class DynoResult
{
    public const double WattsPerHp = 745.7;

    /// <param name="limiter">Engine speed the rev limiter allows; the peak figures are those the engine can reach</param>
    public DynoResult(double displacement, double compression, IReadOnlyList<(double Rpm, double Torque)> curve, double limiter = double.MaxValue)
    {
        Displacement = displacement;
        Compression = compression;
        Curve = curve;

        foreach (var (rpm, torque) in curve)
        {
            if (rpm > limiter) break;

            if (torque > MaxTorque) (MaxTorque, MaxTorqueRpm) = (torque, rpm);

            var power = Power(rpm, torque);
            if (power > MaxPowerHp) (MaxPowerHp, MaxPowerRpm) = (power, rpm);
        }
    }

    /// <summary>Cubic metres</summary>
    public double Displacement { get; }

    /// <summary>Static compression ratio, x:1</summary>
    public double Compression { get; }

    /// <summary>Net torque at the flywheel in Nm, at wide open throttle</summary>
    public IReadOnlyList<(double Rpm, double Torque)> Curve { get; }

    public double MaxTorque { get; }
    public double MaxTorqueRpm { get; }
    public double MaxPowerHp { get; }
    public double MaxPowerRpm { get; }

    public double TorqueAt(double rpm)
    {
        if (Curve.Count == 0) return 0;
        if (rpm <= Curve[0].Rpm) return Curve[0].Torque * Math.Max(0, rpm / Curve[0].Rpm);

        for (var i = 1; i < Curve.Count; i++)
        {
            if (rpm > Curve[i].Rpm) continue;

            var (left, right) = (Curve[i - 1], Curve[i]);
            return left.Torque + (right.Torque - left.Torque) * (rpm - left.Rpm) / (right.Rpm - left.Rpm);
        }

        return 0;
    }

    public double PowerHpAt(double rpm) => Power(rpm, TorqueAt(rpm));

    private static double Power(double rpm, double torque) => torque * rpm * Math.PI / 30.0 / WattsPerHp;
}

/// <summary>
/// The engine simulation the source game kept in native code: from what the parts add up to (<see cref="DynoInputs"/>)
/// to a torque curve. A mean value model, one operating point per speed: how much air the cylinders trap through
/// their valves in the time they have, what the fuel system can match, and the share of the heat the ideal cycle
/// turns into work at this compression.
///
/// The energy side follows the scripts' own figures: their heating values only add up to the power the source
/// game showed when the full ideal air cycle efficiency is applied and nothing is taken off for friction (builds
/// held back by their fuel system land within 5% that way). Breathing is fitted to the builds with a known
/// power in engine_builds.json; tools/EngineBench "rated" shows how the model does against them.
/// </summary>
public static class EngineDyno
{
    private const double RpmStep = 250;
    private const double AirDensity = 1.184;        // kg/m3 at 25 C
    private const double Atmosphere = 1.0;          // bar
    private const double HeatCapacityRatio = 1.4;

    // First order filling: how fast a cylinder fills and empties (1/s at full breathing), and how breathing
    // figures below 1 weigh in. Fitted.
    private const double IntakeRate = 680;
    private const double ExhaustRate = 565;
    private const double FlowExponent = 1.42;

    // Share of the charge displaced by burnt gas that did not get out. Fitted.
    private const double ResidualPenalty = 0.6;

    // Long valve timing costs cylinder filling at low speed: charge pushed back out before the valve shuts
    private const double ReversionRpm = 1800;
    private const double ReversionGain = 1.2;
    private const double ReversionBase = 0.15;

    // A burn that is not centred wastes work; degrees of spark error for the work to drop to zero
    private const double PhasingWidth = 140;

    // Boost in the scripts' units to bar of manifold pressure
    private const double BoostScale = 0.25;

    public static DynoResult Run(DynoInputs inputs)
    {
        var cylinderVolume = Math.PI / 4 * inputs.Bore * inputs.Bore * inputs.Stroke;
        var displacement = cylinderVolume * inputs.Cylinders;
        var compression = inputs.ClearanceVolume > 0 ? (cylinderVolume + inputs.ClearanceVolume) / inputs.ClearanceVolume : 1;

        var curve = new List<(double, double)>();
        var maxRpm = inputs.MaxRpm > 0 ? inputs.MaxRpm : inputs.RpmLimit * 1.25;
        // The inputs are script figures: NaN fails every comparison, so it is ruled out by name
        if (!double.IsFinite(displacement) || !double.IsFinite(compression) || !double.IsFinite(maxRpm)
            || displacement <= 0 || inputs.Cylinders <= 0 || compression <= 1.5 || maxRpm <= 0 || maxRpm > 30_000)
            return new DynoResult(double.IsFinite(displacement) ? Math.Max(0, displacement) : 0, double.IsFinite(compression) ? compression : 1, curve);

        for (var rpm = RpmStep; rpm <= maxRpm + 1; rpm += RpmStep)
        {
            // A point the model cannot work out (a figure of the parts that is not a number) makes no torque
            var torque = Torque(inputs, rpm, displacement, compression);
            curve.Add((rpm, double.IsFinite(torque) ? Math.Max(0, torque) : 0));
        }

        return new DynoResult(displacement, compression, curve, inputs.RpmLimit > 0 && double.IsFinite(inputs.RpmLimit) ? inputs.RpmLimit : double.MaxValue);
    }

    private static double Torque(DynoInputs inputs, double rpm, double displacement, double compression)
    {
        var cycleTime = 120.0 / rpm;

        // Breathing: the intake fills the cylinder for as long as it is open, the exhaust clears it
        var intakeDuration = Span(inputs.IntakeOpen, inputs.IntakeClose);
        var exhaustDuration = Span(inputs.ExhaustOpen, inputs.ExhaustClose);
        var filling = 1 - Math.Exp(-IntakeRate * Flow(inputs.IntakeMax) * intakeDuration * cycleTime);
        var residual = Math.Exp(-ExhaustRate * Flow(inputs.ExhaustMax) * exhaustDuration * cycleTime);

        // A valve open much longer than the stroke lets charge back out while the piston is slow
        var excess = Math.Max(0, intakeDuration - 0.25) / 0.25;
        var reversion = 1 - Math.Min(0.6, ReversionGain * (ReversionBase + excess) * Math.Exp(-rpm / ReversionRpm));

        var volumetric = filling * (1 - ResidualPenalty * residual) * reversion;

        var pressure = Atmosphere + BoostScale * Boost(inputs, rpm);
        var airPerSecond = AirDensity * pressure / Atmosphere * displacement * volumetric / cycleTime;
        if (inputs.MaxAirFlow > 0) airPerSecond = Math.Min(airPerSecond, inputs.MaxAirFlow);

        // Fuel follows air at the set ratio as far as the fuel system goes; the air beyond that burns nothing
        var ratio = Math.Max(1, inputs.MixtureRatio);
        var fuelPerSecond = airPerSecond / ratio;
        if (inputs.MaxFuelFlow > 0) fuelPerSecond = Math.Min(fuelPerSecond, inputs.MaxFuelFlow);
        var mixturePerSecond = fuelPerSecond * (1 + ratio);

        var ideal = 1 - Math.Pow(compression, 1 - HeatCapacityRatio);
        var wallLoss = Math.Clamp(1 - inputs.HeatLoss / 100.0 * cycleTime * 0.25, 0.5, 1);
        var power = mixturePerSecond * inputs.MixtureHeat * ideal * wallLoss * Phasing(inputs, rpm);

        return power / (rpm * Math.PI / 30.0);
    }

    /// <summary>Length of a valve event as a fraction of the cycle; events may wrap around the end of the cycle</summary>
    private static double Span(double open, double close)
    {
        var span = close - open;
        return span < 0 ? span + 1 : span;
    }

    private static double Flow(double breathing) => Math.Pow(Math.Clamp(breathing, 0, 1), FlowExponent);

    /// <summary>
    /// Best work comes with the burn centred a little after top dead centre, which takes a spark lead of about
    /// half the burn. Too early fights the piston, too late wastes expansion.
    /// </summary>
    private static double Phasing(DynoInputs inputs, double rpm)
    {
        var progress = inputs.SparkRpm1 > inputs.SparkRpm0
            ? Math.Clamp((rpm - inputs.SparkRpm0) / (inputs.SparkRpm1 - inputs.SparkRpm0), 0, 1)
            : 0;
        var spark = inputs.SparkMin + inputs.SparkInc * progress;

        var leadDegrees = (0.5 - spark) * 720;
        var burnDegrees = inputs.BurnTime * rpm * 6;
        var bestLead = burnDegrees * 0.5 + 8;

        var error = (leadDegrees - bestLead) / PhasingWidth;
        return Math.Clamp(1 - error * error, 0.5, 1);
    }

    /// <summary>
    /// Chargers spin with the engine; boost peaks where they work best and the wastegate caps it. A blower's
    /// script hands over its working band (engine speeds) multiplied by the square of its drive ratio, so the
    /// speed it is compared with is the engine's times that square: a roots blower geared 4:1 with its band at
    /// 900-4400 rpm makes its boost there, not at 10,000.
    /// </summary>
    public static double Boost(DynoInputs inputs, double rpm)
    {
        if (inputs.BoostMax <= 0 || inputs.TurboRpmRange <= 0) return 0;

        var multiplier = Math.Max(1, inputs.TurboRpmMultiplier);
        var chargerRpm = rpm * multiplier * multiplier;
        var offset = (chargerRpm - inputs.TurboRpmOptimum) / inputs.TurboRpmRange;
        var boost = inputs.BoostMax * Math.Exp(-0.5 * offset * offset);
        return inputs.BoostWastegate > 0 ? Math.Min(boost, inputs.BoostWastegate) : boost;
    }
}
