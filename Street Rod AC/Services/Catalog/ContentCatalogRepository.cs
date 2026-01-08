using LiteDB;
using Street_Rod_AC.Models.Catalog;
using System.IO;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// LiteDB implementation of content catalog repository
    /// Shares database with game saves but uses independent collections
    /// </summary>
    public class ContentCatalogRepository : IContentCatalogRepository
    {
        private readonly string _databasePath;
        private const string CarsCollection = "catalog_cars";

        public ContentCatalogRepository()
        {
            // Same database directory as game saves
            var dbDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StreetRodAC",
                "Saves");

            Directory.CreateDirectory(dbDirectory);
            _databasePath = Path.Combine(dbDirectory, "catalog.db");
        }

        public void UpsertCar(CarDefinition car)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            // Ensure indexes exist
            collection.EnsureIndex(x => x.Id, unique: true);
            collection.EnsureIndex(x => x.Brand);
            collection.EnsureIndex(x => x.Source);
            collection.EnsureIndex(x => x.Status);

            collection.Upsert(car);
        }

        public void UpsertCars(IEnumerable<CarDefinition> cars)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            // Ensure indexes exist
            collection.EnsureIndex(x => x.Id, unique: true);
            collection.EnsureIndex(x => x.Brand);
            collection.EnsureIndex(x => x.Source);
            collection.EnsureIndex(x => x.Status);

            collection.Upsert(cars);
        }

        public CarDefinition? GetCar(string id)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.FindById(id);
        }

        public List<CarDefinition> GetAllCars()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.FindAll().ToList();
        }

        public List<CarDefinition> GetCarsByStatus(ContentStatus status)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Find(x => x.Status == status).ToList();
        }

        public bool CarExists(string id)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Exists(x => x.Id == id);
        }

        public void UpdateCarStatus(string id, ContentStatus status)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            var car = collection.FindById(id);
            if (car != null)
            {
                car.Status = status;
                car.LastUpdatedDate = DateTime.Now;
                collection.Update(car);
            }
        }

        public void MarkAllCarsAsLegacy()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            var allCars = collection.FindAll().ToList();
            foreach (var car in allCars)
            {
                if (car.Status == ContentStatus.Active)
                {
                    car.Status = ContentStatus.Legacy;
                    car.LastUpdatedDate = DateTime.Now;
                }
            }
            collection.Update(allCars);
        }

        public void DeleteBrokenCars()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            collection.DeleteMany(x => x.Status == ContentStatus.Broken);
        }

        public int GetCarCount()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Count();
        }
    }
}
