namespace Street_Rod_AC.Models.GameState
{
    /// <summary>
    /// A piece in the newspaper, written from a race when it was settled (<see cref="Services.News.NewsWriter"/>):
    /// the paper prints the latest, the weightiest first
    /// </summary>
    public class NewsArticle
    {
        /// <summary>The day it happened, in game time</summary>
        public DateTime Date { get; set; }

        public string Headline { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        /// <summary>How much of a story it is: the player's pink slips and the King lead the page, a rival's wreck is a filler</summary>
        public int Weight { get; set; }

        /// <summary>The player is in it</summary>
        public bool AboutPlayer { get; set; }
    }
}
