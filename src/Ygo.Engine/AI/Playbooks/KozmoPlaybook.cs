namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Kozmo (2015): small Psychic pilots (Farmgirl, Strawman, Goodwitch, Wickedwitch) banish themselves to Special Summon a
    /// big Kozmo ship from the hand: Dark Destroyer destroys a monster, Sliprider a Spell/Trap, Forerunner gains LP. Each
    /// ship, destroyed, floats into a smaller Kozmo from the Deck. Emergency Teleport fetches a Level 3 or lower pilot;
    /// Kozmotown brings banished pilots back to the hand.
    /// </summary>
    public sealed class KozmoPlaybook : Playbook
    {
        private const uint DarkDestroyer = 55885348, Farmgirl = 31061682, Forerunner = 20849090, Goodwitch = 67050396,
            Sliprider = 94454495, Strawman = 56907986, Wickedwitch = 93302695, Teleport = 67723438, Kozmotown = 67237709,
            Sarcophagus = 75500286, BigEye = 80117527, GiantGrinder = 88120966, FlareMetal = 44405066;

        private static readonly uint[] Ships = { DarkDestroyer, Forerunner, Sliprider };
        private static readonly uint[] Pilots = { Farmgirl, Strawman, Goodwitch, Wickedwitch };

        public override string Deck => "kozmo";

        public KozmoPlaybook()
        {
            Activate("Kozmotown", Kozmotown, () => !V.InSpellTrapZone(Kozmotown));
            Activate("Kozmotown returns a banished pilot", Kozmotown, perTurn: 2);
            Activate("Emergency Teleport fetches a pilot", Teleport, () => V.InHandAny(Ships));
            NormalSummon("Kozmo Farmgirl", Farmgirl, () => V.InHandAny(Ships));
            NormalSummon("Kozmo Goodwitch", Goodwitch, () => V.InHandAny(DarkDestroyer, Forerunner, Sliprider));
            NormalSummon("Kozmo Strawman", Strawman, () => V.InHandAny(Ships));
            NormalSummon("Kozmoll Wickedwitch", Wickedwitch, () => V.InHandAny(Ships));
            Activate("Farmgirl tags out into a ship", Farmgirl, () => V.InHandAny(Ships) || V.InHandAny(Goodwitch, Wickedwitch));
            Activate("Strawman tags out into a ship", Strawman, () => V.InHandAny(Ships) || V.InHand(Farmgirl) > 0);
            Activate("Goodwitch tags out into a ship", Goodwitch, () => V.InHandAny(Ships));
            Activate("Wickedwitch tags out into a ship", Wickedwitch, () => V.InHandAny(Ships));
            SpecialSummon("two Level 8s into Gimmick Puppet Giant Grinder", GiantGrinder, () => V.OpponentMonsterCount > 0);
            SpecialSummon("two Level 7s into Number 11: Big Eye", BigEye, () => V.OpponentMonsterCount > 0);

            HoldActivation(Sarcophagus, () => V.InHandAny(Ships) && V.InHandAny(Pilots));

            Prefer(SummonPicks, Teleport, Farmgirl, Strawman);
            Prefer(SummonPicks, Farmgirl, DarkDestroyer, Forerunner, Sliprider, Goodwitch, Wickedwitch);
            Prefer(SummonPicks, Strawman, DarkDestroyer, Forerunner, Sliprider, Goodwitch, Wickedwitch, Farmgirl);
            Prefer(SummonPicks, Goodwitch, DarkDestroyer, Forerunner, Sliprider);
            Prefer(SummonPicks, Wickedwitch, DarkDestroyer, Forerunner, Sliprider);
            Prefer(SummonPicks, DarkDestroyer, Forerunner, Sliprider, Goodwitch, Wickedwitch);
            Prefer(SummonPicks, Forerunner, Sliprider, Goodwitch, Wickedwitch, Farmgirl);
            Prefer(SummonPicks, Sliprider, Goodwitch, Wickedwitch, Farmgirl, Strawman);
            Prefer(Searches, Kozmotown, Farmgirl, Strawman, Goodwitch);
            Prefer(Searches, 0, DarkDestroyer, Farmgirl, Teleport, Kozmotown);
            Prefer(Banish, Sarcophagus, DarkDestroyer, Kozmotown, Teleport);
            Avoid(Materials, 0, DarkDestroyer, Forerunner);
        }
    }
}
