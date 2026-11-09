namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Yubel Fiendsmith (2024): Samsara D Lotus Tributes itself for Spirit of Yubel, which sets a Yubel Spell/Trap;
    /// Nightmare Throne (via Terraforming) finds a 0 ATK/DEF Fiend; Dark Beckoning Beast searches Opening of the Spirit
    /// Gates and gives an extra Normal Summon. The Fiendsmith engine runs beside it: Engraver discards for Fiendsmith's
    /// Tract, Tract adds a LIGHT Fiend (Lurrie, discarded, summons itself), and a LIGHT Fiend becomes Requiem, which
    /// summons Lacrima from the Deck. Kashtira Unicorn and Fenrir come down free on an empty field.
    /// </summary>
    public sealed class YubelPlaybook : Playbook
    {
        private const uint Lotus = 62318994, SpiritOfYubel = 90829280, Squirmer = 24215921, Yubel = 78371393, TerrorIncarnate = 4779091,
            Engraver = 60764609, Lacrima = 28803166, Lurrie = 97651498, Beckoning = 81034083, Sharvara = 41165831,
            KashtiraUnicorn = 68304193, Fenrir = 32909498, Throne = 93729896, Pain = 65261141, Tract = 98567237, KashtiraBirth = 69540484,
            Wraitsoth = 71832012, SpiritGates = 80312545, SetRotation = 73468603, Terraforming = 73628505,
            Phantom = 80453041, Desirae = 82135803, AerialEater = 28143384, Varudras = 70636044, Caesar = 79559912,
            Goddess = 98127546, Sequence = 49867899, Yama = 24269961, Rage = 67680512, LittleKnight = 29301450,
            Moon = 71818935, Muckraker = 71607202, Requiem = 2463794, Fuwalos = 42141493,
            // No Limits (Rue's nl-yubel, docs/dev/NO-LIMITS-SPEC-2026-11-02.md 7.2): cards the as-built list doesn't run.
            Sangan = 26202165, Witch = 78010363;

        private static readonly uint[] LightFiends = { Engraver, Lacrima, Lurrie };
        private static readonly uint[] Yubels = { Yubel, SpiritOfYubel, TerrorIncarnate };

        public override string Deck => "yubel";

        public YubelPlaybook()
        {
            SpecialSummon("Kashtira Fenrir on an empty field", Fenrir, () => V.MyMonsterCount == 0);
            SpecialSummon("Kashtira Unicorn on an empty field", KashtiraUnicorn, () => V.MyMonsterCount == 0);
            Activate("Kashtira Fenrir searches", Fenrir);
            Activate("Kashtira Unicorn searches", KashtiraUnicorn);
            Activate("Terraforming finds Nightmare Throne", Terraforming, () => !V.InSpellTrapZone(Throne) && !V.InHandAny(Throne));
            Activate("Nightmare Throne finds a Fiend", Throne, () => !V.InSpellTrapZone(Throne));
            Activate("Fiendsmith Engraver discards for Fiendsmith's Tract", Engraver, () => V.InHand(Engraver) > 0);
            Activate("Fiendsmith's Tract adds a LIGHT Fiend", Tract);
            NormalSummon("Dark Beckoning Beast searches", Beckoning);
            Activate("Opening of the Spirit Gates", SpiritGates, () => !V.InSpellTrapZone(SpiritGates));
            NormalSummon("Samsara D Lotus", Lotus);
            Activate("Samsara D Lotus calls a Yubel monster", Lotus, () => V.InDeckAny(Yubels) > 0);
            NormalSummon("Lacrima sends a Fiendsmith card", Lacrima);
            SpecialSummon("a LIGHT Fiend into Fiendsmith's Requiem", Requiem, () => V.OnFieldAny(LightFiends) > 0);
            Activate("Fiendsmith's Requiem summons a Fiendsmith", Requiem, () => V.IsMyTurn);
            Activate("Fiendsmith Engraver comes back", Engraver);
            SpecialSummon("two Level 10s into Varudras", Varudras);
            SpecialSummon("Links into Fiendsmith's Sequence", Sequence, () => V.MyMonsterCount >= 2);
            Activate("Fiendsmith's Sequence Fusion Summons", Sequence, () => V.IsMyTurn);
            SpecialSummon("Links into S:P Little Knight", LittleKnight, () => V.MyMonsterCount >= 2 && V.OpponentCardCount > 0);
            Activate("Kashtira Birth revives a Kashtira", KashtiraBirth);
            // Phantom shuffles its materials away, so it comes last, from a Yubel already on the field.
            SpecialSummon("Yubel + a 0 ATK Fiend into Phantom of Yubel", Phantom, () => V.OnFieldAny(Yubels) > 0);

            HoldSpecialSummon(Phantom, () => V.OnFieldAny(Yubels) == 0);
            HoldSpecialSummon(Muckraker);
            HoldSpecialSummon(Moon);
            HoldSpecialSummon(Goddess, () => V.OpponentMonsterCount == 0);
            HoldNormalSummon(SpiritOfYubel);
            HoldNormalSummon(Yubel);

            Prefer(Searches, Engraver, Tract);
            Prefer(Searches, Tract, Lacrima, Engraver, Lurrie);
            Prefer(Searches, Throne, Lotus, Squirmer, SpiritOfYubel);
            Prefer(Searches, SpiritOfYubel, Pain, Throne);
            Prefer(Searches, Beckoning, SpiritGates);
            Prefer(Searches, SpiritGates, Beckoning);
            Prefer(Searches, Terraforming, Throne, Wraitsoth);
            Prefer(Searches, Fenrir, KashtiraUnicorn);
            Prefer(Searches, KashtiraUnicorn, KashtiraBirth);
            Prefer(Searches, 0, Lotus, Engraver, Tract, SpiritOfYubel);
            Prefer(ToGrave, Tract, Lurrie, Engraver, Fuwalos);
            Prefer(ToGrave, 0, Lurrie, Engraver);
            Prefer(SummonPicks, Lotus, SpiritOfYubel, Yubel);
            Prefer(SummonPicks, Requiem, Lacrima, Engraver);
            Prefer(SummonPicks, Sequence, Desirae, Phantom, AerialEater);
            Prefer(Materials, 0, Lurrie, Lacrima, Lotus, Squirmer, Beckoning, Requiem, Engraver);
            Avoid(Materials, 0, Phantom, Varudras, Goddess);

            // ---- No Limits (Rue's nl-yubel, 7.2): Fiendsmith's Lacrima (46640168; the playbook's Lacrima above is the Crimson
            // Tears) and the old Fiend searchers. Additive only: every card below is one the as-built yubel list doesn't run,
            // so the base plays as before (7.1).
            // Fiendsmith's Lacrima from Tract's GY Fusion and Sequence: it revives Engraver, and sent to the GY it shuffles
            // Lurrie back to burn 1200.
            Prefer(SummonPicks, Tract, FiendsmithLacrima);
            Prefer(SummonPicks, Sequence, FiendsmithLacrima);
            Prefer(SummonPicks, FiendsmithLacrima, Engraver, Lacrima, Lurrie);
            ChooseOption(FiendsmithLacrima, 3);
            Prefer(new[] { DuelContext.HintToDeck }, FiendsmithLacrima, Lurrie, Lacrima, Engraver);
            Avoid(Materials, 0, FiendsmithLacrima);
            // Sangan and Witch: bodies that search Spirit of Yubel, Samsara D Lotus or Engraver when they leave the field (as Link
            // material, or Tributed). Normal Summoned only when no searcher is in hand.
            NormalSummon("Sangan searches when it leaves the field", Sangan, () => !V.InHandAny(Lotus, Beckoning, Lacrima));
            NormalSummon("Witch of the Black Forest searches when it leaves the field", Witch, () => !V.InHandAny(Lotus, Beckoning, Lacrima));
            Prefer(Searches, Sangan, SpiritOfYubel, Lotus, Engraver, Yubel, Beckoning);
            Prefer(Searches, Witch, SpiritOfYubel, Lotus, Engraver, Yubel, Beckoning);
            Prefer(Materials, 0, Sangan, Witch);
            Prefer(Tributes, 0, Sangan, Witch);
        }
    }
}
