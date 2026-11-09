using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Engine
{
    public sealed class DuelResult
    {
        /// <summary>0 or 1 for a winner, 2 for a draw, -1 if the duel was stopped before it ended.</summary>
        public int Winner = -1;
        public int Reason;
        public int Turns;
        public int Prompts;
        public int Retries;
    }

    /// <summary>
    /// Drives a duel: steps the core, forwards every message to listeners, and routes prompts
    /// to the right player. Call Step() repeatedly (e.g. once per frame in Unity) or Run() to finish at once.
    /// </summary>
    /// <summary>
    /// Every response given to the core, in order (including ones it rejected). Together with the
    /// <see cref="DuelRecipe"/> this reproduces the duel exactly, which is what the turn planner uses.
    /// </summary>
    public sealed class ResponseLog
    {
        public List<byte[]> Responses { get; } = new List<byte[]>();
    }

    /// <summary>Everything needed to recreate a duel from scratch: same cards, scripts, seed, and decks.</summary>
    public sealed class DuelRecipe
    {
        public ICardDataProvider Cards = null!;
        public IScriptProvider Scripts = null!;
        public DuelSettings Settings = new DuelSettings();
        public Deck Deck0 = new Deck();
        public Deck Deck1 = new Deck();
        /// <summary>Tag duels: each team's second duelist (core duelist 1). Both set, or neither.</summary>
        public Deck? Partner0, Partner1;

        public bool IsTag => Partner0 != null && Partner1 != null;

        /// <summary>Cards placed before the duel starts (lessons and puzzles): in the hand, on the field, in the GY.</summary>
        public List<PlacedCard> Setup = new List<PlacedCard>();

        public Duel Create()
        {
            var duel = new Duel(Cards, Scripts, Settings);
            duel.AddDeck(0, Deck0, deckId: 0);
            duel.AddDeck(1, Deck1, deckId: 1);
            if (IsTag)
            {
                duel.AddDeck(0, Partner0!, duelist: 1, deckId: 2);
                duel.AddDeck(1, Partner1!, duelist: 1, deckId: 3);
            }
            var looks = new[] { Deck0.Looks, Deck1.Looks, Partner0?.Looks, Partner1?.Looks };
            if (looks.Any(l => l != null)) duel.Looks = new CopyLooks(looks);
            foreach (var c in Setup) duel.AddCard(c.Team, c.Code, c.Location, c.Position, sequence: c.Sequence);
            return duel;
        }
    }

    /// <summary>A card put somewhere before the duel starts.</summary>
    public sealed class PlacedCard
    {
        public byte Team;
        public uint Code;
        public uint Location;
        public uint Sequence;
        public uint Position;

        public PlacedCard(byte team, uint code, uint location, uint sequence = 0, uint position = Ygo.Engine.Position.FaceUpAttack)
        {
            Team = team; Code = code; Location = location; Sequence = sequence; Position = position;
        }
    }

    public sealed class DuelRunner
    {
        private const int MaxRetriesPerPrompt = 200;

        private readonly Duel _duel;
        private readonly IDuelist[] _players;
        private Prompt? _pending;
        private int _attempt;
        private bool _started;
        private bool _waiting;

        /// <summary>Every message from the core, type byte first. Hook UI / logging here.</summary>
        public event Action<byte[]>? MessageReceived;

        public DuelResult Result { get; } = new DuelResult();
        public bool IsFinished { get; private set; }

        /// <summary>True while a player (usually a human) has not answered the current prompt yet.</summary>
        public bool IsWaiting => _waiting;
        /// <summary>The seat being waited on, or -1.</summary>
        public int WaitingFor => _waiting && _pending != null ? _pending.Player : -1;

        private readonly ResponseLog? _log;

        public DuelRunner(Duel duel, IDuelist player0, IDuelist player1, ResponseLog? log = null)
        {
            _duel = duel;
            _players = new[] { player0, player1 };
            _log = log;
        }

        /// <summary>Win reason for a Yata-lock (not a core reason: the core would let it play on).</summary>
        public const byte ReasonYataLock = 0xF0;
        /// <summary>Win reason when a player concedes (gives up the duel).</summary>
        public const byte ReasonConceded = 0xF1;
        /// <summary>Win (or draw) reason for the Back Alley's turn cap (<see cref="TurnCap"/>): turn 41 never starts.</summary>
        public const byte ReasonTurnCap = 0xF2;

        /// <summary>The ending a house rule or a concession asked for: the loser (2 for a draw) and the reason.</summary>
        private (int loser, byte reason)? _concession;

        /// <summary>
        /// Ends the duel with <paramref name="loser"/> losing, once the messages being handled now are done (so a core
        /// win in the same batch still counts first). Listeners get a MSG_WIN with <paramref name="reason"/>.
        /// </summary>
        public void Concede(int loser, byte reason) => _concession ??= (loser & 1, reason);

        /// <summary>Ends the duel as a draw once the messages being handled are done (MSG_WIN with player 2).</summary>
        public void EndInDraw(byte reason) => _concession ??= (2, reason);

        private void EndByConcession()
        {
            if (_concession is not { } c || Result.Winner >= 0) return;
            int winner = c.loser == 2 ? 2 : 1 - c.loser;
            var win = new[] { Msg.Win, (byte)winner, c.reason };
            MessageReceived?.Invoke(win);
            Result.Winner = winner;
            Result.Reason = c.reason;
            IsFinished = true;
        }

        public DuelResult Run(int maxPrompts = 20000)
        {
            while (Result.Prompts < maxPrompts && Step()) { }
            return Result;
        }

        /// <summary>
        /// Advances the core once and answers the prompt it stops on, if any.
        /// Returns false once the duel is over.
        /// </summary>
        public bool Step()
        {
            if (IsFinished) return false;
            if (!_started)
            {
                _duel.Start();
                _started = true;
            }

            // A concession while waiting on a player (you gave up at your own decision): over now, unanswered.
            if (_waiting && _concession != null)
            {
                EndByConcession();
                return false;
            }
            // Still waiting on a player: ask again, and only continue once they have answered.
            if (_waiting && !Answer()) return true;

            var status = _duel.Process();
            byte[]? promptMessage = null;
            bool retry = false;

            foreach (var message in _duel.GetMessages())
            {
                MessageReceived?.Invoke(message);
                switch (message[0])
                {
                    case Msg.Retry:
                        retry = true;
                        break;
                    case Msg.NewTurn:
                        Result.Turns++;
                        foreach (var p in _players) p.OnPhaseChanged();
                        break;
                    case Msg.NewPhase:
                        foreach (var p in _players) p.OnPhaseChanged();
                        break;
                    case Msg.Win:
                        Result.Winner = message[1];
                        Result.Reason = message[2];
                        break;
                    default:
                        if (PromptParser.IsPrompt(message[0])) promptMessage = message;
                        break;
                }
            }

            // A house rule (a Yata-lock) or a player conceding decided the duel: it ends here, as a win for the other player.
            if (status != DuelStatus.End) EndByConcession();
            if (status == DuelStatus.End || Result.Winner >= 0)
            {
                IsFinished = true;
                return false;
            }
            if (status != DuelStatus.Awaiting) return true;

            if (retry && _pending != null)
            {
                Result.Retries++;
                if (++_attempt > MaxRetriesPerPrompt)
                    throw new InvalidOperationException($"Player {_pending.Player} could not answer {_pending.GetType().Name}{Describe(_pending)}"
                                                        + $" (last answer {BitConverter.ToString(_lastResponse ?? Array.Empty<byte>())})");
            }
            else if (promptMessage != null)
            {
                _pending = PromptParser.Parse(promptMessage);
                _attempt = 0;
                Result.Prompts++;
            }
            else
            {
                throw new InvalidOperationException("Core is awaiting a response but sent no prompt");
            }

            Answer();
            return true;
        }

        private byte[]? _lastResponse;

        /// <summary>For the error when a prompt can't be answered: what it offered.</summary>
        private static string Describe(Prompt prompt) => prompt switch
        {
            BattlePrompt p => $": activatable [{string.Join(", ", p.Activatable.Select(c => $"{c.Card.Code}/{c.Description}"))}], "
                                       + $"attackers [{string.Join(", ", p.Attackers.Select(c => c.Code))}], main2 {p.CanMain2}, end {p.CanEndPhase}",
            IdlePrompt p => $": activatable [{string.Join(", ", p.Activatable.Select(c => $"{c.Card.Code}/{c.Description}"))}]",
            _ => "",
        };

        private bool Answer()
        {
            var response = _players[_pending!.Player].Respond(_pending, _attempt);
            _waiting = response == null;
            _lastResponse = response;
            if (response != null)
            {
                _log?.Responses.Add(response);
                _duel.SetResponse(response);
            }
            return response != null;
        }
    }
}
