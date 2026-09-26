using System.IO;
using System.Text.Json;
using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Street
{
    /// <summary>How the street is lit: by day, at dusk, at night</summary>
    public enum StreetLight
    {
        Day,
        Dusk,
        Night
    }

    /// <summary>
    /// Which light the street is in at an hour of the game. The sun sets earlier in winter than in summer (about
    /// 16:25 at the turn of the year, 20:10 at midsummer, as in the middle of the States): dusk is the hour and a
    /// half before it, night from half an hour after it.
    /// </summary>
    public static class StreetLights
    {
        private const double MeanSunset = 18.3;
        private const double SunsetSwing = 1.9;
        private const int Midsummer = 172;

        public static double SunsetHour(DateTime date) =>
            MeanSunset + SunsetSwing * Math.Cos(2 * Math.PI * (date.DayOfYear - Midsummer) / 365.0);

        public static StreetLight At(DateTime time)
        {
            var hour = time.TimeOfDay.TotalHours;
            var sunset = SunsetHour(time);
            if (hour >= sunset + 0.5 || hour < 5.5) return StreetLight.Night;
            if (hour >= sunset - 1.5 || hour < 7) return StreetLight.Dusk;
            return StreetLight.Day;
        }

        /// <summary>How the street is spoken of at that light: "afternoon", "dusk", "night"</summary>
        public static string Describe(StreetLight light) => light switch
        {
            StreetLight.Day => "day",
            StreetLight.Dusk => "dusk",
            _ => "night"
        };
    }

    /// <summary>Where a car stands in the street: metres from the middle, degrees about the vertical (0 faces +z)</summary>
    public readonly record struct StreetSpot(float X, float Z, float Heading);

    /// <summary>A street light lit at night</summary>
    public sealed record StreetLamp(float X, float Y, float Z, byte[] Color, float Brightness, float Range);

    /// <summary>
    /// The renderer's light for a light of the day; colours r, g, b. <paramref name="CubemapAmbient"/> is the fill
    /// the renderer lights cars with from its reflections: over 0 it is a white light of about the same strength
    /// whatever the value, so the cars glow at night; 0 lights them with the ambient colours alone.
    /// </summary>
    public sealed record StreetLighting(float Brightness, byte[] Color, float Ambient, byte[] AmbientUp, byte[] AmbientDown,
        float CubemapAmbient, IReadOnlyList<StreetLamp> Lamps);

    /// <summary>
    /// A street the Cruise screen is set in, as Assets/Streets/&lt;id&gt;/street.json describes it: its models for
    /// each light, where the player's car is parked and the lane the rivals drive up in.
    /// </summary>
    public sealed record StreetScene(
        string Id,
        string Title,
        string Folder,
        StreetSpot Player,
        float LaneOffset,
        float RivalStop,
        float ApproachFrom,
        float LeaveTo,
        float[] Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights)
    {
        /// <summary>The model of the street in that light: street_day.kn5 and so on</summary>
        public string Kn5For(StreetLight light) => Path.Combine(Folder, $"street_{StreetLights.Describe(light)}.kn5");

        /// <summary>The light of the day as the renderer gets it; the day's when the file leaves one out</summary>
        public StreetLighting LightingFor(StreetLight light) =>
            Lights.TryGetValue(light, out var lighting) ? lighting
            : Lights.TryGetValue(StreetLight.Day, out var day) ? day
            : StreetScenes.DefaultLighting;
    }

    public static class StreetScenes
    {
        private static readonly IAppLogger Logger = AppLoggerFactory.CreateLogger("Street");

        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        internal static readonly StreetLighting DefaultLighting = new(1.5f, [200, 180, 180], 2f, [150, 180, 180], [150, 180, 180], 0.5f, []);

        public static string StreetsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Streets");

        private sealed record SpotEntry(float X, float Z, float Heading);

        private sealed record LampEntry(float X, float Y, float Z, int[]? Color, float Brightness, float Range);

        private sealed record LightEntry(float Brightness, int[]? Color, float Ambient, int[]? AmbientUp, int[]? AmbientDown,
            float? CubemapAmbient, List<LampEntry>? Lamps);

        private sealed record SceneEntry(string? Id, string? Title, SpotEntry? Player, float LaneOffset, float RivalStop,
            float ApproachFrom, float LeaveTo, float[]? Sun, Dictionary<string, LightEntry>? Lights);

        /// <summary>The first street under <paramref name="root"/> with a street.json and a model for every light; null when there is none (logged)</summary>
        public static StreetScene? LoadFirst(string root)
        {
            if (!Directory.Exists(root))
            {
                Logger.Warning("No streets folder: {Path}", root);
                return null;
            }

            foreach (var folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var scene = Load(folder);
                if (scene != null) return scene;
            }

            Logger.Warning("No street under {Path} could be loaded", root);
            return null;
        }

        /// <summary>The street in <paramref name="folder"/>; null when its file is missing or broken, or a model is (logged)</summary>
        public static StreetScene? Load(string folder)
        {
            var file = Path.Combine(folder, "street.json");
            SceneEntry? entry;
            try
            {
                if (!File.Exists(file)) return null;
                entry = JsonSerializer.Deserialize<SceneEntry>(File.ReadAllText(file), Options);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not read the street {Path}", file);
                return null;
            }

            if (entry?.Player == null || entry.LaneOffset <= 0f || entry.ApproachFrom <= 0f || entry.LeaveTo <= 0f)
            {
                Logger.Warning("The street {Path} has no parking spot or no lane", file);
                return null;
            }

            var lights = new Dictionary<StreetLight, StreetLighting>();
            foreach (var (name, light) in entry.Lights ?? [])
            {
                if (!Enum.TryParse<StreetLight>(name, ignoreCase: true, out var which)) continue;
                lights[which] = new StreetLighting(light.Brightness, Rgb(light.Color, 200), light.Ambient,
                    Rgb(light.AmbientUp, 150), Rgb(light.AmbientDown, 150), light.CubemapAmbient ?? 0.5f,
                    (light.Lamps ?? []).Select(l => new StreetLamp(l.X, l.Y, l.Z, Rgb(l.Color, 255), l.Brightness, l.Range)).ToList());
            }

            var sun = entry.Sun is { Length: 3 } s ? s : [0.2f, 1f, 0.8f];
            var scene = new StreetScene(entry.Id ?? Path.GetFileName(folder), entry.Title ?? entry.Id ?? "The street", folder,
                new StreetSpot(entry.Player.X, entry.Player.Z, entry.Player.Heading), entry.LaneOffset, entry.RivalStop,
                entry.ApproachFrom, entry.LeaveTo, sun, lights);

            var missing = Enum.GetValues<StreetLight>().Select(scene.Kn5For).FirstOrDefault(p => !File.Exists(p));
            if (missing != null)
            {
                Logger.Warning("The street {Id} has no model {Path}", scene.Id, missing);
                return null;
            }

            return scene;
        }

        // JSON numbers, not the base64 a byte[] is read from
        private static byte[] Rgb(int[]? color, byte fallback) =>
            color is { Length: 3 } ? color.Select(c => (byte)Math.Clamp(c, 0, 255)).ToArray() : [fallback, fallback, fallback];
    }
}
