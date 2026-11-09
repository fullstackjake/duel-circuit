namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Radiant Typhoon (2026): Wind monsters built around Mystical Space Typhoon. Eldam, Swen and Meghala Special Summon
    /// themselves while the opponent has no Spell/Trap (or MST is in the GY); Eldam searches a Radiant Typhoon monster,
    /// Swen a Radiant Typhoon Spell or MST. The engine runs on Quick-Play activations: Krosea, Fonix and Varuroon come
    /// out of the hand, Meghala calls another from the Deck, and Marine Eidolon places Mandate. The trick is MST chained
    /// to the deck's own Radiant Typhoon Quick-Play: the Spell resolves, MST destroys it, and it Sets itself again for the
    /// opponent's turn.
    /// </summary>
    public sealed class RadiantTyphoonPlaybook : Playbook
    {
        private const uint Eldam = 54143349, Meghala = 27755794, Swen = 80538047, ShieldBearer = 34242278, Krosea = 16922142,
            Fonix = 85315450, Varuroon = 53927851, Shiina = 12197223, Bandit = 24203749, Mst = 5318639, SuperPoly = 48130397,
            Chant = 67115133, Vision = 20508881, Ascendance = 25940932, Mandate = 53813120,
            TotemBird = 71068247, Number75 = 71166481, MarineEidolon = 39341885, Greatfly = 90512490, Wynn = 30674956,
            Hraesvelgr = 49105782, Ulti = 96113307;

        /// <summary>The Radiant Typhoon Quick-Play Spells: they Set themselves again when MST destroys them.</summary>
        private static readonly uint[] TyphoonSpells = { Vision, Chant, Ascendance };
        private static readonly uint[] Starters = { Eldam, Swen, Meghala };

        public override string Deck => "radiant-typhoon";

        private bool MstReady => V.InHand(Mst) > 0 || V.InSpellTrapZone(Mst);

        public RadiantTyphoonPlaybook()
        {
            LeadWithRules();
            // The free bodies first: Eldam finds Swen, Swen finds the Spells.
            SpecialSummon("Radiant Typhoon Eldam summons itself", Eldam);
            SpecialSummon("Radiant Typhoon Swen summons itself", Swen);
            Activate("Noble Knight's Shield-Bearer finds Eldam", ShieldBearer, () => !V.InHandAny(Eldam) && V.InDeck(Eldam) > 0);
            Activate("Radiant Typhoon Chant finds a starter", Chant, () => !V.InHandAny(Starters) && V.MyMonsterCount == 0);
            NormalSummon("Normal Summon Eldam for another search", Eldam);
            NormalSummon("Normal Summon Swen for another search", Swen);
            SpecialSummon("Radiant Typhoon Meghala summons itself", Meghala);
            // Marine Eidolon searches MST and places Mandate on every Quick-Play after.
            SpecialSummon("Link into Radiant Typhoon Varuroon, the Marine Eidolon", MarineEidolon,
                () => V.OnField(MarineEidolon) == 0 && V.OnFieldAny(Eldam, Swen, Meghala) >= 2);
            // The trick: a Radiant Typhoon Quick-Play with MST to chain onto it.
            Activate("Radiant Typhoon Vision draws two (MST chains onto it)", Vision, () => V.IsMyMainPhase && MstReady);
            Activate("Radiant Typhoon Ascendance brings back a Radiant Typhoon", Ascendance,
                () => V.IsMyMainPhase && MstReady && V.InGraveyardAny(Krosea, Eldam, Swen, Meghala) > 0 && V.MyMonsterCount < 5);
            Activate("Radiant Typhoon Chant (MST chains onto it)", Chant, () => V.IsMyMainPhase && MstReady);
            SpecialSummon("Xyz into Number 75: Bamboozling Gossip Shadow", Number75, () => V.IsFirstTurn && V.NonTunersOfLevel(3) >= 2);
            SpecialSummon("Xyz into Totem Bird", TotemBird, () => V.NonTunersOfLevel(3) >= 2);

            ChainWhen("MST onto our own Radiant Typhoon Spell: it Sets itself again", Mst, () => V.ChainHasMine(TyphoonSpells));
            ChainWhen("Radiant Typhoon Krosea joins on a Quick-Play", Krosea, () => true);
            ChainWhen("Radiant Typhoon Fonix joins on a Quick-Play", Fonix, () => true);
            ChainWhen("Radiant Typhoon Varuroon joins on a Quick-Play", Varuroon, () => V.IsMyTurn || V.ChainTopIsTheirs);
            OwnTargets(Mst);

            // The Radiant Typhoon Spells wait for the Main Phase on our turn,
            // and for the free summons first, so Meghala is out when they're activated.
            foreach (var spell in TyphoonSpells)
                HoldActivation(spell, () => V.IsMyTurn && (!V.IsMyMainPhase || V.InHandAny(Starters) && V.MyMonsterCount < 4));
            // MST without our own Spell on the chain waits for a real target.
            HoldActivation(Mst, () => V.OpponentSpellTrapCount == 0 && !V.ChainHasMine(TyphoonSpells));
            // Bandit Tributes hand monsters going first: it waits while the engine has a starter.
            HoldNormalSummon(Bandit, () => V.InHandAny(Starters) || V.OpponentMonsterCount == 0);
            HoldSpecialSummon(Greatfly);
            HoldSpecialSummon(Wynn);
            HoldSpecialSummon(Ulti);
            HoldSpecialSummon(Hraesvelgr, () => V.IsFirstTurn);
            Boss(Krosea, Fonix, Varuroon);

            // MST on our own Radiant Typhoon Spell while the opponent has none to hit.
            PreferIf(() => V.OpponentSpellTrapCount == 0, new[] { DuelContext.HintDestroy, DuelContext.HintTarget }, Mst, Vision, Chant, Ascendance, Mandate);
            PreferIf(() => V.InHand(Swen) == 0, Searches, Eldam, Swen, Krosea, Meghala);
            Prefer(Searches, Eldam, Krosea, Meghala, Fonix, Mst);
            PreferIf(() => !MstReady, Searches, Swen, Mst, Vision);
            Prefer(Searches, Swen, Vision, Chant, Ascendance, Mst);
            Prefer(Searches, Krosea, Varuroon, Fonix, Vision, Mst);
            Prefer(Searches, Chant, Eldam, Swen, Meghala, Mst);
            Prefer(Searches, ShieldBearer, Eldam);
            Prefer(SummonPicks, Meghala, Krosea, Eldam, Swen);
            Prefer(SummonPicks, Ascendance, Krosea, Eldam, Swen, Meghala);
            // Fonix and Varuroon come back from the GY when MST is activated: they're the discards.
            Prefer(ToGrave, 0, Fonix, Varuroon, Bandit, Ascendance, Chant);
            Avoid(ToGrave, 0, Mst, Krosea, Shiina);
            Prefer(Materials, 0, Eldam, Swen, Meghala, ShieldBearer);
            Avoid(Materials, 0, Krosea, Fonix, Varuroon, MarineEidolon);
        }
    }
}
