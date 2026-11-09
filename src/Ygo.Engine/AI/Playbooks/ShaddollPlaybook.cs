namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Shaddoll (2014): Shaddoll Fusion makes El Shaddoll Construct or Winda (using Deck monsters when the opponent has an
    /// Extra Deck monster); every Shaddoll sent to the GY by an effect triggers (Hedgehog searches, Beast draws, Squamata
    /// sends another, Dragon pops a Spell/Trap, Falco comes back), so the materials and send effects pick those first.
    /// Mathematician and Kuribandit fuel the GY for Sephylon and Black Luster Soldier.
    /// </summary>
    public sealed class ShaddollPlaybook : Playbook
    {
        private const uint Beast = 3717252, Dragon = 77723643, Falco = 37445295, Hedgehog = 4939890, Squamata = 30328508,
            Mathematician = 41386308, Kuribandit = 16404809, Envoy = 72989439, Sephylon = 8967776, ShaddollFusion = 44394295,
            FoolishBurial = 81439173, Construct = 20366274, Winda = 94977269, SinisterShadowGames = 77505534, SuperPoly = 48130397;

        public override string Deck => "shaddoll";

        public ShaddollPlaybook()
        {
            Activate("Foolish Burial a Shaddoll", FoolishBurial);
            Activate("Shaddoll Fusion", ShaddollFusion);
            NormalSummon("Mathematician sends a Shaddoll", Mathematician);
            Activate("Sephylon revives a Fairy", Sephylon);

            Prefer(new[] { DuelContext.HintFusionMaterial }, 0, Squamata, Hedgehog, Beast, Dragon, Falco);
            Prefer(SummonPicks, ShaddollFusion, Construct, Winda);
            Prefer(SummonPicks, SuperPoly, Construct, Winda);
            Prefer(ToGrave, Mathematician, Hedgehog, Squamata, Falco, Dragon);
            Prefer(ToGrave, FoolishBurial, Beast, Squamata, Hedgehog, Falco);
            Prefer(ToGrave, Construct, Squamata, Hedgehog, Beast, Falco, Dragon);
            Prefer(ToGrave, Squamata, Hedgehog, Beast, Falco, Dragon);
            Prefer(ToGrave, SinisterShadowGames, Squamata, Hedgehog, Beast, Falco, Dragon);
            Prefer(Searches, Hedgehog, ShaddollFusion, Squamata, Beast, Falco, Dragon);
            Prefer(Searches, 0, ShaddollFusion, SinisterShadowGames);
            Prefer(ToGrave, 0, Beast, Squamata, Hedgehog, Falco, Dragon);
        }
    }
}
