namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Unchained (2023): Fiends that destroy their own cards for value. Every Set Unchained Spell/Trap (Prison, Chamber,
    /// Escape, Wailing) summons an Unchained monster from the Deck when it's destroyed, and the Twins summon one when they
    /// are; Aruha, Sharvara and Rakea destroy a card of ours to summon themselves (or just to trigger it). So the Traps go
    /// down first, then the destroying starts. Tour Guide brings Fiendish Rhino Warrior (in the GY it sends Sharvara, which
    /// Sets an Unchained Trap from the Deck); two Fiends become Unchained Soul Lord of Yama (it searches an Unchained
    /// monster). The bodies link up into Unchained Soul of Rage and Unchained Abomination (it destroys a card each time one
    /// is destroyed), and two Level 6s into D/D/D Wave High King Caesar. Going second Rage and Anguish take the opponent's
    /// monsters as Link Material. Vice King Requiem stays in the Deck's corner: summoning it locks us out of the Fiends.
    /// </summary>
    public sealed class UnchainedPlaybook : Playbook
    {
        private const uint Abominable = 1966438, Requiem = 25857977, Sharvara = 41165831, Shyama = 88554436, Aruha = 26236560,
            Rakea = 53624265, Sarama = 31588572, Rhino = 81035362, TourGuide = 10802915, Ash = 14558127, Veiler = 97268402,
            Gate = 46372010, Wailing = 54807656, Prison = 27412542, Prosperity = 84211599, Chamber = 80801743, Escape = 53417695,
            Typhon = 93039339, Machinex = 46593546, Caesar = 79559912, Abomination = 29479265, Anguish = 93084621,
            Unicorn = 38342335, Yama = 24269961, Dharc = 8264361, Rage = 67680512, LittleKnight = 29301450, Muckraker = 71607202;

        /// <summary>The Set cards that summon from the Deck when destroyed: what our own destroying aims at.</summary>
        private static readonly uint[] Floaters = { Escape, Chamber, Wailing, Prison };
        private static readonly uint[] Twins = { Aruha, Rakea, Sarama };
        private static readonly uint[] Souls = { Sharvara, Shyama, Abominable };
        private static readonly uint[] SmallFiends = { TourGuide, Rhino, Aruha, Rakea, Sarama };
        private static readonly uint[] LinkBodies = { TourGuide, Rhino, Aruha, Rakea, Sarama, Sharvara, Shyama, Abominable };

        public override string Deck => "unchained";

        public UnchainedPlaybook()
        {
            Activate("Pot of Prosperity digs", Prosperity);
            Activate("Abomination's Prison searches", Prison, () => V.IsMyTurn && V.InHand(Prison) > 0);
            // The floaters go down first, so destroying them later summons from the Deck.
            SetSpellTrap("Set Escape of the Unchained to destroy later", Escape, () => V.IsMyTurn);
            SetSpellTrap("Set Abominable Chamber to destroy later", Chamber, () => V.IsMyTurn);
            SetSpellTrap("Set Wailing of the Unchained Souls to destroy later", Wailing, () => V.IsMyTurn);
            NormalSummon("Tour Guide From the Underworld brings a Fiend", TourGuide);
            // Two Level 6 Souls make Caesar; two smaller Fiends make Yama, which searches.
            SpecialSummon("two Level 6 Fiends into D/D/D Wave High King Caesar", Caesar);
            SpecialSummon("two Fiends into Unchained Soul Lord of Yama", Yama, () => V.OnField(Yama) == 0 && V.OnFieldAny(SmallFiends) >= 1 && V.OnFieldAny(LinkBodies) >= 2);
            // The destroyers: aim at our own Set floaters (or a spent body), summoning on the way.
            Activate("Unchained Twins - Aruha destroys our card to summon itself", Aruha, () => V.InHand(Aruha) > 0 && HasFodder);
            Activate("Unchained Soul of Sharvara destroys our card to summon itself", Sharvara, () => V.InHand(Sharvara) > 0 && HasFodder);
            Activate("Unchained Soul of Shyama destroys our card", Shyama, () => HasFodder && V.IsMyTurn);
            Activate("Unchained Twins - Rakea destroys our card", Rakea, () => V.OnField(Rakea) > 0 && V.SpellTrapCount > 0 && V.IsMyTurn);
            Activate("Unchained Twins - Sarama Sets an Unchained card", Sarama, () => V.OnField(Sarama) > 0 && V.IsMyTurn);
            // Link up: Abomination (Link-4) on top of a Link and two more, or two Links; Rage from an Unchained Soul.
            SpecialSummon("a Link and more into Unchained Abomination", Abomination,
                () => V.OnFieldAny(Yama, Rage, Anguish) > 0 && (V.MyMonsterCount >= 3 || V.OnFieldAny(Yama, Rage, Anguish) >= 2));
            SpecialSummon("an Unchained Soul into Unchained Soul of Rage", Rage, () => V.OnField(Rage) == 0 && V.OnFieldAny(Souls) > 0);

            // Requiem's summon locks us to D/D/D monsters, and Dark Contract with the Gate only finds it.
            HoldActivation(Requiem, () => V.InHandAny(TourGuide, Aruha, Sharvara, Shyama, Abominable) || V.MyMonsterCount > 0);
            HoldActivation(Gate, () => V.InHandAny(TourGuide, Aruha, Sharvara, Shyama, Abominable, Prison) || V.MyMonsterCount > 0);
            HoldSpecialSummon(Muckraker);
            HoldSpecialSummon(Dharc);
            HoldSpecialSummon(LittleKnight, () => V.IsFirstTurn);
            HoldSpecialSummon(Unicorn, () => V.OpponentCardCount == 0);
            HoldSpecialSummon(Typhon, () => V.IsFirstTurn);
            // Rhino stops our own Fiends being destroyed: don't Normal Summon it into the middle of the combo.
            HoldNormalSummon(Rhino);
            // Shyama destroys a card of ours: only one that floats, never itself or the Links.
            HoldActivation(Shyama, () => !HasFodder);
            Boss(Abomination, Caesar);

            Prefer(Searches, Prison, Sharvara, Aruha, Abominable, Escape, Chamber);
            PreferIf(() => V.InHand(Sharvara) > 0, Searches, Prison, Aruha, Escape);
            Prefer(Searches, Yama, Aruha, Sharvara, Abominable, Shyama, Rakea);
            PreferIf(() => V.InHand(Aruha) > 0, Searches, Yama, Sharvara, Abominable);
            Prefer(Searches, Prosperity, Prison, TourGuide, Sharvara, Aruha, Escape);
            Prefer(SummonPicks, TourGuide, Rhino, Aruha, Rakea, Sarama);
            // Floats from the Deck: Souls (Level 6, Caesar) and Twins that float again.
            Prefer(SummonPicks, 0, Shyama, Sharvara, Abominable);
            Prefer(ToGrave, Rhino, Sharvara, Shyama, Abominable);
            Prefer(new[] { DuelContext.HintSet }, Sharvara, Escape, Chamber, Wailing, Prison);
            Prefer(new[] { DuelContext.HintSet }, Sarama, Escape, Chamber, Wailing, Prison);
            // Our own destroying: the Set floaters first, then spent bodies; never the Links or Caesar.
            foreach (var destroyer in new[] { Aruha, Sharvara, Shyama, Rakea, Sarama })
            {
                Prefer(new[] { DuelContext.HintDestroy, DuelContext.HintTarget }, destroyer, Escape, Chamber, Wailing, Prison, Rakea, Sarama, Aruha, TourGuide, Rhino);
                Avoid(new[] { DuelContext.HintDestroy, DuelContext.HintTarget }, destroyer, Abomination, Caesar, Rage, Yama, Anguish);
            }
            Prefer(Materials, Yama, TourGuide, Rhino, Aruha, Rakea, Sarama, Ash);
            Avoid(Materials, Yama, Sharvara, Shyama);
            Prefer(Materials, Rage, Aruha, Rakea, Sarama, TourGuide, Rhino, Sharvara, Shyama, Abominable);
            Prefer(Materials, Abomination, Yama, Aruha, Rakea, Sarama, TourGuide, Rhino, Rage);
            Prefer(Materials, Caesar, Sharvara, Shyama);
            Avoid(Materials, 0, Abomination, Caesar);
            Prefer(Banish, Prosperity, Muckraker, Dharc, Unicorn, LittleKnight, Machinex, Typhon);
            Avoid(Banish, Prosperity, Abomination, Yama, Rage, Caesar, Anguish);
        }

        /// <summary>Something of ours worth destroying: a Set floater, or a Twin (it floats too).</summary>
        private bool HasFodder => V.InSpellTrapZoneAny(Floaters) || V.OnFieldAny(Twins) > 0 || V.OnFieldAny(TourGuide, Rhino) > 0;
    }
}
