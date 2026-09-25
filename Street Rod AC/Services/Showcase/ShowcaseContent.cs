using System.IO;
using System.Text.Json;
using Street_Rod_AC.Controls.Showcase;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;

namespace Street_Rod_AC.Services.Showcase
{
    /// <summary>
    /// What the main screen has to show: the rooms listed in Assets/Showcase/scenes.json that are there to be loaded,
    /// and the installed cars of the catalog.
    /// </summary>
    public static class ShowcaseContent
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Showcase");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public static string ScenesFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Showcase", "scenes.json");

        private sealed record SceneEntry(string? Id, float WallRadius);

        private sealed record SceneFile(List<SceneEntry>? Scenes);

        /// <summary>
        /// The scenes the file lists and that can be found, in its order. Empty when the file is missing or broken
        /// (logged): the main screen falls back to its picture.
        /// </summary>
        public static IReadOnlyList<ShowcaseScene> LoadScenes(string file, string garagesPath, string showroomsPath)
        {
            SceneFile? parsed;
            try
            {
                if (!File.Exists(file))
                {
                    Logger.Warning("Main screen scenes not found: {Path}", file);
                    return [];
                }

                parsed = JsonSerializer.Deserialize<SceneFile>(File.ReadAllText(file), Options);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the main screen scenes from {Path}", file);
                return [];
            }

            var scenes = new List<ShowcaseScene>();
            foreach (var entry in parsed?.Scenes ?? [])
            {
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.WallRadius <= 0f)
                {
                    Logger.Warning("Skipped a main screen scene with no id or no wall radius");
                    continue;
                }

                var kn5 = FindKn5(entry.Id, garagesPath, showroomsPath);
                if (kn5 == null)
                {
                    Logger.Information("Main screen scene {Id} is not installed; left out", entry.Id);
                    continue;
                }

                scenes.Add(new ShowcaseScene(entry.Id, kn5, entry.WallRadius));
            }

            return scenes;
        }

        /// <summary>
        /// The model of a scene: the game's own garage by that id, else the AC showroom, whose model is not always
        /// spelled like its folder
        /// </summary>
        public static string? FindKn5(string id, string garagesPath, string showroomsPath)
        {
            foreach (var root in new[] { garagesPath, showroomsPath })
            {
                var folder = Path.Combine(root, id);
                if (!Directory.Exists(folder)) continue;

                var named = Path.Combine(folder, id + ".kn5");
                if (File.Exists(named)) return named;

                var any = Directory.EnumerateFiles(folder, "*.kn5", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (any != null) return any;
            }

            return null;
        }

        /// <summary>
        /// The installed cars of the catalog (as <see cref="Catalog.InstalledCars.Only"/> finds them), each with its
        /// folder and the name it is shown by
        /// </summary>
        public static IReadOnlyList<ShowcaseCar> Cars(IEnumerable<CarDefinition> installed, string carsPath) =>
            installed
                .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                .Select(c => new ShowcaseCar(c.Id, Path.Combine(carsPath, c.Id), Title(c), c.AvailableSkins?.ToList() ?? []))
                .ToList();

        /// <summary>The year in front of the name, unless the name already has it</summary>
        public static string Title(CarDefinition car)
        {
            var name = string.IsNullOrWhiteSpace(car.Name) ? car.Id : car.Name.Trim();
            return car.Year is { } year && !name.Contains(year.ToString()) ? $"{year} {name}" : name;
        }
    }
}
