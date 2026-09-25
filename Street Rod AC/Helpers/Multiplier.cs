namespace Street_Rod_AC.Helpers;

/// <summary>
/// The career's multipliers (prices, prizes, wear) applied safely: one out of a hand-edited save counts as 1.
///
/// Plain BCL on purpose: the tools compile it by link.
/// </summary>
public static class Multiplier
{
    /// <summary>A price scaled by a multiplier, never negative; a multiplier out of a hand-edited save counts as 1</summary>
    public static decimal Scale(decimal price, double multiplier) =>
        Math.Max(0m, price * (decimal)Sane(multiplier));

    /// <summary>A multiplier that is not a positive, finite number (a save edited by hand) counts as 1</summary>
    public static double Sane(double multiplier) =>
        double.IsFinite(multiplier) && multiplier > 0 ? Math.Min(multiplier, 100) : 1.0;
}
