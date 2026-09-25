using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Dialogs
{
    /// <summary>
    /// Shows a <see cref="PlayerMessage"/> the way it is meant to be seen: a timeslip as a timeslip, the game
    /// won as the victory screen, anything else as text. The dialog service queues them, one at a time.
    /// </summary>
    public static class PlayerMessageDialogs
    {
        /// <param name="toMainMenu">Where the victory screen's Main Menu button goes</param>
        public static void Show(DialogService dialogService, PlayerMessage message, Action toMainMenu)
        {
            if (message.Timeslip is { } slip)
                dialogService.ShowDialog(new Timeslip.TimeslipDialogViewModel(dialogService, slip));
            else if (message.Victory is { } victory)
                dialogService.ShowDialog(new Victory.VictoryDialogViewModel(dialogService, victory, toMainMenu));
            else
                dialogService.ShowDialog(new Information.InformationDialogViewModel(dialogService, message.Text, message.Title));
        }
    }
}
