namespace StreetRodAC.Tests;

/// <summary>
/// The test classes that record race sessions, run one after the other: each deserializes <c>ProcessedRaceSession</c>
/// through LiteDB's global mapper, which builds a type's mapping on first use, and a second thread can see it
/// half-built (the id read as an ObjectId instead of the session's string).
/// </summary>
[CollectionDefinition(Name)]
public sealed class RaceSessionTests
{
    public const string Name = "Race sessions";
}
