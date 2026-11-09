namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Dragon Rulers (2013): the four Level 7 Rulers summon themselves by banishing two Dragons or monsters of their
    /// Attribute from hand or GY, and discard with another card for an effect (Blaster destroys, Tidal mills, Tempest
    /// searches a Dragon, Redox revives). The baby Rulers discard to search their Level 7. Two Rulers make Mecha Phantom
    /// Beast Dracossack (or Big Eye against a big monster); Sacred Sword of Seven Stars draws two; Super Rejuvenation draws
    /// for each Dragon discarded this turn.
    /// </summary>
    public sealed class DragonRulerPlaybook : Playbook
    {
        private const uint Blaster = 53804307, Tidal = 26400609, Tempest = 89399912, Redox = 90411554,
            Burner = 53797637, Stream = 27415516, Lightning = 89185742, Reactan = 91020571, FlamvellGuard = 21615956,
            SacredSword = 45725480, SuperRejuvenation = 27770341, GoldSarcophagus = 75500286,
            Dracossack = 22110647, BigEye = 80117527;

        private static readonly uint[] Big = { Blaster, Tidal, Tempest, Redox };
        private static readonly uint[] Babies = { Burner, Stream, Lightning, Reactan };
        private static readonly uint[] Dragons = { Blaster, Tidal, Tempest, Redox, Burner, Stream, Lightning, Reactan, FlamvellGuard };

        public override string Deck => "dragon-ruler";

        public DragonRulerPlaybook()
        {
            Activate("Sacred Sword of Seven Stars draws two", SacredSword, () => V.InHandAny(Big) && V.HandCount >= 3);
            Activate("Gold Sarcophagus banishes a Ruler for later", GoldSarcophagus);
            // Babies search the big Ruler of their Attribute.
            foreach (var baby in Babies) Activate("baby Ruler searches a Level 7", baby, perTurn: 1);
            // Big Rulers: summon themselves, then the field fills toward Rank 7.
            foreach (var big in Big) Activate("Dragon Ruler effect", big, perTurn: 2);
            SpecialSummon("two Rulers into Dracossack", Dracossack, () => V.OnFieldAny(Big) >= 2);
            SpecialSummon("two Rulers into Big Eye (steal their best)", BigEye, () => V.OnFieldAny(Big) >= 2 && V.OpponentMonsterCount > 0);
            Activate("Dracossack makes tokens / destroys", Dracossack);
            HoldActivation(SuperRejuvenation, () => V.InGraveyardAny(Dragons) < 3);

            // Costs: spend babies and used Rulers, keep the big ones you can still summon.
            Prefer(Banish, 0, Burner, Stream, Lightning, Reactan, FlamvellGuard);
            Avoid(Banish, 0, Blaster, Tidal, Tempest, Redox);
            Prefer(ToGrave, 0, Burner, Stream, Lightning, Reactan, FlamvellGuard);
            Prefer(Searches, Tempest, Blaster, Redox, Tidal, Burner, Stream);
            Prefer(Searches, 0, Blaster, Tempest, Redox, Tidal);
            Prefer(SummonPicks, Redox, Blaster, Tempest, Tidal);
            Prefer(Materials, 0, Tidal, Tempest, Redox, Blaster);
        }
    }
}
