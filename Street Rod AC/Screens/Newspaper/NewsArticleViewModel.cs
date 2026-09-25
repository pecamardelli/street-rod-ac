using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Screens.Newspaper
{
    /// <summary>A piece on the paper's race pages; the first is the lead story, in bigger type</summary>
    public sealed class NewsArticleViewModel(NewsArticle article, DateTime today, bool isLead)
    {
        public string Headline { get; } = article.Headline;
        public string Body { get; } = article.Body;
        public bool IsLead { get; } = isLead;

        public string DateDisplay { get; } = (today.Date - article.Date.Date).Days switch
        {
            <= 0 => "Today",
            1 => "Yesterday",
            _ => article.Date.ToString("dddd, MMMM d")
        };
    }
}
