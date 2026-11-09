namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Artmage (2026): Artmage Power Patron Fusion Summons at Quick-Effect speed in the Main Phase, and searches an Artmage
    /// Spell/Trap when it goes to the GY. It comes from the Deck two ways: Medius the Pure Special Summons it, and Pendulum
    /// Treasure puts Nervedo face-up in the Extra Deck, which Special Summons it. Graflare and Finmel then come out of the
    /// hand (Graflare Sets an Artmage Spell, Finmel draws), and the Fusions are Diactorus (Medius + an Artmage: a negate),
    /// Non-Finito (it Sets an Artmage card and Fusion Summons again on the opponent's turn) and Nerva.
    /// </summary>
    public sealed class ArtmagePlaybook : Playbook
    {
        private const uint ArtmagePowerPatron = 23829452, Medius = 97556336, TheoMedius = 90875418, Graflare = 60946049,
            Finmel = 34541940, Junoldo = 10266279, Jupredo = 43871165, Nervedo = 17473466, Bandit = 24203749, Vidolium = 70488851,
            PendulumTreasure = 26237713, Theorealize = 48739627, Terminus = 25661743, PastLull = 36709484, Vandalism = 1122030,
            Citrinitas = 37279096, Pact = 23599634, Impasto = 44654994, ArsPurification = 62368221, ArsUnification = 99753860,
            ArsInfinity = 36270527,
            NonFinito = 74631897, Diactorus = 27184601, Nerva = 53589300, Dragostapelia = 69946549, Junora = 5914858,
            Medicurius = 4063756, Purification = 31822037, Dharc = 8264361, BeyondThePendulum = 22125101, LittleKnight = 29301450;

        private static readonly uint[] Artmages = { ArtmagePowerPatron, Graflare, Finmel, Nervedo, NonFinito, Diactorus, Nerva };

        public override string Deck => "artmage";

        public ArtmagePlaybook()
        {
            LeadWithRules();
            // Artmage Power Patron out of the Deck first: Medius, or Pendulum Treasure into Nervedo.
            NormalSummon("Medius the Pure calls Artmage Power Patron", Medius, () => V.OnField(ArtmagePowerPatron) == 0);
            NormalSummon("Artmage Power Patron from the hand", ArtmagePowerPatron, () => V.InDeck(ArtmagePowerPatron) == 0 && !V.InHandAny(Medius));
            Activate("Theorealize Past Lull calls Medius", PastLull);
            Activate("Pendulum Treasure: Nervedo calls Artmage Power Patron", PendulumTreasure,
                () => V.OnField(ArtmagePowerPatron) == 0 || V.InExtraDeck(Vidolium) > 0);
            Activate("Artmage Vandalism finds Medius", Vandalism, () => V.InHand(Medius) == 0 && V.OnField(Medius) == 0);
            Activate("Terminus finds Medius", Terminus, () => V.InHand(Medius) == 0);
            Activate("Ars Magna Citrinitas finds Medius", Citrinitas);
            Activate("Artmage Pact calls an Artmage", Pact);
            // With an Artmage out: the hand ones join.
            Activate("Artmage Graflare joins and Sets an Artmage Spell", Graflare, () => V.OnFieldAny(Artmages) > 0 || V.InSpellTrapZoneAny(Vandalism, Citrinitas, Pact));
            Activate("Artmage Finmel joins and draws", Finmel, () => V.OnFieldAny(Artmages) > 0 || V.InSpellTrapZoneAny(Vandalism, Citrinitas, Pact));
            Activate("Theorealize Medius joins and finds Past Lull", TheoMedius);
            // A Power Patron Pendulum as a Scale is the Power Patron card Theorealize Medius needs on the field.
            foreach (var scale in new[] { Jupredo, Junoldo, Vidolium, Nervedo })
                Activate("A Power Patron Scale lets Theorealize Medius summon itself", scale,
                    () => V.InHand(TheoMedius) > 0 && V.InHand(scale) > 0 && V.OnFieldAny(Medius, ArtmagePowerPatron) == 0 && V.PendulumScales == 0);
            // Artmage Power Patron's Fusion, at the end of the line on our turn (it searches again from the GY).
            Activate("Artmage Power Patron Fusion Summons", ArtmagePowerPatron,
                () => V.IsMyMainPhase && V.MyMonsterCount >= 2 && !V.InHandAny(Medius, Graflare, Finmel) || !V.IsMyTurn);
            Activate("Medius the Pure comes back", Medius, () => V.MyMonsterCount < 3);

            // Bandit eats two hand cards: only without Medius (the combo first, while it can still start).
            HoldNormalSummon(Bandit, () => V.InHandAny(Medius, TheoMedius, ArtmagePowerPatron) || V.InHand(PendulumTreasure) > 0 && V.InDeck(Nervedo) > 0);
            HoldSpecialSummon(Dharc);
            HoldSpecialSummon(BeyondThePendulum);
            HoldSpecialSummon(LittleKnight, () => V.IsFirstTurn);
            Boss(Nerva, Diactorus, NonFinito);
            // Not removal of our own cards: Theorealize Medius summons itself, the Ars Magna banish themselves to search.
            OwnTargets(TheoMedius, ArsPurification, ArsUnification, ArsInfinity);

            Prefer(SummonPicks, Medius, ArtmagePowerPatron, TheoMedius);
            Prefer(Searches, Medius, ArtmagePowerPatron, TheoMedius);
            // Pendulum Treasure: Nervedo while Artmage Power Patron is still in the Deck, then Vidolium (it searches).
            PreferIf(() => V.OnField(ArtmagePowerPatron) == 0 && V.InDeck(ArtmagePowerPatron) > 0, Asks(PendulumTreasure, 0), 0, Nervedo);
            PreferIf(() => V.OnField(ArtmagePowerPatron) > 0 || V.InDeck(ArtmagePowerPatron) == 0, Asks(PendulumTreasure, 0), 0, Vidolium, Nervedo, Junoldo, Jupredo);
            Prefer(Asks(Medius, 2), 0, ArtmagePowerPatron, TheoMedius);
            Prefer(new[] { DuelContext.HintSet }, 0, Impasto, Pact, Vandalism, Citrinitas);
            Prefer(SummonPicks, Nervedo, ArtmagePowerPatron, Graflare, Finmel);
            Prefer(SummonPicks, Pact, Medius, Graflare, Finmel);
            Prefer(SummonPicks, PastLull, Medius);
            Prefer(SummonPicks, ArtmagePowerPatron, Diactorus, Nerva, NonFinito);
            Prefer(SummonPicks, 0, Diactorus, Nerva, NonFinito, ArtmagePowerPatron, Medius);
            Prefer(Searches, ArtmagePowerPatron, Pact, Impasto, Vandalism);
            Prefer(Searches, Citrinitas, Medius, ArsInfinity);
            Prefer(Searches, Vidolium, Terminus, ArtmagePowerPatron, Medius);
            Prefer(Searches, Terminus, Medius);
            Prefer(Searches, TheoMedius, PastLull, Theorealize);
            Prefer(Searches, 0, Medius, ArtmagePowerPatron, Pact, PendulumTreasure);
            Prefer(ToGrave, Terminus, Nervedo, Jupredo, Junoldo);
            Prefer(ToGrave, 0, Bandit, Vidolium, Jupredo, Junoldo);
            Prefer(Materials, 0, ArtmagePowerPatron, Medius, TheoMedius, Graflare, Finmel, Bandit);
            Avoid(Materials, 0, Nerva, Diactorus);
        }
    }
}
