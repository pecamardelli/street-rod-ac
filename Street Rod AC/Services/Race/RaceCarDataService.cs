using System.IO;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;
using Street_Rod_AC.Services.Parts;

namespace Street_Rod_AC.Services.Race
{
    /// <summary>The data a car goes racing with, as its parts make it</summary>
    public sealed class RaceCarData
    {
        public RaceCarData(string carId, CarBuildResult build)
        {
            CarId = carId;
            Build = build;
        }

        /// <summary>The car's folder under content\cars</summary>
        public string CarId { get; }

        /// <summary>
        /// The id the car races under when another car of its model is in the race: it gets a copy of the folder
        /// with its own data and sound (<see cref="Parts.Export.AcCarFolder"/>); null races in the car's own folder
        /// </summary>
        public string? CloneId { get; init; }

        /// <summary>A car that races as its author made it, in a copy of its folder</summary>
        public static RaceCarData AsAuthored(string carId, string cloneId) => new(carId, new CarBuildResult()) { CloneId = cloneId };

        public CarBuildResult Build { get; }

        public bool CanDrive => Build.CanDrive;

        /// <summary>Why the car cannot go, for the player</summary>
        public string Problem => string.Join("; ", Build.Problems);
    }

    /// <summary>
    /// Turns what a car has on it into the Assetto Corsa data files for a race: the engine on the dyno, the
    /// running gear against the factory's. A car without parts (no catalog, an older save) races on the
    /// data its author gave it.
    /// </summary>
    public class RaceCarDataService
    {
        private readonly ICarPartsService _parts;
        private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("CarData");

        public RaceCarDataService(ICarPartsService parts)
        {
            _parts = parts;
        }

        /// <returns>The files to race with; null when the car has no parts to speak of and races as it is</returns>
        public RaceCarData? Prepare(Car car)
        {
            if (!_parts.IsAvailable || !car.HasPartsAssigned) return null;

            var catalog = _parts.Catalog;
            var carDirectory = Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId);
            Func<string, string?> readFile;
            try
            {
                readFile = AcCarDataReader.ForCar(carDirectory);
            }
            catch (Exception ex)
            {
                _logger.Warning("{Car}: data cannot be read, racing as it is: {Error}", car.DefinitionId, ex.Message);
                return null;
            }

            var build = new CarBuild
            {
                Engine = _parts.Evaluate(car),
                FactoryEngineMass = _parts.FactoryEngineMass(car),
                RunningGear = car.HasRunningGearAssigned ? RunningGear.Mounted(car.Parts) : null,
                FactoryRunningGear = car.HasRunningGearAssigned ? _parts.FactoryRunningGear(car) : null
            };

            var result = AcCarBuild.Generate(catalog, build, readFile);
            if (result.CanDrive) result.Sound = _parts.ChooseSound(car, build.Engine);
            _logger.Information("{Car}: {Power:0} hp, {Files} file(s) to change{Sound}{Problems}", car.DefinitionId, build.Engine?.Dyno?.MaxPowerHp ?? 0,
                result.Files.Count, result.Sound == null ? "" : ", the sound of " + result.Sound.DonorId,
                result.Problems.Count == 0 ? "" : ", cannot drive: " + string.Join("; ", result.Problems));
            return new RaceCarData(car.DefinitionId, result);
        }
    }
}
