using System;
using System.Threading;
using Ygo.Engine.AI;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// Plays a batch of quick bot-vs-bot games on a background thread: your deck (piloted by the generic bot)
    /// against an opponent deck and character, alternating who goes first. Read the counters while it runs.
    /// Planning is off here for speed, so results reflect the deck more than the pilot.
    /// </summary>
    public sealed class DeckTester
    {
        private Thread? _thread;
        private volatile bool _cancel;

        public int Games { get; private set; }
        public int Completed;
        public int Wins, Draws, TotalTurns;
        public int GamesFirst, WinsFirst, GamesSecond, WinsSecond;
        public volatile bool Running;
        public string? Error;

        public void Start(ICardDataProvider cards, IScriptProvider scripts, Deck mine, Deck theirs, Personality opponent,
            int games, int seed)
        {
            Cancel();
            Games = games;
            Completed = Wins = Draws = TotalTurns = GamesFirst = WinsFirst = GamesSecond = WinsSecond = 0;
            Error = null;
            _cancel = false;
            Running = true;
            var myDeck = mine.Clone();
            var theirDeck = theirs.Clone();
            _thread = new Thread(() => Run(cards, scripts, myDeck, theirDeck, opponent, games, seed)) { IsBackground = true, Name = "Deck test" };
            _thread.Start();
        }

        public void Cancel()
        {
            _cancel = true;
            _thread?.Join(2000);
            _thread = null;
            Running = false;
        }

        private void Run(ICardDataProvider cards, IScriptProvider scripts, Deck mine, Deck theirs, Personality opponent, int games, int seed)
        {
            try
            {
                for (int g = 0; g < games && !_cancel; g++)
                {
                    int mySeat = g % 2; // alternate who goes first
                    var settings = new DuelSettings { Seed = (ulong)(seed + g) };
                    using var duel = new Duel(cards, scripts, settings);
                    duel.AddDeck(0, mySeat == 0 ? mine : theirs);
                    duel.AddDeck(1, mySeat == 0 ? theirs : mine);
                    var context = new DuelContext(duel, cards);
                    var me = new SmartDuelist("you", seed + g * 2, context);
                    var them = new SmartDuelist(opponent.Name, seed + g * 2 + 1, context, personality: opponent);
                    var runner = mySeat == 0 ? new DuelRunner(duel, me, them) : new DuelRunner(duel, them, me);
                    runner.MessageReceived += context.Observe;
                    var result = runner.Run(20000);

                    bool won = result.Winner == mySeat;
                    if (result.Winner > 1 || result.Winner < 0) Draws++;
                    if (won) Wins++;
                    if (mySeat == 0) { GamesFirst++; if (won) WinsFirst++; }
                    else { GamesSecond++; if (won) WinsSecond++; }
                    TotalTurns += result.Turns;
                    Interlocked.Increment(ref Completed);
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                Running = false;
            }
        }
    }
}
