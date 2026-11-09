namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Nekroz (2015): Ritual Summon the Nekroz monsters with Nekroz Mirror (banishes Graveyard tributes), Kaleidoscope (sends
    /// an Extra Deck monster) and Cycle (revives from the GY). Manju and Senju search a Ritual piece when Normal Summoned;
    /// Brionac and Clausolas discard to search a Nekroz monster or Spell; Shurit can be the whole tribute. Trishula is the
    /// finisher (banishes a hand, field and GY card); Unicore stops Extra Deck monsters' effects.
    /// </summary>
    public sealed class NekrozPlaybook : Playbook
    {
        private const uint Brionac = 26674724, Clausolas = 99185129, Trishula = 52068432, Unicore = 89463537, Valkyrus = 25857246,
            Shurit = 90307777, Manju = 95492061, Senju = 23401839, Djinn = 8903700, BullBlader = 36088082,
            Mirror = 14735698, Kaleidoscope = 51124303, Cycle = 97211663, Preparation = 96729612, Reinforcement = 32807846;

        private static readonly uint[] RitualMonsters = { Trishula, Brionac, Unicore, Valkyrus, Clausolas, Shurit };
        private static readonly uint[] RitualSpells = { Mirror, Kaleidoscope, Cycle };

        public override string Deck => "nekroz";

        public NekrozPlaybook()
        {
            Activate("Preparation of Rites finds a Nekroz", Preparation);
            Activate("Reinforcement finds a Ritual searcher", Reinforcement, () => !V.InHandAny(Manju, Senju));
            NormalSummon("Manju searches a Ritual piece", Manju);
            NormalSummon("Senju searches a Ritual monster", Senju, () => V.InHand(Manju) == 0);
            Activate("Brionac discards to search a Nekroz", Brionac, () => V.InHandAny(RitualSpells));
            Activate("Clausolas discards to search a Ritual Spell", Clausolas, () => !V.InHandAny(RitualSpells));
            // The Ritual: the engine offers only what the tributes allow; the preferences pick the best Nekroz.
            Activate("Nekroz Mirror Ritual Summons", Mirror, () => V.InHandAny(RitualMonsters) || V.InGraveyardAny(RitualMonsters) > 0);
            Activate("Nekroz Kaleidoscope Ritual Summons", Kaleidoscope, () => V.InHandAny(RitualMonsters));
            Activate("Nekroz Cycle Ritual Summons", Cycle, () => V.InHandAny(RitualMonsters) || V.InGraveyardAny(RitualMonsters) > 0);
            Activate("Trishula banishes three", Trishula);

            Prefer(SummonPicks, 0, Trishula, Brionac, Unicore, Valkyrus, Clausolas, Shurit);
            Prefer(Searches, Manju, Mirror, Brionac, Trishula, Kaleidoscope);
            Prefer(Searches, Senju, Brionac, Trishula, Shurit, Unicore);
            Prefer(Searches, Brionac, Shurit, Trishula, Clausolas, Unicore);
            Prefer(Searches, Clausolas, Mirror, Kaleidoscope, Cycle);
            Prefer(Searches, Preparation, Brionac, Trishula);
            Prefer(Searches, 0, Mirror, Brionac, Manju, Trishula);
            // Tribute the searchers first; Shurit counts as the whole Level.
            Prefer(Tributes, 0, Shurit, Manju, Senju, Djinn, BullBlader, Clausolas, Valkyrus);
            Avoid(Tributes, 0, Trishula, Brionac, Unicore);
            Prefer(Banish, Mirror, Shurit, Clausolas, Valkyrus, Manju, Senju);
            HoldActivation(Unicore, () => V.InHandAny(RitualSpells)); // keep Unicore to Ritual Summon
        }
    }
}
