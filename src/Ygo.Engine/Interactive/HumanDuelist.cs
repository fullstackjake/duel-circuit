using System;
using Ygo.Engine.AI;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Interactive
{
    /// <summary>
    /// A player whose decisions come from a UI. When a prompt arrives it publishes a
    /// <see cref="ChoiceSet"/> and returns null (the runner waits); the UI calls <see cref="Submit"/>
    /// and the next runner step delivers the answer.
    /// </summary>
    public sealed class HumanDuelist : IDuelist
    {
        private readonly PromptPresenter _presenter;
        private readonly RandomDuelist _fallback;
        private Prompt? _shownPrompt;
        private int _shownAttempt = -1;
        private byte[]? _answer;

        /// <summary>The decision currently waiting on the player, or null.</summary>
        public ChoiceSet? Current { get; private set; }

        /// <summary>True when the engine rejected the previous answer to this same prompt.</summary>
        public bool PreviousAnswerRejected { get; private set; }

        /// <summary>Raised whenever <see cref="Current"/> changes (new prompt, retry, or answered).</summary>
        public event Action? Changed;

        public PromptPresenter Presenter => _presenter;

        public HumanDuelist(DuelContext context, int seed = 0)
        {
            _presenter = new PromptPresenter(context);
            _fallback = new RandomDuelist("You", seed, context.Cards);
        }

        public void OnPhaseChanged() { }

        public byte[]? Respond(Prompt prompt, int attempt)
        {
            if (_answer != null && ReferenceEquals(prompt, _shownPrompt) && attempt == _shownAttempt)
            {
                var answer = _answer;
                _answer = null;
                Current = null;
                Changed?.Invoke();
                return answer;
            }

            // Routine prompts answer themselves; if the engine rejects that answer, find any legal one.
            var auto = _presenter.AutoAnswer(prompt);
            if (auto != null) return attempt == 0 ? auto : _fallback.Respond(prompt, attempt);

            if (!ReferenceEquals(prompt, _shownPrompt) || attempt != _shownAttempt)
            {
                _shownPrompt = prompt;
                _shownAttempt = attempt;
                PreviousAnswerRejected = attempt > 0;
                Current = _presenter.Present(prompt);
                Changed?.Invoke();
            }
            return null;
        }

        /// <summary>Answers the current prompt. Takes effect on the runner's next step.</summary>
        public void Submit(byte[] answer)
        {
            if (Current == null) return;
            _answer = answer;
        }
    }
}
