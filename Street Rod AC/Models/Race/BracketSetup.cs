namespace Street_Rod_AC.Models.Race
{
    /// <summary>
    /// A bracket race as the race mode runs it (race.ini [STREET_ROD] DIAL_IN, BRACKET_RIVAL): both dial-ins, in
    /// seconds over the quarter, and how the rival drives, the seconds it takes to leave after its green and how far
    /// over its dial-in it aims when it takes the stripe (<see cref="Services.Race.BracketRules"/>)
    /// </summary>
    public sealed record BracketSetup(double PlayerDialIn, double OpponentDialIn, double RivalReaction, double RivalMargin);
}
