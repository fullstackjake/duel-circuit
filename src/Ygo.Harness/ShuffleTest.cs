using System.Text;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Harness;

/// <summary>
/// `--shuffle-test`: a Deck an effect looked through is shuffled after the effect resolves, found or not
/// (<see cref="DuelFlags.ShuffleAfterDeckLook"/>, patches/ygopro-core/0003, docs/dev/SHUFFLE-2026-11-07.md). Seat 0 goes first
/// with the cards in hand and plays them in order (Dark Hole last, which sends its own Sangan to the GY); the deck's order
/// is read before the first activation and at the end of the turn, and MSG_SHUFFLE_DECK is counted. Each case runs with
/// the flag and without it (the old behaviour old replays keep):
/// <list type="bullet">
/// <item>Sangan with no valid monster in the Deck: shuffles with the flag, not without.</item>
/// <item>A search the player declines (a cancelable pick from the Deck): shuffles with the flag.</item>
/// <item>An excavate that picks from the top cards and uses DisableShuffleCheck: the rest keep their order.</item>
/// <item>A card picked from the Deck and put on top (no ShuffleDeck): it stays on top.</item>
/// <item>Reinforcement of the Army with no Warrior in the Deck: can't be activated.</item>
/// <item>Reinforcement finding a Warrior: every message the same with the flag as without.</item>
/// </list>
/// The test cards' scripts stand in for Pot of Greed's (a Normal Spell), so the shapes the card pool uses are checked as-is.
/// </summary>
public static class ShuffleTest
{
    private const uint Sangan = 26202165, DarkHole = 53129443, RotA = 32807846, DarkBlade = 11321183, PotOfGreed = 55144522;

    /// <summary>Monsters with more than 1500 ATK, none of them Warriors: nothing Sangan or Reinforcement can find.</summary>
    private static readonly uint[] Filler =
    {
        2311603, 4148264, 5053103, 5388481, 5464695, 7359741, 7459013, 10202894, 11091375, 11813953, 11987744, 12493482, 13429800,
        13723605, 14531242, 14575467, 14898066, 16587243, 18108166, 22916281, 23635815, 23771716, 24530661, 24611934, 24639891,
        30532390, 35052053, 36119641, 37265642, 38289717, 38445524, 39256679, 41396436, 42071342, 43096270, 44203504, 46534755,
        47226949, 47879985, 47894537,
    };

    private const string Header = "local s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetType(EFFECT_TYPE_ACTIVATE)\n\te1:SetCode(EVENT_FREE_CHAIN)\n\te1:SetOperation(s.activate)\n\tc:RegisterEffect(e1)\nend\n";

    /// <summary>Declinable search: a cancelable pick of any card in the Deck (the player cancels).</summary>
    private const string Declinable = Header +
        "function s.activate(e,tp)\n\tlocal g=Duel.SelectMatchingCard(tp,Card.IsAbleToHand,tp,LOCATION_DECK,0,1,1,true,nil)\n\tif g and #g>0 then Duel.SendtoHand(g,nil,REASON_EFFECT) end\nend\n";

    /// <summary>Excavate: look at the top 3, add 1 of them (picked from the group), DisableShuffleCheck, the rest stay.</summary>
    private const string Excavate = Header +
        "function s.activate(e,tp)\n\tlocal g=Duel.GetDecktopGroup(tp,3)\n\tDuel.ConfirmDecktop(tp,3)\n\tlocal sg=g:Select(tp,1,1,nil)\n\tDuel.DisableShuffleCheck()\n\tDuel.SendtoHand(sg,nil,REASON_EFFECT)\nend\n";

    /// <summary>Pick a card in the Deck and put it on top, with no ShuffleDeck (the 159 top-placing scripts' risky shape).</summary>
    private const string ToTop = Header +
        "function s.activate(e,tp)\n\tlocal g=Duel.SelectMatchingCard(tp,aux.TRUE,tp,LOCATION_DECK,0,1,1,nil)\n\tif #g>0 then Duel.MoveSequence(g:GetFirst(),0) end\nend\n";

    /// <summary>A Normal Spell with its own target and cost functions (for the operation-info and cost/target cases).</summary>
    private static string Full(string target, string cost, string activate) =>
        "local s,id=GetID()\nfunction s.initial_effect(c)\n\tlocal e1=Effect.CreateEffect(c)\n\te1:SetCategory(CATEGORY_TOHAND+CATEGORY_SEARCH)\n"
        + "\te1:SetType(EFFECT_TYPE_ACTIVATE)\n\te1:SetCode(EVENT_FREE_CHAIN)\n\te1:SetCost(s.cost)\n\te1:SetTarget(s.target)\n\te1:SetOperation(s.activate)\n\tc:RegisterEffect(e1)\nend\n"
        + $"function s.cost(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n{cost}\nend\n"
        + $"function s.target(e,tp,eg,ep,ev,re,r,rp,chk)\n\tif chk==0 then return true end\n{target}\nend\n"
        + $"function s.activate(e,tp)\n{activate}\nend\n";

    private const string NothingThere = "function(c) return c:IsCode(1) end";

    /// <summary>The opponent's Deck: a cancelable pick (cancelled).</summary>
    private const string OpponentLook = Header +
        "function s.activate(e,tp)\n\tDuel.SelectMatchingCard(tp,aux.TRUE,tp,0,LOCATION_DECK,1,1,true,nil)\nend\n";

    /// <summary>Group:Select over a GetMatchingGroup of the Deck (cancelled): the prompt path, no operation info.</summary>
    private const string GroupSelect = Header +
        "function s.activate(e,tp)\n\tlocal g=Duel.GetMatchingGroup(Card.IsAbleToHand,tp,LOCATION_DECK,0,nil)\n\tg:Select(tp,1,1,true,nil)\nend\n";

    /// <summary>GetMatchingGroup + Select finding nothing, declared as a search of the Deck (the operation-info detector).</summary>
    private static readonly string EmptyGroupDeclared = Full("\tDuel.SetOperationInfo(0,CATEGORY_TOHAND,nil,1,tp,LOCATION_DECK)", "",
        $"\tlocal g=Duel.GetMatchingGroup({NothingThere},tp,LOCATION_DECK,0,nil)\n\tif #g>0 then Duel.SendtoHand(g:Select(tp,1,1,nil),nil,REASON_EFFECT) end");

    /// <summary>The same with no operation info: nothing says it looked, so nothing shuffles (the detector's control).</summary>
    private static readonly string EmptyGroupUndeclared = Full("", "",
        $"\tlocal g=Duel.GetMatchingGroup({NothingThere},tp,LOCATION_DECK,0,nil)\n\tif #g>0 then Duel.SendtoHand(g:Select(tp,1,1,nil),nil,REASON_EFFECT) end");

    /// <summary>Looks in the cost and the target (finding nothing), none in the operation.</summary>
    private static readonly string CostTargetLook = Full($"\tDuel.SelectMatchingCard(tp,{NothingThere},tp,LOCATION_DECK,0,0,1,nil)",
        $"\tDuel.SelectMatchingCard(tp,{NothingThere},tp,LOCATION_DECK,0,0,1,nil)", "");

    /// <summary>A look, then the script's own ShuffleDeck.</summary>
    private const string LookThenShuffle = Header +
        "function s.activate(e,tp)\n\tDuel.SelectMatchingCard(tp,Card.IsAbleToHand,tp,LOCATION_DECK,0,1,1,true,nil)\n\tDuel.ShuffleDeck(tp)\nend\n";

    /// <summary>A search whose filter reads the top card of the Deck, finding nothing.</summary>
    private const string FilterReadsTop = Header +
        "function s.activate(e,tp)\n\tDuel.SelectMatchingCard(tp,function(c) local t=Duel.GetDecktopGroup(tp,1) return #t<0 end,tp,LOCATION_DECK,0,1,1,nil)\nend\n";

    private sealed class Overrides : IScriptProvider
    {
        private readonly IScriptProvider _inner;
        public string? Pot;
        public Overrides(IScriptProvider inner) { _inner = inner; }
        public byte[]? Load(string name) =>
            Pot != null && name.EndsWith($"c{PotOfGreed}.lua", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(Pot) : _inner.Load(name);
    }

    /// <summary>Seat 0: activates the listed cards in order; cancels a cancelable pick; otherwise picks the first card offered.</summary>
    private sealed class Player : IDuelist
    {
        private readonly List<uint> _plays;
        private readonly ScriptedDuelist _rest;
        public readonly List<uint> Offered = new List<uint>();
        public uint Picked;
        public Player(ICardDataProvider cards, IEnumerable<uint> plays) { _plays = plays.ToList(); _rest = new ScriptedDuelist(1, cards); }
        public void OnPhaseChanged() { }
        public byte[]? Respond(Prompt prompt, int attempt)
        {
            switch (prompt)
            {
                case IdlePrompt p:
                    Offered.Clear();
                    Offered.AddRange(p.Activatable.Select(a => a.Card.Code));
                    while (_plays.Count > 0)
                    {
                        int at = p.Activatable.FindIndex(a => a.Card.Code == _plays[0]);
                        _plays.RemoveAt(0);
                        if (at >= 0) return Responses.Idle(IdleAction.Activate, at);
                    }
                    return Responses.Idle(p.CanEndPhase ? IdleAction.ToEndPhase : IdleAction.ToBattlePhase);
                case SelectCardPrompt p when p.Cancelable:
                    return Responses.CancelSelection();
                case SelectCardPrompt p:
                    Picked = p.Cards[0].Code;
                    return Responses.Cards(new[] { 0 });
            }
            return _rest.Respond(prompt, attempt);
        }
    }

    private sealed class Outcome
    {
        public List<uint> Before = new List<uint>(), After = new List<uint>();
        public int Shuffles, OpponentShuffles;
        public List<uint> OpponentBefore = new List<uint>(), OpponentAfter = new List<uint>();
        public List<uint> Offered = new List<uint>();
        public uint Picked;
        public List<string> Messages = new List<string>();
    }

    private static Outcome Play(ICardDataProvider cards, IScriptProvider scripts, bool flag, uint[] hand, bool sangan, uint extra = 0, string? pot = null)
    {
        var deck = new Deck { Name = "filler" };
        deck.Main.AddRange(Filler);
        if (extra != 0) deck.Main[^1] = extra;
        var recipe = new DuelRecipe
        {
            Cards = cards, Scripts = new Overrides(scripts) { Pot = pot },
            Settings = new DuelSettings { Seed = 7, StartingHand = 0, Flags = flag ? DuelFlags.ModeMR5 : DuelFlags.ModeMR5 & ~DuelFlags.ShuffleAfterDeckLook },
            Deck0 = deck, Deck1 = deck.Clone(),
        };
        foreach (var code in hand) recipe.Setup.Add(new PlacedCard(0, code, Location.Hand, 0, Position.FaceDownDefense));
        if (sangan) recipe.Setup.Add(new PlacedCard(0, Sangan, Location.MZone, 2, Position.FaceUpAttack));
        using var duel = recipe.Create();
        var me = new Player(cards, hand);
        var runner = new DuelRunner(duel, me, new ScriptedDuelist(2, cards));
        var o = new Outcome();
        List<uint> Order(byte player = 0) => duel.QueryLocation(player, Location.Deck).Select(c => c?.Code ?? 0).ToList();
        int turns = 0;
        bool started = false;
        runner.MessageReceived += m =>
        {
            if (m[0] == Msg.NewTurn && ++turns >= 2) { runner.Concede(1, DuelRunner.ReasonConceded); return; }
            if (turns != 1) return;
            if (!started && m[0] == Msg.NewPhase) { started = true; o.Before = Order(); o.OpponentBefore = Order(1); }
            if (m[0] == Msg.ShuffleDeck && m[1] == 0) o.Shuffles++;
            if (m[0] == Msg.ShuffleDeck && m[1] == 1) o.OpponentShuffles++;
            o.Messages.Add(Convert.ToHexString(m));
        };
        duel.Start();
        runner.Run(500);
        o.After = Order();
        o.OpponentAfter = Order(1);
        o.Offered = me.Offered;
        o.Picked = me.Picked;
        return o;
    }

    /// <summary>The deck read bottom to top; "kept" when it's the old order with the moved cards taken out.</summary>
    private static bool Kept(List<uint> before, List<uint> after)
    {
        var left = new List<uint>(before);
        foreach (var gone in before.GroupBy(c => c).SelectMany(g => Enumerable.Repeat(g.Key, Math.Max(0, g.Count() - after.Count(c => c == g.Key))))) left.Remove(gone);
        return left.SequenceEqual(after);
    }

    public static int Run(ICardDataProvider cards, IScriptProvider scripts)
    {
        int bad = 0;
        void Report(string what, bool ok, string detail)
        {
            if (!ok) bad++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}  ({detail})");
        }

        // 1. Sangan finds nothing.
        var on = Play(cards, scripts, true, new[] { DarkHole }, sangan: true);
        var off = Play(cards, scripts, false, new[] { DarkHole }, sangan: true);
        Report("Sangan with no valid monster: shuffles with the flag, not without", on.Shuffles == 1 && !Kept(on.Before, on.After) && off.Shuffles == 0 && Kept(off.Before, off.After),
            $"with: {on.Shuffles} shuffle(s), order kept {Kept(on.Before, on.After)}; without: {off.Shuffles}, kept {Kept(off.Before, off.After)}");

        // 2. A declined search.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: Declinable);
        off = Play(cards, scripts, false, new[] { PotOfGreed }, sangan: false, pot: Declinable);
        Report("a declined search shuffles (with the flag; not without)", on.Shuffles == 1 && on.After.Count == 40 && off.Shuffles == 0 && off.Before.SequenceEqual(off.After),
            $"with: {on.Shuffles}, {on.After.Count} cards; without: {off.Shuffles}, unchanged {off.Before.SequenceEqual(off.After)}");

        // 3. Excavate with DisableShuffleCheck.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: Excavate);
        Report("a DisableShuffleCheck excavate keeps the Deck's order", on.Shuffles == 0 && on.After.Count == 39 && Kept(on.Before, on.After),
            $"{on.Shuffles} shuffle(s), {on.After.Count} cards, order kept {Kept(on.Before, on.After)}");

        // 4. Picked and put on top, no ShuffleDeck.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: ToTop);
        Report("a card picked from the Deck and put on top stays on top", on.Shuffles == 0 && on.After.Count == 40 && on.After[^1] == on.Picked,
            $"{on.Shuffles} shuffle(s), top {cards.Get(on.After[^1])?.Name}, picked {cards.Get(on.Picked)?.Name}");

        // 5. Reinforcement with no Warrior.
        on = Play(cards, scripts, true, new[] { RotA }, sangan: false);
        Report("Reinforcement of the Army can't activate with no Warrior in the Deck", !on.Offered.Contains(RotA) && on.Shuffles == 0,
            $"offered [{string.Join(", ", on.Offered.Select(c => cards.Get(c)?.Name))}], {on.Shuffles} shuffle(s)");

        // 6. A successful search: the same duel with and without the flag.
        on = Play(cards, scripts, true, new[] { RotA }, sangan: false, extra: DarkBlade);
        off = Play(cards, scripts, false, new[] { RotA }, sangan: false, extra: DarkBlade);
        Report("a successful search plays out exactly as before (every message the same)", on.Shuffles == 1 && on.After.Count == 39 && on.Messages.SequenceEqual(off.Messages) && on.After.SequenceEqual(off.After),
            $"with: {on.Shuffles} shuffle(s), {on.Messages.Count} messages; without: {off.Shuffles}, {off.Messages.Count}; same {on.Messages.SequenceEqual(off.Messages)}");

        // 7. The opponent's Deck.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: OpponentLook);
        Report("a look at the opponent's Deck shuffles the opponent's Deck (not yours)", on.OpponentShuffles == 1 && on.Shuffles == 0 && !on.OpponentBefore.SequenceEqual(on.OpponentAfter),
            $"opponent {on.OpponentShuffles} shuffle(s), yours {on.Shuffles}");

        // 8. Group:Select.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: GroupSelect);
        off = Play(cards, scripts, false, new[] { PotOfGreed }, sangan: false, pot: GroupSelect);
        Report("Group:Select over the Deck (declined) shuffles", on.Shuffles == 1 && off.Shuffles == 0, $"with: {on.Shuffles}; without: {off.Shuffles}");

        // 9. GetMatchingGroup + Select finding nothing: the operation-info detector.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: EmptyGroupDeclared);
        off = Play(cards, scripts, false, new[] { PotOfGreed }, sangan: false, pot: EmptyGroupDeclared);
        var control = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: EmptyGroupUndeclared);
        Report("a search finding nothing through GetMatchingGroup + Select shuffles (its operation info names the Deck)", on.Shuffles == 1 && off.Shuffles == 0 && control.Shuffles == 0,
            $"with: {on.Shuffles}; without: {off.Shuffles}; with no operation info: {control.Shuffles}");

        // 10. Cost and target looks.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: CostTargetLook);
        bool chained = on.Messages.Any(m => m.StartsWith("46", StringComparison.Ordinal)); // MSG_CHAINING (70): it was activated
        Report("looks in the cost and the target don't shuffle", chained && on.Shuffles == 0 && on.Before.SequenceEqual(on.After), $"activated {chained}, {on.Shuffles} shuffle(s)");

        // 11. A look plus the script's own ShuffleDeck.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: LookThenShuffle);
        Report("a look plus ShuffleDeck shuffles exactly once", on.Shuffles == 1, $"{on.Shuffles} shuffle(s)");

        // 12. A filter reading the top card.
        on = Play(cards, scripts, true, new[] { PotOfGreed }, sangan: false, pot: FilterReadsTop);
        Report("a filter that reads the top card doesn't cancel the look", on.Shuffles == 1, $"{on.Shuffles} shuffle(s)");

        Console.WriteLine(bad == 0 ? "shuffle test: all passed" : $"shuffle test: {bad} failed");
        return bad == 0 ? 0 : 1;
    }
}
