
using LiteDB;
using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// LiteDB implementation of content catalog repository.
    /// catalog.db is the catalog's own database (%AppData%\StreetRodAC\Catalog), apart from the game saves
    /// </summary>
    public class ContentCatalogRepository : IContentCatalogRepository
    {
        private readonly string _databasePath;
        private const string CarsCollection = "catalog_cars";

        /// <summary>The indexes are made by the first write of the session; after that they are there</summary>
        private bool _indexesEnsured;

        public ContentCatalogRepository()
        {
            _databasePath = CatalogDatabase.DefaultPath;
        }

        private void EnsureIndexes(ILiteCollection<CarDefinition> collection)
        {
            if (_indexesEnsured) return;

            collection.EnsureIndex(x => x.Brand);
            collection.EnsureIndex(x => x.Source);
            collection.EnsureIndex(x => x.Status);
            _indexesEnsured = true;
        }

        public void UpsertCar(CarDefinition car)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            EnsureIndexes(collection);
            collection.Upsert(car);
        }

        public void UpsertCars(IEnumerable<CarDefinition> cars)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);

            EnsureIndexes(collection);
            collection.Upsert(cars);
        }

        public CarDefinition? GetCar(string id)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.FindById(id);
        }

        public List<CarDefinition> GetAllCars()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.FindAll().ToList();
        }

        public List<CarDefinition> GetCarsByStatus(ContentStatus status)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Find(x => x.Status == status).ToList();
        }

        public bool CarExists(string id)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Exists(x => x.Id == id);
        }

        public void UpdateCarStatus(string id, ContentStatus status)
        {
            using var db = CatalogDatabase.Open(_databasePath);
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
            using var db = CatalogDatabase.Open(_databasePath);
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
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            collection.DeleteMany(x => x.Status == ContentStatus.Broken);
        }

        public int GetCarCount()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarDefinition>(CarsCollection);
            return collection.Count();
        }
    }
}
