using Street_Rod_AC.Models.Race;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Dialogs.Victory
{
    /// <summary>
    /// The game is won: the path that won it and the career sheet. The player keeps racing on the same save
    /// (it stays marked as won) or goes back to the main menu; the save is written before this comes up.
    /// </summary>
    public class VictoryDialogViewModel : BaseDialogViewModel
    {
        private readonly DialogService _dialogService;
        private readonly Action _toMainMenu;

        public VictoryDialogViewModel(DialogService dialogService, VictoryCard card, Action toMainMenu)
        {
            _dialogService = dialogService;
            _toMainMenu = toMainMenu;
            VictoryName = card.VictoryName;
            Description = card.Description;
            Stats = card.Stats;
            Footnote = card.Footnote;

            KeepRacingCommand = new RelayCommand(() => _dialogService.CloseDialog());
            MainMenuCommand = new RelayCommand(OnMainMenu);
        }

        public string VictoryName { get; }
        public string Description { get; }
        public IReadOnlyList<VictoryStat> Stats { get; }
        public string Footnote { get; }
        public bool HasFootnote => !string.IsNullOrWhiteSpace(Footnote);

        public RelayCommand KeepRacingCommand { get; }
        public RelayCommand MainMenuCommand { get; }

        // Closed first: the main menu comes up without the dialog over it
        private void OnMainMenu()
        {
            _dialogService.CloseDialog();
            _toMainMenu();
        }
    }
}
