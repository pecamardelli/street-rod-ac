using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.Newspaper
{
    /// <summary>One of the player's cars in the paper, with the buyer on the phone if there is one</summary>
    public sealed class PlayerCarAdViewModel(string carName, string askingDisplay, string offerDisplay, bool hasOffer,
        RelayCommand acceptCommand, RelayCommand declineCommand)
    {
        public string CarName { get; } = carName;
        public string AskingDisplay { get; } = askingDisplay;
        public string OfferDisplay { get; } = offerDisplay;
        public bool HasOffer { get; } = hasOffer;
        public RelayCommand AcceptCommand { get; } = acceptCommand;
        public RelayCommand DeclineCommand { get; } = declineCommand;
    }
}
