using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-ordercheck &lt;folder&gt;`: choosing the order of effects that activate at the same time. Your Sangan and Witch of the
    /// Black Forest are on the field with Dark Hole in hand: Dark Hole destroys both, both mandatory effects trigger, and
    /// you pick them onto the chain one at a time, the chain dialog saying what each becomes (checked against the chain
    /// that forms). Then Spellbook Organization: the top 3 cards of your Deck back in the order you choose on the order
    /// screen (checked against the Deck). Also the chain dialog's hint on a synthetic choice.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator OrderCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"order-{name}.png"));
                yield return null;
                yield return null;
            }
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"order check: {(ok ? "ok  " : "FAIL")} {what}"); }
            const uint sangan = 26202165, witch = 78010363, darkHole = 53129443, luster = 11091375, organization = 96677818;
            yield return new WaitForSecondsRealtime(0.5f);
            _checkSetup = new List<PlacedCard>
            {
                new PlacedCard(0, sangan, Location.MZone, 1, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, witch, Location.MZone, 3, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, darkHole, Location.Hand),
                new PlacedCard(0, organization, Location.Hand),
                new PlacedCard(1, luster, Location.MZone, 2, Ygo.Engine.Position.FaceUpAttack),
            };
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 31;
            _paused = false;
            _nextFirstPlayer = 0; // a board set for your turn 1 (even with -gosecond)
            NewDuel();
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            var until = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is IdlePrompt && _board.TurnPlayer == 0)) yield return null;
            var idle = _view.Prompt.Current;
            Report("your Main Phase, with Sangan and Witch of the Black Forest out", idle?.Prompt is IdlePrompt);
            if (idle == null) { Application.Quit(); yield break; }

            // The chain dialog's hint when several cards could be picked, on a chain with nothing on it yet.
            var refs = new[] { new CardRef(sangan, 0, Location.MZone, 1), new CardRef(witch, 0, Location.MZone, 3) };
            var hint = new ChoiceSet { Title = "You can activate a card or effect", IsChain = true, ChainLength = 0, Prompt = new ChainPrompt { Player = 0 } };
            foreach (var r in refs) hint.Choices.Add(new Choice { Label = "Activate", Card = r });
            _view.ShowChoices(hint, false);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("chain-hint");
            var texts = _view.Dialogs.Root?.GetComponentsInChildren<Text>().Select(t => t.text).ToList() ?? new List<string>();
            Report($"several cards to pick: each says it becomes Chain Link 1, and the hint says it resolves last ({texts.FirstOrDefault(t => t.Contains("resolves")) ?? "no hint"})",
                texts.Count(t => t == "→ Chain Link 1") == 2 && texts.Any(t => t.Contains("Chain Link 1 and resolves last")));
            _view.ShowChoices(idle, false);
            yield return null;

            // Dark Hole: both monsters are destroyed and both mandatory effects trigger. You pick which goes on the chain
            // first, one at a time (the core asks for required effects that way): Witch, then Sangan.
            int activate = idle.Choices.FindIndex(c => c.Card?.Code == darkHole);
            Report("Dark Hole can be activated", activate >= 0);
            if (activate < 0) { Application.Quit(); yield break; }
            _view.Prompt.ClickChoice(activate);
            bool askedBoth = false, hinted = false;
            var picked = new List<uint>();
            List<(int number, uint code)> links = new List<(int, uint)>();
            until = Time.realtimeSinceStartup + 20;
            ChoiceSet? lastLogged = null;
            // (After the first pick the core chains the last required effect itself: stop once both links are there.)
            while (Time.realtimeSinceStartup < until && picked.Count < 2 && links.Count < 2)
            {
                var set = _view.Prompt.Current;
                if (set != null && set != lastLogged)
                {
                    lastLogged = set;
                    Debug.Log($"order check: prompt {set.Prompt.GetType().Name} \"{set.Title}\" [{string.Join(", ", set.Choices.Select(c => c.Label))}] forced {(set.Prompt as ChainPrompt)?.Forced}");
                }
                if (set != null && set.IsChain && set.Prompt is ChainPrompt { Forced: true } && set.Choices.Any(c => c.Card?.Code is sangan or witch))
                {
                    if (set.Choices.Count >= 2 && !askedBoth)
                    {
                        askedBoth = true;
                        yield return new WaitForSecondsRealtime(0.3f);
                        yield return Shot("required-both");
                        var shown = _view.Dialogs.Root?.GetComponentsInChildren<Text>().Select(t => t.text).ToList() ?? new List<string>();
                        hinted = shown.Count(t => t.StartsWith("→ Chain Link")) >= 2 && shown.Any(t => t.Contains("resolves last"));
                    }
                    int pick = set.Choices.FindIndex(c => c.Card?.Code == (picked.Count == 0 ? witch : sangan));
                    if (pick < 0) pick = 0;
                    picked.Add(set.Choices[pick].Card!.Value.Code);
                    _view.Prompt.ClickChoice(pick);
                }
                else if (set != null && set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set != null && set.Prompt is PlacePrompt or PositionPrompt) _view.Prompt.ClickChoice(0);
                else if (set != null && set.Prompt is SelectCardPrompt && set.Mode == ChoiceMode.Single && set.Choices.Count > 0) _view.Prompt.ClickChoice(0);
                if (_narrator != null && _narrator.Chain.Links.Count >= 2) links = _narrator.Chain.Links.Select(l => (l.Number, l.Code)).ToList();
                yield return null;
            }
            Report("both required effects are offered together, each saying the Chain Link it becomes, with the resolve order explained", askedBoth && hinted);
            until = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < until && links.Count < 2)
            {
                if (_narrator != null && _narrator.Chain.Links.Count >= 2) links = _narrator.Chain.Links.Select(l => (l.Number, l.Code)).ToList();
                var set = _view.Prompt.Current;
                if (set != null && set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set != null && set.Prompt is SelectCardPrompt && set.Mode == ChoiceMode.Single && set.Choices.Count > 0) _view.Prompt.ClickChoice(0);
                yield return null;
            }
            Report($"the chain forms in the order you picked ({string.Join(", ", links.Select(l => $"{l.number}: {_cardPack!.Get(l.code)?.Name}"))})",
                links.Count >= 2 && links.First(l => l.number == 1).code == witch && links.First(l => l.number == 2).code == sangan);

            // Spellbook Organization: the top 3 cards back in your order (the first on top).
            until = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is IdlePrompt && _board.TurnPlayer == 0))
            {
                var set = _view.Prompt.Current;
                if (set != null && set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set != null && set.Prompt is not IdlePrompt && set.Mode == ChoiceMode.Single && set.Choices.Count > 0) _view.Prompt.ClickChoice(0);
                else if (set != null && set.Mode == ChoiceMode.Multi) { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
                yield return null;
            }
            var main = _view.Prompt.Current;
            int organize = main?.Choices.FindIndex(c => c.Card?.Code == organization) ?? -1;
            Report("back in your Main Phase with Spellbook Organization to activate", organize >= 0);
            if (organize < 0) { Debug.Log(bad == 0 ? "order check: all passed" : $"order check: {bad + 1} failed"); Application.Quit(); yield break; }
            _view.Prompt.ClickChoice(organize);
            until = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is SortPrompt))
            {
                var set = _view.Prompt.Current;
                if (set != null && set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set != null && set.Prompt is PlacePrompt or PositionPrompt) _view.Prompt.ClickChoice(0);
                yield return null;
            }
            var order = _view.Prompt.Current;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("order-empty");
            Report($"you're asked the order of the top {order?.Choices.Count} cards ({order?.Title})", order?.Prompt is SortPrompt { IsChain: false } && _view.Dialogs.Kind == "order");
            if (order?.Prompt is not SortPrompt) { Debug.Log($"order check: {bad} failed"); Application.Quit(); yield break; }
            // The last card offered on top, then the first, then the middle.
            var mine = new List<int> { order.Choices.Count - 1, 0 };
            mine.AddRange(Enumerable.Range(1, order.Choices.Count - 2));
            foreach (var i in mine) _view.Prompt.ClickChoice(i);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("order-placed");
            var captions = _view.Dialogs.Root?.GetComponentsInChildren<Text>().Select(t => t.text).ToList() ?? new List<string>();
            Report($"each card shows its place ({string.Join(", ", captions.Where(t => t.Contains("top")))})",
                captions.Contains("on top") && captions.Contains("2nd from the top") && _view.Prompt.OrderOf(mine[0]) == 0);
            // Taking one back out, and putting it back.
            _view.Prompt.ClickChoice(mine[^1]);
            bool tookBack = _view.Prompt.OrderOf(mine[^1]) < 0;
            _view.Prompt.ClickChoice(mine[^1]);
            Report("clicking a placed card takes it back out", tookBack && _view.Prompt.OrderOf(mine[^1]) == mine.Count - 1);
            var wantTop = mine.Select(i => order.Choices[i].Card!.Value.Code).ToList();
            _view.Prompt.Confirm();
            yield return new WaitForSecondsRealtime(1f);
            var deck = _duel!.QueryLocation(0, Location.Deck);
            var top = Enumerable.Range(0, wantTop.Count).Select(k => deck[deck.Count - 1 - k]?.Code ?? 0).ToList();
            Report($"the Deck's top is in your order ({string.Join(", ", top.Select(c => _cardPack!.Get(c)?.Name))})", top.SequenceEqual(wantTop));
            Debug.Log(bad == 0 ? "order check: all passed" : $"order check: {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
