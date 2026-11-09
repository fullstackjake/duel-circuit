using System.Linq;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>
    /// Gouki Knightmares (2018), the Black Market's headliner "Gumblar Gail" (docs/dev/HEADLINERS-2026-11-01.md). Gouki Suprex
    /// brings a second Gouki with it; every Gouki that leaves the field for the GY searches another, so two of them Link
    /// into a Knightmare and come straight back with Gouki Re-Match; Instant Fusion and Destiny HERO - Malicious add bodies.
    /// Four Link Materials' worth of Effect Monsters make Topologic Gumblar Dragon (the payoff), and every monster
    /// Special Summoned where a Link points after that rips one or two cards from both hands. Its deck plays as
    /// "#recipe gumblar-gail" (the gouki deck on the roster plays without it).
    /// </summary>
    public sealed class GumblarGailPlaybook : Playbook
    {
        private const uint Gumblar = 22593417, Suprex = 24073068, Riscorpio = 60461077, Headbatt = 54088068, Twistcobra = 97688360,
            Bearhug = 12097275, Octostretch = 85008676, ReMatch = 62376646, Malicious = 9411399, InstantFusion = 1845204,
            Ntss = 80532587, Seraphinite = 3113836, Brigandine = 98827725, SoulCharge = 54447022, Iblee = 10158145,
            Phoenix = 2857636, Cerberus = 75452921, Unicorn = 38342335, Mermaid = 3679218, Isolde = 59934749, Linkuriboh = 41999284,
            Reprodocus = 34989413, SummonSorceress = 61665245, TriGate = 32617464, Firewall = 5043010, Kagari = 63288573,
            Reinforcement = 32807846, OneForOne = 2295440, Foolish = 81439173, LivingFossil = 34959756, Reborn = 83764718;

        private static readonly uint[] Goukis = { Suprex, Riscorpio, Headbatt, Twistcobra, Bearhug, Octostretch };
        /// <summary>The Link-2s two Goukis make on the way (different names; no target needed to summon them).</summary>
        private static readonly uint[] LinkTwos = { Phoenix, Cerberus, Isolde };
        /// <summary>Gumblar's materials, cheapest first: the Goukis (each searches another), Malicious, the Link-2s.</summary>
        private static readonly uint[] GumblarMaterials = { Twistcobra, Headbatt, Octostretch, Suprex, Riscorpio, Bearhug, Malicious, Ntss, Seraphinite,
            Phoenix, Cerberus, Isolde, Mermaid, Linkuriboh, Kagari };

        public override string Deck => "gumblar-gail";

        private static int LinkRating(uint code) => code switch
        {
            Firewall => 4, Unicorn => 3, SummonSorceress => 3, TriGate => 3, Phoenix => 2, Cerberus => 2, Isolde => 2, Reprodocus => 2,
            Mermaid => 1, Linkuriboh => 1, Kagari => 1, _ => 0,
        };

        /// <summary>Gumblar's material count on the field now: each face-up Effect Monster 1, a Link its rating.</summary>
        private int MaterialValue => V.FaceUpCodes.Where(c => c != Brigandine).Sum(c => LinkRating(c) is var r && r > 0 ? r : 1);

        private int GoukisInHand => Goukis.Sum(g => V.InHand(g));

        /// <summary>
        /// About how much Gumblar material this turn can still add: the Normal Summon (Suprex brings a second Gouki), Headbatt,
        /// Instant Fusion, Malicious from the GY, Re-Match (two Goukis back once a Link has sent them).
        /// </summary>
        private int MaterialToCome
        {
            get
            {
                int n = 0;
                if (!V.NormalSummonUsed)
                {
                    if (V.InHand(Suprex) > 0) n += GoukisInHand >= 2 ? 2 : 1;
                    else if (V.InHandAny(Riscorpio, Twistcobra, Headbatt, Octostretch)) n += 1;
                }
                if (V.InHand(Headbatt) > 0 && GoukisInHand >= 2) n++;
                if (V.InHand(InstantFusion) > 0 && V.MyLifePoints > 1000 && V.InExtraDeckAny(Ntss, Seraphinite)) n++;
                n += System.Math.Min(2, V.InGraveyard(Malicious));
                if (V.InHand(ReMatch) > 0) n += 2;
                return n;
            }
        }

        private bool GumblarReady => V.InExtraDeck(Gumblar) > 0 && V.OnField(Gumblar) == 0;

        public GumblarGailPlaybook()
        {
            var cost = new[] { DuelContext.HintDiscard, DuelContext.HintToGrave };
            // Bodies, a Link-2 that sends two Goukis (their searches), Re-Match and Malicious for two more, then Gumblar.
            ComboRoute("Gouki into Gumblar", () => V.IsMyMainPhase && GumblarReady && MaterialValue + MaterialToCome >= 4)
                .AnyDecision()
                .Waiting()
                .Counts("summons", () => V.SpecialSummonsThisDuel)
                .NormalSummon("Gouki Suprex brings a Gouki", Suprex, () => MaterialValue < 4)
                    .Pick(SummonPicks, Twistcobra, Headbatt, Octostretch, Riscorpio, Bearhug)
                .NormalSummon("Gouki Riscorpio, no Tribute", Riscorpio, () => MaterialValue < 4)
                .NormalSummon("Gouki Twistcobra", Twistcobra, () => MaterialValue < 4)
                .Activate("Gouki Headbatt joins", Headbatt, () => MaterialValue < 4)
                    .Pick(cost, Octostretch, Bearhug, Riscorpio, Twistcobra)
                .Activate("Instant Fusion", InstantFusion, () => MaterialValue < 4)
                    .Pick(SummonPicks, Ntss, Seraphinite)
                .Activate("Malicious calls Malicious", Malicious, () => MaterialValue < 4)
                .SpecialSummon("Knightmare Phoenix from two Goukis", Phoenix, () => MaterialValue < 4 && V.InHand(ReMatch) > 0)
                    .Pick(Materials, GumblarMaterials)
                .SpecialSummon("Knightmare Cerberus from two Goukis", Cerberus, () => MaterialValue < 4 && V.InHand(ReMatch) > 0)
                    .Pick(Materials, GumblarMaterials)
                .Activate("Gouki Re-Match brings two back", ReMatch, () => MaterialValue < 4)
                .Activate("Malicious calls Malicious", Malicious, () => MaterialValue < 4)
                .SpecialSummon("Topologic Gumblar Dragon", Gumblar, () => MaterialValue >= 4)
                    .Pick(Materials, GumblarMaterials)
                    .Payoff();

            // ---- The engine outside the route.
            Activate("Reinforcement of the Army finds Suprex", Reinforcement);
            Activate("One for One", OneForOne);
            Activate("Foolish Burial sends Malicious", Foolish);
            NormalSummon("Gouki Suprex", Suprex);
            NormalSummon("Gouki Riscorpio", Riscorpio);
            Activate("Gouki Headbatt", Headbatt);
            Activate("Instant Fusion", InstantFusion, () => V.MyLifePoints > 2000);
            Activate("Malicious calls Malicious", Malicious, () => V.InDeck(Malicious) > 0 && V.FreeMonsterZones > 0);
            Activate("Gouki Re-Match", ReMatch);
            SpecialSummon("Topologic Gumblar Dragon", Gumblar, () => MaterialValue >= 4);
            Boss(Gumblar, Firewall);
            // Gumblar first: no other big Link spends its materials while Gumblar can be made this turn.
            foreach (var big in new[] { Unicorn, SummonSorceress, TriGate, Firewall })
                HoldSpecialSummon(big, () => GumblarReady && MaterialValue + MaterialToCome >= 4);

            Prefer(Searches, Reinforcement, Suprex, Riscorpio, Twistcobra, Headbatt);
            Prefer(Searches, 0, Suprex, ReMatch, Twistcobra, Headbatt, Riscorpio, Bearhug, Malicious);
            Prefer(ToGrave, Foolish, Malicious);
            Prefer(cost, 0, Malicious, Octostretch, Bearhug, Brigandine);
            Avoid(cost, 0, Suprex, ReMatch, InstantFusion, SoulCharge);
            Prefer(SummonPicks, ReMatch, Suprex, Twistcobra, Riscorpio, Headbatt, Bearhug, Octostretch);
            Prefer(SummonPicks, InstantFusion, Ntss, Seraphinite);
            Prefer(SummonPicks, 0, Twistcobra, Headbatt, Suprex, Malicious);
            Prefer(Materials, Gumblar, GumblarMaterials);
            Prefer(Materials, 0, Goukis);
        }
    }
}
