namespace Street_Rod_AC.Models.GameState
{
    /// <summary>One thing the street is talking about: a rival bought a car, put a blower on, went broke, came back</summary>
    public class StreetTalkItem
    {
        /// <summary>Game time</summary>
        public DateTime Date { get; set; }

        public string Text { get; set; } = string.Empty;

        public StreetTalkItem() { }

        public StreetTalkItem(DateTime date, string text)
        {
            Date = date;
            Text = text;
        }
    }
}
