using Serilog;
using Serilog.Events;
using System.IO;

namespace Street_Rod_AC.Logging
{
    /// <summary>
    /// Factory for creating categorized loggers.
    /// Initializes and configures the global logging infrastructure.
    /// </summary>
    public static class AppLoggerFactory
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initializes the global logging infrastructure.
        /// Must be called once at application startup.
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized)
                    return;

                // Configure log file path
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "StreetRodAC",
                    "Logs");

                Directory.CreateDirectory(logDirectory);

                var logFilePath = Path.Combine(logDirectory, "streetrod-.log");

                // Configure Serilog with rolling file logs
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.File(
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB
                        retainedFileCountLimit: 7, // Keep 7 days of logs
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                _isInitialized = true;

                Log.Information("Logging system initialized. Log directory: {LogDirectory}", logDirectory);
            }
        }

        /// <summary>
        /// Creates a categorized logger for a specific component.
        /// </summary>
        /// <param name="category">The category/context for the logger</param>
        public static IAppLogger CreateLogger(string category)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Logging system not initialized. Call Initialize() first.");
            }

            var contextLogger = Log.ForContext("SourceContext", category);
            return new SerilogLogger(contextLogger);
        }

        /// <summary>
        /// Creates a logger for a specific type
        /// </summary>
        public static IAppLogger CreateLogger<T>()
        {
            return CreateLogger(typeof(T).Name);
        }

        /// <summary>
        /// Flushes and closes the logging infrastructure.
        /// Should be called at application shutdown.
        /// </summary>
        public static void Shutdown()
        {
            Log.Information("Shutting down logging system");
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Predefined log categories for consistency
    /// </summary>
    public static class LogCategory
    {
        public const string App = "App";
        public const string Startup = "Startup";
        public const string Import = "Import";
        public const string Catalog = "Catalog";
        public const string Save = "Save";
        public const string ACIntegration = "ACIntegration";
        public const string UI = "UI";
        public const string Navigation = "Navigation";
        public const string Dialog = "Dialog";
        public const string Performance = "Performance";
        public const string Diagnostics = "Diagnostics";
    }
}
