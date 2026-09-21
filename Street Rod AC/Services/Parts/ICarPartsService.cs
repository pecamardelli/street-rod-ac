using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts;
using Street_Rod_AC.Parts.Cars;
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

        /// <summary>The engine of a car that has been around: the factory build, now and then worked on</summary>
        BuiltEngine? CreateUsedEngine(CarDefinition car, double condition);

        /// <summary>An engine in a few words, e.g. "GM 327-396 small block, 258 hp"</summary>
        string? Describe(PartInstance engine, EngineReport? report);
    }
}
