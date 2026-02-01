using Street_Rod_AC.Models.Career.Milestones;
using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Implementation of milestone management service
    /// </summary>
    public class MilestoneService : IMilestoneService
    {
        private readonly Dictionary<string, MilestoneDefinition> _milestones;

        public MilestoneService()
        {
            _milestones = CreateDefaultMilestones();
        }

        /// <summary>
        /// Create the default set of milestones for the game
        /// </summary>
        private static Dictionary<string, MilestoneDefinition> CreateDefaultMilestones()
        {
            var milestones = new Dictionary<string, MilestoneDefinition>();

            // Racing milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "first_blood",
                Name = "First Blood",
                Description = "Win your first race",
                Trigger = MilestoneTrigger.TotalWins,
                TargetValue = 1,
                Category = "Racing"
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "veteran",
                Name = "Veteran Racer",
                Description = "Win 10 races",
                Trigger = MilestoneTrigger.TotalWins,
                TargetValue = 10,
                Category = "Racing",
                Unlocks = ["victory:King"] // Unlocks King challenge
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "legend",
                Name = "Racing Legend",
                Description = "Win 50 races",
                Trigger = MilestoneTrigger.TotalWins,
                TargetValue = 50,
                Category = "Racing"
            });

            // Reputation milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "street_cred",
                Name = "Street Cred",
                Description = "Reach 25 reputation",
                Trigger = MilestoneTrigger.ReputationReached,
                TargetValue = 25,
                Category = "Reputation",
                Unlocks = ["event:muscle_car_events"]
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "respected",
                Name = "Respected",
                Description = "Reach 50 reputation",
                Trigger = MilestoneTrigger.ReputationReached,
                TargetValue = 50,
                Category = "Reputation"
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "feared",
                Name = "Feared",
                Description = "Reach 75 reputation",
                Trigger = MilestoneTrigger.ReputationReached,
                TargetValue = 75,
                Category = "Reputation",
                Unlocks = ["victory:Domination"]
            });

            // Pink slip milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "pink_slip_hunter",
                Name = "Pink Slip Hunter",
                Description = "Win 5 pink slip races",
                Trigger = MilestoneTrigger.PinkSlipWins,
                TargetValue = 5,
                Category = "Pink Slips",
                Unlocks = ["victory:PinkSlipCollector"]
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "title_collector",
                Name = "Title Collector",
                Description = "Win 10 pink slip races",
                Trigger = MilestoneTrigger.PinkSlipWins,
                TargetValue = 10,
                Category = "Pink Slips"
            });

            // Car collection milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "garage_full",
                Name = "Garage Full",
                Description = "Own 5 cars at once",
                Trigger = MilestoneTrigger.CarsOwned,
                TargetValue = 5,
                Category = "Collection",
                Unlocks = ["event:car_shows"]
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "car_dealer",
                Name = "Car Dealer",
                Description = "Own 10 cars at once",
                Trigger = MilestoneTrigger.CarsOwned,
                TargetValue = 10,
                Category = "Collection"
            });

            // Opponent milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "rival_crusher",
                Name = "Rival Crusher",
                Description = "Defeat 5 different opponents",
                Trigger = MilestoneTrigger.OpponentsDefeated,
                TargetValue = 5,
                Category = "Opponents"
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "no_mercy",
                Name = "No Mercy",
                Description = "Defeat 10 different opponents",
                Trigger = MilestoneTrigger.OpponentsDefeated,
                TargetValue = 10,
                Category = "Opponents"
            });

            // Money milestones
            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "big_winner",
                Name = "Big Winner",
                Description = "Earn $10,000 from races",
                Trigger = MilestoneTrigger.MoneyEarned,
                TargetValue = 10000,
                Category = "Money"
            });

            AddMilestone(milestones, new MilestoneDefinition
            {
                Id = "high_roller",
                Name = "High Roller",
                Description = "Earn $50,000 from races",
                Trigger = MilestoneTrigger.MoneyEarned,
                TargetValue = 50000,
                Category = "Money"
            });

            return milestones;
        }

        private static void AddMilestone(Dictionary<string, MilestoneDefinition> dict, MilestoneDefinition milestone)
        {
            dict[milestone.Id] = milestone;
        }

        public IEnumerable<MilestoneDefinition> GetAllMilestones()
        {
            return _milestones.Values;
        }

        public MilestoneDefinition? GetMilestone(string milestoneId)
        {
            return _milestones.TryGetValue(milestoneId, out var milestone) ? milestone : null;
        }

        public IEnumerable<MilestoneDefinition> GetMilestonesByCategory(string category)
        {
            return _milestones.Values.Where(m => m.Category == category);
        }

        public MilestoneProgress GetProgress(string milestoneId, CareerState career)
        {
            var milestone = GetMilestone(milestoneId);
            if (milestone == null)
            {
                return new MilestoneProgress
                {
                    Definition = new MilestoneDefinition { Id = milestoneId, Name = "Unknown" },
                    CurrentValue = 0,
                    IsCompleted = false
                };
            }

            var currentValue = career.GetCounter(milestone.Trigger);
            var isCompleted = career.CompletedMilestones.Contains(milestoneId);

            return new MilestoneProgress
            {
                Definition = milestone,
                CurrentValue = currentValue,
                IsCompleted = isCompleted
            };
        }

        public IEnumerable<MilestoneProgress> GetAllProgress(CareerState career)
        {
            foreach (var milestone in _milestones.Values)
            {
                yield return GetProgress(milestone.Id, career);
            }
        }

        public List<MilestoneDefinition> CheckForCompletedMilestones(CareerState career)
        {
            var newlyCompleted = new List<MilestoneDefinition>();

            foreach (var milestone in _milestones.Values)
            {
                // Skip already completed milestones
                if (career.CompletedMilestones.Contains(milestone.Id))
                    continue;

                var currentValue = career.GetCounter(milestone.Trigger);

                if (currentValue >= milestone.TargetValue)
                {
                    career.CompletedMilestones.Add(milestone.Id);
                    newlyCompleted.Add(milestone);
                }
            }

            return newlyCompleted;
        }

        public List<string> GetUnlocksForMilestone(string milestoneId)
        {
            var milestone = GetMilestone(milestoneId);
            return milestone?.Unlocks ?? [];
        }

        public bool IsMilestoneCompleted(string milestoneId, CareerState career)
        {
            return career.CompletedMilestones.Contains(milestoneId);
        }
    }
}
