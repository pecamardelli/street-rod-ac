using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Models.Race;
using Street_Rod_AC.Screens.Diner;

namespace Street_Rod_AC.Services.Street
{
    /// <summary>What a rival who pulls up puts to the player: the kind of race and what it is for</summary>
    /// <param name="Wager">The cash on it; 0 with pink slips</param>
    public sealed record StreetOffer(RaceType RaceType, bool PinkSlips, decimal Wager);

    /// <summary>
    /// The rules of the street: how long the player sits at the curb before somebody pulls up, who it is, and what
    /// they offer. No state and no UI, so they can be checked on their own; the dice are the caller's.
    ///
    /// The street is busier the later it gets, busiest from 20:00 when the stakes double. Rivals who want a rematch
    /// come looking for the player, and the aggressive ones are the ones out at night. A rival is met once a night:
    /// the one who was waved off does not come round again.
    /// </summary>
    public static class StreetEncounters
    {
        /// <summary>Game minutes, on average, before somebody pulls up: by day, at dusk, and from 20:00</summary>
        public const double DayWait = 50;
        public const double DuskWait = 35;
        public const double NightWait = 18;

        public const int ShortestWait = 5;
        public const int LongestWait = 120;

        /// <summary>A rival out to get their car back is this many times likelier to be the one who shows</summary>
        public const double GrudgeWeight = 4;

        /// <summary>The King cruises too, but seldom</summary>
        public const double KingWeight = 0.25;

        /// <summary>A rival of about the player's standing (<see cref="MatchupCalculator.MatchedReputationBand"/>) is likelier than one far off</summary>
        public const double MatchedWeight = 1.5;

        /// <summary>A drag race most of the time: the street is a straight line from light to light, more so at night</summary>
        public const double DragShareByDay = 0.65;
        public const double DragShareAtNight = 0.8;

        /// <summary>How often a rival of middling aggression, on Normal, offers pink slips unasked</summary>
        public const double PinkSlipOffer = 0.06;

        public static double MeanWait(DateTime time) =>
            MatchupCalculator.IsNight(time) ? NightWait
            : StreetLights.At(time) == StreetLight.Day ? DayWait
            : DuskWait;

        /// <summary>Game minutes until the next rival pulls up: the waits of a street where somebody may come along at any moment</summary>
        public static int MinutesToNext(DateTime time, Random random)
        {
            var wait = -Math.Log(1 - random.NextDouble()) * MeanWait(time);
            return (int)Math.Clamp(Math.Round(wait), ShortestWait, LongestWait);
        }

        /// <summary>How likely <paramref name="rival"/> is to be the one who pulls up, against the others out</summary>
        public static double Weight(Opponent rival, int playerReputation, DateTime time)
        {
            if (rival.IsKing) return KingWeight;

            var weight = 1.0;
            if (rival.Grudge != null) weight *= GrudgeWeight;
            if (Math.Abs(rival.Stats.Reputation - playerReputation) <= MatchupCalculator.MatchedReputationBand) weight *= MatchedWeight;

            // At night the ones who like a risk are out; by day it hardly matters
            var aggression = Math.Clamp(rival.Aggression, 0, 100) / 100.0;
            weight *= MatchupCalculator.IsNight(time) ? 0.5 + aggression : 0.9 + 0.2 * aggression;
            return weight;
        }

        /// <summary>
        /// Who pulls up out of <paramref name="candidates"/> (the racers out and able to race), leaving out the ones
        /// met already tonight; null when there is nobody left
        /// </summary>
        public static Opponent? PickRival(IReadOnlyList<Opponent> candidates, int playerReputation, DateTime time,
            IReadOnlySet<Guid> metTonight, Random random)
        {
            var left = candidates.Where(o => !metTonight.Contains(o.OpponentId)).ToList();
            if (left.Count == 0) return null;

            var weights = left.Select(o => Weight(o, playerReputation, time)).ToList();
            var roll = random.NextDouble() * weights.Sum();
            for (var i = 0; i < left.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return left[i];
            }

            return left[^1];
        }

        /// <summary>The kind of race a rival asks for, given what the player's AC has installed; null with no track at all</summary>
        public static RaceType? PickRaceType(DateTime time, bool hasDragStrip, bool hasCircuit, Random random)
        {
            if (!hasDragStrip && !hasCircuit) return null;
            if (!hasCircuit) return RaceType.DragRace;
            if (!hasDragStrip) return RaceType.Circuit;

            var drag = MatchupCalculator.IsNight(time) ? DragShareAtNight : DragShareByDay;
            return random.NextDouble() < drag ? RaceType.DragRace : RaceType.Circuit;
        }

        /// <summary>
        /// What the rival offers. The King and a rival with a grudge want pink slips; anybody else now and then
        /// (the bolder, the likelier, and the difficulty's pink-slip figure on top); otherwise cash, a share of what
        /// the street's limits allow tonight that grows with the rival's aggression. Pink slips only when
        /// <paramref name="wouldStakePinkSlips"/> (the rival would stake their car against the player's, going by what
        /// the two are worth). Null when the two of them cannot scrape a cash bet together and pink slips are not on,
        /// and for the King or a rival with a grudge who would not stake their car.
        /// </summary>
        public static StreetOffer? OfferFrom(Opponent rival, decimal playerMoney, RaceType raceType, DateTime time,
            double pinkSlipFactor, bool wouldStakePinkSlips, Random random)
        {
            if (rival.IsKing || rival.Grudge != null) return wouldStakePinkSlips ? new StreetOffer(raceType, true, 0m) : null;

            var aggression = Math.Clamp(rival.Aggression, 0, 100) / 100.0;
            if (random.NextDouble() < PinkSlipOffer * pinkSlipFactor * (0.5 + aggression) && wouldStakePinkSlips)
                return new StreetOffer(raceType, true, 0m);

            var (min, max) = MatchupCalculator.WagerLimits(raceType, playerMoney, rival.Money, rival.Stats.Reputation, time);
            if (max <= 0m || min <= 0m) return null;

            var share = (decimal)Math.Clamp(0.25 + 0.6 * aggression * random.NextDouble(), 0, 1);
            var wager = Math.Round((min + (max - min) * share) / 5m) * 5m;
            return new StreetOffer(raceType, false, MatchupCalculator.ClampWager(wager, min, max));
        }
    }
}
