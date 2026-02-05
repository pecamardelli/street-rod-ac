namespace Street_Rod_AC.Services.Talk
{
    /// <summary>
    /// Service for generating opponent dialogue messages
    /// </summary>
    public interface ITalkService
    {
        /// <summary>
        /// Get a contextual message from an opponent
        /// </summary>
        Task<string> GetMessageAsync(TalkContext context);

        /// <summary>
        /// Check if the service is available (for AI services)
        /// </summary>
        bool IsAvailable { get; }
    }
}
