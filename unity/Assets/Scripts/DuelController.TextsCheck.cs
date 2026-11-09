using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The checks for card texts at a table (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md phase 1, chunk 1.2): a deck of yours with
    /// pre-errata Chaos Emperor Dragon. At a circuit past its errata (November 2018) the versus line names it and the duel
    /// plays the current card; at the Goat Cup the same slot plays the original; the deck editor cycles Mind Master through
    /// both originals and says which text plays under the picked list (a deck's own "#banlist" doesn't overrule the circuit's
    /// texts); Kit bringing your deck to a Tag Cup plays the same texts. -campaigncheck runs them, -themecheck shoots the
    /// versus line and the editor's note in both themes.
    /// </summary>
    public sealed partial class DuelController
    {
        private const string TextsCheckDeck = "Texts Check", TextsCheckOldDeck = "Texts Check 2004";

        /// <summary>The pieces of the texts checks: Chaos Emperor Dragon, its original text's row, and a circuit past its errata.</summary>
        private (uint ced, TextRow? row, int modern) TextsCheckSetup()
        {
            var ced = _cardPack!.All.FirstOrDefault(c => c.Name == "Chaos Emperor Dragon - Envoy of the End" && c.Alias == 0)?.Code ?? 0;
            var row = _campaign.Texts.Timeline(ced).FirstOrDefault(r => r.Collectible);
            int modern = -1;
            for (int i = 0; row != null && i < _campaign.Circuits.All.Count && modern < 0; i++)
                if (!_campaign.CircuitTexts(i).Allows(row) && _campaign.Circuits.All[i].Matches.Count > 0) modern = i;
            return (ced, row, modern);
        }

        /// <summary>
        /// Your deck with one pre-errata Chaos Emperor Dragon in its first slot (and a copy of the card in your collection),
        /// saved as a campaign deck and made the one you play. Returns the deck you played before.
        /// </summary>
        private string TextsCheckDeckIn(Profile profile, TextRow row)
        {
            var deck = _library.Load(profile.Deck).Clone();
            deck.Name = TextsCheckDeck;
            deck.Campaign = true;
            deck.Notes.Remove("loaner");
            deck.Main[0] = row.Code;
            // Phase 2: the slot plays its original only from a copy carrying it (a real printing from before the errata).
            OriginalCopyIn(profile, row);
            _library.Save(deck);
            var was = profile.Deck;
            profile.Deck = deck.Name;
            _campaign.Save();
            return was;
        }

        /// <summary>
        /// A copy of <paramref name="row"/>'s card carrying its original text in the collection (unless one is there): its
        /// earliest real printing before the errata, stamped as a pack copy would be. The copy it adds, or null.
        /// </summary>
        private CardCopy? OriginalCopyIn(Profile profile, TextRow row)
        {
            if (profile.Collection.Count(row.Base, row.Code) > 0) return null;
            var copy = new CardCopy { Code = row.Base, Rarity = Rarity.Common, Grade = 8, Id = profile.Collection.NextId++, Source = CopySource.Pack, Paid = 10 };
            if (_campaign.Printings is PrintingIndex printings && CopyPrints.Earliest(row.Base, null, "", printings, _cardPack) is Printing first)
            {
                copy.Rarity = first.Rarity;
                profile.Collection.Stamp(copy, first, printings);
            }
            copy.Text = row.Code; // (the earliest printing carries it; a texts.txt row is checked to have one by --texts-check)
            profile.Collection.Insert(copy);
            return copy;
        }

        private static string NoTags(string text) => System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");

        // ---- -campaigncheck ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Original texts at the table: the editor's note and Mind Master's two originals, then a modern circuit's versus and
        /// duel (current text), then the Goat Cup's duel (the original). The duels are conceded (Start over follows).
        /// </summary>
        private System.Collections.IEnumerator TextsCampaignCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            System.Collections.IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float until = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < until;) yield return null;
            }
            var profile = _campaign.Profile!;
            var (ced, row, modern) = TextsCheckSetup();
            report($"texts.txt loaded ({_campaign.Texts.Count} rows); pre-errata Chaos Emperor Dragon is {row?.Code} (until {row?.Until}), and circuit {modern} "
                   + $"({(modern >= 0 ? _campaign.Circuits.All[modern].Name : "-")}) plays {(modern >= 0 ? _campaign.CircuitTexts(modern).Describe() : "-")}",
                _campaign.Texts.Count > 0 && row != null && modern >= 0);
            if (row == null || modern < 0) yield break;
            var cedName = _cardPack!.Get(ced)?.Name ?? "";
            var played = TextsCheckDeckIn(profile, row);
            yield return ClearForTexts();

            // The editor under the modern circuit's list: the original's note says it plays its current text there.
            _editor.Open(TextsCheckDeck, profile.Collection, _campaign.CircuitList(modern)?.Name, _campaign.CircuitTexts(modern));
            yield return ClearForTexts();
            yield return new WaitForSecondsRealtime(0.6f);
            var cedNote = _editor.DebugShowText(row.Code);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot("texts-editor-ced");
            report($"the editor's note on pre-errata CED under {_editor.BanlistName}: \"{cedNote.note}\" (switch reads \"{cedNote.label}\")",
                cedNote.code == row.Code && cedNote.note.Contains("plays its current text") && cedNote.note.Contains("November 2018"));
            // Mind Master: two originals of one name, told apart by entry. The switch offers the collectible one (current,
            // original, current again); the row only CPUs play never shows (spec S1).
            var mm = _cardPack!.All.FirstOrDefault(c => c.Name == "Mind Master" && c.Alias == 0)?.Code ?? 0;
            var mmRows = _campaign.Texts.Timeline(mm).ToList();
            var collectible = mmRows.Where(r => r.Collectible).Select(r => r.Code).ToList();
            var cpuOnly = mmRows.Where(r => r.CpuOnly).Select(r => r.Code).ToList();
            // The switch offers only texts you own a copy of (phase 2): an original Mind Master joins for this step.
            var mmCopy = collectible.Count > 0 && _campaign.Texts.Row(collectible[0]) is TextRow mmRow ? OriginalCopyIn(profile, mmRow) : null;
            var seen = new List<(uint code, string label, string note)> { _editor.DebugShowText(mm) };
            for (int step = 0; step < 2; step++)
            {
                seen.Add(_editor.DebugCycleText());
                yield return new WaitForSecondsRealtime(0.4f);
                if (step == 0) yield return shot("texts-editor-mind-master-1");
            }
            report($"CARD TEXT cycles Mind Master by entry, CPU-only text hidden: {string.Join(" -> ", seen.Select(s => $"{s.code} \"{s.label}\""))}",
                mm != 0 && mmRows.Count == 2 && collectible.Count == 1 && cpuOnly.Count == 1
                && seen[0].code == mm && seen[1].code == collectible[0] && seen[2].code == mm && seen.All(s => !cpuOnly.Contains(s.code)));
            report($"the original shows its note: \"{seen[1].note}\"", seen[1].note.Contains("September 2026"));
            if (mmCopy != null) profile.Collection.Remove(mmCopy);
            _editor.Close();
            yield return ClearForTexts();

            // Outside era-limited play a circuit opens the editor with no list, so a deck noted "#banlist TCG April 2004"
            // shows its own list: the note and the default version still go by the circuit's texts (today's at the
            // October 2026 circuit), until you pick a list by hand.
            int october = -1;
            for (int i = 0; i < _campaign.Circuits.All.Count; i++) if (_campaign.Circuits.All[i].Banlist == "TCG October 2026") october = i;
            var sangan = _cardPack!.All.FirstOrDefault(c => c.Name == "Sangan" && c.Alias == 0)?.Code ?? 0;
            var witch = _cardPack!.All.FirstOrDefault(c => c.Name == "Witch of the Black Forest" && c.Alias == 0)?.Code ?? 0;
            var sanganRow = _campaign.Texts.Timeline(sangan).FirstOrDefault(r => r.Collectible);
            if (october >= 0 && sanganRow != null)
            {
                var old = _library.Load(TextsCheckDeck).Clone();
                old.Name = TextsCheckOldDeck;
                old.Notes["banlist"] = "TCG April 2004";
                old.Main[1] = sanganRow.Code;
                _library.Save(old);
                _editor.Open(TextsCheckOldDeck, profile.Collection, null, _campaign.CircuitTexts(october));
                yield return ClearForTexts();
                yield return new WaitForSecondsRealtime(0.6f);
                var sanganNote = _editor.DebugShowText(sanganRow.Code);
                var added = _editor.DebugResolve(witch);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return shot("texts-editor-old-deck");
                report($"a deck noted TCG April 2004 opened from {_campaign.Circuits.All[october].Name} (list shown {added.list}): Sangan's original reads "
                       + $"\"{sanganNote.note}\", and Witch of the Black Forest adds as {added.code} ({_cardPack!.Get(added.code)?.Name})",
                    added.list == "TCG April 2004" && sanganNote.code == sanganRow.Code && sanganNote.note.StartsWith("Original text. At this circuit it plays its current text")
                    && sanganNote.note.Contains("September 2016") && witch != 0 && added.code == witch);
                _editor.Close();
                yield return ClearForTexts();
            }
            else report($"the October 2026 circuit ({october}) and Sangan's original text ({sanganRow?.Code}) are installed", false);

            // A modern circuit: the versus says CED plays its current text, and the duel deals the current card.
            _versusDeck = null;
            ShowVersus(modern, 0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return shot("texts-versus");
            var line = _scenes.TextsLine;
            report($"the versus at {_campaign.Circuits.All[modern].Name} says \"{line}\"", line == $"1 card plays its current text here: {cedName}.");
            if (_scenes.Buttons.TryGetValue("Duel!", out var duel)) duel();
            // (A circuit's opening scene may come first: stepped through.)
            for (float until = Time.realtimeSinceStartup + 30; !YourTurnWaits(modern) && Time.realtimeSinceStartup < until;)
            {
                if (_storyScreen.IsOpen) _storyScreen.DebugAdvance();
                yield return new WaitForSecondsRealtime(0.1f);
            }
            var codes = DuelCodes();
            var recorded = _recording?.Decks[0].Main ?? new List<uint>();
            report($"the duel plays the current card ({ced}: {codes.Count(c => c == ced)} in hand and deck, {codes.Count(c => c == row.Code)} pre-errata), "
                   + $"and the replay records it ({recorded.Count(c => c == ced)} current, {recorded.Count(c => c == row.Code)} pre-errata)",
                _campaignMatch?.circuit == modern && codes.Contains(ced) && !codes.Contains(row.Code) && recorded.Contains(ced) && !recorded.Contains(row.Code)
                && _textsDowngraded.SequenceEqual(new[] { ced }));
            yield return ConcedeToResults();

            // A Tag Cup past CED's errata with Kit bringing your deck (lent): the partner's slot plays the current card too,
            // and the versus line names it. (Your own deck there is the one you played before: no originals.)
            var kit = _campaign.ShopRegular;
            int tagCup = -1;
            for (int i = 0; kit != null && i < _campaign.Circuits.TagCups.Count && tagCup < 0; i++)
                if (_campaign.Circuits.TagCups[i].Matches.Count > 0 && !_campaign.CircuitTexts(_campaign.Circuits.TagCups[i].After).Allows(row)) tagCup = i;
            if (kit != null && tagCup >= 0)
            {
                profile = _campaign.Profile!;
                profile.Deck = played;
                profile.TagPartner = kit.Id;
                profile.PartnerDecks[kit.Id] = TextsCheckDeck;
                _campaign.Save();
                yield return ClearForTexts();
                _versusDeck = null;
                ShowTagVersus(tagCup, 0, kit.Id);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return shot("texts-tag-versus");
                var tagLine = _scenes.TextsLine;
                var lent = _campaign.PartnerDeck(kit);
                _scenes.Close();
                seed++;
                StartCampaignTag(tagCup, 0, kit.Id);
                yield return Until(() => _campaignTag is { } ct && ct.cup == tagCup && _recording?.Partners != null, 10);
                var partnerMain = _recording?.Partners?[0].Main ?? new List<uint>();
                report($"at {_campaign.Circuits.TagCups[tagCup].Name} ({_campaign.CircuitTexts(_campaign.Circuits.TagCups[tagCup].After).Describe()}) {kit.Name} brings {lent}: "
                       + $"the versus says \"{tagLine}\", and the partner's deck deals {partnerMain.Count(c => c == ced)} current CED, {partnerMain.Count(c => c == row.Code)} pre-errata",
                    lent == TextsCheckDeck && tagLine == $"1 card plays its current text here: {cedName}." && partnerMain.Contains(ced) && !partnerMain.Contains(row.Code)
                    && _textsDowngraded.SequenceEqual(new[] { ced }));
                yield return ConcedeToResults();
                profile = _campaign.Profile!;
                profile.PartnerDecks.Remove(kit.Id);
                profile.Deck = TextsCheckDeck;
                _campaign.Save();
            }
            else report($"Kit ({kit?.Id}) and a Tag Cup past CED's errata ({tagCup}) are installed", false);

            // The Goat Cup (a Goat table, cards out by September 2005): the same slot plays its original. (Started directly:
            // the cup's list keeps CED out of a legal deck; the text a slot plays is what's checked.)
            var goatRules = _campaign.CupRules(0);
            var goatLine = _campaign.TextsLine(_library.Load(TextsCheckDeck), goatRules);
            uint goatEntry = _campaign.Texts.Goat(ced), expected = goatEntry != 0 ? goatEntry : row.Code;
            yield return ClearForTexts();
            _versusDeck = TextsCheckDeck;
            seed++;
            StartCupDuel(0, 0);
            yield return Until(() => _campaignCup is { } lc && lc.cup == 0 && _runner != null && _runner.IsWaiting && _runner.WaitingFor == 0 && _human?.Current != null, 30);
            codes = DuelCodes();
            recorded = _recording?.Decks[0].Main ?? new List<uint>();
            yield return shot("texts-goat-duel");
            report($"at the {_campaign.Library.Cups[0].Name} ({goatRules.Texts.Describe()}) the slot plays its original {expected} "
                   + $"({_cardPack!.Get(expected)?.Name}: {codes.Count(c => c == expected)} dealt, {codes.Count(c => c == ced)} current) and the versus line is empty",
                goatRules.Texts.Goat && goatLine.Length == 0 && codes.Contains(expected) && !codes.Contains(ced)
                && recorded.Contains(expected) && _textsDowngraded.Count == 0);
            yield return ConcedeToResults();

            profile = _campaign.Profile!;
            profile.Deck = played;
            _campaign.Save();
            _campaignScreen.Close();
        }

        /// <summary>A circuit match of <paramref name="circuit"/> is under way and waits on you.</summary>
        private bool YourTurnWaits(int circuit) => _campaignMatch is { } cm && cm.circuit == circuit && _runner != null && _runner.IsWaiting
                                                   && _runner.WaitingFor == 0 && _human?.Current != null;

        /// <summary>Your cards' codes in hand and deck as the duel reports them.</summary>
        private List<uint> DuelCodes()
        {
            if (_duel == null) return new List<uint>();
            return _duel.QueryLocation(0, Location.Hand).Concat(_duel.QueryLocation(0, Location.Deck))
                .OfType<FieldCard>().Select(c => c.Code).ToList();
        }

        /// <summary>The screen clear for a texts shot: any story scene stepped through, the campaign screen closed (it sits over the editor).</summary>
        private System.Collections.IEnumerator ClearForTexts()
        {
            for (int line = 0; line < 40 && _storyScreen.IsOpen; line++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.2f); }
            _campaignScreen.Close();
        }

        /// <summary>Concedes the duel on the board and steps past its results and any story they raise.</summary>
        private System.Collections.IEnumerator ConcedeToResults()
        {
            ConcedeDuel();
            var shown = Time.realtimeSinceStartup + 8;
            while (Time.realtimeSinceStartup < shown && !(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"))) yield return null;
            if (_scenes.Buttons.TryGetValue("Continue", out var back)) back();
            yield return new WaitForSecondsRealtime(0.4f);
            for (int line = 0; line < 40 && _storyScreen.IsOpen; line++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.2f); }
            _scenes.Close();
        }

        // ---- -themecheck ---------------------------------------------------------------------------------------------------

        /// <summary>In one theme: the versus line at a modern circuit, and the editor's note on the original there.</summary>
        private System.Collections.IEnumerator TextsThemeShots(string theme, Func<string, System.Collections.IEnumerator> shot, Action<string, bool> report)
        {
            var profile = _campaign.Profile!;
            var (_, row, modern) = TextsCheckSetup();
            if (row == null || modern < 0) { report($"the {theme} theme: texts.txt and a circuit past CED's errata are installed", false); yield break; }
            var played = TextsCheckDeckIn(profile, row);
            yield return ClearForTexts();
            _versusDeck = null;
            ShowVersus(modern, 0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return shot($"{theme}-texts-versus");
            var line = _scenes.TextsLine;
            _scenes.Close();
            _editor.Open(TextsCheckDeck, profile.Collection, _campaign.CircuitList(modern)?.Name, _campaign.CircuitTexts(modern));
            yield return ClearForTexts();
            yield return new WaitForSecondsRealtime(0.6f);
            var note = _editor.DebugShowText(row.Code).note;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return shot($"{theme}-texts-editor");
            _editor.Close();
            profile.Deck = played;
            _campaign.Save();
            report($"the {theme} theme draws the versus texts line (\"{line}\") and the editor's note (\"{note}\")", line.Length > 0 && note.Contains("current text"));
        }
    }
}
