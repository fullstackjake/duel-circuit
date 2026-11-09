using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The deck editor's card versions as collectibles (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.3, 5.4, phase 2): which texts
    /// you own copies of, the CARD TEXT switch limited to them, the own-copies line per version, and -versioncheck's campaign
    /// half. The switch itself (CycleText, ChooseVersion, Resolve, the note) stays in DeckEditor.cs.
    /// </summary>
    public sealed partial class DeckEditor
    {
        /// <summary>The text a copy must carry for a slot holding this entry (<see cref="Collection.AskedText"/>; 0: any copy).</summary>
        private uint AskedText(uint code) => Collection.AskedText(code, _versions.TextRows, _cards);

        /// <summary>Your copies (not at the desk) carrying the text this entry asks for; every copy for an entry any copy plays.</summary>
        private int OwnedOfText(uint code)
        {
            if (OwnedCards == null) return Collection.MaxCopies;
            var key = Banlist.BaseCode(code, _cards);
            var asked = AskedText(code);
            return asked == 0 ? OwnedCards.Count(key) : OwnedCards.Count(key, asked);
        }

        /// <summary>The deck's slots (Main, Extra and Side) asking for this entry's text: an original's copies are spoken for by these.</summary>
        private int SlotsAsking(uint code)
        {
            var key = Banlist.BaseCode(code, _cards);
            var asked = AskedText(code);
            return _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Count(c => Banlist.BaseCode(c, _cards) == key && AskedText(c) == asked);
        }

        /// <summary>Whether adding this entry is backed by a copy of its text not yet in the deck (always, outside the campaign or for any-copy entries).</summary>
        private bool HasSpare(uint code) => OwnedCards == null || AskedText(code) == 0 || OwnedOfText(code) > SlotsAsking(code);

        /// <summary>
        /// Whether the campaign's CARD TEXT switch offers this entry: the current text (and any alternate text) always; an
        /// original only when you own a copy carrying it; a ruling-only card's 2005 ruling under a Goat list; a version no
        /// copy can carry, never. Outside the campaign every text is offered.
        /// </summary>
        private bool OffersText(uint code)
        {
            if (OwnedCards == null) return true;
            if (_cards.Get(code) is not CardInfo info) return false;
            var kind = CardVersions.KindOf(info);
            if (kind == CardVersions.TextKind.Current || kind == CardVersions.TextKind.Other) return true;
            var key = Banlist.BaseCode(code, _cards);
            var asked = AskedText(code);
            if (asked != 0) return OwnedCards.Count(key, asked) > 0;
            return kind == CardVersions.TextKind.Goat && _versions.TextRows.Earliest(key) == null && TableTexts.Goat;
        }

        /// <summary>The texts the CARD TEXT switch cycles here: <see cref="CardVersions.PlayerTexts"/> you own (the one shown always stays).</summary>
        private List<uint> SwitchTexts(uint code)
        {
            var shown = _versions.Canonical(code);
            return _versions.PlayerTexts(code).Where(c => c == shown || OffersText(c)).ToList();
        }

        /// <summary>
        /// The own-copies part of the detail line, per version for a card with an original text (spec 5.4): "own 3 · 1 original
        /// (MRD-069, 2002) · 2 current"; "own 3" for any other card.
        /// </summary>
        private string OwnCopiesLine(uint key, int owned)
        {
            if (OwnedCards == null || !_versions.TextRows.Timeline(key).Any(r => r.Collectible)) return $"own {owned}";
            var copies = OwnedCards.CopiesOf(key).Where(c => !c.AtDesk).ToList();
            var parts = new List<string> { $"own {owned}" };
            var rows = _versions.TextRows;
            bool several = rows.Timeline(key).Count(r => r.Collectible) > 1;
            // By the same test as play (CardDetail.OriginalRow): a stored text that's no longer a collectible row reads current.
            foreach (var text in copies.Where(c => CardDetail.CarriesOriginal(c, rows)).GroupBy(c => CardDetail.OriginalRow(c, rows)!.Code))
            {
                var prints = text.Where(c => c.Set.Length > 0).Select(c => c.Printed.Length >= 4 ? $"{c.Set}, {c.Printed.Substring(0, 4)}" : c.Set).Distinct().ToList();
                var until = several && rows.Row(text.Key) is TextRow row && row.Until.Length >= 7 ? $" until {CardTexts.ShortMonth(row.Until)}" : "";
                parts.Add($"{text.Count()} original{until}{(prints.Count > 0 ? $" ({string.Join("; ", prints)})" : "")}");
            }
            int current = copies.Count(c => !CardDetail.CarriesOriginal(c, rows));
            if (current > 0 || parts.Count == 1) parts.Add($"{current} current");
            return string.Join(" · ", parts);
        }

        /// <summary>For automated checks: the own-copies line for a card (no markup).</summary>
        public string DebugOwnCopies(uint code)
        {
            var key = Banlist.BaseCode(code, _cards);
            return OwnCopiesLine(key, OwnedCards?.Count(key) ?? 0);
        }

        /// <summary>For automated checks: the texts the CARD TEXT switch offers for a card, and the shortfall notes under the deck.</summary>
        public (List<uint> texts, List<string> shortfalls) DebugTextsOffered(uint code) =>
            (SwitchTexts(code), OwnedCards == null ? new List<string>() : CollectionRules.TextShortfalls(_deck, OwnedCards, _versions.TextRows, _cards).Select(p => p.Message).ToList());

        /// <summary>The versus line for a deck at a table's texts, from a collection's copies (set by the duel screen: CampaignState.TextsLine).</summary>
        public Func<Deck, TextRules, Collection, string>? VersusLine;

        /// <summary>
        /// -versioncheck's campaign half (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 12, phase 2): a collection of stamped copies
        /// (1 original and 2 current Sangan; 3 current Witch of the Black Forest, one of them from before printings were
        /// recorded). The CARD TEXT switch offers only texts you own; switching Sangan's text changes 1 of 3 slots; the
        /// own-copies line counts each version; the card detail lists each copy's printing ("printing not recorded" for the
        /// unstamped one); Resolve adds an original only with a spare copy; the shortfall note and the versus line.
        /// </summary>
        private System.Collections.IEnumerator CampaignVersionCheck(Action<string, bool> Report, Func<string, System.Collections.IEnumerator> Shot)
        {
            var printings = CardFoil.Printings;
            var rows = _versions.TextRows;
            CardInfo? Named(string name) => _cards.All.FirstOrDefault(c => c.Name == name && c.Alias == 0);
            var sangan = Named("Sangan");
            var witch = Named("Witch of the Black Forest");
            var sanganRow = sangan == null ? null : rows.Timeline(sangan.Code).FirstOrDefault(r => r.Collectible);
            var witchRow = witch == null ? null : rows.Timeline(witch.Code).FirstOrDefault(r => r.Collectible);
            if (printings == null || sangan == null || witch == null || sanganRow == null || witchRow == null)
            {
                Report($"campaign versions: printings.bin ({printings != null}), Sangan's and Witch's original rows ({sanganRow?.Code}, {witchRow?.Code}) are installed", false);
                yield break;
            }
            var owned = new Collection { Texts = rows, Printings = printings, Salt = 0x5A9 };
            CardCopy Stamped(CardInfo card, Printing? printing)
            {
                var copy = owned.NewCopy(card.Code, printing?.Rarity ?? Rarity.Common, CopySource.Pack);
                owned.Stamp(copy, printing, printings);
                owned.Insert(copy);
                return copy;
            }
            var first = CopyPrints.Earliest(sangan.Code, null, "", printings, _cards);
            var newest = CopyPrints.NewestOfAny(sangan.Code, 9999, printings, _cards);
            var original = Stamped(sangan, first);
            Stamped(sangan, newest);
            Stamped(sangan, newest);
            var witchNow = CopyPrints.NewestOfAny(witch.Code, 9999, printings, _cards);
            Stamped(witch, witchNow);
            Stamped(witch, witchNow);
            var unstamped = Stamped(witch, null);
            Report($"the copies are stamped: Sangan {original.Set} {original.Printed} carries text {original.Text} (its original {sanganRow.Code}), "
                   + $"the others {newest?.SetCode} {(newest is Printing n ? printings.DateOf(n) : "")}, current; one Witch unstamped",
                original.Text == sanganRow.Code && owned.Count(sangan.Code, sanganRow.Code) == 1 && owned.Count(sangan.Code, 0) == 2
                && owned.Count(witch.Code, 0) == 3 && unstamped.Set.Length == 0 && unstamped.Text == 0);

            // A campaign deck under TCG April 2004 (originals play as printed): 3 Sangan, 3 Witch, all current text.
            var copiesOf = CardDetail.CopiesOf;
            CardDetail.CopiesOf = code => owned.CopiesOf(code);
            Open(null, owned, "TCG April 2004");
            _deck = new Deck { Name = "Version Check", Campaign = true };
            for (int i = 0; i < 3; i++) { _deck.Main.Add(sangan.Code); _deck.Main.Add(witch.Code); }
            _preferred.Clear();
            RefreshDeck();
            yield return null;

            // The switch: Sangan offers its original (you own one); Witch offers only its current text (you own none).
            ShowDetail(sangan);
            var sanganTexts = SwitchTexts(sangan.Code);
            bool OwnedText(uint c) => AskedText(c) is var a && (a == 0 ? CardVersions.KindOf(_cards.Get(c)!) == CardVersions.TextKind.Current || OffersText(c) : owned.Count(sangan.Code, a) > 0);
            Report($"CARD TEXT on Sangan offers {string.Join(", ", sanganTexts.Select(c => _cards.Get(c)?.Name))} (every text: {_versions.PlayerTexts(sangan.Code).Count})",
                sanganTexts.Contains(sanganRow.Code) && sanganTexts.Contains(sangan.Code) && sanganTexts.All(OwnedText));
            var ownLine = DebugOwnCopies(sangan.Code);
            Report($"the own-copies line reads \"{ownLine}\"", ownLine == $"own 3 · 1 original ({original.Set}, {original.Printed.Substring(0, 4)}) · 2 current");
            var detail = Plain(_detail.text);
            Report($"the detail shows it: \"{detail}\"", detail.Contains(ownLine));
            var sanganCopies = _cardDetail.DebugCopiesShown;
            Report($"the card detail lists each copy's printing: \"{sanganCopies.Replace("\n", " / ")}\"",
                sanganCopies.Split('\n').Length == 3 && sanganCopies.Contains($"{original.Set} {original.Printed.Substring(0, 4)} · ORIGINAL TEXT")
                && sanganCopies.Split('\n').Count(l => l.Contains("ORIGINAL TEXT")) == 1);
            yield return Shot("versions-campaign-sangan");

            ShowDetail(witch);
            var witchTexts = SwitchTexts(witch.Code);
            var witchNote = Plain(TextNote(witch));
            Report($"CARD TEXT on Witch offers only {string.Join(", ", witchTexts.Select(c => _cards.Get(c)?.Name))} (every text: {_versions.PlayerTexts(witch.Code).Count}); the note: \"{witchNote}\"",
                witchTexts.Count == 1 && witchTexts[0] == witch.Code && _versions.PlayerTexts(witch.Code).Count > 1
                && witchNote.Contains("You don't own an original copy. Old sets in the back catalog print it."));
            var witchCopies = _cardDetail.DebugCopiesShown;
            Report($"an unstamped copy reads \"printing not recorded\": \"{witchCopies.Replace("\n", " / ")}\"",
                witchCopies.Split('\n').Count(l => l.Contains("printing not recorded")) == 1 && !witchCopies.Contains("ORIGINAL TEXT"));
            CycleText();
            int witches = _deck.Main.Count(c => c == witch.Code);
            Report($"clicking CARD TEXT on Witch changes nothing ({witches} current Witch)", _selected?.Code == witch.Code && witches == 3);
            yield return Shot("versions-campaign-witch");

            // Switching Sangan's text: 1 of 3 slots, as many as you own original copies.
            // (Under a 2004 list the detail opens on the original you have a spare of, so the switch goes round to it.)
            ShowDetail(sangan);
            int clicks = 0;
            do { CycleText(); clicks++; } while (clicks < 5 && _selected?.Code != sanganRow.Code);
            var status = Plain(_status.text);
            int originals = _deck.Main.Count(c => c == sanganRow.Code), currents = _deck.Main.Count(c => c == sangan.Code);
            Report($"switching to the original: \"{status}\" ({originals} original, {currents} current Sangan in the deck)",
                _selected?.Code == sanganRow.Code && originals == 1 && currents == 2
                && status == "Switched 1 of 3 Sangan to its original text (you own 1 original copy).");
            var note = Plain(TextNote(_selected!));
            Report($"the original's note under TCG April 2004: \"{note}\"", note.StartsWith("Original text, as printed until") && note.EndsWith("It plays as printed under this list."));
            yield return Shot("versions-campaign-switched");
            // Adding another Sangan: no spare original, so the current text.
            RemoveOne(sangan.Code);
            var added = Resolve(sangan);
            Report($"with your one original in the deck, a Sangan added from search is {added.Name}", added.Code == sangan.Code);
            Change(d => d.Main.Add(sangan.Code));

            // A Witch slot asking for the original you don't own: a shortfall note, and the versus line names it.
            Change(d => d.Main[d.Main.IndexOf(witch.Code)] = witchRow.Code);
            var shortfalls = CollectionRules.TextShortfalls(_deck, owned, rows, _cards);
            var problems = Plain(_problems.text);
            Report($"the shortfall note: \"{string.Join("; ", shortfalls.Select(p => p.Message))}\" (in the problems box: {problems.Contains(shortfalls.FirstOrDefault()?.Message ?? "-")}; still legal: {CollectionRules.Validate(_deck, owned, _cards).Count == 0})",
                shortfalls.Count == 1 && shortfalls[0].Code == witch.Code && shortfalls[0].Message.Contains("you own no original copies, so 1 plays its current text")
                && problems.Contains(shortfalls[0].Message) && CollectionRules.Validate(_deck, owned, _cards).Count == 0);
            yield return Shot("versions-campaign-shortfall");
            if (VersusLine != null)
            {
                var april = VersusLine(_deck, TableTexts, owned);
                Report($"the versus line under TCG April 2004: \"{april}\"",
                    april == $"1 card plays its current text here: {witch.Name}. You own too few original copies of {witch.Name}.");
                var today = _banlists.FirstOrDefault(b => b.Name == "TCG October 2026");
                var modern = today == null ? "" : VersusLine(_deck, TextRules.ForList(today, _banlists), owned);
                Report($"the versus line under TCG October 2026: \"{modern}\"",
                    modern == $"2 cards play their current text here: {sangan.Name}, {witch.Name}.");
            }
            else Report("the duel screen gave the editor the versus line", false);

            // A custom loaner (or your edit of a partner's deck) edits with the recipe's cards on top of yours: your real copies
            // count per text as they play (Loaners.Borrowed), and a borrowed card is current text.
            var mirror = Named("Mirror Force");
            var recipe = new Deck { Name = "Version Check Recipe" };
            for (int i = 0; i < 3; i++) { recipe.Main.Add(sanganRow.Code); recipe.Main.Add(witchRow.Code); if (mirror != null) recipe.Main.Add(mirror.Code); }
            var lent = Ygo.Engine.Campaign.Loaners.Borrowed(recipe, _cards, owned);
            ClearRecovery(); // (the first deck's autosave: it would offer itself back on this opening)
            _dirty = false;
            Open(null, lent, "TCG April 2004");
            _deck = new Deck { Name = "Version Check (custom loaner)", Campaign = true };
            _deck.Notes["loaner"] = recipe.Name;
            _deck.Main.AddRange(recipe.Main);
            _preferred.Clear();
            RefreshDeck();
            yield return null;
            ShowDetail(witch);
            var lentWitch = SwitchTexts(witch.Code);
            var lentLine = DebugOwnCopies(sangan.Code);
            var lentShort = CollectionRules.TextShortfalls(_deck, lent, rows, _cards).Select(p => p.Message).ToList();
            Report($"a custom loaner's cards (Loaners.Borrowed) count your copies per text: Sangan {lent.Count(sangan.Code)} ({lent.Count(sangan.Code, sanganRow.Code)} original), "
                   + $"Witch {lent.Count(witch.Code)} ({lent.Count(witch.Code, witchRow.Code)} original), Mirror Force {(mirror == null ? -1 : lent.Count(mirror.Code))} borrowed; "
                   + $"Witch's switch offers {lentWitch.Count} text(s); \"{lentLine}\"; shortfalls: {string.Join("; ", lentShort)}",
                lent.Count(sangan.Code) == 3 && lent.Count(sangan.Code, sanganRow.Code) == 1 && lent.Count(witch.Code) == 3 && lent.Count(witch.Code, witchRow.Code) == 0
                && (mirror == null || lent.Count(mirror.Code, 0) == 3) && lentWitch.Count == 1 && lentWitch[0] == witch.Code
                && lentLine == ownLine && lentShort.Count == 2 && lentShort.Any(m => m.StartsWith(sangan.Name + ": 3 slots")) && lentShort.Any(m => m.StartsWith(witch.Name + ": 3 slots")));
            yield return Shot("versions-campaign-loaner");

            CardDetail.CopiesOf = copiesOf;
            // Its edits autosaved a recovery copy (recovery-campaign.txt): not a deck anyone left unsaved.
            ClearRecovery();
            _dirty = false;
        }

        private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "").Replace("\n", " ");
    }
}
