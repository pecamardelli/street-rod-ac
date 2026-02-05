namespace Street_Rod_AC.Services.Talk
{
    /// <summary>
    /// Static talk service using preset dialogue lines based on context
    /// </summary>
    public class StaticTalkService : ITalkService
    {
        private readonly Random _random = new();

        public bool IsAvailable => true;

        public Task<string> GetMessageAsync(TalkContext context)
        {
            var messages = GetMessagesForContext(context);
            var message = messages[_random.Next(messages.Count)];
            return Task.FromResult(message);
        }

        private List<string> GetMessagesForContext(TalkContext context)
        {
            return context.Trigger switch
            {
                TalkTrigger.OpponentSelected => GetGreetingMessages(context),
                TalkTrigger.TrackSelected => GetTrackSelectedMessages(context),
                TalkTrigger.BetTypeChanged when context.IsPinkSlipBet => GetPinkSlipMessages(context),
                TalkTrigger.ChallengeAccepted => GetAcceptedMessages(context),
                TalkTrigger.ChallengeRejected => GetRejectedMessages(context),
                _ => GetGreetingMessages(context)
            };
        }

        private List<string> GetGreetingMessages(TalkContext context)
        {
            var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;
            var aggression = context.Opponent.Aggression;

            // High reputation opponent (cocky/dismissive)
            if (repDiff > 20)
            {
                if (aggression >= 70)
                {
                    return new List<string>
                    {
                        "You got some nerve walking up to me.",
                        "Beat it, kid. I don't race amateurs.",
                        "You sure you're in the right place?",
                        "I've scraped better competition off my tires.",
                        "This ain't a kiddie ride, pal."
                    };
                }
                return new List<string>
                {
                    "You sure you want to do this?",
                    "I've got a reputation to maintain, you know.",
                    "Don't waste my time unless you're serious.",
                    "Look, I respect the hustle, but you're outmatched.",
                    "Maybe come back when you've got some wins under your belt."
                };
            }

            // Lower reputation opponent (defensive/eager)
            if (repDiff < -20)
            {
                if (aggression >= 70)
                {
                    return new List<string>
                    {
                        "Yeah, I know who you are. So what?",
                        "Your reputation don't scare me!",
                        "I've got nothing to lose and everything to prove.",
                        "Everyone's gotta fall sometime. Today's your day.",
                        "Don't let my record fool you. I'm ready."
                    };
                }
                return new List<string>
                {
                    "I've been waiting for a shot like this.",
                    "People underestimate me. Big mistake.",
                    "I'm faster than my record shows.",
                    "Ready to be surprised?",
                    "This could be my big break."
                };
            }

            // Matched reputation (competitive/respectful)
            if (aggression >= 70)
            {
                return new List<string>
                {
                    "Finally, some real competition!",
                    "I've heard about you. Let's see what you've got.",
                    "This is gonna be good.",
                    "You and me, we're cut from the same cloth.",
                    "I've been looking for a challenge."
                };
            }
            return new List<string>
            {
                "This should be a good race.",
                "May the best driver win.",
                "You look like you know what you're doing.",
                "I respect your skills. Let's race.",
                "Two evenly matched racers. I like it."
            };
        }

        private List<string> GetTrackSelectedMessages(TalkContext context)
        {
            var aggression = context.Opponent.Aggression;
            var trackName = context.SelectedTrackName ?? "that track";

            if (aggression >= 70)
            {
                return new List<string>
                {
                    $"You picked {trackName}? Bold choice.",
                    "That's my kind of track. You're gonna regret this.",
                    "Good pick. I'll enjoy beating you there.",
                    "I know every inch of that strip.",
                    "You just made this easier for me."
                };
            }
            return new List<string>
            {
                $"{trackName}, huh? I can work with that.",
                "Fair choice. Let's do this.",
                "I've had some good runs there.",
                "Solid pick. Should be a good race.",
                "That works for me."
            };
        }

        private List<string> GetPinkSlipMessages(TalkContext context)
        {
            var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;
            var aggression = context.Opponent.Aggression;

            // Higher rep opponent - confident about pink slips
            if (repDiff > 15)
            {
                return new List<string>
                {
                    "Pink slips? Ha! I'll take that ride off your hands.",
                    "You're betting your car? This is gonna be fun.",
                    "Bold move. I respect that. Stupid, but respectable.",
                    "Your car's gonna look great in my garage.",
                    "Finally someone with guts. Let's do this."
                };
            }

            // Lower rep opponent - nervous but trying to be brave
            if (repDiff < -15)
            {
                if (aggression >= 60)
                {
                    return new List<string>
                    {
                        "Pink slips? You're on! I ain't scared!",
                        "If you're willing to lose your car, who am I to stop you?",
                        "High stakes? That's how I like it!",
                        "You want my car? Come and take it!",
                        "This is my chance to upgrade. Let's go!"
                    };
                }
                return new List<string>
                {
                    "Pink slips... you sure about this?",
                    "That's a big bet. But okay, I'm in.",
                    "I hope you know what you're doing...",
                    "Alright, but don't say I didn't warn you.",
                    "This just got real serious."
                };
            }

            // Matched reputation
            return new List<string>
            {
                "Pink slips it is. Winner takes all.",
                "Now we're talking! High stakes racing!",
                "You've got guts. I like that.",
                "This is what real street racing is about.",
                "May the best car win... literally."
            };
        }

        private List<string> GetAcceptedMessages(TalkContext context)
        {
            var aggression = context.Opponent.Aggression;

            if (aggression >= 70)
            {
                return new List<string>
                {
                    "You're on! Don't chicken out now!",
                    "See you at the line. Try not to embarrass yourself.",
                    "Let's settle this on the strip!",
                    "I'm gonna enjoy this.",
                    "Time to put up or shut up!"
                };
            }
            return new List<string>
            {
                "You're on. Good luck out there.",
                "Alright, let's do this.",
                "See you at the starting line.",
                "May the best driver win.",
                "Let's race!"
            };
        }

        private List<string> GetRejectedMessages(TalkContext context)
        {
            var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;

            if (repDiff > 20)
            {
                return new List<string>
                {
                    "Not worth my time. Come back when you're somebody.",
                    "I don't race nobodies.",
                    "Maybe if you had something worth betting...",
                    "Pass. I've got better things to do.",
                    "You're not in my league, kid."
                };
            }
            return new List<string>
            {
                "Not today. The terms aren't right.",
                "I'll pass on this one.",
                "That's not a fair bet. Try again.",
                "Nah, I'm not feeling it.",
                "Maybe another time."
            };
        }
    }
}
