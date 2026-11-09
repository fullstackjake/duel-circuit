namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Blackwing (2009): DARK Winged Beasts that swarm. Black Whirlwind goes up first, so each Normal Summoned Blackwing
    /// searches one with less ATK (Shura or Bora find Gale the Whirlwind, the Tuner); Bora and Gale Special Summon
    /// themselves next to another Blackwing, and Blizzard revives one from the GY. Gale + a Level 4 becomes Blackwing Armor
    /// Master (it can't be destroyed by battle): the turn-1 wall, behind Icarus Attack, Bottomless and Solemn. Dark Armed
    /// Dragon comes out with exactly three DARKs in the GY. Going second, Sirocco gets in free and the Synchros attack.
    /// </summary>
    public sealed class BlackwingPlaybook : Playbook
    {
        private const uint Blizzard = 22835145, Bora = 49003716, Gale = 2009101, Kalut = 85215458, Shura = 58820853,
            Sirocco = 75498415, DarkArmed = 65192027, Allure = 1475311, Whirlwind = 91351370, HeavyStorm = 19613556,
            Trunade = 42703248, Reborn = 83764718,
            Arcanite = 31924889, BlackRose = 73580471, ArmedWing = 76913983, ArmorMaster = 69031175, Colossal = 23693634,
            DarkStrike = 32646477, Goyo = 7391448, MagicalAndroid = 43385557, RedDragon = 70902743, Stardust = 44508094;

        private static readonly uint[] Level4OrLower = { Bora, Gale, Kalut, Shura };

        public override string Deck => "blackwing";

        public BlackwingPlaybook()
        {
            // Black Whirlwind before the Normal Summon it searches off.
            Activate("Black Whirlwind", Whirlwind, () => !V.InSpellTrapZone(Whirlwind) && V.MyMonsterCount == 0);
            // Sirocco comes in without a Tribute while only the opponent has monsters.
            NormalSummon("Blackwing - Sirocco the Dawn, free on an empty field", Sirocco, () => V.MyMonsterCount == 0 && V.OpponentMonsterCount > 0);
            NormalSummon("Blackwing - Blizzard revives a Blackwing", Blizzard, () => V.InGraveyardAny(Level4OrLower) > 0);
            NormalSummon("Blackwing - Shura searches", Shura, () => V.InSpellTrapZone(Whirlwind));
            NormalSummon("Blackwing - Bora searches", Bora, () => V.InSpellTrapZone(Whirlwind));
            NormalSummon("Blackwing - Kalut searches", Kalut, () => V.InSpellTrapZone(Whirlwind) && !V.InHandAny(Shura, Bora));
            // Without Whirlwind: one Blackwing in, so Bora and Gale can follow from the hand.
            NormalSummon("Blackwing - Gale the Whirlwind, then Bora", Gale, () => V.InHand(Bora) > 0);
            NormalSummon("Blackwing - Shura, then Gale or Bora", Shura, () => V.InHandAny(Gale, Bora));
            NormalSummon("Blackwing - Kalut, then Gale or Bora", Kalut, () => V.InHandAny(Gale, Bora));
            SpecialSummon("Blackwing - Gale the Whirlwind joins", Gale);
            SpecialSummon("Blackwing - Bora the Spear joins", Bora, perTurn: 2);
            SpecialSummon("Dark Armed Dragon", DarkArmed);
            // Allure after the summons: it banishes a DARK from the hand, so not the only one we'd summon.
            Activate("Allure of Darkness draws", Allure, () => V.InHandAny(Kalut, Sirocco, Blizzard, Shura, Bora, Gale, DarkArmed));
            // Synchros: Armor Master (Gale + a Level 4) is the wall; Level 8s when the levels add up to that instead.
            SpecialSummon("Gale + a Level 4 into Blackwing Armor Master", ArmorMaster);
            SpecialSummon("Synchro into Stardust Dragon", Stardust);
            SpecialSummon("Synchro into Red Dragon Archfiend", RedDragon, () => !V.IsFirstTurn);
            SpecialSummon("Synchro into Blackwing Armed Wing", ArmedWing, () => !V.IsFirstTurn);
            SpecialSummon("Synchro into Goyo Guardian", Goyo);
            SpecialSummon("Synchro into Blackwing Armed Wing", ArmedWing);

            // Black Rose would destroy our own board; the rest only earn their place attacking.
            HoldSpecialSummon(BlackRose, () => V.OpponentMonsterCount < 2);
            HoldSpecialSummon(Arcanite, () => V.IsFirstTurn);
            HoldSpecialSummon(DarkStrike, () => V.IsFirstTurn);
            HoldSpecialSummon(MagicalAndroid, () => V.IsFirstTurn);
            HoldSpecialSummon(Colossal, () => V.IsFirstTurn);
            // Blanket Spell removal waits for targets; Monster Reborn for something worth reviving.
            HoldActivation(HeavyStorm, () => V.OpponentCardCount - V.OpponentMonsterCount < 2);
            HoldActivation(Trunade, () => V.OpponentCardCount - V.OpponentMonsterCount < 2);
            HoldActivation(Reborn, () => V.IsFirstTurn && V.MyMonsterCount >= 2);
            Boss(ArmorMaster, Stardust);

            Prefer(Searches, Whirlwind, Gale, Bora, Kalut, Blizzard);
            PreferIf(() => V.InHand(Gale) > 0, Searches, Whirlwind, Bora, Kalut, Blizzard);
            Prefer(Banish, Allure, Kalut, Sirocco, Blizzard, Shura);
            Avoid(Banish, Allure, Gale, Bora, DarkArmed);
            Prefer(SummonPicks, Blizzard, Bora, Shura, Gale, Kalut);
            Prefer(SummonPicks, Reborn, DarkArmed, Shura, Bora, Gale);
            Prefer(SummonPicks, 0, ArmorMaster, Stardust, RedDragon, Goyo, ArmedWing);
            Prefer(Materials, ArmorMaster, Gale, Blizzard, Bora, Kalut, Shura);
            Prefer(Materials, 0, Gale, Blizzard, Bora, Kalut, Shura, Sirocco);
            Avoid(Materials, 0, ArmorMaster, Stardust);
            // Dark Armed Dragon banishes a DARK from the GY as its cost: the spent Blackwings, not its own fuel for later.
            Prefer(Banish, DarkArmed, Kalut, Blizzard, Gale, Bora, Shura);
            Prefer(Tributes, 0, Kalut, Blizzard, Gale, Bora, Shura);
        }
    }
}
