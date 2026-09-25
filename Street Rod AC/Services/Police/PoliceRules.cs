using Street_Rod_AC.Models.GameState;

namespace Street_Rod_AC.Services.Police
{
    /// <summary>
    /// The street's police: how likely a street race draws them, how many come, and what getting caught costs.
    /// The chase itself is the race mode's (apps\new-modes\sr_race), which reports who got away and who was busted.
    ///
    /// A road race only: a drag strip has room for the two racers and nobody else, and nowhere to run.
    /// </summary>
    public static class PoliceRules
    {
        /// <summary>From this hour the streets are dark and the patrols busier (the diner's night stakes start then too)</summary>
        public const int NightFromHour = 20;

        /// <summary>The chance of a patrol by day and by night, before the racers' names and the stakes</summary>
        public const double DayChance = 0.08;
        public const double NightChance = 0.30;

        /// <summary>A known name draws attention: up to this much more at 100 reputation</summary>
        public const double ReputationChance = 0.15;

        /// <summary>A pink slip, or a wager of <see cref="BigWager"/> or more, gets talked about beforehand</summary>
        public const double PinkSlipChance = 0.05;
        public const double BigWagerChance = 0.05;
        public const decimal BigWager = 1000m;

        public const double MaxChance = 0.5;

        /// <summary>The share of patrols that are speed traps parked round the track; the rest drive up from behind</summary>
        public const double TrapShare = 2.0 / 3.0;

        /// <summary>The fine for a first bust, what each earlier one adds, and the most it gets to</summary>
        public const decimal BaseFine = 750m;
        public const decimal FinePerBust = 500m;
        public const decimal MaxFine = 5000m;

        /// <summary>Days in the impound for a first bust, one more for each earlier one, up to the most</summary>
        public const int BaseImpoundDays = 2;
        public const int MaxImpoundDays = 7;

        /// <summary>What the impound charges for each day it keeps the car</summary>
        public const decimal ImpoundFeePerDay = 100m;

        /// <summary>The impound opens at this hour</summary>
        public const int ImpoundOpensHour = 8;

        public static bool IsNight(DateTime time) => time.Hour >= NightFromHour;

        /// <summary>The chance a patrol comes across a street race, 0 to <see cref="MaxChance"/></summary>
        public static double Chance(DateTime time, int reputation, bool isPinkSlip, decimal cashWager)
        {
            var chance = IsNight(time) ? NightChance : DayChance;
            chance += ReputationChance * Math.Clamp(reputation, 0, 100) / 100.0;
            if (isPinkSlip) chance += PinkSlipChance;
            if (cashWager >= BigWager) chance += BigWagerChance;
            return Math.Min(chance, MaxChance);
        }

        /// <summary>
        /// The police send one car for each racer: each sticks to its prey and tries to get past it (the race mode).
        /// A track needs a pit box for each, after the racers' two.
        /// </summary>
        public const int CopsPerRace = 2;

        /// <summary>The risk in the diner's words</summary>
        public static string RiskLabel(double chance) => chance switch
        {
            < 0.15 => "Low",
            < 0.30 => "Moderate",
            _ => "High"
        };

        /// <summary>The fine for a racer caught <paramref name="earlierBusts"/> times before</summary>
        public static decimal Fine(int earlierBusts) =>
            Math.Min(BaseFine + FinePerBust * Math.Max(0, earlierBusts), MaxFine);

        /// <summary>Days the car is held for a racer caught <paramref name="earlierBusts"/> times before</summary>
        public static int ImpoundDays(int earlierBusts) =>
            Math.Min(BaseImpoundDays + Math.Max(0, earlierBusts), MaxImpoundDays);

        /// <summary>
        /// The car goes to the impound: it can be collected when the impound opens, <see cref="ImpoundDays"/> days
        /// on, for a day's fee for each of them
        /// </summary>
        public static void Impound(Car car, DateTime busted, int earlierBusts)
        {
            var days = ImpoundDays(earlierBusts);
            car.ImpoundedUntil = busted.Date.AddDays(days).AddHours(ImpoundOpensHour);
            car.ImpoundFee = ImpoundFeePerDay * days;
        }

        /// <summary>The car is in the impound and its days are up: it can be collected for its fee</summary>
        public static bool CanCollect(Car car, DateTime now) => car.ImpoundedUntil is { } until && now >= until;

        /// <summary>A rival's car left in the impound this many days past its date is auctioned off</summary>
        public const int AuctionAfterDays = 14;

        /// <summary>The car has waited in the impound long enough past its date to be auctioned off (rivals' cars)</summary>
        public static bool IsForAuction(Car car, DateTime now) =>
            car.ImpoundedUntil is { } until && now >= until.AddDays(AuctionAfterDays);

        /// <summary>Out of the impound</summary>
        public static void Release(Car car)
        {
            car.ImpoundedUntil = null;
            car.ImpoundFee = 0m;
        }
    }
}
