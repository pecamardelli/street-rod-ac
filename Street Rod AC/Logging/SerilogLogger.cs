using Serilog;
using Serilog.Events;

namespace Street_Rod_AC.Logging
{
    /// <summary>
    /// Serilog-based implementation of IAppLogger.
    /// Wraps Serilog behind application abstraction.
    /// </summary>
    internal class SerilogLogger(ILogger logger) : IAppLogger
    {
        private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public void Debug(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Debug(messageTemplate, propertyValues);
        }

        public void Information(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Information(messageTemplate, propertyValues);
        }

        public void Warning(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Warning(messageTemplate, propertyValues);
        }

        public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            _logger.Warning(exception, messageTemplate, propertyValues);
        }

        public void Error(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Error(messageTemplate, propertyValues);
        }

        public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            _logger.Error(exception, messageTemplate, propertyValues);
        }

        public void Critical(string messageTemplate, params object?[] propertyValues)
        {
            _logger.Fatal(messageTemplate, propertyValues);
        }

        public void Critical(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            _logger.Fatal(exception, messageTemplate, propertyValues);
        }
    }
}
