namespace Street_Rod_AC.Helpers;

/// <summary>
/// Figures that run from 0 to 1 (health, wear, condition). What a NaN or an infinity stands for is the caller's to
/// say: a part nobody measured is as good as new, a report that makes no sense counts for nothing.
///
/// Plain BCL on purpose, like <see cref="Multiplier"/>.
/// </summary>
public static class Unit
{
    /// <summary><paramref name="value"/> held to 0..1; <paramref name="ifNotFinite"/> when it is not a number</summary>
    public static double Clamp01(double value, double ifNotFinite) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : ifNotFinite;
}
