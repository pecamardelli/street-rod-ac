using LiteDB;
using Street_Rod_AC.Models.Catalog;

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

        /// <summary>The indexes are made by the first write of the session; after that they are there</summary>
        private bool _indexesEnsured;

        public CarProfileRepository()
        {
            _databasePath = CatalogDatabase.DefaultPath;
        }

        private void EnsureIndexes(ILiteCollection<CarProfile> collection)
        {
            if (_indexesEnsured) return;

            collection.EnsureIndex(x => x.DealerPrecedence);
            _indexesEnsured = true;
        }

        public CarProfile? GetProfile(string carDefinitionId)
        {
            using var db = CatalogDatabase.Open(_databasePath);
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
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            EnsureIndexes(collection);
            collection.Upsert(profile);
        }

        public void UpsertProfiles(IEnumerable<CarProfile> profiles)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            EnsureIndexes(collection);
            collection.Upsert(profiles);
        }

        public bool UpdateProfile(string carDefinitionId, Func<CarProfile, CarProfile?> change)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);

            var stored = collection.FindById(carDefinitionId);
            return stored != null && change(stored) is { } changed && collection.Update(changed);
        }

        public int MergeProfiles(IEnumerable<CarProfile> created, IEnumerable<KeyValuePair<string, Func<CarProfile, CarProfile?>>> changes)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            EnsureIndexes(collection);

            var stored = 0;

            // One transaction for the lot, as one Upsert was: a batch of hundreds is one write, not hundreds
            db.BeginTrans();
            try
            {
                foreach (var profile in created)
                {
                    if (collection.FindById(profile.CarDefinitionId) != null) continue;
                    collection.Insert(profile);
                    stored++;
                }

                foreach (var (id, change) in changes)
                {
                    if (collection.FindById(id) is { } current && change(current) is { } changed && collection.Update(changed))
                        stored++;
                }

                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }

            return stored;
        }

        public List<CarProfile> GetAllProfiles()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return [.. collection.FindAll()];
        }

        public bool ProfileExists(string carDefinitionId)
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Exists(x => x.CarDefinitionId == carDefinitionId);
        }

        public int GetProfileCount()
        {
            using var db = CatalogDatabase.Open(_databasePath);
            var collection = db.GetCollection<CarProfile>(ProfilesCollection);
            return collection.Count();
        }
    }
}
