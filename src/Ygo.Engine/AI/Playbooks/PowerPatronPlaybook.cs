namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Power Patron (2026): Pendulum monsters that summon a boss from the Extra Deck with their monster effects (Nervedo
    /// makes Nerva, a Fusion; Junoldo makes Junora, a Synchro; Jupredo makes Jupiter, an Xyz), and do something when they
    /// land face-up in the Extra Deck (Nervedo Special Summons another Power Patron from the Deck, Junoldo and Vidolium
    /// search). Pendulum Treasure, Shade and Medius the Pure feed them. The board to build is all three bosses: with
    /// Nerva, Junora and Jupiter out, Plundered Power Patron Plane - Vidolia stops the opponent's GY effects.
    /// </summary>
    public sealed class PowerPatronPlaybook : Playbook
    {
        private const uint Medius = 97556336, Shade = 77157846, Junoldo = 10266279, Jupredo = 43871165, Nervedo = 17473466,
            Vidolium = 70488851, Terminus = 25661743, PendulumTreasure = 26237713, Vidolia = 51669847, Vandalism = 1122030,
            BookOfEclipse = 35480699, FallenAndVirtuous = 30271097, Impasto = 44654994, Lechery = 70917315,
            NonFinito = 74631897, Nerva = 53589300, Junora = 5914858, Jupiter = 68231287, Mercurium = 22984000,
            Titaniklad = 41373230, EcclesiaDarkDragon = 78397661, Clockwork = 41739381, ExceedThePendulum = 92812851;

        private static readonly uint[] Shades = { Junoldo, Jupredo, Nervedo };

        public override string Deck => "power-patron";

        /// <summary>The boss still to make: Junora (Junoldo), then Jupiter (Jupredo), then Nerva (Nervedo).</summary>
        private uint NextShade => V.OnField(Junora) == 0 && V.InExtraDeck(Junora) > 0 ? Junoldo
            : V.OnField(Jupiter) == 0 && V.InExtraDeck(Jupiter) > 0 ? Jupredo
            : V.OnField(Nerva) == 0 && V.InExtraDeck(Nerva) > 0 ? Nervedo : Junoldo;

        public PowerPatronPlaybook()
        {
            // Each Shade on the field becomes its boss.
            Activate("Junoldo becomes Junora the Power Patron of Tuning", Junoldo, () => V.OnField(Junoldo) > 0 && V.OnField(Junora) == 0);
            Activate("Jupredo becomes Jupiter the Power Patron of Destruction", Jupredo, () => V.OnField(Jupredo) > 0 && V.OnField(Jupiter) == 0);
            Activate("Nervedo becomes Nerva the Power Patron of Creation", Nervedo, () => V.OnField(Nervedo) > 0 && V.OnField(Nerva) == 0);
            Activate("Plundered Power Patron Plane - Vidolia finds a Shade", Vidolia);

            Boss(Nerva, Junora, Jupiter, Vidolium);
            HoldSpecialSummon(Clockwork);
            HoldSpecialSummon(Mercurium, () => V.OnField(Jupiter) == 0);
            // Vidolium sends the three bosses back: only once the lock is no use (Vidolia not out).
            HoldSpecialSummon(Vidolium, () => V.OnField(Nerva) + V.OnField(Junora) + V.OnField(Jupiter) >= 3 && V.InSpellTrapZone(Vidolia));

            // The Shade called from the Deck is the one whose boss is still missing.
            PreferIf(() => NextShade == Junoldo, SummonPicks, Nervedo, Junoldo, Jupredo);
            PreferIf(() => NextShade == Jupredo, SummonPicks, Nervedo, Jupredo, Junoldo);
            PreferIf(() => NextShade == Nervedo, SummonPicks, Medius, Nervedo, Junoldo, Jupredo);
            PreferIf(() => NextShade == Junoldo, Asks(Medius, 2), 0, Junoldo);
            PreferIf(() => NextShade == Jupredo, Asks(Medius, 2), 0, Jupredo);
            PreferIf(() => NextShade == Nervedo, Asks(Medius, 2), 0, Nervedo);
            PreferIf(() => V.InExtraDeck(Nervedo) == 0, Asks(PendulumTreasure, 0), 0, Nervedo);
            Prefer(Searches, Junoldo, Vidolium, Nervedo, Jupredo);
            Prefer(Searches, Vidolium, Vidolia, Terminus, PendulumTreasure);
            Prefer(Searches, Jupredo, Vidolia, Terminus);
            Prefer(Materials, 0, Medius, Shade, Lechery);
            Avoid(Materials, 0, Nerva, Junora, Jupiter);
        }
    }
}
