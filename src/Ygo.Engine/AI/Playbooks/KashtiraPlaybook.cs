namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Kashtira (2023): Fenrir and Unicorn Special Summon themselves on an empty field and search (Fenrir a Kashtira
    /// monster, Unicorn a Kashtira Spell); Pressured Planet Wraitsoth (via Terraforming) adds another; Riseheart joins a
    /// Kashtira and becomes Level 7 by banishing the opponent's top 3; Kashtiratheosis and Kashtira Birth add bodies. Two
    /// Level 7s become Kashtira Shangri-Ira, which locks zones each time the opponent's cards are banished face-down.
    /// </summary>
    public sealed class KashtiraPlaybook : Playbook
    {
        private const uint Fenrir = 32909498, Unicorn = 68304193, Riseheart = 31149212, Scareclaw = 78534861, Prosperity = 84211599,
            Terraforming = 73628505, Wraitsoth = 71832012, Theosis = 34447918, Birth = 69540484, BigBang = 33925864,
            AriseHeart = 48626373, ShangriIra = 73542331, Zeus = 90448279, BigEye = 80117527, DarkArmed = 78144171,
            FlareMetal = 44405066, Baronne = 84815190, Goliath = 23689428, Donner = 8728498;

        private static readonly uint[] Kashtiras = { Fenrir, Unicorn, Riseheart, Scareclaw };

        public override string Deck => "kashtira";

        public KashtiraPlaybook()
        {
            Activate("Pot of Prosperity digs", Prosperity, () => V.MyMonsterCount == 0);
            Activate("Terraforming finds Wraitsoth", Terraforming, () => !V.InSpellTrapZone(Wraitsoth) && !V.InHandAny(Wraitsoth));
            Activate("Pressured Planet Wraitsoth searches", Wraitsoth, () => !V.InSpellTrapZone(Wraitsoth));
            SpecialSummon("Kashtira Fenrir on an empty field", Fenrir, () => V.MyMonsterCount == 0);
            SpecialSummon("Kashtira Unicorn on an empty field", Unicorn, () => V.MyMonsterCount == 0);
            Activate("Kashtira Fenrir searches", Fenrir);
            Activate("Kashtira Unicorn searches", Unicorn);
            Activate("Kashtira Birth", Birth, () => !V.InSpellTrapZone(Birth));
            SpecialSummon("Kashtira Riseheart joins a Kashtira", Riseheart);
            Activate("Kashtira Riseheart banishes and becomes Level 7", Riseheart);
            Activate("Kashtiratheosis calls a Kashtira", Theosis);
            Activate("Scareclaw Kashtira summons itself", Scareclaw);
            NormalSummon("Kashtira Fenrir (Kashtira Birth)", Fenrir);
            NormalSummon("Kashtira Unicorn (Kashtira Birth)", Unicorn);
            NormalSummon("Kashtira Riseheart", Riseheart, () => V.OnFieldAny(Kashtiras) > 0);
            Activate("Kashtira Birth revives a Kashtira", Birth, perTurn: 2);
            SpecialSummon("two Level 7s into Kashtira Shangri-Ira", ShangriIra, () => V.OnField(ShangriIra) == 0);
            SpecialSummon("Kashtira Arise-Heart", AriseHeart);
            SpecialSummon("two Level 7s into Number 11: Big Eye", BigEye, () => V.OpponentMonsterCount > 0 && V.OnField(ShangriIra) > 0);

            HoldSpecialSummon(Goliath);
            HoldSpecialSummon(Donner);

            Prefer(Searches, Fenrir, Unicorn, Riseheart, Scareclaw, Fenrir);
            Prefer(Searches, Unicorn, Birth, Theosis);
            Prefer(Searches, Wraitsoth, Fenrir, Unicorn, Riseheart);
            Prefer(Searches, Terraforming, Wraitsoth);
            Prefer(Searches, 0, Fenrir, Unicorn, Birth, Theosis);
            Prefer(SummonPicks, Theosis, Unicorn, Fenrir, Riseheart, Scareclaw);
            Prefer(SummonPicks, Birth, Fenrir, Unicorn, Riseheart);
            Prefer(Banish, Riseheart, Theosis, BigBang, Birth);
            Prefer(Banish, Scareclaw, Theosis, BigBang);
            Avoid(Banish, Prosperity, ShangriIra, AriseHeart, BigEye);
            Prefer(Materials, 0, Riseheart, Scareclaw, Unicorn, Fenrir);
        }
    }
}
