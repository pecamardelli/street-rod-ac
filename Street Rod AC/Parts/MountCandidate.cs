using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Parts;

/// <summary>
/// A loose part (with whatever is on it) in one of the places it could go: on a slot of a mounted part, or in
/// the engine bay when <see cref="Parent"/> is null. <see cref="Tag"/> is the caller's, to know the place again.
/// </summary>
public sealed record MountCandidate(InstalledPart Part, InstalledPart? Parent, int ParentSlot, int OwnSlot, object? Tag = null);
