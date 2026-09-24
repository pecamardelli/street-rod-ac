using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.ViewModels;

namespace Street_Rod_AC.Screens.NewGame
{
    /// <summary>
    /// The difficulty a new career starts on: one of the presets, whose figures the player may then adjust (the
    /// difficulty then reads Custom). The multipliers are shown and set in percent.
    /// </summary>
    public class GameRulesEditor : ObservableObject
    {
        private GameRules _rules = GameRules.For(Difficulty.Normal);

        public GameRulesEditor()
        {
            PickCommand = new RelayCommand<string>(name =>
            {
                if (Enum.TryParse<Difficulty>(name, out var difficulty) && difficulty != Difficulty.Custom) Pick(difficulty);
            });
        }

        /// <summary>Picks a preset: Easy, Normal or Hard</summary>
        public RelayCommand<string> PickCommand { get; }

        public IReadOnlyList<PinkSlipFrequency> PinkSlipFrequencies { get; } = Enum.GetValues<PinkSlipFrequency>();

        /// <summary>The rules as they stand, a copy for the new save</summary>
        public GameRules Build() => _rules.Copy();

        public Difficulty Difficulty => _rules.Difficulty;

        public bool IsEasy => Difficulty == Difficulty.Easy;
        public bool IsNormal => Difficulty == Difficulty.Normal;
        public bool IsHard => Difficulty == Difficulty.Hard;

        public string Summary => Difficulty switch
        {
            Difficulty.Easy => "Cheaper cars and parts, bigger prizes, cars that last, rivals who take it easy.",
            Difficulty.Hard => "Dearer cars and parts, smaller prizes, cars that wear fast, rivals who push, and more pink slips.",
            Difficulty.Custom => "Your own mix.",
            _ => "The street as it is."
        };

        public int CarPricePercent
        {
            get => Percent(_rules.CarPriceMultiplier);
            set => Change(() => _rules.CarPriceMultiplier = value / 100.0);
        }

        public int PartPricePercent
        {
            get => Percent(_rules.PartPriceMultiplier);
            set => Change(() => _rules.PartPriceMultiplier = value / 100.0);
        }

        public int PrizePercent
        {
            get => Percent(_rules.RacePrizeMultiplier);
            set => Change(() => _rules.RacePrizeMultiplier = value / 100.0);
        }

        public int WearPercent
        {
            get => Percent(_rules.CarWearMultiplier);
            set => Change(() => _rules.CarWearMultiplier = value / 100.0);
        }

        public int OpponentSkill
        {
            get => _rules.OpponentSkillModifier;
            set => Change(() => _rules.OpponentSkillModifier = value);
        }

        public int OpponentAggression
        {
            get => _rules.OpponentAggressionModifier;
            set => Change(() => _rules.OpponentAggressionModifier = value);
        }

        public PinkSlipFrequency PinkSlipFrequency
        {
            get => _rules.PinkSlipFrequency;
            set => Change(() => _rules.PinkSlipFrequency = value);
        }

        public bool RaceSimulationEnabled
        {
            get => _rules.RaceSimulationEnabled;
            set => Change(() => _rules.RaceSimulationEnabled = value);
        }

        public bool SeasonalRacingEnabled
        {
            get => _rules.SeasonalRacingEnabled;
            set => Change(() => _rules.SeasonalRacingEnabled = value);
        }

        public bool MarketRefreshEnabled
        {
            get => _rules.MarketRefreshEnabled;
            set => Change(() => _rules.MarketRefreshEnabled = value);
        }

        public void Pick(Difficulty difficulty)
        {
            _rules = GameRules.For(difficulty);
            OnPropertyChanged(string.Empty);
        }

        private static int Percent(double multiplier) => (int)Math.Round(multiplier * 100);

        /// <summary>A figure set by hand: once it differs from the preset's, the difficulty is Custom</summary>
        private void Change(Action set)
        {
            set();
            _rules.Difficulty = MatchingPreset() ?? Difficulty.Custom;
            OnPropertyChanged(string.Empty);
        }

        private Difficulty? MatchingPreset()
        {
            foreach (var preset in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                var p = GameRules.For(preset);
                if (Percent(p.CarPriceMultiplier) == CarPricePercent && Percent(p.PartPriceMultiplier) == PartPricePercent &&
                    Percent(p.RacePrizeMultiplier) == PrizePercent && Percent(p.CarWearMultiplier) == WearPercent &&
                    p.OpponentSkillModifier == OpponentSkill && p.OpponentAggressionModifier == OpponentAggression &&
                    p.PinkSlipFrequency == PinkSlipFrequency && p.RaceSimulationEnabled == RaceSimulationEnabled &&
                    p.SeasonalRacingEnabled == SeasonalRacingEnabled && p.MarketRefreshEnabled == MarketRefreshEnabled)
                {
                    return preset;
                }
            }

            return null;
        }
    }
}
