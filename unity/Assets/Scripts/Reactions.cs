using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine;
using Personality = Ygo.Engine.AI.Personality;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// When duelists react during a duel, and what they say. Moments: "summon" (they bring out something big),
    /// "strike" (they deal a big hit), "hit" (they take one), "danger" (their LP falls to 1000 or less). Characters'
    /// own lines come from their profile; everyone else gets lines that fit their style. Each duelist reacts at most
    /// every few seconds, and not to every summon, so it stays a spice rather than a chatter.
    /// </summary>
    public sealed class Reactions
    {
        private static readonly Dictionary<string, string[]> Bold = new Dictionary<string, string[]>
        {
            ["summon"] = new[] { "Here it comes!", "Try stopping this one!" },
            ["strike"] = new[] { "Ha! Take that!", "Right in the Life Points!" },
            ["hit"] = new[] { "Not bad. My turn next.", "That all you've got?" },
            ["danger"] = new[] { "I'm not finished yet!" },
        };
        private static readonly Dictionary<string, string[]> Careful = new Dictionary<string, string[]>
        {
            ["summon"] = new[] { "Exactly as planned.", "Everything in its place." },
            ["strike"] = new[] { "Patience pays off.", "Just as I calculated." },
            ["hit"] = new[] { "Hm. I'll remember that.", "A setback. Nothing more." },
            ["danger"] = new[] { "This is getting close." },
        };
        private static readonly Dictionary<string, string[]> Even = new Dictionary<string, string[]>
        {
            ["summon"] = new[] { "Let's see you handle this!", "My ace is on the field!" },
            ["strike"] = new[] { "Right on target!", "How's that feel?" },
            ["hit"] = new[] { "Ugh, nice one.", "Okay, that hurt." },
            ["danger"] = new[] { "Still standing!" },
        };

        private readonly Random _rng = new Random();
        private readonly float[] _last = { -100, -100 };
        private readonly int[] _lp = { 8000, 8000 };

        public void Reset(int lp0, int lp1)
        {
            _last[0] = _last[1] = -100;
            _lp[0] = lp0;
            _lp[1] = lp1;
        }

        public static string Line(Personality who, string kind, Random rng)
        {
            if (who.Lines.TryGetValue(kind, out var own) && own.Count > 0) return own[rng.Next(own.Count)];
            var set = who.Aggression >= 0.65 || who.Caution <= 0.2 ? Bold : who.Caution >= 0.65 || who.Patience >= 0.7 ? Careful : Even;
            var lines = set[kind];
            return lines[rng.Next(lines.Length)];
        }

        /// <summary>
        /// Looks at what just happened and returns the reactions to show: (player, moment, delay in seconds).
        /// <paramref name="lifePoints"/> are both players' LP after these events.
        /// </summary>
        public List<(int player, string kind, float delay)> Read(IReadOnlyList<DuelEvent> events, ICardDataProvider cards, int[] lifePoints, float now)
        {
            var found = new List<(int, string, float)>();
            void Add(int player, string kind, float delay, bool always = false)
            {
                if (player < 0 || player > 1 || found.Any(f => f.Item1 == player)) return;
                if (!always && now - _last[player] < 6) return;
                _last[player] = now;
                found.Add((player, kind, delay));
            }
            // Falling into danger always gets a reaction (it happens once).
            for (int p = 0; p < 2; p++)
                if (_lp[p] > 1000 && lifePoints[p] > 0 && lifePoints[p] <= 1000) Add(p, "danger", 0.7f, always: true);
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case DuelEventKind.NormalSummon:
                    case DuelEventKind.SpecialSummon:
                    case DuelEventKind.FlipSummon:
                        var info = cards.Get(e.Code);
                        bool big = info != null && ((info.Level & 0xff) >= 7 || (info.Type & CardType.ExtraDeckTypes) != 0 || info.Attack >= 2500);
                        if (big && _rng.NextDouble() < 0.7) Add(e.To.Controller, "summon", 1.4f);
                        break;
                    case DuelEventKind.Damage when e.Amount >= 1500:
                        // The one hit reacts; whoever hit them may gloat instead, now and then.
                        if (_rng.NextDouble() < 0.6) Add(e.Player, "hit", 0.6f);
                        else Add(1 - e.Player, "strike", 0.6f);
                        break;
                }
            }
            _lp[0] = lifePoints[0];
            _lp[1] = lifePoints[1];
            return found;
        }
    }
}
