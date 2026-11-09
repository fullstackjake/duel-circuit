namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Mekk-Knight Invoked Evolved (the player's starter; the 2026 TCG guide in docs/dev/decks): a going-second board-breaker.
    /// Its turn, in the guide's order: interrupt (Ash Blossom, Infinite Impermanence, Mulcharmy Fuwalos: the CPU's hand-trap
    /// sense plays those on the opponent's turn), break (Gameciel takes their hardest monster, Forbidden Droplet and Harpie's
    /// Feather Duster, Super Polymerization fuses their monsters away), occupy (Mekk-Knights land in columns holding 2 or
    /// more cards: Purple Nightfall banishes itself to fetch Blue Sky, which searches one Mekk-Knight per opposing card in
    /// its column), convert (the Normal Summon goes to Aleister, who fetches Invocation, or to Girsu, who sends World
    /// Legacy's Memory and makes World Legacy Tokens for Lib the World Key Blademaster, who Sets World Legacy's Secret),
    /// then finish or stabilize: Invocation fuses Aleister into Mechaba (Artemis turns him into the LIGHT material) or
    /// Purgatrio when the opponent has many cards (Almiraj for the FIRE material), and Avramax, S:P Little Knight and
    /// Accesscode Talker close. World Legacy's Memory comes last: after it resolves only Mekk-Knights can be Special
    /// Summoned for the rest of the turn.
    /// </summary>
    public sealed class MekkKnightPlaybook : Playbook
    {
        private const uint Aleister = 86120751, Invocation = 74063034, Meltdown = 47679935, Terraforming = 73628505,
            BlueSky = 20537097, IndigoEclipse = 92204263, PurpleNightfall = 28692962, RedMoon = 56809158, YellowStar = 29415459,
            Girsu = 69811710, WlToken = 46647145, WlMemory = 68191756, WlSecret = 98935722,
            Gameciel = 55063751, Droplet = 24299458, Harpies = 18144506, SuperPoly = 48130397,
            Mechaba = 75286621, Purgatrio = 12307878, Augoeides = 97300502, Caliga = 13529466, Raidjin = 49513164,
            Garura = 11765832, Mudragon = 54757758, Artemis = 34755994, Almiraj = 60303245, MorningStar = 72006609,
            Lib = 39752820, LittleKnight = 29301450, Accesscode = 86066372, Avramax = 21887175;

        /// <summary>The win conditions, kept rather than spent as material.</summary>
        private static readonly uint[] Bosses = { Mechaba, Avramax, Accesscode, Purgatrio, Augoeides };
        private static readonly uint[] Knights = { BlueSky, IndigoEclipse, PurpleNightfall, RedMoon, YellowStar };
        /// <summary>Extra Deck monsters that aren't bosses: Avramax's materials.</summary>
        private static readonly uint[] SpareExtra = { Artemis, Almiraj, MorningStar, Lib, LittleKnight, Caliga, Raidjin, Garura, Mudragon };

        public override string Deck { get; }

        public MekkKnightPlaybook(string deck = "mekk-knight-invoked")
        {
            Deck = deck;

            // ---- Combo route, going first (nothing of theirs to work off): Aleister into Mechaba, which negates on their
            // turn. (A going-second route in the guide's order lost games against the usual play, 28% to 34% in 120
            // duels: going second, the planner's freedom to reorder the turn is worth more than the fixed line.)
            ComboRoute("Aleister into Mechaba", () => V.IsMyTurn && V.IsFirstTurn && V.InHandAny(Aleister, Meltdown, Terraforming))
                .Activate("Terraforming fetches Magical Meltdown", Terraforming, () => !V.InSpellTrapZone(Meltdown) && !V.InHandAny(Meltdown))
                .Activate("Magical Meltdown fetches Aleister", Meltdown, () => !V.InSpellTrapZone(Meltdown))
                    .Pick(Searches, Aleister)
                .NormalSummon("Aleister the Invoker fetches Invocation", Aleister)
                    .Pick(Searches, Invocation)
                .SpecialSummon("Aleister into Artemis (LIGHT: Mechaba)", Artemis, () => V.OnField(Aleister) > 0 && V.InHand(Invocation) > 0)
                .Activate("Invocation fuses Invoked Mechaba", Invocation)
                    .Pick(SummonPicks, Mechaba, Augoeides, Caliga);

            // 2 / Break first, before committing monsters.
            SpecialSummon("Gameciel takes their strongest monster", Gameciel, () => V.OpponentMonsterCount > 0);
            Activate("Harpie's Feather Duster clears their backrow", Harpies, () => V.OpponentSpellTrapCount >= 2);
            Activate("Forbidden Droplet negates their monsters", Droplet, () => V.OpponentMonsterCount > 0 && !V.IsFirstTurn);
            Activate("Super Polymerization fuses their monsters away", SuperPoly, () => V.OpponentMonsterCount > 0);

            // Fields: Meltdown fetches Aleister; Terraforming fetches Meltdown.
            Activate("Magical Meltdown fetches Aleister", Meltdown, () => !V.InSpellTrapZone(Meltdown));
            Activate("Terraforming fetches Magical Meltdown", Terraforming, () => !V.InSpellTrapZone(Meltdown) && !V.InHandAny(Meltdown));

            // 3 / Occupy: Purple first (it fetches Blue Sky), Blue Sky where it searches, the rest where they hit.
            SpecialSummon("Mekk-Knight Purple Nightfall into a busy column", PurpleNightfall);
            Activate("Purple Nightfall banishes itself for Blue Sky", PurpleNightfall, () => V.OnField(PurpleNightfall) > 0 && V.InHand(BlueSky) == 0 && V.InDeck(BlueSky) > 0);
            SpecialSummon("Mekk-Knight Blue Sky where it searches", BlueSky, () => V.BestMekkColumn >= 1);
            SpecialSummon("Mekk-Knight Red Moon facing a monster", RedMoon, () => V.OpponentMonsterCount > 0);
            SpecialSummon("Mekk-Knight Yellow Star facing a Spell/Trap", YellowStar, () => V.OpponentSpellTrapCount > 0);
            SpecialSummon("Mekk-Knight Indigo Eclipse into a busy column", IndigoEclipse);

            // 4 / Convert: the Normal Summon goes to Aleister (Invocation) or, without him, Girsu (the World Legacy line).
            NormalSummon("Aleister the Invoker fetches Invocation", Aleister, () => V.InHand(Invocation) > 0 || V.InDeck(Invocation) > 0);
            NormalSummon("Girsu sends World Legacy's Memory", Girsu, () => !V.InHandAny(Aleister) || V.IsFirstTurn);
            Activate("Girsu's World Legacy Tokens", Girsu, () => V.MyMonsterCount == 1 && V.OnField(Girsu) > 0, perTurn: 2);
            SpecialSummon("Girsu and a Token into Lib the World Key Blademaster", Lib, () => V.InGraveyardAny(WlMemory, WlSecret) > 0);
            Activate("Lib Sets World Legacy's Secret", Lib, () => V.InDeck(WlSecret) > 0 && !V.InSpellTrapZone(WlSecret));
            // Aleister into a material for Invocation: LIGHT Artemis for Mechaba, FIRE Almiraj for Purgatrio when it's wide.
            SpecialSummon("Aleister into Almiraj (FIRE: Purgatrio)", Almiraj,
                () => V.OnField(Aleister) > 0 && V.InHand(Invocation) > 0 && V.OpponentCardCount >= 4 && !V.IsFirstTurn);
            SpecialSummon("Aleister into Artemis (LIGHT: Mechaba)", Artemis, () => V.OnField(Aleister) > 0 && V.InHand(Invocation) > 0);
            Activate("Invocation fuses an Invoked monster", Invocation);

            // 6 / Finish: Avramax from two Extra Deck monsters; S:P from two Effect Monsters (a Link among them); Accesscode on top.
            SpecialSummon("Mekk-Knight Crusadia Avramax from two spare Extra Deck monsters", Avramax, () => V.OnFieldAny(SpareExtra) >= 2);
            SpecialSummon("Morning Star and S:P Little Knight into Accesscode Talker", Accesscode,
                () => V.OnFieldAny(MorningStar, LittleKnight, Lib) >= 2 && !V.IsFirstTurn);
            SpecialSummon("S:P Little Knight", LittleKnight, () => V.OnFieldAny(Lib, Artemis, Almiraj, MorningStar) > 0 && V.OpponentCardCount > 0);
            SpecialSummon("Mekk-Knight of the Morning Star (a World Legacy search)", MorningStar,
                () => V.OnFieldAny(Knights) > 0 && V.InHandAny(BlueSky, IndigoEclipse, PurpleNightfall, RedMoon, YellowStar, WlMemory, WlSecret));
            // Last: Memory brings a Mekk-Knight, and from then on only Mekk-Knights can be Special Summoned this turn.
            Activate("World Legacy's Memory brings a Mekk-Knight", WlMemory);
            Boss(Bosses);

            // Nothing that isn't a Mekk-Knight can follow Memory: hold it while a Fusion, Link or Token line is still to come.
            HoldActivation(WlMemory, () => V.InHandAny(Invocation, Aleister, Girsu, SuperPoly) || V.OnFieldAny(Aleister, Girsu) > 0);
            // (Aleister's hand effect, +1000 ATK/DEF to a Fusion Monster, waits for a battle it turns or for lethal: the CPU's
            // general rule for ATK pumps, SmartDuelist.PumpTurnsBattle.)
            // Blue Sky from the hand into a column with none of their cards searches nothing: keep it for one that does.
            HoldSpecialSummon(BlueSky, () => V.BestMekkColumn < 1);

            Prefer(Searches, Terraforming, Meltdown);
            Prefer(Searches, Meltdown, Aleister);
            Prefer(Searches, Invocation, Aleister);
            // Purple Nightfall fetches Blue Sky first; Blue Sky picks by what's facing it.
            Prefer(Searches, PurpleNightfall, BlueSky, RedMoon, YellowStar, IndigoEclipse);
            PreferIf(() => V.OpponentSpellTrapCount > 0, Searches, BlueSky, YellowStar, RedMoon, IndigoEclipse, PurpleNightfall);
            Prefer(Searches, BlueSky, RedMoon, IndigoEclipse, YellowStar, PurpleNightfall);
            Prefer(Searches, MorningStar, WlSecret, WlMemory);
            // Girsu sends Memory (it enables Lib, and Secret stays in the Deck for Lib to Set).
            Prefer(ToGrave, Girsu, WlMemory, WlSecret);
            // Invocation: Mechaba to stabilize; Purgatrio when they hold many cards; Augoeides to remove a monster.
            PreferIf(() => V.OpponentCardCount >= 4 && !V.IsFirstTurn, SummonPicks, Invocation, Purgatrio, Mechaba, Augoeides, Caliga, Raidjin);
            Prefer(SummonPicks, Invocation, Mechaba, Augoeides, Purgatrio, Caliga, Raidjin);
            Prefer(SummonPicks, SuperPoly, Garura, Mudragon, Mechaba, Purgatrio, Caliga, Raidjin);
            Prefer(SummonPicks, WlMemory, PurpleNightfall, IndigoEclipse, RedMoon, YellowStar, BlueSky);
            Prefer(SummonPicks, WlSecret, Gameciel, Mechaba, Purgatrio);
            // Invocation banishes from the GY before the field; never a boss.
            Avoid(Materials, Invocation, PurpleNightfall, BlueSky, IndigoEclipse);
            Prefer(Materials, Lib, WlToken, Girsu);
            Prefer(Materials, LittleKnight, Lib, WlToken, Artemis, Almiraj, BlueSky, IndigoEclipse);
            Prefer(Materials, Accesscode, MorningStar, LittleKnight, Lib);
            Prefer(Materials, Avramax, Artemis, Almiraj, MorningStar, Lib, LittleKnight, Caliga, Raidjin, Garura, Mudragon);
            Avoid(Materials, 0, Bosses);
            // Super Polymerization's discard: a spare Mekk-Knight or a used World Legacy card, never Invocation or a hand trap.
            Prefer(ToGrave, SuperPoly, WlMemory, YellowStar, RedMoon, IndigoEclipse, Girsu);
        }
    }
}
