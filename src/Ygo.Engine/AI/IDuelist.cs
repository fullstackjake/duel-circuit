using Ygo.Engine.Protocol;

namespace Ygo.Engine.AI
{
    /// <summary>
    /// Anything that can answer prompts: a bot, a human via UI, a network peer.
    /// </summary>
    public interface IDuelist
    {
        /// <summary>
        /// Answer a prompt. <paramref name="attempt"/> is 0 the first time; it increases each
        /// time the core rejects the previous answer (MSG_RETRY) for the same prompt.
        /// Return null to wait (e.g. a human is still deciding); the runner asks again on its next step.
        /// </summary>
        byte[]? Respond(Prompt prompt, int attempt);

        /// <summary>Called at every turn and phase change.</summary>
        void OnPhaseChanged();
    }
}
