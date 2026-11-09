using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Engine
{
    /// <summary>
    /// One team in a tag duel: two duelists sharing a field, Graveyard and LP. The core keeps one of them "active" per
    /// team (their Deck, hand and Extra Deck are the ones in play) and swaps at the start of each of the team's turns
    /// (after the first), announcing it with <see cref="Msg.TagSwap"/>. Every prompt goes to whoever is active; feed
    /// this seat every core message through <see cref="Observe"/> so it follows the swaps.
    /// </summary>
    public sealed class TagSeat : IDuelist
    {
        public int Team { get; }
        /// <summary>The duelists by core duelist index: 0 is active at the start, 1 swaps in first.</summary>
        public IDuelist[] Members { get; }
        public int Active { get; private set; }
        public IDuelist Current => Members[Active];
        /// <summary>The team's lead is core duelist 0 (this team takes the duel's first turn), else duelist 1.</summary>
        public bool LeadFirst { get; }

        public TagSeat(int team, IDuelist first, IDuelist second, bool leadFirst = true)
        {
            Team = team;
            Members = new[] { first, second };
            LeadFirst = leadFirst;
        }

        public void Observe(byte[] message)
        {
            if (message.Length > 1 && message[0] == Msg.TagSwap && message[1] == Team) Active = 1 - Active;
        }

        public void OnPhaseChanged()
        {
            foreach (var m in Members) m.OnPhaseChanged();
        }

        public byte[]? Respond(Prompt prompt, int attempt) => Current.Respond(prompt, attempt);
    }
}

namespace Ygo.Engine
{
    /// <summary>
    /// Tag duels with the classic turn order: the starting team's lead, the other lead, the starting team's partner, the
    /// other partner, and around again. The core swaps a team's duelist at each of its turns except the duel's first,
    /// so the starting team's lead goes in as its first duelist and the other team's lead as its second (it swaps in
    /// for that team's first turn). Who starts is <see cref="DuelSettings.FirstPlayer"/>.
    /// </summary>
    public static class TagDuel
    {
        public static DuelRecipe Recipe(ICardDataProvider cards, IScriptProvider scripts, DuelSettings settings,
            Deck leadA, Deck partnerA, Deck leadB, Deck partnerB)
        {
            bool aStarts = settings.FirstPlayer == 0;
            return new DuelRecipe
            {
                Cards = cards, Scripts = scripts, Settings = settings,
                Deck0 = aStarts ? leadA : partnerA, Partner0 = aStarts ? partnerA : leadA,
                Deck1 = aStarts ? partnerB : leadB, Partner1 = aStarts ? leadB : partnerB,
            };
        }

        /// <summary>The two teams' seats, from each team's lead and partner (same order as <see cref="Recipe"/>).</summary>
        public static (TagSeat a, TagSeat b) Seats(IDuelist leadA, IDuelist partnerA, IDuelist leadB, IDuelist partnerB, int firstPlayer = 0) =>
            firstPlayer == 0
                ? (new TagSeat(0, leadA, partnerA, leadFirst: true), new TagSeat(1, partnerB, leadB, leadFirst: false))
                : (new TagSeat(0, partnerA, leadA, leadFirst: false), new TagSeat(1, leadB, partnerB, leadFirst: true));

        /// <summary>Which of a team's duelists is playing: 0 the lead, 1 the partner.</summary>
        public static int Role(TagSeat seat) => seat.LeadFirst ? seat.Active : 1 - seat.Active;
    }
}
