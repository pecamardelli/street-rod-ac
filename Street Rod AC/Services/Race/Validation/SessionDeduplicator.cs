using Street_Rod_AC.Logging;

namespace Street_Rod_AC.Services.Race.Validation
{
    /// <summary>
    /// Service for checking if a race session has already been processed
    /// Uses session_id as the primary deduplication key
    /// </summary>
    public class SessionDeduplicator
    {
        private readonly IRaceSessionRepository _sessionRepository;
        private readonly IAppLogger _logger;

        public SessionDeduplicator(IRaceSessionRepository sessionRepository)
        {
            _sessionRepository = sessionRepository;
            _logger = AppLoggerFactory.CreateLogger(LogCategory.RaceIngestion);
        }

        /// <summary>
        /// Check if a session ID has already been processed in the save
        /// </summary>
        /// <param name="saveName">The save the result would be applied to</param>
        /// <param name="sessionId">Session UUID from race result</param>
        /// <returns>True if already processed, false if new</returns>
        /// <exception cref="Exception">
        /// The save could not be read. Taking that for "not processed" would apply a result twice; the caller
        /// leaves the file where it is and tries again later.
        /// </exception>
        public async Task<bool> IsProcessedAsync(string saveName, string sessionId)
        {
            try
            {
                var exists = await _sessionRepository.IsProcessedAsync(saveName, sessionId);

                if (exists)
                {
                    _logger.Debug("Session {SessionId} has already been processed (duplicate)", sessionId);
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking if session {SessionId} is processed", sessionId);
                throw;
            }
        }
    }
}
