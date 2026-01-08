using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Catalog
{
    /// <summary>
    /// Service for importing Assetto Corsa cars into the content catalog
    /// </summary>
    public interface ICarImportService
    {
        /// <summary>
        /// Performs a full import of all cars from AC installation
        /// </summary>
        /// <param name="progress">Optional progress callback (current, total, carName)</param>
        Task<ImportResult> ImportCarsAsync(IProgress<ImportProgress>? progress = null);

        /// <summary>
        /// Performs an incremental update, only importing changed content
        /// </summary>
        Task<ImportResult> IncrementalUpdateAsync(IProgress<ImportProgress>? progress = null);
    }

    /// <summary>
    /// Result of an import operation
    /// </summary>
    public class ImportResult
    {
        public int TotalFound { get; set; }
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = [];
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Progress information for import operations
    /// </summary>
    public class ImportProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentCarName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
    }
}
