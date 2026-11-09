namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// The Junk Synchron Synchro engine with a Cyber Dragon side engine. Used by decks/synchron.ydk (the full
    /// combo deck) and decks/modern.ydk (which shares the Cyber Dragon and Junk Synchron / Halq lines);
    /// rules about cards a deck does not run simply never fire.
    ///
    /// Setup: Tuning / Junk Converter find Junk Synchron; Foolish Burial or One for One put Quillbolt in the GY.
    /// Start: Normal Summon Junk Synchron, revive a Level 2 or lower monster (Quillbolt first).
    /// Extend: Link into Crystron Halqifibrax for another Tuner from the Deck; Quillbolt, Jet Synchron and
    /// Glow-Up Bulb revive themselves from the GY; Junk Speeder summons more Synchron Tuners.
    /// Finish: the biggest Synchro the engine allows (Baronne de Fleur, Junk Destroyer, Stardust...); going
    /// first, Halq + a Synchro become S:P Little Knight to hold up an interruption.
    /// Cyber line: free Cyber Dragons when only the opponent has monsters; two become Nova, then Infinity.
    /// </summary>
    public sealed class SynchronPlaybook : Playbook
    {
        private const uint JunkSynchron = 63977008, Quillbolt = 23571046, Doppelwarrior = 53855409, JetSynchron = 9742784,
            GlowUpBulb = 67441435, JunkForward = 56410040, JunkConverter = 11069680, CyberDragon = 70095154,
            MaxxC = 23434538, Veiler = 97268402,
            Tuning = 96363153, FoolishBurial = 81439173, OneForOne = 2295440, PotOfProsperity = 84211599,
            JunkSpeeder = 77075360, JunkWarrior = 60800381, Armades = 88033975, CoralDragon = 42566602,
            ShootingRiser = 68431965, Stardust = 44508094, JunkDestroyer = 74860293, Baronne = 84815190,
            Halqifibrax = 50588353, LittleKnight = 29301450, Knightmare = 2857636, Nova = 58069384,
            Infinity = 10443957, FormulaSynchron = 50091196;

        private static readonly ulong[] Materials =
            { DuelContext.HintLinkMaterial, DuelContext.HintSynchroMaterial, DuelContext.HintXyzMaterial };
        private static readonly ulong[] SummonPicks = { DuelContext.HintSpSummon, DuelContext.HintTarget };
        private static readonly ulong[] Searches = { DuelContext.HintAddToHand };
        private static readonly ulong[] ToGrave = { DuelContext.HintToGrave, DuelContext.HintDiscard };

        private readonly string _deck;
        public override string Deck => _deck;

        public SynchronPlaybook(string deck)
        {
            _deck = deck;
            // --- Cyber line: free bodies need our field empty, so they come first.
            SpecialSummon("Cyber Dragon (opponent has monsters, we have none)", CyberDragon, perTurn: 2);
            SpecialSummon("Nova into Cyber Dragon Infinity", Infinity);
            SpecialSummon("two Cyber Dragons into Cyber Dragon Nova", Nova, () => V.OnField(CyberDragon) >= 2);

            // --- Setup: get a revive target into the GY.
            Activate("Foolish Burial a revive target", FoolishBurial, () => V.InGraveyard(Quillbolt) == 0);
            Activate("One for One into a Level 1 Tuner", OneForOne);

            // --- Start and extend.
            NormalSummon("Junk Synchron to start the Synchro line", JunkSynchron,
                () => V.GraveyardMonstersAtOrBelowLevel(2) > 0 || V.MyMonsterCount > 0);
            Activate("Quillbolt revives itself (we control a Tuner)", Quillbolt, () => V.ControlTuner);
            Activate("Jet Synchron revives itself for a Synchro", JetSynchron, () => HasNonTuner);
            Activate("Glow-Up Bulb revives itself for a Synchro", GlowUpBulb, () => HasNonTuner);
            // Halq only when the line continues: a second small body to link with (never Cyber Dragon),
            // a Tuner to pull from the Deck, and a non-Tuner to Synchro with afterwards (Quillbolt / Doppelwarrior).
            SpecialSummon("Tuner + small body into Crystron Halqifibrax", Halqifibrax,
                () => V.ControlTuner && SmallBodies >= 2
                      && V.InDeck(JunkSynchron) + V.InDeck(JetSynchron) + V.InDeck(GlowUpBulb) > 0
                      && (V.InGraveyard(Quillbolt) + V.OnField(Quillbolt) + V.InHand(Doppelwarrior) > 0));

            // --- Finish: biggest legal Synchro first (the engine only offers what the materials allow).
            SpecialSummon("Synchro into Baronne de Fleur", Baronne);
            SpecialSummon("Synchro into Junk Destroyer", JunkDestroyer, () => V.OpponentCardCount > 0);
            SpecialSummon("Synchro into Stardust Dragon", Stardust);
            SpecialSummon("Synchro into Shooting Riser Dragon", ShootingRiser);
            SpecialSummon("Synchro into Coral Dragon", CoralDragon, () => V.OpponentCardCount > 0);
            SpecialSummon("Synchro into Junk Speeder for more Tuners", JunkSpeeder,
                () => V.InDeck(JunkSynchron) + V.InDeck(JetSynchron) + V.InDeck(GlowUpBulb) > 0);
            SpecialSummon("Synchro into Junk Warrior", JunkWarrior);
            SpecialSummon("Synchro into Armades", Armades);
            SpecialSummon("Halq + Synchro into S:P Little Knight (going first)", LittleKnight,
                () => V.IsFirstTurn && V.OnField(Halqifibrax) > 0);

            // --- Don't spend pieces the combo needs.
            HoldActivation(JetSynchron, () => !HasNonTuner);
            HoldActivation(GlowUpBulb, () => !HasNonTuner);
            HoldSpecialSummon(Knightmare, () => V.OpponentCardCount == 0);
            HoldSpecialSummon(LittleKnight, () => !V.IsFirstTurn);
            HoldSpecialSummon(FormulaSynchron); // Level 2 Synchro: only worth it as part of a longer line
            HoldNormalSummon(JetSynchron, () => !HasNonTuner); // a GY-revival Tuner, not a body
            // Halq's quick effect would trade it for a small Tuner Synchro; keep it on the field.
            HoldActivation(Halqifibrax, () => !V.IsMyTurn);

            // --- Selections.
            Prefer(Searches, Tuning, JunkSynchron, JetSynchron);
            Prefer(Searches, JunkConverter, JunkSynchron, JetSynchron);
            Prefer(ToGrave, FoolishBurial, Quillbolt, GlowUpBulb, JetSynchron, Doppelwarrior);
            Prefer(ToGrave, OneForOne, Quillbolt, Doppelwarrior, JunkForward, JunkConverter);
            Prefer(ToGrave, JunkConverter, JetSynchron, GlowUpBulb, JunkSynchron);
            Prefer(ToGrave, JetSynchron, Quillbolt, Doppelwarrior, JunkForward);
            Prefer(SummonPicks, OneForOne, JetSynchron, GlowUpBulb);
            Prefer(SummonPicks, Halqifibrax, JunkSynchron, JetSynchron, GlowUpBulb);
            Prefer(SummonPicks, JunkSynchron, Quillbolt, Doppelwarrior, JetSynchron, GlowUpBulb, Veiler, MaxxC);

            // Materials: spend the bodies that get banished or are small; keep Cyber Dragons for their own line.
            Prefer(Materials, Halqifibrax, JunkSynchron, JetSynchron, GlowUpBulb, Quillbolt, Doppelwarrior, JunkForward);
            Prefer(Materials, 0, Quillbolt, JetSynchron, GlowUpBulb, Doppelwarrior, JunkForward, JunkSynchron);
            Avoid(Materials, 0, CyberDragon);
            Prefer(Materials, Nova, CyberDragon);
            Prefer(Materials, LittleKnight, Halqifibrax, JunkWarrior, Armades, JunkSpeeder);

            // Pot of Prosperity banishes Extra Deck cards face-down as its cost: never the combo pieces.
            Avoid(new[] { DuelContext.HintRemove }, PotOfProsperity,
                Halqifibrax, Baronne, JunkDestroyer, Stardust, JunkSpeeder, JunkWarrior, Nova, Infinity, LittleKnight);
        }

        private bool HasNonTuner => V.NonTunersOfLevel(1) + V.NonTunersOfLevel(2) + V.NonTunersOfLevel(3)
                                    + V.NonTunersOfLevel(4) + V.NonTunersOfLevel(5) > 0;

        private int SmallBodies => V.OnField(Quillbolt) + V.OnField(Doppelwarrior) + V.OnField(JunkForward)
                                   + V.OnField(Veiler) + V.OnField(MaxxC) + V.OnField(JetSynchron) + V.OnField(GlowUpBulb);
    }
}
