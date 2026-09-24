using Street_Rod_AC.Models.Career.Events;
using Street_Rod_AC.Models.Career.Filters;
using Street_Rod_AC.Models.Catalog;
using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;

namespace Street_Rod_AC.Services.Career
{
    /// <summary>
    /// Implementation of race event management
    /// </summary>
    public class RaceEventService : IRaceEventService
    {
        private readonly Dictionary<string, RaceEventDefinition> _eventDefinitions;
        private readonly ICarFilterService _filterService;
        private readonly Func<string, CarDefinition?>? _carDefinitionLookup;
        private readonly Random _random = new();

        public RaceEventService(
            ICarFilterService filterService,
            Func<string, CarDefinition?>? carDefinitionLookup = null)
        {
            _filterService = filterService;
            _carDefinitionLookup = carDefinitionLookup;
            _eventDefinitions = CreateDefaultEvents();
        }

        /// <summary>
        /// Create the default set of events for the game
        /// </summary>
        private static Dictionary<string, RaceEventDefinition> CreateDefaultEvents()
        {
            var events = new Dictionary<string, RaceEventDefinition>();

            // Ford Fanatics - Ford only event
            AddEvent(events, new RaceEventDefinition
            {
                Id = "ford_fanatics",
                Name = "Ford Fanatics",
                Description = "A gathering of Ford enthusiasts. Bring your best Blue Oval machine!",
                RaceType = RaceType.DragRace,
                EntryRequirements = new BrandFilter("Ford"),
                MinReputation = 0,
                Reward = new EventReward(500, 5),
                Schedule = EventSchedule.Weekly
            });

            // Budget Brawl - Cars under $5000
            AddEvent(events, new RaceEventDefinition
            {
                Id = "budget_brawl",
                Name = "Budget Brawl",
                Description = "Think expensive cars always win? Prove them wrong with your bargain build!",
                RaceType = RaceType.DragRace,
                EntryRequirements = new ValueFilter(null, 5000),
                MinReputation = 0,
                Reward = new EventReward(1000, 3),
                Schedule = EventSchedule.Weekly
            });

            // Muscle Madness - American muscle over 300hp
            AddEvent(events, new RaceEventDefinition
            {
                Id = "muscle_madness",
                Name = "Muscle Madness",
                Description = "American iron only! Bring the biggest, baddest V8 you've got.",
                RaceType = RaceType.DragRace,
                EntryRequirements = CompositeCarFilter.And(
                    new OriginFilter("USA"),
                    new PowerFilter(300, null)
                ),
                MinReputation = 25,
                RequiredMilestones = ["street_cred"],
                Reward = new EventReward(750, 10, "rare_camshaft", "High-performance camshaft"),
                Schedule = EventSchedule.Weekly
            });

            // Classic Showdown - Pre-1970 cars
            AddEvent(events, new RaceEventDefinition
            {
                Id = "classic_showdown",
                Name = "Classic Showdown",
                Description = "Only the classics qualify. Show off your vintage ride!",
                RaceType = RaceType.DragRace,
                EntryRequirements = new DecadeFilter(null, 1969),
                MinReputation = 15,
                Reward = new EventReward(750, 10),
                Schedule = EventSchedule.Weekly
            });

            // Fifties Fever - 1950s cars only
            AddEvent(events, new RaceEventDefinition
            {
                Id = "fifties_fever",
                Name = "Fifties Fever",
                Description = "Celebrate the golden age of American motoring. 1950s only!",
                RaceType = RaceType.DragRace,
                EntryRequirements = DecadeFilter.ForDecade(1950),
                MinReputation = 30,
                Reward = new EventReward(1000, 8),
                Schedule = EventSchedule.Weekly
            });

            // Chevy Challenge
            AddEvent(events, new RaceEventDefinition
            {
                Id = "chevy_challenge",
                Name = "Chevy Challenge",
                Description = "Bow tie brigade only! Let's see what your Chevy can do.",
                RaceType = RaceType.DragRace,
                EntryRequirements = new BrandFilter("Chevrolet"),
                MinReputation = 10,
                Reward = new EventReward(600, 5),
                Schedule = EventSchedule.Weekly
            });

            // High Stakes - Pink slip event
            AddEvent(events, new RaceEventDefinition
            {
                Id = "high_stakes",
                Name = "High Stakes",
                Description = "Winner takes all. Are you willing to risk your ride?",
                RaceType = RaceType.DragRace,
                MinReputation = 50,
                RequiredMilestones = ["pink_slip_hunter"],
                IsPinkSlip = true,
                Reward = new EventReward(0, 15), // Main reward is the car!
                Schedule = EventSchedule.Weekly
            });

            // Underdog Challenge - Low HP cars
            AddEvent(events, new RaceEventDefinition
            {
                Id = "underdog_challenge",
                Name = "Underdog Challenge",
                Description = "Small motors, big hearts. Under 200hp only.",
                RaceType = RaceType.DragRace,
                EntryRequirements = new PowerFilter(null, 200),
                MinReputation = 0,
                Reward = new EventReward(400, 5),
                Schedule = EventSchedule.Daily
            });

            // European Invasion
            AddEvent(events, new RaceEventDefinition
            {
                Id = "european_invasion",
                Name = "European Invasion",
                Description = "European engineering takes on the strips. Import power!",
                RaceType = RaceType.DragRace,
                EntryRequirements = new OriginFilter("European"),
                MinReputation = 20,
                Reward = new EventReward(800, 7),
                Schedule = EventSchedule.Weekly
            });

            // Sixties Showdown
            AddEvent(events, new RaceEventDefinition
            {
                Id = "sixties_showdown",
                Name = "Sixties Showdown",
                Description = "The muscle car era at its finest. 1960s machinery only!",
                RaceType = RaceType.DragRace,
                EntryRequirements = DecadeFilter.ForDecade(1960),
                MinReputation = 20,
                Reward = new EventReward(850, 8),
                Schedule = EventSchedule.Weekly
            });

            return events;
        }

        private static void AddEvent(Dictionary<string, RaceEventDefinition> dict, RaceEventDefinition evt)
        {
            dict[evt.Id] = evt;
        }

        public IEnumerable<RaceEventDefinition> GetAllEventDefinitions()
        {
            return _eventDefinitions.Values;
        }

        public RaceEventDefinition? GetEventDefinition(string eventId)
        {
            return _eventDefinitions.TryGetValue(eventId, out var evt) ? evt : null;
        }

        public IEnumerable<RaceEventDefinition> GetEligibleEvents(CareerState career)
        {
            var reputation = career.GetCounter(Models.Career.Milestones.MilestoneTrigger.ReputationReached);

            foreach (var evt in _eventDefinitions.Values)
            {
                // Check reputation requirement
                if (reputation < evt.MinReputation)
                    continue;

                // Check milestone requirements
                if (evt.RequiredMilestones.Any(m => !career.CompletedMilestones.Contains(m)))
                    continue;

                // Check if one-time event already completed
                if (evt.Schedule == EventSchedule.OneTime &&
                    career.CompletedEventIds.Contains(evt.Id))
                    continue;

                yield return evt;
            }
        }

        public IEnumerable<RaceEventInstance> GetActiveEvents(CareerState career, DateTime currentTime)
        {
            return career.ActiveEvents
                .Where(e => e.IsAvailable(currentTime))
                .Select(e => new RaceEventInstance
                {
                    InstanceId = e.InstanceId,
                    EventDefinitionId = e.EventDefinitionId,
                    AvailableFrom = e.AvailableFrom,
                    ExpiresAt = e.ExpiresAt,
                    IsCompleted = e.IsCompleted,
                    PlayerWon = e.PlayerWon
                });
        }

        /// <summary>How likely an eligible event is to turn up when events are generated</summary>
        private const double EventChance = 0.5;

        public List<RaceEventInstance> GenerateEvents(CareerState career, DateTime currentTime, double pinkSlipFactor = 1.0)
        {
            var newEvents = new List<RaceEventInstance>();
            var eligibleEvents = GetEligibleEvents(career).ToList();

            // Remove expired events first
            CleanupExpiredEvents(career, currentTime);

            foreach (var eventDef in eligibleEvents)
            {
                // Check if this event type is already active
                var hasActive = career.ActiveEvents.Any(e =>
                    e.EventDefinitionId == eventDef.Id && e.IsAvailable(currentTime));

                if (hasActive)
                    continue;

                // Check schedule constraints
                var lastCompleted = career.ActiveEvents
                    .Where(e => e.EventDefinitionId == eventDef.Id && e.IsCompleted)
                    .OrderByDescending(e => e.CompletedAt)
                    .FirstOrDefault();

                bool shouldGenerate = eventDef.Schedule switch
                {
                    EventSchedule.OneTime => !career.CompletedEventIds.Contains(eventDef.Id),
                    EventSchedule.Daily => lastCompleted == null ||
                        (currentTime - lastCompleted.CompletedAt)?.TotalHours >= 24,
                    EventSchedule.Weekly => lastCompleted == null ||
                        (currentTime - lastCompleted.CompletedAt)?.TotalDays >= 7,
                    EventSchedule.Permanent => true,
                    _ => false
                };

                if (!shouldGenerate)
                    continue;

                // Random chance to generate (not all eligible events appear each time); a pink-slip event
                // turns up more or less often with the game's difficulty
                var chance = eventDef.IsPinkSlip ? Math.Min(0.95, EventChance * pinkSlipFactor) : EventChance;
                if (_random.NextDouble() > chance && eventDef.Schedule != EventSchedule.Permanent)
                    continue;

                var instance = new RaceEventInstance
                {
                    EventDefinitionId = eventDef.Id,
                    AvailableFrom = currentTime,
                    ExpiresAt = eventDef.Schedule switch
                    {
                        EventSchedule.Daily => currentTime.AddHours(24),
                        EventSchedule.Weekly => currentTime.AddDays(7),
                        _ => null
                    },
                    TrackId = eventDef.TrackId,
                    OpponentName = eventDef.SpecificOpponent
                };

                // Add to career state, under the instance's id: that is what the invitation hands back
                career.ActiveEvents.Add(new RaceEventState
                {
                    InstanceId = instance.InstanceId,
                    EventDefinitionId = instance.EventDefinitionId,
                    AvailableFrom = instance.AvailableFrom,
                    ExpiresAt = instance.ExpiresAt,
                    IsCompleted = false
                });

                newEvents.Add(instance);
            }

            return newEvents;
        }

        public bool CanEnterEvent(string eventId, string carDefinitionId, Car? carInstance, CareerState career)
        {
            var eventDef = GetEventDefinition(eventId);
            if (eventDef == null)
                return false;

            // Check reputation
            var reputation = career.GetCounter(Models.Career.Milestones.MilestoneTrigger.ReputationReached);
            if (reputation < eventDef.MinReputation)
                return false;

            // Check milestones
            if (eventDef.RequiredMilestones.Any(m => !career.CompletedMilestones.Contains(m)))
                return false;

            // Check car filter
            if (eventDef.EntryRequirements != null && _carDefinitionLookup != null)
            {
                var carDef = _carDefinitionLookup(carDefinitionId);
                if (carDef == null)
                    return false;

                if (!_filterService.Matches(eventDef.EntryRequirements, carDef, carInstance))
                    return false;
            }

            return true;
        }

        /// <param name="eventInstanceId">The instance the player entered (<see cref="RaceEventState.InstanceId"/>).
        /// Guid.Empty only for a race from before instances were carried into it: then the first open event in
        /// its window is taken, as it always was.</param>
        /// <param name="completedAt">Game time of the race (gameState.Date), never the real clock</param>
        public EventReward? CompleteEvent(Guid eventInstanceId, bool playerWon, CareerState career, DateTime completedAt)
        {
            var eventState = eventInstanceId != Guid.Empty
                ? career.ActiveEvents.FirstOrDefault(e => e.InstanceId == eventInstanceId && !e.IsCompleted)
                // A day of slack past the expiry: completedAt is after the race's time was spent, and a race
                // started late in the evening finishes the next morning, past the midnight its event expired at
                : career.ActiveEvents.FirstOrDefault(e =>
                    !e.IsCompleted &&
                    e.AvailableFrom <= completedAt &&
                    (!e.ExpiresAt.HasValue || e.ExpiresAt.Value.AddDays(1) >= completedAt));

            if (eventState == null)
                return null;

            var eventDef = GetEventDefinition(eventState.EventDefinitionId);
            if (eventDef == null)
                return null;

            // Mark as completed. When it was is what brings a daily or weekly event back.
            eventState.IsCompleted = true;
            eventState.PlayerWon = playerWon;
            eventState.CompletedAt = completedAt;

            // Track one-time event completion
            if (eventDef.Schedule == EventSchedule.OneTime)
            {
                career.CompletedEventIds.Add(eventDef.Id);
            }

            // Return reward only if player won
            return playerWon ? eventDef.Reward : null;
        }

        /// <summary>
        /// Takes out the events that ran out unentered, and the completed ones once they have done their job:
        /// a completed daily or weekly event is kept until its schedule would bring it back (that is what
        /// GenerateEvents looks at), a completed one-time event lives on in CompletedEventIds. Without this the
        /// save's list only ever grew.
        /// </summary>
        public int CleanupExpiredEvents(CareerState career, DateTime currentTime)
        {
            var expiredCount = career.ActiveEvents.RemoveAll(e =>
                (!e.IsCompleted && e.ExpiresAt.HasValue && e.ExpiresAt.Value < currentTime)
                || (e.IsCompleted && CompletionHasRunOut(e, currentTime)));

            return expiredCount;
        }

        private bool CompletionHasRunOut(RaceEventState state, DateTime currentTime)
        {
            // Completed before CompletedAt was recorded: nothing would ever bring it back, so it only blocks
            if (!state.CompletedAt.HasValue) return true;

            var keepFor = GetEventDefinition(state.EventDefinitionId)?.Schedule switch
            {
                EventSchedule.Daily => TimeSpan.FromHours(24),
                EventSchedule.Weekly => TimeSpan.FromDays(7),
                _ => TimeSpan.Zero
            };

            return currentTime - state.CompletedAt.Value >= keepFor;
        }
    }
}
