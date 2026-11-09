namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Sky Striker (2018): one monster, many spells. Sky Striker Ace - Raye tributes itself to Special Summon a Sky Striker
    /// Ace Link (Kagari returns a Sky Striker Spell from the GY, Shizuku searches one, Hayate attacks directly); Engage
    /// searches another Sky Striker card (and draws once three Spells are in the GY); Area Zero (via Terraforming) and Hornet
    /// Drones keep the engine going. The Links tag back into Raye when they leave.
    /// </summary>
    public sealed class SkyStrikerPlaybook : Playbook
    {
        private const uint Raye = 26077387, AreaZero = 50005218, Terraforming = 73628505, Engage = 63166095, WidowAnchor = 98338152,
            Multirole = 24010609, Afterburners = 99550630, HornetDrones = 52340444, SharkCannon = 51227866, EagleBooster = 25733157,
            PotOfDesires = 35261759, Upstart = 70368879, Kagari = 63288573, Shizuku = 90673288, Hayate = 8491308;

        private static readonly uint[] Spells = { Engage, WidowAnchor, Multirole, Afterburners, HornetDrones, SharkCannon, EagleBooster, AreaZero };

        public override string Deck => "sky-striker";

        public SkyStrikerPlaybook()
        {
            Activate("Pot of Desires", PotOfDesires);
            Activate("Upstart Goblin", Upstart);
            Activate("Terraforming finds Area Zero", Terraforming, () => !V.InSpellTrapZone(AreaZero));
            Activate("Area Zero", AreaZero, perTurn: 2);
            Activate("Engage searches a Sky Striker card", Engage);
            NormalSummon("Raye", Raye);
            Activate("Hornet Drones makes a token", HornetDrones, () => V.MyMonsterCount == 0);
            // Raye tags out into a Link: Kagari to get a Spell back, otherwise Shizuku to search.
            Activate("Raye tags out into a Sky Striker Ace Link", Raye, () => V.IsMyTurn);
            Activate("Kagari returns a Sky Striker Spell", Kagari);
            Activate("Shizuku searches a Sky Striker Spell", Shizuku);
            SpecialSummon("Raye into Hayate for a direct hit", Hayate, () => !V.IsFirstTurn && V.OnField(Raye) > 0);

            Prefer(SummonPicks, Raye, Kagari, Shizuku, Hayate);
            Prefer(Searches, Terraforming, AreaZero);
            Prefer(Searches, Engage, Raye, WidowAnchor, Multirole, HornetDrones);
            Prefer(Searches, Shizuku, Engage, WidowAnchor, Afterburners);
            Prefer(Searches, Kagari, Engage, WidowAnchor, Multirole);
            Prefer(Searches, 0, Raye, Engage, WidowAnchor, AreaZero);
            Prefer(Materials, 0, Raye);
            HoldSet(WidowAnchor, () => V.IsMyTurn && V.SpellTrapCount >= 3);
        }
    }
}
