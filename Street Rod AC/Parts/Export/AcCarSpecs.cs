namespace Street_Rod_AC.Parts.Export;

/// <summary>One axle of a car as its Assetto Corsa data describes it</summary>
public sealed record AcAxleSpecs
{
    /// <summary>Tread width, rolling radius and rim radius of the first compound, metres</summary>
    public double TyreWidth { get; init; }
    public double TyreRadius { get; init; }
    public double RimRadius { get; init; }

    /// <summary>Brake torque one wheel of the axle gets at full pedal, Nm</summary>
    public double BrakeTorque { get; init; }

    /// <summary>Wheel rate, N/m</summary>
    public double SpringRate { get; init; }

    public double DampBump { get; init; }
    public double DampRebound { get; init; }

    /// <summary>Kilograms the axle carries at rest, per wheel</summary>
    public double CornerLoad { get; init; }
}

/// <summary>
/// What a car's own data says about the running gear the mapping starts from. A car's factory parts are
/// matched against these, and every change the player makes moves the car's figures in proportion.
/// </summary>
public sealed record AcCarSpecs
{
    public double TotalMass { get; init; }

    /// <summary>Share of the mass on the front axle</summary>
    public double FrontWeightShare { get; init; }

    public AcAxleSpecs Front { get; init; } = new();
    public AcAxleSpecs Rear { get; init; } = new();

    public AcAxleSpecs Axle(bool front) => front ? Front : Rear;

    /// <param name="readFile">Text of a file of the car's data, null when it has no such file</param>
    public static AcCarSpecs Read(Func<string, string?> readFile)
    {
        var car = new IniText(readFile("car.ini"));
        var tyres = new IniText(readFile("tyres.ini"));
        var brakes = new IniText(readFile("brakes.ini"));
        var suspensions = new IniText(readFile("suspensions.ini"));

        var mass = car.GetNumber("BASIC", "TOTALMASS") ?? 0;
        var frontShare = suspensions.GetNumber("BASIC", "CG_LOCATION") ?? 0.5;

        var brakeTorque = brakes.GetNumber("DATA", "MAX_TORQUE") ?? 0;
        var brakeShare = brakes.GetNumber("DATA", "FRONT_SHARE") ?? 0.6;

        AcAxleSpecs Axle(string section, bool front)
        {
            var (rateSection, rateKey) = AcCarIni.SpringRateKey(suspensions, section);
            return new AcAxleSpecs
            {
                TyreWidth = tyres.GetNumber(section, "WIDTH") ?? 0,
                TyreRadius = tyres.GetNumber(section, "RADIUS") ?? 0,
                RimRadius = tyres.GetNumber(section, "RIM_RADIUS") ?? 0,
                BrakeTorque = brakeTorque * (front ? brakeShare : 1 - brakeShare),
                SpringRate = suspensions.GetNumber(rateSection, rateKey) ?? 0,
                DampBump = suspensions.GetNumber(section, "DAMP_BUMP") ?? 0,
                DampRebound = suspensions.GetNumber(section, "DAMP_REBOUND") ?? 0,
                CornerLoad = mass * (front ? frontShare : 1 - frontShare) / 2
            };
        }

        return new AcCarSpecs
        {
            TotalMass = mass,
            FrontWeightShare = frontShare,
            Front = Axle("FRONT", true),
            Rear = Axle("REAR", false)
        };
    }
}
