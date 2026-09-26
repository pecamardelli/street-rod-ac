using Street_Rod_AC.Audio;

namespace Street_Rod_AC.Controls.Street;

/// <summary>A car to stand in the street: its AC folder and skin</summary>
public sealed record StreetCar(string Directory, string? Skin);

/// <summary>Where the rival is in the scene</summary>
public enum RivalPhase
{
    /// <summary>Nobody in the lane</summary>
    None,

    /// <summary>Coming up the lane</summary>
    Arriving,

    /// <summary>Stopped beside the player, engine running</summary>
    Alongside,

    /// <summary>Pulling away</summary>
    Leaving
}

/// <summary>
/// What goes on in the street, between the Cruise screen, which decides, and the street's viewport, which shows it:
/// the screen sends a rival up the lane and away again; the viewport drives the car there and says when it has
/// stopped alongside and when it has gone. Lives on the UI thread.
/// </summary>
public sealed class StreetStage
{
    /// <summary>The player's own engine, heard from inside the car, against its full volume</summary>
    public const float OwnEngineVolume = 0.55f;

    public StreetCar? RivalCar { get; private set; }

    /// <summary>The rival's engine, which the viewport drives while the car moves</summary>
    public EngineRunner? RivalEngine { get; private set; }

    public RivalPhase Phase { get; private set; }

    /// <summary>
    /// A viewport has the street up and drives the rival. With none (no 3D, or not up yet) a rival is simply there
    /// at once, and gone at once.
    /// </summary>
    public bool IsShown { get; internal set; }

    /// <summary>Something for the viewport to act on</summary>
    public event Action? Changed;

    /// <summary>The rival has stopped alongside</summary>
    public event Action? RivalAlongside;

    /// <summary>The rival has driven out of sight</summary>
    public event Action? RivalGone;

    /// <summary>A rival drives up in <paramref name="car"/>, with its engine running</summary>
    public void Arrive(StreetCar car, EngineRunner engine)
    {
        RivalCar = car;
        RivalEngine = engine;
        Phase = RivalPhase.Arriving;
        Changed?.Invoke();
        if (!IsShown) ReportAlongside();
    }

    /// <summary>The rival pulls away; one still on the way turns and goes as well</summary>
    public void Leave()
    {
        if (Phase is RivalPhase.None or RivalPhase.Leaving) return;
        Phase = RivalPhase.Leaving;
        Changed?.Invoke();
        if (!IsShown) ReportGone();
    }

    /// <summary>Nobody in the lane, at once (the screen is going, the light changed)</summary>
    public void Clear()
    {
        if (Phase == RivalPhase.None && RivalCar == null) return;
        Phase = RivalPhase.None;
        RivalCar = null;
        RivalEngine = null;
        Changed?.Invoke();
    }

    /// <summary>For the viewport: the car has stopped beside the player</summary>
    internal void ReportAlongside()
    {
        if (Phase != RivalPhase.Arriving) return;
        Phase = RivalPhase.Alongside;
        RivalAlongside?.Invoke();
    }

    /// <summary>For the viewport: the car is out of sight (or could not be shown at all)</summary>
    internal void ReportGone()
    {
        if (Phase == RivalPhase.None) return;
        Phase = RivalPhase.None;
        RivalCar = null;
        RivalEngine = null;
        RivalGone?.Invoke();
    }
}
