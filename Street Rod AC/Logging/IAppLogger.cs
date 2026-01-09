namespace Street_Rod_AC.Logging
{
    /// <summary>
    /// Application-level logging abstraction.
    /// Wraps the underlying logging engine to prevent direct coupling.
    /// </summary>
    public interface IAppLogger
    {
        /// <summary>
        /// Logs a debug message with structured properties
        /// </summary>
        void Debug(string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs an information message with structured properties
        /// </summary>
        void Information(string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs a warning message with structured properties
        /// </summary>
        void Warning(string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs a warning with exception and structured properties
        /// </summary>
        void Warning(Exception exception, string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs an error message with structured properties
        /// </summary>
        void Error(string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs an error with exception and structured properties
        /// </summary>
        void Error(Exception exception, string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs a critical error with structured properties
        /// </summary>
        void Critical(string messageTemplate, params object?[] propertyValues);

        /// <summary>
        /// Logs a critical error with exception and structured properties
        /// </summary>
        void Critical(Exception exception, string messageTemplate, params object?[] propertyValues);
    }
}
