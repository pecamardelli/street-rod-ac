using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Repository for managing the content catalog (cars and tracks)
    /// </summary>
    public interface IContentCatalogRepository
    {
        // Car operations
        void UpsertCar(CarDefinition car);
        void UpsertCars(IEnumerable<CarDefinition> cars);
        CarDefinition? GetCar(string id);
        List<CarDefinition> GetAllCars();
        List<CarDefinition> GetCarsByStatus(ContentStatus status);
        bool CarExists(string id);
        void UpdateCarStatus(string id, ContentStatus status);

        // Bulk operations
        void MarkAllCarsAsLegacy();
        void DeleteBrokenCars();

        // Utility
        int GetCarCount();
    }
}
