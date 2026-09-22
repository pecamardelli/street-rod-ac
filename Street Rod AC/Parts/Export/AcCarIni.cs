namespace Street_Rod_AC.Parts.Export;

/// <summary>What the engine's export and the running gear's both do to a car's data files</summary>
public static class AcCarIni
{
    /// <summary>
    /// The car weighs what it did plus the difference the parts make. Less than half a kilo is no change,
    /// and no parts take the car below half its weight.
    /// </summary>
    public static void AddMass(IniText car, double delta)
    {
        if (Math.Abs(delta) < 0.5 || car.GetNumber("BASIC", "TOTALMASS") is not { } mass) return;

        car.Set("BASIC", "TOTALMASS", Math.Max(mass * 0.5, mass + delta), "0");
    }

    /// <summary>Where an axle keeps its wheel rate: the classic kinds on the axle section, a coil-over on the coil-over</summary>
    public static (string Section, string Key) SpringRateKey(IniText suspensions, string axle) =>
        suspensions.Get(axle, "SPRING_RATE") != null ? (axle, "SPRING_RATE") : (axle + "_COILOVER_0", "RATE");
}
