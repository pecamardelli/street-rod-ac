namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// Something the player should be told after a race (an event reward, a forfeit, a milestone). Services
    /// return these instead of opening dialogs; the screen that ran the race shows them, one at a time.
    /// </summary>
    public sealed record PlayerMessage(string Title, string Text);
}
