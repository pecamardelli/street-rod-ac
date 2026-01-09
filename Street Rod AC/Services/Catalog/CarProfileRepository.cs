using LiteDB;
using Street_Rod_AC.Models.Catalog;
using System.IO;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// LiteDB implementation of car profile repository
    /// Shares catalog.db with CarDefinitions but uses separate collection
    /// </summary>
    public class CarProfileRepository : ICarProfileRepository
    {
        private readonly string _databasePath;
        private const string ProfilesCollection = "catalog_carprofiles";

        public CarProfileRepository()
        {
            // Same database directory as catalog
            var dbDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StreetRodAC",
                "Saves");

            Directory.CreateDirectory(dbDirectory);
            _databasePath = Path.Combine(dbDirectory, "catalog.db");
        }

        public CarProfile? GetProfile(string carDefinitionId)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.FindById(carDefinitionId);
        }

        public CarProfile GetOrCreateProfile(string carDefinitionId, Func<CarProfile> createDefault)
        {
            var existing = GetProfile(carDefinitionId);
            if (existing != null)
                return existing;

            var newProfile = createDefault();
            UpsertProfile(newProfile);
            return newProfile;
        }

        public void UpsertProfile(CarProfile profile)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            // Ensure indexes
            collection.EnsureIndex(x => x.CarDefinitionId, unique: true);
            collection.EnsureIndex(x => x.DealerPrecedence);

            collection.Upsert(profile);
        }

        public List<CarProfile> GetAllProfiles()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.FindAll().ToList();
        }

        public bool ProfileExists(string carDefinitionId)
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Exists(x => x.CarDefinitionId == carDefinitionId);
        }

        public int GetProfileCount()
        {
            using var db = new LiteDatabase(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Count();
        }
    }
}
