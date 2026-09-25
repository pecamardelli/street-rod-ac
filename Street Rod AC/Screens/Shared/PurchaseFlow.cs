using Street_Rod_AC.Dialogs;
using Street_Rod_AC.Dialogs.Confirmation;
using Street_Rod_AC.Dialogs.Information;
using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Screens.UsedCarMarket;
using Street_Rod_AC.Services.Market;

namespace Street_Rod_AC.Screens.Shared
{
    /// <summary>
    /// Buying a car on screen, the same wherever it is for sale (the paper's ads or a dealer's lot): the
    /// question with the car's details, the purchase, and what came of it. Every dialog goes through the
    /// dialog service's queue, so a save warning is read before the success message and not replaced by it.
    /// </summary>
    public sealed class PurchaseFlow
    {
        private readonly DialogService _dialogService;
        private readonly ICarPurchaseService _purchaseService;
        private readonly IAppLogger _logger;

        public PurchaseFlow(DialogService dialogService, ICarPurchaseService purchaseService, IAppLogger logger)
        {
            _dialogService = dialogService;
            _purchaseService = purchaseService;
            _logger = logger;
        }

        /// <summary>The question put to the player before a car is bought</summary>
        public static string ConfirmationMessage(UsedCarListingViewModel car, decimal bankroll)
        {
            var def = car.CarDefinition;
            return $"Purchase this {def.Brand} {def.Name}?\n\n" +
                   $"Year: {def.Year ?? 0}\n" +
                   $"Price: ${car.Listing.Price:N0}\n" +
                   $"Condition: {car.ConditionLabel}\n" +
                   $"Mileage: {car.Listing.Mileage:N0} km\n" +
                   $"History: {car.HistoryDisplay}\n" +
                   $"Dealer: {car.DealerName}\n\n" +
                   $"Your bankroll: ${bankroll:N0}";
        }

        /// <summary>
        /// Asks, and buys when the player says yes. <paramref name="onFinished"/> runs after the result has
        /// been shown, for the screen to move on or reload; it is not called when the player said no, nor when
        /// the purchase failed with an error (nothing was bought then).
        /// </summary>
        public void Offer(GameState gameState, UsedCarListingViewModel car, Action<PurchaseResult> onFinished)
        {
            _logger.Information("Player attempting to purchase car {CarId} for ${Price}", car.CarDefinition.Id, car.Listing.Price);

            _dialogService.ShowDialog(new ConfirmationDialogViewModel(
                _dialogService,
                ConfirmationMessage(car, gameState.Player.Money),
                "Purchase Car?",
                confirmed =>
                {
                    if (confirmed) Complete(gameState, car, onFinished);
                }));
        }

        private async void Complete(GameState gameState, UsedCarListingViewModel car, Action<PurchaseResult> onFinished)
        {
            PurchaseResult result;
            try
            {
                // The only await that can throw (the car's factory engine) comes before anything changes hands
                result = await _purchaseService.PurchaseAsync(gameState, car.Listing, car.CarDefinition);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "The purchase of {CarId} failed", car.CarDefinition.Id);
                _dialogService.ShowDialog(new InformationDialogViewModel(
                    _dialogService,
                    $"The purchase could not be completed, and nothing was bought:\n\n{ex.Message}",
                    "Purchase Failed"));
                return;
            }

            try
            {
                foreach (var (title, message) in ResultDialogs(result))
                {
                    _dialogService.ShowDialog(new InformationDialogViewModel(_dialogService, message, title));
                }

                onFinished(result);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not finish the purchase of {CarId} on screen", car.CarDefinition.Id);
            }
        }

        /// <summary>What the player is told after a purchase, in the order they read it</summary>
        public static IReadOnlyList<(string Title, string Message)> ResultDialogs(PurchaseResult result)
        {
            if (!result.Succeeded)
            {
                var title = result.Outcome == PurchaseOutcome.NotEnoughMoney ? "Insufficient Funds" : "Car Unavailable";
                return new[] { (title, result.Message) };
            }

            var dialogs = new List<(string, string)>();
            if (result.SaveFailed)
            {
                dialogs.Add(("Save Warning", "The purchase was successful but failed to save the game. Please save manually."));
            }

            dialogs.Add(("Purchase Successful", result.Message));
            return dialogs;
        }
    }
}
