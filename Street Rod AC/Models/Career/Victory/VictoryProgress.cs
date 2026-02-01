namespace Street_Rod_AC.Models.Career.Victory
{
    /// <summary>
    /// Represents the current progress toward a victory condition
    /// </summary>
    public class VictoryProgress
    {
        /// <summary>
        /// Overall progress percentage (0-100)
        /// </summary>
        public float Percentage { get; set; }

        /// <summary>
        /// Human-readable description of current progress
        /// </summary>
        public string ProgressDescription { get; set; } = string.Empty;

        /// <summary>
        /// Steps/requirements that have been completed
        /// </summary>
        public List<string> CompletedSteps { get; set; } = [];

        /// <summary>
        /// Steps/requirements that still need to be completed
        /// </summary>
        public List<string> RemainingSteps { get; set; } = [];

        /// <summary>
        /// Create progress indicating not yet started
        /// </summary>
        public static VictoryProgress NotStarted(string description)
        {
            return new VictoryProgress
            {
                Percentage = 0,
                ProgressDescription = description,
                RemainingSteps = [description]
            };
        }

        /// <summary>
        /// Create progress indicating complete
        /// </summary>
        public static VictoryProgress Complete(string description)
        {
            return new VictoryProgress
            {
                Percentage = 100,
                ProgressDescription = description,
                CompletedSteps = [description]
            };
        }
    }
}
