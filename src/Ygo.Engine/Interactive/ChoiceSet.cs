using System;
using System.Collections.Generic;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Interactive
{
    public enum ChoiceMode
    {
        /// <summary>Pick exactly one; picking answers the prompt.</summary>
        Single,
        /// <summary>Toggle between Min and Max choices, then Confirm.</summary>
        Multi,
        /// <summary>Pick one at a time (the core asks again after each pick); Finish when allowed.</summary>
        Incremental,
        /// <summary>Put every choice in order: click them first to last, then Confirm (Finish keeps the default order).</summary>
        Order,
    }

    public sealed class Choice
    {
        public string Label = "";
        /// <summary>The card this choice is about, so the UI can let you click it on the board.</summary>
        public CardRef? Card;
        /// <summary>A short button label for the card's action menu ("Summon", "Set", "Activate"...).</summary>
        public string Verb = "";
        /// <summary>Incremental selections: already picked (picking it again unselects it).</summary>
        public bool AlreadySelected;
    }

    /// <summary>
    /// A prompt rendered as a plain list of choices. The UI only shows labels and returns indices;
    /// the set itself knows how to turn those into the core's response bytes.
    /// </summary>
    public sealed class ChoiceSet
    {
        public string Title = "";
        public string Detail = "";
        public ChoiceMode Mode = ChoiceMode.Single;
        public int Min = 1;
        public int Max = 1;
        public List<Choice> Choices = new List<Choice>();
        /// <summary>A chance to respond (chain): the UI asks "Chain?" with the cards that can, and a No.</summary>
        public bool IsChain;
        /// <summary>For chains and chain orders: how many links are on the chain already (the next one is that + 1).</summary>
        public int ChainLength;

        /// <summary>The prompt this set was built from.</summary>
        public Prompt Prompt = null!;

        internal Func<IReadOnlyList<int>, byte[]> Build = _ => Array.Empty<byte>();
        internal byte[]? CancelBytes;
        internal byte[]? FinishBytes;

        public bool CanCancel => CancelBytes != null;
        public bool CanFinish => FinishBytes != null;

        public byte[] Choose(params int[] indices) => Build(indices);
        public byte[] Choose(IReadOnlyList<int> indices) => Build(indices);

        public byte[] Cancel() => CancelBytes ?? throw new InvalidOperationException("This prompt cannot be cancelled");
        public byte[] Finish() => FinishBytes ?? throw new InvalidOperationException("This prompt cannot be finished yet");

        public bool IsValidCount(int count) => count >= Min && count <= Max;
    }
}
