using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Parts.Sounds;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Parts.Logic;

namespace Street_Rod_AC.Services.Parts
{
    /// <summary>
    /// Ties the cars of the game to the parts catalog. The catalog and the dyno figures of its engine builds
    /// are loaded when first asked for; <see cref="WarmUpAsync"/> does that ahead of time, off the UI thread.
    /// </summary>
    public interface ICarPartsService
    {
        PartsCatalog Catalog { get; }

        EngineBuildIndex Builds { get; }

        /// <summary>
        /// False when there are no converted parts to work with, or they could not be read; cars then simply
        /// have no part trees. Never throws.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>Loads the catalog and gives every car profile without a factory engine its suggestion</summary>
        Task WarmUpAsync();

        /// <summary>The engine build a car leaves the factory with: the profile's, else the best match</summary>
        RatedBuild? GetStockBuild(CarDefinition car);

        /// <summary>
        /// Gives a car that never had parts (an older save, a car made without them) its factory engine, worn
        /// like the car. True when the car changed.
        /// </summary>
        bool EnsureParts(Car car);

        /// <summary>
        /// <see cref="EnsureParts"/> for a screen: the engine is put together on a worker thread, the car is
        /// changed on the thread that called. Saving the car is the caller's, on that thread as well.
        /// </summary>
        Task<bool> EnsurePartsAsync(Car car);

        /// <summary>
        /// Brings the parts of a loaded save in step with the catalog: a pack replaced by a later release gave its
        /// parts new ids and moved some slots. Parts of the player that no longer fit where they were go on the
        /// shelf; a car whose engine the catalog no longer has gets its factory engine again; parts and cars for
        /// sale that are gone leave the offer. True when the save changed.
        /// </summary>
        bool BringUpToDate(GameState game);

        /// <summary>The engine of a car that has been around: the factory build, now and then worked on</summary>
        BuiltEngine? CreateUsedEngine(CarDefinition car, double condition);

        /// <summary>An engine in a few words, e.g. "GM 327-396 small block, 258 hp"</summary>
        string? Describe(PartInstance engine, EngineReport? report);

        /// <summary>The car's engine on the dyno; null for a car without one or without parts</summary>
        EngineReport? Evaluate(Car car);

        /// <summary>Every engine sound there is: the library's and the installed cars' own, read once</summary>
        SoundLibrary Sounds { get; }

        /// <summary>
        /// The sound the car's engine races with; null when it keeps its own (no engine, no sounds, or the
        /// choice is the bank the car already has)
        /// </summary>
        CarSound? ChooseSound(Car car, EngineReport? report);

        /// <summary>Kilograms of the engine the car's Assetto Corsa data was made with: its factory build</summary>
        double? FactoryEngineMass(Car car);

        /// <summary>What the car's own Assetto Corsa data says about its running gear; null when it cannot be read</summary>
        AcCarSpecs? Specs(string carDefinitionId);

        /// <summary>The running gear the car left the factory with, matched to its data; null without a catalog or data</summary>
        (RunningGearFactory.AxleParts Front, RunningGearFactory.AxleParts Rear)? FactoryRunningGear(Car car);
    }
}
