namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Burning Abyss (2014): Malebranche monsters Special Summon themselves from the hand while you control no Spells or
    /// Traps, and each does something when sent to the GY (Graff summons another from the Deck, Cir revives one, Scarm
    /// searches one). Tour Guide From the Underworld summons a Level 3 Fiend from the Deck. Two Level 3s make Dante, whose
    /// mill feeds the Graveyard effects; Rubic as a Tuner makes Virgil. Spells and Traps wait until the monsters are out.
    /// </summary>
    public sealed class BurningAbyssPlaybook : Playbook
    {
        private const uint Scarm = 84764038, Graff = 20758643, Cir = 57143342, Rubic = 734741, Alich = 47728740, Calcab = 73213494,
            TourGuide = 10802915, FoolishBurial = 81439173, Dante = 83531441, Virgil = 601193, Downerd = 72167543;

        private static readonly uint[] Malebranche = { Scarm, Graff, Cir, Rubic, Alich, Calcab };

        public override string Deck => "burning-abyss";

        public BurningAbyssPlaybook()
        {
            Activate("Foolish Burial sends Graff or Scarm", FoolishBurial);
            NormalSummon("Tour Guide summons a Malebranche", TourGuide);
            foreach (var m in Malebranche)
                Activate("Malebranche summons itself from the hand", m, () => V.SpellTrapCount == 0);
            SpecialSummon("two Level 3s into Dante", Dante, () => V.MyMonsterCount >= 2);
            SpecialSummon("Rubic + Malebranche into Virgil", Virgil, () => V.OnField(Rubic) > 0);
            Activate("Dante mills for Graveyard effects", Dante);

            // Malebranche can't come out once a Spell/Trap is face-down on our side.
            HoldSet(0, () => V.InHandAny(Malebranche) && V.MyMonsterCount < 5);
            Prefer(ToGrave, FoolishBurial, Graff, Scarm, Cir);
            Prefer(SummonPicks, TourGuide, Scarm, Graff, Cir, Alich);
            Prefer(SummonPicks, Graff, Cir, Scarm, Alich, Calcab, Rubic);
            Prefer(SummonPicks, Cir, Graff, Scarm, Alich);
            Prefer(Searches, Scarm, Graff, Cir, Rubic);
            Prefer(Materials, 0, TourGuide, Graff, Scarm, Cir, Alich, Calcab, Rubic);
            Avoid(Materials, Downerd, Dante);
        }
    }
}
