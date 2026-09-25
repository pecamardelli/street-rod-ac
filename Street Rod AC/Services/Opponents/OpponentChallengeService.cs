using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Parts.Cars;
using Street_Rod_AC.Services.Catalog;
using Street_Rod_AC.Services.Market;
using Street_Rod_AC.Services.Parts;

namespace Street_Rod_AC.Services.Opponents
{
    /// <summary>
    /// Service for handling opponent challenge logic (acceptance/decline decisions)
    /// </summary>
    public class OpponentChallengeService : IOpponentChallengeService
    {
        private readonly Random _random;
        private readonly IAppLogger _logger;
        private readonly IContentCatalogRepository _catalogRepository;
        private readonly ICarProfileRepository _carProfileRepository;
        private readonly ICarPartsService? _partsService;

        /// <param name="partsService">Weighs what has been done to the two engines in a pink-slip challenge; without
        /// it the cars are valued as if they had their factory engines</param>
        public OpponentChallengeService(
            IContentCatalogRepository catalogRepository,
            ICarProfileRepository carProfileRepository,
            ICarPartsService? partsService = null)
        {
            _random = new Random();
            _logger = AppLoggerFactory.CreateLogger("OpponentChallenge");
            _catalogRepository = catalogRepository;
            _carProfileRepository = carProfileRepository;
            _partsService = partsService;
        }

        /// <summary>
        /// For testing races without being turned down: every challenge is accepted. Off unless a debug build
        /// or a test turns it on; it never ships on.
        /// </summary>
        public bool AlwaysAccept { get; set; }

        public bool CanRace(Car car)
        {
            try
            {
                var groupOf = _partsService is { IsAvailable: true } parts ? CarCondition.Groups(parts.Catalog) : null;
                return CarCondition.WhyCannotRace(car, groupOf).Count == 0;
            }
            catch (Exception ex)
            {
                // Not knowing is no reason to keep a racer from the tables
                _logger.Warning("Could not tell whether the {Car} can race: {Error}", car.DefinitionId, ex.Message);
                return true;
            }
        }

        /// <summary>
        /// Evaluate whether an opponent accepts a challenge
        /// </summary>
        public ChallengeResponse EvaluateChallenge(
            Opponent opponent,
            Player player,
            Car playerCar,
            Car opponentCar,
            bool isPinkSlip,
            decimal cashWager = 0,
            double pinkSlipFactor = 1.0)
        {
            _logger.Information("Evaluating challenge: {OpponentName} vs {PlayerName}, PinkSlip: {IsPinkSlip}, Wager: {Wager}",
                opponent.Name, player.Name, isPinkSlip, cashWager);

            if (AlwaysAccept)
            {
                _logger.Warning("Challenge acceptance bypassed for testing - opponent automatically accepts");
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = $"{opponent.Name}: You're on! Let's race!",
                    DeclineReason = null
                };
            }

            if (!CanRace(opponentCar))
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = "My car's laid up. Come back when it's running.",
                    DeclineReason = ChallengeDeclineReason.CarNotAvailable
                };
            }

            // The King races for one thing, and he takes on anybody who has made it to his table
            if (opponent.IsKing)
            {
                return isPinkSlip
                    ? new ChallengeResponse { Accepted = true, Message = "Pink slips it is. Hope you said goodbye to that car." }
                    : new ChallengeResponse
                    {
                        Accepted = false,
                        Message = "I don't race for pocket money. Put your pink slip on the table, or go home.",
                        DeclineReason = ChallengeDeclineReason.NotInterested
                    };
            }

            // Get car definitions for value comparison
            var playerCarDef = _catalogRepository.GetCar(playerCar.DefinitionId);
            var opponentCarDef = _catalogRepository.GetCar(opponentCar.DefinitionId);

            if (playerCarDef == null || opponentCarDef == null)
            {
                _logger.Warning("Could not find car definitions for challenge evaluation");
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = "I need to check my car first. Try again later.",
                    DeclineReason = ChallengeDeclineReason.CarNotAvailable
                };
            }

            // Evaluate based on bet type
            if (isPinkSlip)
            {
                return EvaluatePinkSlipChallenge(opponent, player, playerCar, playerCarDef, opponentCar, opponentCarDef, pinkSlipFactor);
            }
            else
            {
                return EvaluateCashChallenge(opponent, player, cashWager);
            }
        }

        /// <summary>
        /// Evaluate a pink slip challenge
        /// </summary>
        private ChallengeResponse EvaluatePinkSlipChallenge(
            Opponent opponent,
            Player player,
            Car playerCar,
            CarDefinition playerCarDef,
            Car opponentCar,
            CarDefinition opponentCarDef,
            double pinkSlipFactor)
        {
            // What each car is worth as it stands: model, condition, engine. The same sum the market uses.
            var playerCarValue = ValueOf(playerCar, playerCarDef);
            var opponentCarValue = ValueOf(opponentCar, opponentCarDef);

            // A car worth nothing (no price for its model, a wreck) is nothing to stake either way. Without a
            // value to compare against, the ratio would divide by zero.
            if (opponentCarValue <= 0 || playerCarValue <= 0)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetCarValueMismatchMessage(opponent, isPlayerCarCheaper: playerCarValue <= 0),
                    DeclineReason = ChallengeDeclineReason.CarValueMismatch
                };
            }

            var valueRatio = (double)(playerCarValue / opponentCarValue);

            // Check car value mismatch (won't risk expensive car for cheap car)
            if (valueRatio < 0.6)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetCarValueMismatchMessage(opponent, isPlayerCarCheaper: true),
                    DeclineReason = ChallengeDeclineReason.CarValueMismatch
                };
            }

            // Calculate acceptance chance based on multiple factors
            var acceptanceChance = 0.5; // Base 50%

            // Reputation factor
            var reputationDiff = opponent.Stats.Reputation - player.Stats.Reputation;
            if (reputationDiff > 20)
            {
                // Opponent is much better - more confident, more likely to accept
                acceptanceChance += 0.2;
            }
            else if (reputationDiff < -20)
            {
                // Player is much better - opponent less confident
                acceptanceChance -= 0.2;
            }

            // Aggression factor (high aggression = more willing to risk)
            acceptanceChance += (opponent.Aggression - 50) * 0.004; // ±0.2 max

            // Age factor (younger = more reckless)
            if (opponent.Age < 26)
            {
                acceptanceChance += 0.15;
            }
            else if (opponent.Age > 45)
            {
                acceptanceChance -= 0.1;
            }

            // Skill confidence factor
            var skillDiff = opponent.Skill - 90; // How much better than average
            acceptanceChance += skillDiff * 0.01; // ±0.1 max

            // Pink slip history factor
            if (opponent.Stats.PinkSlipsLost > opponent.Stats.PinkSlipsWon)
            {
                // Bad pink slip history makes them more cautious
                acceptanceChance -= 0.15;
            }

            // Car value ratio factor (more willing if player's car is worth more)
            if (valueRatio > 1.2)
            {
                acceptanceChance += 0.15;
            }

            // Where pink slips are rare (the game's difficulty), rivals are warier of staking their car
            acceptanceChance += (pinkSlipFactor - 1) * 0.2;

            // Clamp to 5-95%
            acceptanceChance = Math.Clamp(acceptanceChance, 0.05, 0.95);

            _logger.Information("Pink slip acceptance chance for {Name}: {Chance:P0}", opponent.Name, acceptanceChance);

            // Make decision
            if (_random.NextDouble() < acceptanceChance)
            {
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = GetAcceptanceMessage(opponent)
                };
            }
            else
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetPinkSlipDeclineMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.TooRisky
                };
            }
        }

        /// <summary>
        /// Evaluate a cash wager challenge
        /// </summary>
        private ChallengeResponse EvaluateCashChallenge(
            Opponent opponent,
            Player player,
            decimal cashWager)
        {
            // Check if opponent has any money at all (safety check)
            if (opponent.Money <= 0)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetInsufficientFundsMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.InsufficientFunds
                };
            }

            // Check if opponent has enough money
            if (cashWager > opponent.Money)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetInsufficientFundsMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.InsufficientFunds
                };
            }

            // Check if bet is too high relative to their bankroll
            var wagerRatio = (double)(cashWager / opponent.Money);
            if (wagerRatio > 0.5 && opponent.Aggression < 60)
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetBetTooHighMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.BetTooHigh
                };
            }

            // Check reputation requirements
            var reputationDiff = opponent.Stats.Reputation - player.Stats.Reputation;

            // Very low player reputation - opponent might refuse
            if (player.Stats.Reputation < 30 && reputationDiff > 25)
            {
                if (_random.NextDouble() < 0.4) // 40% chance to refuse rookies
                {
                    return new ChallengeResponse
                    {
                        Accepted = false,
                        Message = GetReputationTooLowMessage(opponent),
                        DeclineReason = ChallengeDeclineReason.ReputationTooLow
                    };
                }
            }

            // Calculate acceptance chance
            var acceptanceChance = 0.7; // Base 70% for cash races (less risky than pink slips)

            // Aggression factor
            acceptanceChance += (opponent.Aggression - 50) * 0.003; // ±0.15 max

            // Wager ratio factor (higher wagers = more caution unless aggressive)
            if (wagerRatio > 0.3)
            {
                acceptanceChance -= 0.2;
                // But aggressive opponents don't mind high stakes
                if (opponent.Aggression > 70)
                {
                    acceptanceChance += 0.15;
                }
            }

            // Age factor
            if (opponent.Age < 26)
            {
                acceptanceChance += 0.1;
            }

            // Recent form factor
            if (opponent.Stats.Races >= 3)
            {
                var recentWins = opponent.Stats.Wins;
                var recentRaces = opponent.Stats.Races;
                if (recentRaces > 0)
                {
                    var recentWinRate = (double)recentWins / recentRaces;
                    if (recentWinRate > 0.7)
                    {
                        // On a winning streak, more confident
                        acceptanceChance += 0.1;
                    }
                    else if (recentWinRate < 0.3)
                    {
                        // On a losing streak, less confident
                        acceptanceChance -= 0.15;
                    }
                }
            }

            // Clamp to 10-95%
            acceptanceChance = Math.Clamp(acceptanceChance, 0.1, 0.95);

            _logger.Information("Cash challenge acceptance chance for {Name}: {Chance:P0}", opponent.Name, acceptanceChance);

            // Make decision
            if (_random.NextDouble() < acceptanceChance)
            {
                return new ChallengeResponse
                {
                    Accepted = true,
                    Message = GetAcceptanceMessage(opponent)
                };
            }
            else
            {
                return new ChallengeResponse
                {
                    Accepted = false,
                    Message = GetGenericDeclineMessage(opponent),
                    DeclineReason = ChallengeDeclineReason.NotInterested
                };
            }
        }

        /// <summary>The car's worth by <see cref="CarValuation"/>; a model without a price is worth what was paid for the car</summary>
        private decimal ValueOf(Car car, CarDefinition definition) =>
            CarValuation.WorthOf(car, _carProfileRepository.GetProfile, _ => definition, _partsService);

        #region Message Generation

        private string GetAcceptanceMessage(Opponent opponent)
        {
            var messages = opponent.Aggression switch
            {
                >= 70 => new[]
                {
                    "You're on! Let's see what you got!",
                    "Finally, some action! Let's race!",
                    "Hope you're ready to lose!",
                    "This'll be fun. Let's go!"
                },
                <= 40 => new[]
                {
                    "Alright, I accept your challenge.",
                    "Let's do this. Good luck.",
                    "Fair enough. See you at the track.",
                    "Okay, I'll race you."
                },
                _ => new[]
                {
                    "You're on. Let's race.",
                    "Alright, let's settle this on the track.",
                    "Challenge accepted. Let's go.",
                    "Deal. May the best racer win."
                }
            };

            return messages[_random.Next(messages.Length)];
        }

        private string GetPinkSlipDeclineMessage(Opponent opponent)
        {
            var messages = new[]
            {
                "Pink slips? That's too much for me. Not tonight.",
                "I'm not risking my ride. Lower the stakes.",
                "My car means too much to me. Cash only.",
                "That's a bet I can't afford to lose. Pass."
            };

            return messages[_random.Next(messages.Length)];
        }

        private string GetCarValueMismatchMessage(Opponent opponent, bool isPlayerCarCheaper)
        {
            if (isPlayerCarCheaper)
            {
                var messages = new[]
                {
                    "Pink slips? Your bucket of bolts isn't worth my ride.",
                    "I'm not risking my car for that heap. Bring something better.",
                    "Come back with a car that's actually worth something.",
                    "That's not a fair trade. My car's worth way more than yours."
                };
                return messages[_random.Next(messages.Length)];
            }
            else
            {
                var messages = new[]
                {
                    "Your car's too nice. I can't match that stake.",
                    "That's out of my league. Find someone with a better ride.",
                    "I'd love to, but my car isn't worth that much."
                };
                return messages[_random.Next(messages.Length)];
            }
        }

        private string GetInsufficientFundsMessage(Opponent opponent)
        {
            var messages = new[]
            {
                "That's more than I've got on me. Lower the stakes.",
                "I don't have that kind of cash. Make it smaller.",
                "I'm not that flush right now. Can't cover that bet.",
                "That's too rich for my blood. I don't have it."
            };

            return messages[_random.Next(messages.Length)];
        }

        private string GetBetTooHighMessage(Opponent opponent)
        {
            var messages = new[]
            {
                "That's way too steep for me. Not worth the risk.",
                "Lower the bet and maybe we can talk.",
                "That's not a bet, that's my rent money. No thanks.",
                "I'm not gambling that much. Make it reasonable."
            };

            return messages[_random.Next(messages.Length)];
        }

        private string GetReputationTooLowMessage(Opponent opponent)
        {
            var messages = new[]
            {
                "Come back when you've proven yourself, rookie.",
                "I don't race amateurs. Win a few more races first.",
                "Beat some other racers before challenging me.",
                "You need more street cred before I'll race you."
            };

            return messages[_random.Next(messages.Length)];
        }

        private string GetGenericDeclineMessage(Opponent opponent)
        {
            var messages = new[]
            {
                "Not feeling it tonight. Maybe another time.",
                "I'm gonna pass on this one. Ask me later.",
                "Nah, I'm not in the mood right now.",
                "Maybe later. I need a break."
            };

            return messages[_random.Next(messages.Length)];
        }

        #endregion
    }
}
