using System.IO;
using Street_Rod_AC.Audio;
using Street_Rod_AC.Configuration;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Parts.Export;

namespace Street_Rod_AC.Services.Parts
{
    /// <summary>
    /// What it takes to start a car's engine where it stands: the engine on its parts, as the dyno has it, and the
    /// sound it races with
    /// </summary>
    public static class EngineSpecs
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("EngineAudio");

        /// <summary>
        /// The engine of a car, worked out away from the UI thread. Null when the car has no engine to start (or
        /// the parts catalog is not there).
        /// </summary>
        public static Task<EngineSpec?> ForAsync(ICarPartsService parts, Car car, string name)
        {
            // The worker gets a copy made here, on the caller's thread: the car's own parts may be changed on the
            // workbench while the dyno runs
            var copy = Snapshot(car);
            return Task.Run(() =>
            {
                try
                {
                    return For(parts, copy, name);
                }
                catch (Exception ex)
                {
                    Logger.Warning("{Car}: the engine cannot be started in the garage: {Error}", copy.DefinitionId, ex.Message);
                    return null;
                }
            });
        }

        /// <summary>The car with copies of its parts (their ids kept), for a worker thread to evaluate</summary>
        private static Car Snapshot(Car car) => new(car.DefinitionId)
        {
            InstanceId = car.InstanceId,
            SkinId = car.SkinId,
            OdometerKM = car.OdometerKM,
            EngineHealth = car.EngineHealth,
            TransmissionHealth = car.TransmissionHealth,
            BodyCondition = car.BodyCondition,
            TireCondition = car.TireCondition,
            BodyDamageKmh = car.BodyDamageKmh?.ToArray() ?? new double[4],
            Parts = car.Parts.Select(p => PartTrees.Clone(p, keepIds: true)).ToList(),
            HasPartsAssigned = car.HasPartsAssigned,
            HasRunningGearAssigned = car.HasRunningGearAssigned,
            PowerHp = car.PowerHp
        };

        /// <summary>
        /// A car for sale, as it is: its own parts, or the factory's when the offer came without any (an older save)
        /// </summary>
        public static async Task<EngineSpec?> ForSaleAsync(ICarPartsService parts, UsedCarListing listing, string name)
        {
            var car = new Car(listing.CarDefinitionId)
            {
                Parts = listing.Parts,
                HasPartsAssigned = listing.Parts.Count > 0,
                HasRunningGearAssigned = true,
                EngineHealth = listing.Condition
            };

            if (!car.HasPartsAssigned)
            {
                // A copy of the list: the offer is not the place to keep a factory engine made up to listen to
                car.Parts = new List<PartInstance>();
                try
                {
                    await parts.EnsurePartsAsync(car);
                }
                catch (Exception ex)
                {
                    // A lot's engine that cannot be put together is one that does not start: the lot goes on
                    Logger.Warning("{Car}: no factory engine to start on the lot: {Error}", car.DefinitionId, ex.Message);
                    return null;
                }
            }

            return await ForAsync(parts, car, name);
        }

        private static EngineSpec? For(ICarPartsService parts, Car car, string name)
        {
            if (!parts.IsAvailable || car.Engine == null) return null;

            var report = parts.Evaluate(car);
            var dyno = report?.Dyno;
            var problem = report == null ? "the engine is not all there"
                : report.Runs ? null
                : report.Problem ?? "it makes no power";

            var sound = parts.ChooseSound(car, report)
                        ?? AcCarSound.FromCar(Path.Combine(AppSettings.Instance.CarsPath, car.DefinitionId), AppSettings.Instance.SfxGuidsPath);

            Func<double, double> torque = dyno is { Curve.Count: > 0 }
                ? rpm => dyno.TorqueAt(Math.Min(rpm, dyno.Curve[^1].Rpm))
                : _ => 250;

            return new EngineSpec(
                name,
                report?.IdleRpm ?? 0,
                report?.LimiterRpm ?? 0,
                report?.Inputs?.Cylinders ?? 8,
                report?.Inertia ?? 0,
                torque,
                dyno?.MaxTorque ?? 0,
                sound,
                problem);
        }
    }
}
