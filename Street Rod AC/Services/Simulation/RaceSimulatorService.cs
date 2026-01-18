using Street_Rod_AC.Logging;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Simulation
{
    /// <summary>
    /// Simulates races between AI opponents when the player is not around.
    /// Handles challenge acceptance, win probability, car wear, and prize distribution.
    /// </summary>
    public class RaceSimulatorService
    {
        private readonly IAppLogger _logger;
        private readonly Random _random;

        public RaceSimulatorService()
        {
            _logger = AppLoggerFactory.CreateLogger("RaceSimulator");
            _random = new Random();
        }

        /// <summary>
        /// Simulate all AI races for a given day
        /// </summary>
        public RaceSimulationResult SimulateDay(GameState gameState, DateTime currentDate)
        {
            _logger.Information("Starting race simulation for {Date}", currentDate);

            var result = new RaceSimulationResult();
            var racers = GetEligibleRacers(gameState);

            if (racers.Count < 2)
            {
                _logger.Information("Not enough eligible racers to simulate races");
                return result;
            }

            // Sort racers by car HP (best cars challenge first)
            var racersByPower = racers
                .Where(r => r.Cars.Count > 0 && GetCarHP(r.Cars[0]) > 0)
                .OrderByDescending(r => GetCarHP(r.Cars[0]))
                .ToList();

            // Apply seasonal modifier to max races
            var seasonalModifier = GetSeasonalModifier(currentDate.Month);
            var maxRaces = (int)Math.Ceiling(racersByPower.Count * seasonalModifier);

            _logger.Information("Eligible racers: {Count}, Max races for day: {MaxRaces}",
                racersByPower.Count, maxRaces);

            var usedRacers = new HashSet<string>();

            while (result.TotalRaces < maxRaces && racersByPower.Count >= 2)
            {
                // Get challenger (highest HP car not yet used)
                var challenger = racersByPower.FirstOrDefault(r => !usedRacers.Contains(r.Name));
                if (challenger == null) break;

                usedRacers.Add(challenger.Name);

                // Find an opponent who accepts the challenge
                var opponent = FindOpponent(challenger, racersByPower, usedRacers);
                if (opponent == null)
                {
                    _logger.Debug("{Challenger} couldn't find an opponent", challenger.Name);
                    continue;
                }

                usedRacers.Add(opponent.Name);

                // Simulate the race
                var raceResult = SimulateRace(challenger, opponent, gameState);
                result.Races.Add(raceResult);
                result.TotalRaces++;

                _logger.Information("Race {Number}: {Challenger} vs {Opponent} - Winner: {Winner} ({RaceType}, Prize: ${Prize})",
                    result.TotalRaces, challenger.Name, opponent.Name,
                    raceResult.WinnerName, raceResult.RaceType, raceResult.Prize);

                // Update racer eligibility after race
                UpdateRacerEligibility(challenger, racersByPower, gameState);
                UpdateRacerEligibility(opponent, racersByPower, gameState);
            }

            _logger.Information("Race simulation complete. Total races: {TotalRaces}", result.TotalRaces);
            return result;
        }

        private List<Racer> GetEligibleRacers(GameState gameState)
        {
            return gameState.Racers.ReadyToRace.Values
                .Where(r => r.Type == RacerType.AI && ShouldRaceAgain(r))
                .ToList();
        }

        private Racer? FindOpponent(Racer challenger, List<Racer> racers, HashSet<string> usedRacers)
        {
            var challengerCar = challenger.Cars.FirstOrDefault();
            if (challengerCar == null) return null;

            foreach (var potential in racers.Where(r => r.Name != challenger.Name && !usedRacers.Contains(r.Name)))
            {
                var potentialCar = potential.Cars.FirstOrDefault();
                if (potentialCar == null) continue;

                if (AcceptChallenge(challenger, potential, challengerCar, potentialCar))
                {
                    return potential;
                }
            }

            return null;
        }

        /// <summary>
        /// Determines if a target racer accepts a challenge based on various factors
        /// </summary>
        private bool AcceptChallenge(Racer challenger, Racer target, Car challengerCar, Car targetCar)
        {
            var challengerHp = GetCarHP(challengerCar);
            var targetHp = GetCarHP(targetCar);

            if (challengerHp <= 0 || targetHp <= 0) return false;

            // Base probability from HP ratio
            var hpRatio = Math.Min(challengerHp, targetHp) / Math.Max(challengerHp, targetHp);
            var baseProbability = Math.Pow(hpRatio, 2);

            // Skill confidence modifier
            var challengerSkill = GetRacerSkill(challenger);
            var targetSkill = GetRacerSkill(target);
            var skillDiff = targetSkill - challengerSkill;
            var skillModifier = 1.0 + (skillDiff / 200.0);

            // Reputation modifier - cocky racers accept more challenges
            var reputationModifier = 1.0 + (target.Stats.Wins * 0.01);

            // Money motivation - broke racers are more desperate
            var moneyPressure = target.Money < 500 ? 1.3 : 1.0;

            // Car condition factor
            var targetCarCondition = GetOverallCarCondition(targetCar);
            var conditionModifier = targetCarCondition > 0.5 ? 1.0 : 0.6;

            // Final probability
            var probability = baseProbability * skillModifier * reputationModifier * moneyPressure * conditionModifier;
            probability = Math.Clamp(probability, 0.1, 0.9);

            return _random.NextDouble() < probability;
        }

        /// <summary>
        /// Simulate a single race between two racers
        /// </summary>
        private SimulatedRaceResult SimulateRace(Racer racer1, Racer racer2, GameState gameState)
        {
            var car1 = racer1.Cars[0];
            var car2 = racer2.Cars[0];

            // Determine race type
            var isRoadRace = ChooseRaceType(racer1, racer2, car1, car2);
            var raceType = isRoadRace ? "Road" : "Drag";

            // Calculate prize
            var prize = CalculatePrize(racer1, racer2, car1, car2, isRoadRace);
            var isPinkSlip = prize < 0;

            // Determine winner
            var racer1WinProbability = CalculateWinProbability(racer1, racer2, car1, car2);
            var racer1Wins = _random.NextDouble() < racer1WinProbability;

            var winner = racer1Wins ? racer1 : racer2;
            var loser = racer1Wins ? racer2 : racer1;
            var winnerCar = racer1Wins ? car1 : car2;
            var loserCar = racer1Wins ? car2 : car1;

            // Apply car wear
            ApplyRaceWear(car1, isRoadRace, racer1Wins);
            ApplyRaceWear(car2, isRoadRace, !racer1Wins);

            // Handle race results
            if (isRoadRace)
            {
                winner.Stats.Wins++;
                loser.Stats.Losses++;
            }
            winner.Stats.Races++;
            loser.Stats.Races++;

            decimal actualPrize = 0;
            Car? carWon = null;

            if (isPinkSlip)
            {
                // Pink slip race - winner gets loser's car
                actualPrize = loserCar.PurchasePrice;
                winner.Stats.PinkSlipsWon++;
                winner.Stats.TotalEarnings += actualPrize;
                loser.Stats.PinkSlipsLost++;

                // Transfer car
                carWon = loserCar;
                loser.Cars.Remove(loserCar);

                // Put car on market instead of giving to winner (simpler)
                gameState.UsedCarMarket.Add(new UsedCarListing
                {
                    CarDefinitionId = loserCar.DefinitionId,
                    SkinId = loserCar.SkinId,
                    Mileage = (int)loserCar.OdometerKM,
                    Condition = (float)GetOverallCarCondition(loserCar),
                    Price = loserCar.PurchasePrice * 0.8m,
                    ListedDate = gameState.Date,
                    IsSold = false,
                    DealerLocation = "street"
                });

                // Check if loser has no more cars
                if (loser.Cars.Count == 0)
                {
                    gameState.Racers.MoveRacer(loser.Name, RacerStatus.Inactive);
                    _logger.Information("{Loser} lost their last car and is now inactive", loser.Name);
                }
            }
            else if (prize > 0)
            {
                // Cash prize
                actualPrize = (decimal)prize;
                winner.Stats.TotalEarnings += actualPrize;
                winner.Money += actualPrize;
                loser.Stats.TotalLosses += actualPrize;
                loser.Money = Math.Max(0, loser.Money - actualPrize);
            }

            // Skill improvement for winner (slight)
            if (winner is Opponent winnerOpponent)
            {
                var skillGain = _random.Next(0, 2); // 0-1 skill gain
                winnerOpponent.AdjustSkill(skillGain);
            }

            // Update reputation
            winner.Stats.Reputation = winner.Stats.CalculateReputation();
            loser.Stats.Reputation = loser.Stats.CalculateReputation();

            return new SimulatedRaceResult
            {
                Racer1Name = racer1.Name,
                Racer2Name = racer2.Name,
                WinnerName = winner.Name,
                LoserName = loser.Name,
                RaceType = raceType,
                Prize = actualPrize,
                IsPinkSlip = isPinkSlip,
                CarWon = carWon
            };
        }

        /// <summary>
        /// Choose race type based on car performance and racer preferences
        /// </summary>
        private bool ChooseRaceType(Racer racer1, Racer racer2, Car car1, Car car2)
        {
            var hp1 = GetCarHP(car1);
            var hp2 = GetCarHP(car2);
            var avgHp = (hp1 + hp2) / 2;

            // Base probability for road race
            double roadRaceProbability = 0.5;

            // High-performance cars prefer road races
            if (avgHp > 300) roadRaceProbability = 0.7;
            else if (avgHp < 150) roadRaceProbability = 0.3;

            // Skilled racers prefer road races
            var avgSkill = (GetRacerSkill(racer1) + GetRacerSkill(racer2)) / 2.0;
            roadRaceProbability += (avgSkill - 50) / 200.0;

            // Money factor - broke racers prefer higher stakes
            var avgMoney = (double)(racer1.Money + racer2.Money) / 2;
            if (avgMoney < 200) roadRaceProbability += 0.1;

            return _random.NextDouble() < Math.Clamp(roadRaceProbability, 0.2, 0.8);
        }

        /// <summary>
        /// Calculate prize for a race
        /// </summary>
        private double CalculatePrize(Racer racer1, Racer racer2, Car car1, Car car2, bool isRoadRace)
        {
            if (isRoadRace)
            {
                var avgCarValue = (double)(car1.PurchasePrice + car2.PurchasePrice) / 2;
                var prob = _random.NextDouble();

                if (prob < 0.2) return Math.Max(50, avgCarValue * 0.05);      // 5% of car value
                else if (prob < 0.6) return Math.Max(100, avgCarValue * 0.1); // 10% of car value
                else if (prob < 0.9) return Math.Max(200, avgCarValue * 0.15); // 15% of car value
                else return -1; // Pink slips
            }
            else
            {
                // Drag race - smaller bets
                var avgMoney = (double)(racer1.Money + racer2.Money) / 2;
                var prob = _random.NextDouble();

                if (prob < 0.3) return 0;                              // Just for fun
                else if (prob < 0.7) return Math.Max(10, avgMoney * 0.02); // 2% of average bankroll
                else return Math.Max(25, avgMoney * 0.05);             // 5% of average bankroll
            }
        }

        /// <summary>
        /// Calculate win probability based on HP, skill, and condition
        /// </summary>
        private double CalculateWinProbability(Racer racer1, Racer racer2, Car car1, Car car2)
        {
            var hp1 = GetCarHP(car1);
            var hp2 = GetCarHP(car2);

            // Base probability from horsepower
            var totalHp = hp1 + hp2;
            var hpAdvantage = totalHp > 0 ? hp1 / totalHp : 0.5;

            // Skill factor
            var skill1 = GetRacerSkill(racer1);
            var skill2 = GetRacerSkill(racer2);
            var skillDiff = skill1 - skill2;
            var skillAdvantage = 0.5 + (skillDiff / 200.0);

            // Car condition factor
            var condition1 = GetOverallCarCondition(car1);
            var condition2 = GetOverallCarCondition(car2);
            var conditionDiff = condition1 - condition2;
            var conditionAdvantage = 0.5 + (conditionDiff / 2.0);

            // Combine factors (HP is most important, then skill, then condition)
            var finalProbability = (hpAdvantage * 0.5) + (skillAdvantage * 0.3) + (conditionAdvantage * 0.2);

            return Math.Clamp(finalProbability, 0.1, 0.9);
        }

        /// <summary>
        /// Apply wear to car components after a race
        /// </summary>
        private void ApplyRaceWear(Car car, bool isRoadRace, bool won)
        {
            var wearMultiplier = isRoadRace ? 1.5 : 1.0;
            var winnerBonus = won ? 0.8 : 1.2; // Winners push less hard

            // Engine wear
            var engineWear = _random.NextDouble() * 0.02 * wearMultiplier * winnerBonus;
            car.EngineHealth = Math.Max(0, car.EngineHealth - engineWear);

            // Transmission wear
            var transWear = _random.NextDouble() * 0.015 * wearMultiplier * winnerBonus;
            car.TransmissionHealth = Math.Max(0, car.TransmissionHealth - transWear);

            // Tire wear (highest)
            var tireWear = _random.NextDouble() * 0.03 * wearMultiplier * winnerBonus;
            car.TireCondition = Math.Max(0, car.TireCondition - tireWear);

            // Body wear (lowest)
            var bodyWear = _random.NextDouble() * 0.005 * wearMultiplier * winnerBonus;
            car.BodyCondition = Math.Max(0, car.BodyCondition - bodyWear);

            // Add mileage
            var mileage = isRoadRace ? _random.Next(20, 50) : _random.Next(5, 15);
            car.OdometerKM += mileage;

            // 5% chance of major damage for losers
            if (!won && _random.NextDouble() < 0.05)
            {
                var damageType = _random.Next(4);
                switch (damageType)
                {
                    case 0:
                        car.EngineHealth = Math.Max(0, car.EngineHealth - _random.NextDouble() * 0.15);
                        break;
                    case 1:
                        car.TransmissionHealth = Math.Max(0, car.TransmissionHealth - _random.NextDouble() * 0.15);
                        break;
                    case 2:
                        car.TireCondition = Math.Max(0, car.TireCondition - _random.NextDouble() * 0.15);
                        break;
                    case 3:
                        car.BodyCondition = Math.Max(0, car.BodyCondition - _random.NextDouble() * 0.1);
                        break;
                }
            }
        }

        /// <summary>
        /// Determine if a racer should race again (smart AI behavior)
        /// </summary>
        private bool ShouldRaceAgain(Racer racer)
        {
            if (racer.Cars.Count == 0) return false;

            var car = racer.Cars[0];
            var condition = GetOverallCarCondition(car);

            // Don't race with severely damaged cars
            if (condition < 0.3) return false;

            // Don't race with worn engine
            if (car.EngineHealth < 0.2) return false;

            // Don't race with failing transmission
            if (car.TransmissionHealth < 0.15) return false;

            // Broke racers are more desperate
            if (racer.Money < 100) return _random.NextDouble() < 0.8;

            // Successful racers are more selective
            if (racer.Stats.Races > 0)
            {
                var winRate = racer.Stats.WinRate;
                if (winRate > 0.7) return _random.NextDouble() < 0.6;
            }

            return _random.NextDouble() < 0.7;
        }

        private void UpdateRacerEligibility(Racer racer, List<Racer> racerList, GameState gameState)
        {
            if (!ShouldRaceAgain(racer))
            {
                racerList.Remove(racer);
            }
        }

        /// <summary>
        /// Get seasonal modifier for race frequency
        /// </summary>
        private double GetSeasonalModifier(int month)
        {
            return month switch
            {
                6 or 7 or 8 => 1.3,   // Summer - more racing
                12 or 1 or 2 => 0.7,  // Winter - less racing
                _ => 1.0
            };
        }

        private double GetCarHP(Car car)
        {
            // Use TotalHP if set, otherwise estimate from purchase price
            if (car.TotalHP > 0) return car.TotalHP;
            return 100 + (double)car.PurchasePrice / 50; // Rough estimate
        }

        private int GetRacerSkill(Racer racer)
        {
            if (racer is Opponent opponent)
                return opponent.Skill;
            return 85; // Default skill
        }

        private double GetOverallCarCondition(Car car)
        {
            return (car.EngineHealth + car.TransmissionHealth + car.BodyCondition + car.TireCondition) / 4.0;
        }
    }

    /// <summary>
    /// Result of a full day of race simulation
    /// </summary>
    public class RaceSimulationResult
    {
        public int TotalRaces { get; set; }
        public List<SimulatedRaceResult> Races { get; set; } = [];
    }

    /// <summary>
    /// Result of a single simulated race
    /// </summary>
    public class SimulatedRaceResult
    {
        public string Racer1Name { get; set; } = string.Empty;
        public string Racer2Name { get; set; } = string.Empty;
        public string WinnerName { get; set; } = string.Empty;
        public string LoserName { get; set; } = string.Empty;
        public string RaceType { get; set; } = string.Empty;
        public decimal Prize { get; set; }
        public bool IsPinkSlip { get; set; }
        public Car? CarWon { get; set; }
    }
}
