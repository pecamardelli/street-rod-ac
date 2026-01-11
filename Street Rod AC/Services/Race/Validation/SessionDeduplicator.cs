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
        /// Check if a session ID has already been processed
        /// </summary>
        /// <param name="sessionId">Session UUID from race result</param>
        /// <returns>True if already processed, false if new</returns>
        public async Task<bool> IsProcessedAsync(string sessionId)
        {
            try
            {
                var exists = await _sessionRepository.IsProcessedAsync(sessionId);

                if (exists)
                {
                    _logger.Debug("Session {SessionId} has already been processed (duplicate)", sessionId);
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking if session {SessionId} is processed", sessionId);
                // On error, assume not processed to avoid data loss
                return false;
            }
        }
    }
}
