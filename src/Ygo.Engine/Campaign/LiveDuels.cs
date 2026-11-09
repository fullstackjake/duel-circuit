using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A campaign duel of yours being played (<see cref="Profile.LiveDuel"/>): which one, and whether a loaner deck plays it.
    /// Saved when Duel! is pressed at its versus screen (before the coin toss, as the alley holds its stakes), cleared when
    /// its result is recorded. A save loaded with one was closed mid-duel (Quit, Cmd-Q, a crash): it settles as a loss
    /// (<see cref="LiveDuels.Settle"/>). The alley's tables and brackets keep their own (<see cref="Profile.AnteLive"/>,
    /// <see cref="Bracket.LiveMatch"/>); Free Duel, lessons, exhibitions and replays never have one.
    /// </summary>
    public sealed class LiveDuel
    {
        /// <summary>One of <see cref="LiveDuels"/>' kinds: circuit, tag, cup, round, guest, locals, ladder.</summary>
        public string Kind = "";
        /// <summary>The circuit, Tag Cup, Library cup or final round (-1 for a guest or a Locals game).</summary>
        public int Event = -1;
        /// <summary>The match in it (a Locals game: the bracket's match; -1 for a guest or a final round).</summary>
        public int Match = -1;
        /// <summary>A guest's character id, a ladder seat's key ("rung.ilse", "visitor.rue": <see cref="LadderSeat.Key"/>), "" otherwise; and whether it's a guest's tag duel.</summary>
        public string Guest = "";
        public bool Tag;
        /// <summary>A loaner deck plays it (a Locals loss then pays half its placement).</summary>
        public bool Loaner;
    }

    /// <summary>A live duel settled as a loss: the result as its kind recorded it, the notice, and the scenes it raised.</summary>
    public sealed class LiveSettlement
    {
        public LiveDuel Duel = new LiveDuel();
        /// <summary>The loss's reward as recorded (a Locals game's: <see cref="BracketResult.Reward"/>), its grants included.</summary>
        public DuelReward Reward = new DuelReward();
        /// <summary>A Locals game's result (null for any other kind).</summary>
        public BracketResult? Bracket;
        /// <summary>Who you left: "Vera", "Rook & Vera" in a tag duel.</summary>
        public string Opponent = "";
        /// <summary>"You left your match against &lt;name&gt; mid-duel. That counts as a loss." and the result's summary.</summary>
        public string Summary = "";
        /// <summary>The story events the result raised (levels reached, card unlocks), as its results screen would play them.</summary>
        public List<string> Events = new List<string>();
    }

    /// <summary>
    /// Campaign duels left mid-way (the app closed or crashed): <see cref="Settle"/> records the live duel as its kind's loss
    /// through the same call a real loss makes (<see cref="Circuits.Record"/>, <see cref="Circuits.RecordTag"/>,
    /// <see cref="Library.Record"/>, <see cref="Library.RecordFinal"/>, <see cref="GuestList.Record"/>,
    /// <see cref="Brackets.RecordYours"/>, <see cref="Campaign.Ladder.SettleLoss"/>), then the card unlocks' <see cref="CardUnlocks.AfterDuel"/> as after any result:
    /// the duel's DP, EXP and level-ups, the loss counted, the run of losses, the ledger's ".loss" lines; a Locals game is
    /// lost in the bracket (the placement paid, half with a loaner), never replayed.
    /// </summary>
    public static class LiveDuels
    {
        public const string Circuit = "circuit", TagCup = "tag", Cup = "cup", Round = "round", Guest = "guest", Locals = "locals", Ladder = "ladder";

        /// <summary>
        /// The live duel, settled as a loss and cleared (once: the record goes either way). <paramref name="unlocks"/> runs
        /// <see cref="CardUnlocks.AfterDuel"/> as a result screen's caller does (null skips it); <paramref name="nameOf"/>
        /// names a Locals entrant by character id; <paramref name="ladder"/> is the Champion's Ladder a ladder duel is settled on
        /// (<see cref="Campaign.Ladder.SettleLoss"/>: rung kept, a sitting ended; without it, or for a seat it no longer has, a plain
        /// loss: <see cref="Campaign.Ladder.PlainLoss"/>). Null when nothing was live, or the duel it names isn't there any more
        /// (a Locals game already recorded, a match the data no longer has). Save after.
        /// </summary>
        public static LiveSettlement? Settle(Profile profile, Circuits circuits, Library library, GuestList guests, CardUnlocks? unlocks = null,
            Matchups? matchups = null, Func<string, string>? nameOf = null, Campaign.Ladder? ladder = null)
        {
            var live = profile.LiveDuel;
            if (live == null) return null;
            profile.LiveDuel = null;
            static bool In(int i, int count) => i >= 0 && i < count;
            int e = live.Event, m = live.Match;
            var s = new LiveSettlement { Duel = live };
            string band = "";
            switch (live.Kind)
            {
                case Circuit when In(e, circuits.All.Count) && In(m, circuits.All[e].Matches.Count):
                    s.Opponent = circuits.All[e].Matches[m].Character.Name;
                    s.Reward = circuits.Record(profile, e, m, 1, live.Loaner);
                    band = circuits.All[e].Band;
                    s.Events = Story.Events(circuits, e, s.Reward);
                    // As after a real result: a save that should have the Library gets it (its scene with the rest).
                    if (library.Open(profile, circuits)) s.Events.Add("library");
                    break;
                case TagCup when In(e, circuits.TagCups.Count) && In(m, circuits.TagCups[e].Matches.Count):
                    var (lead, partner) = circuits.TagCups[e].Matches[m];
                    s.Opponent = $"{lead.Character.Name} & {partner.Character.Name}";
                    s.Reward = circuits.RecordTag(profile, e, m, 1, live.Loaner);
                    s.Events = s.Reward.LevelUps.Select(u => "level:" + u.Level).ToList();
                    break;
                case Cup when In(e, library.Cups.Count) && In(m, library.Cups[e].Matches.Count):
                    s.Opponent = library.Cups[e].Matches[m].Character.Name;
                    s.Reward = library.Record(profile, e, m, 1, live.Loaner, built: false);
                    s.Events = Library.Events(s.Reward);
                    break;
                case Round when In(e, library.Rounds.Count):
                    s.Opponent = library.Rounds[e].Match.Character.Name;
                    s.Reward = library.RecordFinal(profile, e, 1, "");
                    s.Events = Library.Events(s.Reward);
                    break;
                case Guest when guests.All.FirstOrDefault(g => g.Character.Id == live.Guest) is Campaign.Guest guest:
                    s.Opponent = live.Tag ? $"{guest.Character.Name} & {guest.Partner.Name}" : guest.Character.Name;
                    s.Reward = GuestList.Record(profile, circuits, guest, live.Tag, 1, live.Loaner);
                    break;
                case Ladder:
                    // On the ladder passed, as its loss (rung kept, a sitting ended); with no ladder, or a seat it no longer has,
                    // a plain loss (Campaign.Ladder.PlainLoss): never dropped unpaid once the record is cleared.
                    var settled = ladder?.SettleLoss(profile, profile.Ladder, library, live.Guest)
                                  ?? Campaign.Ladder.PlainLoss(profile, profile.Ladder, library, live.Guest);
                    s.Opponent = ladder?.SeatByKey(live.Guest)?.Character.Name ?? nameOf?.Invoke(settled.Seat.Character.Id) ?? settled.Seat.Character.Name;
                    s.Reward = settled.Reward;
                    s.Events = settled.Events.ToList();
                    break;
                case Locals:
                    // Your game in a Locals bracket (an alley bracket's is Brackets.SettleAbandoned's): lost, as a concession.
                    var b = profile.Bracket;
                    if (b == null || Brackets.IsAlley(b) || b.LiveMatch < 0 || b.LiveMatch != m || b.LiveMatch != b.YourMatch()) return null;
                    var them = b.Slots[b.Opponent(b.LiveMatch)].Character;
                    s.Opponent = nameOf?.Invoke(them) ?? them;
                    s.Bracket = Brackets.RecordYours(profile, library, b, 1, live.Loaner, matchups);
                    if (s.Bracket == null) return null;
                    s.Reward = s.Bracket.Reward;
                    s.Events = s.Bracket.Events.ToList();
                    break;
                default:
                    return null;
            }
            unlocks?.AfterDuel(profile, s.Reward, band, null, live.Loaner, tag: live.Kind == TagCup || live.Tag);
            foreach (var ev in CardUnlocks.Events(s.Reward.Grants))
                if (!s.Events.Contains(ev)) s.Events.Add(ev);
            var said = s.Reward.Summary;
            s.Summary = $"You left your match against {s.Opponent} mid-duel. That counts as a loss." + (said.Length > 0 ? " " + said : "");
            return s;
        }

        // ---- The save line --------------------------------------------------------------------------

        /// <summary>"liveduel &lt;kind&gt; &lt;event&gt; &lt;match&gt; &lt;loaner&gt; &lt;tag&gt; &lt;guest|-&gt;" while a duel is live; none between duels (an older save has none).</summary>
        internal static void Write(Profile profile, StringBuilder sb)
        {
            if (profile.LiveDuel is not LiveDuel d) return;
            sb.AppendLine($"liveduel {d.Kind} {d.Event} {d.Match} {(d.Loaner ? 1 : 0)} {(d.Tag ? 1 : 0)} {(d.Guest.Length > 0 ? d.Guest : "-")}");
        }

        internal static LiveDuel? Read(string[] p)
        {
            if (p.Length < 6 || !int.TryParse(p[1], out var e) || !int.TryParse(p[2], out var m)) return null;
            return new LiveDuel { Kind = p[0], Event = e, Match = m, Loaner = p[3] != "0", Tag = p[4] != "0", Guest = p[5] == "-" ? "" : p[5] };
        }
    }
}
