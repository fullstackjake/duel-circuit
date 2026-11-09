using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Presentation;

namespace Ygo.Harness;

/// <summary>
/// `--negator-test [--games N]`: how the CPU plays into a taxing negator. The player starts with either Light and
/// Darkness Dragon face-up (it negates every Spell/Trap Card and monster effect activation, losing 500 ATK/DEF each
/// time) or four monsters it Link Summons into Apollousa, Bow of the Goddess on turn 1 (it negates the CPU's monster
/// effects, losing 800 ATK each time; the player always uses it). Then the player does nothing while a CPU with a real
/// deck plays three turns. Per deck: the CPU's activations and how many were negated, how often the negator was gone by
/// the end, the player's LP, and the cards the CPU still had. Run it with YGO_TAX_SENSE=0 for the CPU without its
/// negator sense. The first game of each deck prints the CPU's play-by-play.
/// </summary>
public static class NegatorTest
{
    public static int Run(ICardDataProvider cards, IScriptProvider scripts, string root, int games)
    {
        const uint fissure = 66788016, luster = 11091375, alexandrite = 43096270, geminiElf = 69140098, battleOx = 5053103;
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Console.WriteLine($"CPU negator sense {(SmartDuelist.TaxSense ? "on" : "off (YGO_TAX_SENSE=0)")}, {games} games per deck, three CPU turns each");
        foreach (var negator in new[] { SmartDuelist.LightAndDarknessDragon, SmartDuelist.Apollousa })
        {
            string negatorName = negator == SmartDuelist.Apollousa ? "Apollousa" : "Light and Darkness Dragon";
            Console.WriteLine($"\n##### {negatorName}");
            foreach (var name in new[] { "classic", "eldlich", "sky-striker", "tearlaments", "ryzeal", "branded-despia" })
            {
                int activations = 0, negated = 0, gone = 0, playerLp = 0, cpuCards = 0, startAtk = 0;
                for (int g = 0; g < games; g++)
                {
                    var filler = new Deck { Name = "filler" };
                    for (int i = 0; i < 30; i++) filler.Main.Add(fissure);
                    if (negator == SmartDuelist.Apollousa) filler.Extra.Add(SmartDuelist.Apollousa);
                    var theirs = Deck.LoadYdk(deckFiles[name]);
                    theirs.Name = name;
                    var recipe = new DuelRecipe
                    {
                        Cards = cards, Scripts = scripts, Settings = new DuelSettings { Seed = (ulong)(11 + g) },
                        Deck0 = filler.Clone(), Deck1 = theirs.Clone(),
                    };
                    if (negator == SmartDuelist.LightAndDarknessDragon)
                        recipe.Setup.Add(new PlacedCard(0, negator, Location.MZone, 2));
                    else
                        foreach (var (code, zone) in new[] { (luster, 0u), (alexandrite, 1u), (geminiElf, 3u), (battleOx, 4u) })
                            recipe.Setup.Add(new PlacedCard(0, code, Location.MZone, zone));
                    using var duel = recipe.Create();
                    var context = new DuelContext(duel, cards);
                    var lines = new List<string>();
                    var style = new Personality { Name = "CPU", Skill = 0.9 };
                    var cpu = new SmartDuelist("CPU", 5 + g, context, g == 0 ? lines.Add : null, style, Playbooks.ForDeck(name));
                    var player = new ScriptedDuelist(1, cards);
                    if (negator == SmartDuelist.Apollousa) { player.Summon.Add(negator); player.Activate.Add(negator); }
                    var runner = new DuelRunner(duel, player, cpu);
                    runner.MessageReceived += context.Observe;
                    var narrator = new DuelNarrator(cards, "You", "CPU");
                    narrator.Line += l => { if (g == 0) lines.Add(l); };
                    narrator.CodeAt = place => BoardState.PublicCodeAt(duel, place);
                    runner.MessageReceived += narrator.Handle;
                    runner.MessageReceived += m =>
                    {
                        if (m[0] == Msg.Chaining)
                        {
                            var r = new Ygo.Engine.Protocol.MessageReader(m, 1);
                            if (r.LocInfo(r.U32()).Controller == 1) activations++;
                        }
                        // The player only ever activates the negator, so every negated link is one of the CPU's.
                        if (m[0] == Msg.ChainNegated || m[0] == Msg.ChainDisabled) negated++;
                    };
                    bool sawNegator = false;
                    for (int i = 0; i < 20000 && runner.Step() && runner.Result.Turns <= 6; i++)
                    {
                        // Its stats as the CPU's first turn begins (Apollousa's ATK is set once its Link Summon succeeds).
                        if (!sawNegator && runner.Result.Turns >= 2
                            && duel.QueryLocation(0, Location.MZone).FirstOrDefault(c => c != null && c.Code == negator) is FieldCard n)
                        {
                            sawNegator = true;
                            startAtk += n.Attack;
                        }
                    }
                    if (!duel.QueryLocation(0, Location.MZone).Any(c => c != null && c.Code == negator)) gone++;
                    playerLp += runner.Result.Winner == 1 ? 0 : context.Board.Players[0].LifePoints;
                    cpuCards += new[] { Location.Hand, Location.MZone, Location.SZone }
                        .Sum(loc => duel.QueryLocation(1, loc).Count(c => c != null));
                    if (g == 0)
                    {
                        Console.WriteLine($"=== {name}: first game");
                        foreach (var l in lines.Where(l => !l.Contains("Fissure") && !l.Contains("draws")).Take(60)) Console.WriteLine("  " + l.TrimEnd());
                    }
                }
                Console.WriteLine($">>> {negatorName} vs {name}: {activations} CPU activations, {negated} negated; the negator was gone in {gone} of {games}; " +
                                  $"player LP {playerLp / games}; CPU cards left {cpuCards / (double)games:0.0}; negator's starting ATK {startAtk / games}");
            }
        }
        return 0;
    }
}
