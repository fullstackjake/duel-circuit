using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-searchcheck &lt;folder&gt;` (absolute; use with -mods &lt;empty folder&gt;): the deck editor's card search
    /// (docs/dev/CARD-SEARCH-SPEC-2026-11-07.md section 10), through the editor's own actions.
    /// <list type="number">
    /// <item>Layout: nothing in the search panel or the More panel overlaps at 1920x1080, and their labels fit.</item>
    /// <item>The search box's switch changes the results for "dark".</item>
    /// <item>The More panel opens and closes (Close, the controller's B); a genre, Match any, an archetype, Limit and Look
    /// change the results and add chips that remove them; Clear these clears only the panel's filters; Limit Forbidden under
    /// a TCG list shows only cards with 0 allowed; Legal here under an era-limited list hides unreleased cards.</item>
    /// <item>Recently viewed: three cards picked come first, newest first; one picked again moves to the front.</item>
    /// <item>The card detail's genre and archetype chips search for them.</item>
    /// <item>One bot duel (you concede once the opponent has shown cards): Seen in duels shows its cards, with the duel
    /// picker; a replay of it and the deck tester's games add nothing.</item>
    /// <item>The campaign: the Look picker over your copies, the scope's Owned only; no DP or card changes.</item>
    /// </list>
    /// It asks for seen.txt to be written (<see cref="DeckEditor.SeenInChecks"/>): other checks never write it.
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator SearchCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"search check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForSecondsRealtime(0.4f); // pictures load
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"search-{name}.png"));
                yield return null;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            // It writes decks and seen.txt: never into your own mods folder.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (it writes seen.txt and the editor's recovery copies there)", false);
                Application.Quit();
                yield break;
            }
            DeckEditor.SeenInChecks = true;
            var seenPath = _editor.SeenPath;
            Report($"a fresh mods folder has no seen.txt ({seenPath})", !File.Exists(seenPath));
            CardInfo? Named(string name) => _cardPack!.All.FirstOrDefault(c => c.Name == name && c.Alias == 0);
            string Names(IEnumerable<CardInfo> cards, int n = 4) => string.Join(", ", cards.Take(n).Select(c => c.Name));

            // ---- Free play: the panel's layout ----
            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.6f);
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name); // no limits: every card searchable, no badges
            var all = _editor.DebugSearch("");
            Report($"the catalog opens on every card ({all.Count}, the grid's 2000 cap) in the All cards scope",
                all.Count == 2000 && _editor.DebugScopeLabel == "All cards");
            var every = _editor.DebugCardSearch.Find("", 20000); // (uncapped: what Seen in duels can show)
            yield return Shot("panel");
            yield return LayoutChecks(Report, more: false, "the search panel");

            // ---- The switch: Name + text / Name / Text ----
            var both = _editor.DebugSearch("dark");
            var named = _editor.DebugSearch("dark", CardSearch.SearchMode.NameOnly);
            yield return Shot("mode-name");
            var texts = _editor.DebugSearch("dark", CardSearch.SearchMode.TextOnly);
            Report($"\"dark\": Name + text {both.Count}, Name {named.Count}, Text {texts.Count}; Name only finds names with \"dark\" ({Names(named.Where(c => c.Name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) < 0), 3)}); Text finds some without",
                both.Count > named.Count && both.Count > texts.Count && named.Count > 0 && texts.Count > 0
                && named.All(c => CardSearch.SquashName(c.Name).Contains("dark")) // (squashed names count: "D/D Ark" is "ddark")
                && texts.Any(c => c.Name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) < 0));
            var label = _editor.DebugCycleMode(); // Text -> Name + text
            Report($"the switch cycles back to Name + text (\"{label.Replace("\n", " ")}\") and the results follow ({_editor.DebugResults.Count})",
                label.EndsWith("Name + text") && _editor.DebugResults.Count == both.Count);
            var close = _editor.DebugSearch("bleu-eyes");
            Report($"a typo finds close names ({Names(close, 2)}) and the status says so (\"{_editor.DebugDeck.status}\")",
                close.Any(c => c.Name == "Blue-Eyes White Dragon") && _editor.DebugDeck.status.Contains("close names"));
            _editor.DebugSearch("@nosucharchetype");
            Report($"an unknown @archetype says so (\"{_editor.DebugDeck.status}\")", _editor.DebugDeck.status.Contains("No archetype called"));

            // Review fixes (2026-11-08): Attribute and Type words filter; a word some name has calls up no lookalikes.
            var earth = _editor.DebugSearch("earth machine lv4");
            Report($"\"earth machine lv4\": {earth.Count} cards, no close names (\"{_editor.DebugDeck.status}\"), no Gil Garth",
                earth.Count > 0 && !_editor.DebugDeck.status.Contains("close names") && _editor.DebugCloseNamesFrom < 0 && !earth.Any(c => c.Name == "Gil Garth"));
            var magican = _editor.DebugSearch("dark magican");
            Report($"\"dark magican\" shows Dark Magician first as a close name ({Names(magican, 2)})", magican.FirstOrDefault()?.Name == "Dark Magician" && _editor.DebugCloseNamesFrom == 0);
            var destroyTrap = _editor.DebugSearch("destroy trap");
            Report($"\"destroy trap\": {destroyTrap.Count} Traps, no close names (\"{_editor.DebugDeck.status}\")",
                destroyTrap.Count > 0 && destroyTrap.All(c => (c.Type & CardType.Trap) != 0) && !_editor.DebugDeck.status.Contains("close names") && _editor.DebugCloseNamesFrom < 0);
            _editor.DebugSearch("raigeki monster");
            Report($"\"raigeki monster\": no close names ({_editor.DebugResults.Count} cards)", !_editor.DebugDeck.status.Contains("close names") && _editor.DebugCloseNamesFrom < 0);
            // Close names stay after the real results through a sort.
            var summons = _editor.DebugSearch("summons");
            var summonsReal = new HashSet<uint>(_editor.DebugCardSearch.Find("summons", CardSearch.SearchMode.NameAndText, "", 20000, closeNames: false).Select(c => c.Code));
            _editor.DebugFilters(0, 0, 1, -1); // sort: Name
            var byName = _editor.DebugResults;
            int closeFrom = _editor.DebugCloseNamesFrom;
            Report($"sorted by name, \"summons\" keeps its {byName.Count - closeFrom} close names after its {closeFrom} matches (A-Z within each: {Names(byName.Skip(closeFrom), 2)})",
                closeFrom > 0 && closeFrom < byName.Count && byName.Count == summons.Count && byName.Take(closeFrom).All(c => summonsReal.Contains(c.Code)) && !byName.Skip(closeFrom).Any(c => summonsReal.Contains(c.Code))
                && byName.Take(closeFrom).Select(c => c.Name).SequenceEqual(byName.Take(closeFrom).Select(c => c.Name).OrderBy(n => n))
                && byName.Skip(closeFrom).Select(c => c.Name).SequenceEqual(byName.Skip(closeFrom).Select(c => c.Name).OrderBy(n => n)));
            _editor.DebugFilters(0, 0, 3, -1); // sort: ATK
            closeFrom = _editor.DebugCloseNamesFrom;
            Report($"sorted by ATK too ({closeFrom} matches first)", closeFrom > 0 && _editor.DebugResults.Take(closeFrom).All(c => summonsReal.Contains(c.Code)) && !_editor.DebugResults.Skip(closeFrom).Any(c => summonsReal.Contains(c.Code)));
            yield return Shot("close-names-sorted");
            _editor.DebugFilters(0, 0, 0, -1);
            // An OR at the box's end doesn't reach the pickers.
            int darkPick = 1 + Array.FindIndex(DuelText.Attributes, a => a.name.Equals("DARK", StringComparison.OrdinalIgnoreCase));
            uint darkBit = DuelText.Attributes[darkPick - 1].bit;
            _editor.DebugFilters(0, darkPick, 0, -1);
            var dragonDark = _editor.DebugSearch("dragon");
            var dragonOr = _editor.DebugSearch("dragon or");
            Report($"box \"dragon or\" with the DARK picker is still dragon AND DARK ({dragonOr.Count} = {dragonDark.Count})",
                dragonOr.Count > 0 && dragonOr.Count == dragonDark.Count && dragonOr.All(c => (c.Attribute & darkBit) != 0));
            _editor.DebugFilters(0, 0, 0, -1);
            // Name only: bare keywords in the box are words.
            var gate = _editor.DebugSearch("fusion gate", CardSearch.SearchMode.NameOnly);
            Report($"Name only \"fusion gate\" finds Fusion Gate first ({Names(gate, 3)})", gate.FirstOrDefault()?.Name == "Fusion Gate");
            yield return Shot("name-fusion-gate");
            _editor.DebugSearch("");

            // ---- The More panel ----
            _editor.DebugToggleMore();
            Report("More opens the panel over the grid", _editor.DebugMoreOpen);
            yield return Shot("more-open");
            yield return LayoutChecks(Report, more: true, "the More panel");
            int before = _editor.DebugResults.Count;
            _editor.DebugGenre("banish");
            var banish = _editor.DebugResults;
            var chips = _editor.DebugActiveChips();
            Report($"a genre (Banish) narrows the results ({before} -> {banish.Count}), adds a chip ({string.Join(", ", chips)}) and counts on the button ({_editor.DebugMoreButton})",
                banish.Count > 0 && banish.Count < before && chips.Contains("Banish") && _editor.DebugMoreButton == "More · 1");
            _editor.DebugGenre("draw");
            int both2 = _editor.DebugResults.Count;
            _editor.DebugMatchAny(true);
            int any = _editor.DebugResults.Count;
            chips = _editor.DebugActiveChips();
            Report($"Banish and Draw: all {both2}, any {any} (chips {string.Join(", ", chips)})", any > both2 && any >= banish.Count && chips.Contains("or Draw"));
            yield return Shot("more-genres");
            Report("the Banish chip in the row removes it", _editor.DebugRemoveChip("Banish") && !_editor.DebugActiveChips().Contains("Banish"));
            _editor.DebugClearMore();

            var suggested = _editor.DebugArchetype("burning ab");
            var abyss = _editor.DebugResults;
            var cs = _editor.DebugCardSearch;
            chips = _editor.DebugActiveChips();
            Report($"the Archetype box suggests ({string.Join("; ", suggested)}) and a pick shows its members ({abyss.Count}: {Names(abyss, 3)}) with a chip",
                suggested.Any(s => s.StartsWith("Burning Abyss")) && abyss.Count >= 10
                && abyss.All(c => cs.ArchetypesOf(c).Any(a => a.name == "Burning Abyss")) && chips.Contains("Burning Abyss"));
            var one = _editor.DebugArchetype("b");
            Report($"one letter suggests nothing ({string.Join("; ", one)})", one.Count == 0);
            yield return Shot("more-archetype");
            Report("the archetype chip in the row removes it", _editor.DebugRemoveChip("Burning Abyss") && _editor.DebugResults.Count == before);

            var tcg = _editor.DebugBanlist(b => b.Name.Contains("TCG") && !b.Whitelist && _cardPack!.All.Any(c => c.Alias == 0 && b.Allowed(c.Code, _cardPack!) == 0));
            _editor.DebugLimit(1);
            var forbidden = _editor.DebugResults;
            var list = _editor.DebugList;
            Report($"Limit Forbidden under {tcg}: {forbidden.Count} cards, all with 0 allowed ({Names(forbidden, 3)}); chip {string.Join(", ", _editor.DebugActiveChips())}",
                forbidden.Count > 0 && forbidden.All(c => list.Allowed(c.Code, _cardPack!) == 0) && _editor.DebugActiveChips().Contains("Forbidden"));
            yield return Shot("more-limit");
            Report("the Forbidden chip removes it", _editor.DebugRemoveChip("Forbidden") && _editor.DebugResults.Count == before);
            _editor.DebugSearch("limit:1");
            var typedLimited = _editor.DebugResults;
            Report($"typed limit:1 under {tcg}: {typedLimited.Count}, all limited to 1", typedLimited.Count > 0 && typedLimited.All(c => list.Allowed(c.Code, _cardPack!) == 1));
            _editor.DebugSearch("limit:9 dark magician");
            Report($"typed limit:9 filters nothing ({_editor.DebugResults.Count} for dark magician)", _editor.DebugResults.Any(c => c.Name == "Dark Magician"));
            var limitedOrSemi = _editor.DebugSearch("-limit:0 -limit:3");
            Report($"typed -limit:0 -limit:3 under {tcg} leaves the Limited and Semi-Limited ({limitedOrSemi.Count}: {Names(limitedOrSemi, 3)})",
                limitedOrSemi.Count > 0 && limitedOrSemi.Count < 500 && limitedOrSemi.All(c => list.Allowed(c.Code, _cardPack!) is 1 or 2));

            var era = _editor.DebugBanlist(b => b.Name == "TCG April 2004");
            var ash = _editor.DebugSearch("ash blossom");
            _editor.DebugLimit(6);
            var ashLegal = _editor.DebugResults;
            var magician = _editor.DebugSearch("dark magician");
            Report($"Legal here under {era}: Ash Blossom {ash.Count} -> {ashLegal.Count} (not out yet), Dark Magician still there ({magician.Count})",
                era != null && ash.Any(c => c.Name.StartsWith("Ash Blossom")) && ashLegal.Count == 0 && magician.Any(c => c.Name == "Dark Magician"));
            _editor.DebugLimit(0);
            _editor.DebugSearch("ash blossom legal");
            Report($"typed \"legal\" does the same ({_editor.DebugResults.Count})", _editor.DebugResults.Count == 0);
            var notLegal = _editor.DebugSearch("ash blossom -legal");
            var dmNotLegal = _editor.DebugSearch("dark magician -legal");
            Report($"typed \"-legal\" keeps only what isn't legal here (Ash Blossom {notLegal.Count}, no Dark Magician among {dmNotLegal.Count})",
                notLegal.Any(c => c.Name.StartsWith("Ash Blossom")) && !dmNotLegal.Any(c => c.Name == "Dark Magician"));
            _editor.DebugSearch("");
            // Changing the list with no ban filter on still updates the More panel's LIMIT caption.
            var goat = _editor.DebugBanlist(b => b.Whitelist);
            Report($"the More panel's caption follows the list with no ban filter on (\"{_editor.DebugLimitCaption}\")", goat != null && _editor.DebugLimitCaption.Contains(goat));
            _editor.DebugLimit(5);
            var onList = _editor.DebugResults;
            var goatList = _editor.DebugList;
            Report($"On the list under {goat} (a whitelist) is its 1s and 2s (\"{_editor.DebugLimitLabel}\": {onList.Count}, {Names(onList, 3)})",
                onList.Count > 0 && onList.Count < 500 && onList.All(c => goatList.Allowed(c.Code, _cardPack!) is 1 or 2) && _editor.DebugLimitLabel == "On the list (1-2)");
            yield return Shot("goat-on-list");
            _editor.DebugLimit(0);
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
            Report($"and back (\"{_editor.DebugLimitCaption}\")", _editor.DebugLimitCaption.Contains(Banlist.None.Name));

            _editor.DebugSearch("dark magician");
            int dm = _editor.DebugResults.Count;
            _editor.DebugLook(5);
            var alt = _editor.DebugResults;
            Report($"Look Alternate art: {dm} -> {alt.Count} ({Names(alt, 3)})", alt.Count > 0 && alt.Count < dm && alt.Any(c => c.Name == "Dark Magician")
                && _editor.DebugActiveChips().Contains("Alternate art"));

            // Clear these: only the panel's (the box stays).
            _editor.DebugGenre("negate");
            _editor.DebugLimit(5);
            _editor.DebugClearMore();
            chips = _editor.DebugActiveChips();
            Report($"Clear these clears the panel's filters and keeps the box ({string.Join(", ", chips)}; {_editor.DebugMoreButton})",
                _editor.MoreFilterCount == 0 && chips.Count == 1 && chips[0] == "\"dark magician\"" && _editor.DebugMoreButton == "More");

            // A long active row scrolls rather than dropping chips.
            _editor.DebugSearch("dragon");
            foreach (var g in new[] { "search", "specialsummon", "destroymonster", "banish", "negate" }) _editor.DebugGenre(g);
            _editor.DebugMatchAny(true);
            _editor.DebugLimit(5);
            var row = _editor.DebugActiveRow;
            chips = _editor.DebugActiveChips();
            Report($"a long active row scrolls ({row.width:0} px of chips in 300, all {chips.Count} there)", row.scrolls && chips.Count == 7);
            _editor.DebugToggleMore();
            yield return Shot("active-row-long");
            _editor.DebugToggleMore();
            _editor.ControllerBack();
            Report("the controller's B closes the panel (and not the editor)", !_editor.DebugMoreOpen && _editor.IsOpen);
            _editor.DebugToggleMore();
            _editor.DebugToggleMore();
            Report("More again closes it", !_editor.DebugMoreOpen);
            _editor.DebugClearMore();
            _editor.DebugSearch("");

            // ---- Recently viewed ----
            _editor.DebugScope(4);
            Report($"Recently viewed starts empty (\"{_editor.DebugEmptyNote}\")", _editor.DebugResults.Count == 0 && _editor.DebugEmptyNote.Contains("Cards you look at"));
            yield return Shot("viewed-empty");
            var picks = new[] { "Sangan", "Pot of Greed", "Dark Magician" }.Select(Named).ToList();
            foreach (var p in picks) if (p != null) _editor.DebugSelect(p.Code);
            _editor.DebugScope(0);
            _editor.DebugScope(4);
            var viewed = _editor.DebugResults.Select(c => c.Name).ToList();
            Report($"three picked cards come first, newest first ({string.Join(", ", viewed.Take(3))})",
                viewed.Take(3).SequenceEqual(new[] { "Dark Magician", "Pot of Greed", "Sangan" }));
            _editor.DebugSelect(picks[0]!.Code);
            _editor.DebugScope(0);
            _editor.DebugScope(4);
            viewed = _editor.DebugResults.Select(c => c.Name).ToList();
            Report($"picking one again moves it to the front ({string.Join(", ", viewed.Take(3))}), and the file has them",
                viewed.Take(3).SequenceEqual(new[] { "Sangan", "Dark Magician", "Pot of Greed" }) && SeenLog.Read(seenPath).Viewed.Count == 3);
            yield return Shot("viewed");
            _editor.DebugScope(0);

            // ---- The card detail's chips ----
            var sangan = picks[0]!;
            var detail = _editor.DebugShow(sangan);
            var tags = detail.ShownTags;
            Report($"Sangan's detail shows {string.Join(", ", tags)}; a click on Search searches it",
                tags.Contains("Search") && detail.DebugClickTag("Search") && _editor.DebugActiveChips().SequenceEqual(new[] { "Search" })
                && _editor.DebugResults.Any(c => c.Name == "Sangan"));
            var graff = _cardPack!.All.FirstOrDefault(c => c.Alias == 0 && c.Name.StartsWith("Graff, Malebranche"));
            detail = _editor.DebugShow(graff!);
            tags = detail.ShownTags;
            Report($"Graff's detail shows its archetype ({string.Join(", ", tags)}); a click on it searches its members",
                tags.Contains("Burning Abyss") && detail.DebugClickTag("Burning Abyss") && _editor.DebugActiveChips().SequenceEqual(new[] { "Burning Abyss" })
                && _editor.DebugResults.Count == abyss.Count);
            yield return Shot("detail-chips");
            _editor.DebugClearMore();
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.3f);

            // ---- One bot duel: you concede once the opponent has shown cards ----
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 41;
            _paused = false;
            _nextFirstPlayer = 1; // they go first, so they show cards before you do anything
            NewDuel();
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            Report("the duel watches the opponent's cards (a check that asks)", _seen != null);
            void AnswerPlainly(ChoiceSet set)
            {
                if (set.Prompt is Ygo.Engine.Protocol.IdlePrompt) _view.GoToPhase("end");
                else if (set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set.Prompt is Ygo.Engine.Protocol.EffectYesNoPrompt || set.Prompt is Ygo.Engine.Protocol.YesNoPrompt)
                    _view.Prompt.ClickChoice(Math.Max(0, set.Choices.FindIndex(c => c.Label == "No")));
                else if (set.Mode == ChoiceMode.Single) _view.Prompt.ClickChoice(0);
                else { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
            }
            var until = Time.realtimeSinceStartup + 90;
            ChoiceSet? answered = null;
            float answeredAt = 0;
            int seenAtConcede = -1;
            var opponent = TeamName(1);
            while (Time.realtimeSinceStartup < until && !_announcedResult)
            {
                var set = _view.Prompt.Current;
                if (set != null && _runner != null && _runner.IsWaiting && (set != answered || Time.realtimeSinceStartup > answeredAt + 1f))
                {
                    if (_board.TurnPlayer == 0 && _seen is { Count: > 0 } && seenAtConcede < 0)
                    {
                        seenAtConcede = _seen.Count;
                        ConcedeDuel();
                    }
                    else { answered = set; answeredAt = Time.realtimeSinceStartup; AnswerPlainly(set); }
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            var log = SeenLog.Read(seenPath);
            var duel = log.Duels.FirstOrDefault();
            Report($"after the duel (conceded with {seenAtConcede} cards seen) seen.txt has it: vs {duel?.Opponent} ({duel?.Mode}), {duel?.Cards.Count} cards "
                   + $"({string.Join(", ", duel?.Cards.Take(4).Select(c => _cardPack!.Get(c.code)?.Name) ?? Array.Empty<string>())})",
                _announcedResult && log.Duels.Count == 1 && duel != null && duel.Opponent == opponent && duel.Mode == "free play" && duel.Cards.Count >= seenAtConcede && seenAtConcede > 0);
            var seenCodes = duel?.Cards.Select(c => c.code).ToList() ?? new List<uint>();

            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.5f);
            _editor.DebugScope(3);
            var seenResults = _editor.DebugResults;
            var searchable = seenCodes.Where(c => every.Any(a => a.Code == c)).ToList();
            Report($"Seen in duels shows the opponent's cards in the order they appeared ({Names(seenResults)}; {seenResults.Count} of {seenCodes.Count})",
                seenResults.Count == searchable.Count && seenResults.Select(c => Banlist.BaseCode(c.Code, _cardPack!)).SequenceEqual(searchable));
            yield return null; // (the last page's cards go at the frame's end)
            var under = _editor.DebugUnderLines();
            Report($"the line under each card reads \"vs {opponent}\" ({under.FirstOrDefault()})", under.Count > 0 && under.All(u => u == $"vs {opponent}"));
            chips = _editor.DebugActiveChips();
            Report($"the duel picker shows that duel ({chips.FirstOrDefault()})", chips.FirstOrDefault() == "The 1 duel");
            var picked = _editor.DebugSeenDuel(+1);
            Report($"the picker's next is the last duel (\"{picked}\", {_editor.DebugResults.Count} cards)", picked == "Last duel" && _editor.DebugResults.Count == seenResults.Count);
            yield return Shot("seen");
            _editor.DebugSearch("monster");
            Report($"other filters apply on top (monsters: {_editor.DebugResults.Count})", _editor.DebugResults.Count <= seenResults.Count && _editor.DebugResults.All(c => (c.Type & CardType.Monster) != 0));
            _editor.DebugSearch("");
            _editor.Close();

            // A replay of it adds nothing.
            var recent = _replays.All().Where(e => e.Recent).ToList();
            if (recent.Count > 0)
            {
                WatchReplay(recent[0].Replay, 0);
                _paused = false;
                UpdateControls();
                var replayUntil = Time.realtimeSinceStartup + 60;
                while (!_announcedResult && Time.realtimeSinceStartup < replayUntil) yield return null;
                yield return new WaitForSecondsRealtime(0.3f);
            }
            Report($"watching its replay (to the end: {_announcedResult}) adds nothing ({SeenLog.Read(seenPath).Duels.Count} duel)", recent.Count > 0 && SeenLog.Read(seenPath).Duels.Count == 1);

            // The deck tester's games add nothing.
            _editor.Open("classic");
            yield return new WaitForSecondsRealtime(0.4f);
            _editor.DebugSetup(null, runTest: true, testHand: false);
            yield return new WaitForSecondsRealtime(4f);
            _editor.Close();
            Report($"the deck tester's games add nothing ({SeenLog.Read(seenPath).Duels.Count} duel)", SeenLog.Read(seenPath).Duels.Count == 1);

            // ---- The campaign: Look over your copies, Owned only in the scope ----
            _campaign.Start(CampaignState.Starters[0]);
            var profile = _campaign.Profile!;
            int dp = profile.Dp, total = profile.Collection.Total;
            _editor.Open(profile.Deck, EditorCollection(profile.Deck), (string?)null);
            yield return new WaitForSecondsRealtime(0.5f);
            Report("the campaign's scope has Owned only", _editor.DebugScope(1) && _editor.DebugScopeLabel.Contains("Owned only"));
            int owned = _editor.DebugResults.Count;
            _editor.DebugScope(0);
            var looks = new List<string>();
            bool looksOk = true;
            foreach (var (look, name) in new[] { (1, "Foil"), (2, "Slabbed"), (3, "Gem Mint"), (4, "Worn"), (5, "Alternate art") })
            {
                _editor.DebugLook(look);
                var found = _editor.DebugResults;
                looks.Add($"{name} {found.Count}");
                bool ok = look == 5 || found.All(c => profile.Collection.CopiesOf(Banlist.BaseCode(c.Code, _cardPack!)).Any(copy => !copy.AtDesk
                    && (look == 1 ? copy.Rarity >= Rarity.SuperRare : look == 2 ? copy.IsSlab : look == 3 ? copy.Shown == 10 : copy.Shown <= 4)));
                looksOk &= ok && (look == 5 || found.Count <= owned);
                if (look == 1) yield return Shot("campaign-foil");
            }
            Report($"Look over your {owned} owned cards: {string.Join(", ", looks)}; each only cards with such a copy", looksOk);
            // Close names stay after the real results with owned cards first.
            var campaignClose = _editor.DebugSearch("summons");
            closeFrom = _editor.DebugCloseNamesFrom;
            Report($"in the campaign (owned first), \"summons\" keeps its close names after its {closeFrom} matches",
                closeFrom > 0 && campaignClose.Take(closeFrom).All(c => summonsReal.Contains(c.Code)) && !campaignClose.Skip(closeFrom).Any(c => summonsReal.Contains(c.Code)));
            _editor.DebugSearch("");
            // A campaign look doesn't follow you into free play.
            _editor.DebugLook(1);
            _editor.Close();
            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"Foil picked in the campaign is gone in free play (look {_editor.DebugLookIndex}, chips {string.Join(", ", _editor.DebugActiveChips())}, {_editor.DebugResults.Count} cards)",
                _editor.DebugLookIndex == 0 && !_editor.DebugActiveChips().Contains("Foil") && _editor.DebugResults.Count == 2000);
            _editor.Close();
            _campaign.Save();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            Report($"no DP changed ({dp} -> {profile.Dp}) and no cards ({total} -> {profile.Collection.Total}); the save reloads",
                profile.Dp == dp && profile.Collection.Total == total);
            Report($"seen.txt still has the duel and the viewed cards after the campaign ({SeenLog.Read(seenPath).Duels.Count} / {SeenLog.Read(seenPath).Viewed.Count})",
                SeenLog.Read(seenPath).Duels.Count == 1 && SeenLog.Read(seenPath).Viewed.Count >= 3);

            Debug.Log($"search check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>The search panel's (or the More panel's) controls: none overlap another, and every label fits its box.</summary>
        private IEnumerator LayoutChecks(Action<string, bool> report, bool more, string what)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            var boxes = _editor.DebugSearchBoxes(more);
            var overlaps = new List<string>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i].rect;
                    var b = boxes[j].rect;
                    // Overlapping by more than half a pixel (sharing an edge is fine).
                    if (a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f)
                        overlaps.Add($"{boxes[i].what} / {boxes[j].what}");
                }
            // The More panel inside the grid's place; the search panel's controls on its backdrop (1400-1920).
            var outside = more ? boxes.Where(b => b.rect.xMin < 1409.5f || b.rect.xMax > 1910.5f || b.rect.yMin < 183.5f || b.rect.yMax > 934.5f).Select(b => b.what).ToList()
                : boxes.Where(b => b.rect.xMin < 1400 || b.rect.xMax > 1920 || b.rect.yMax > 1080.5f).Select(b => b.what).ToList();
            report($"{what}: {boxes.Count} boxes, none overlapping{(overlaps.Count > 0 ? ": " + string.Join("; ", overlaps.Take(4)) : "")}"
                   + $", all inside it{(outside.Count > 0 ? ": " + string.Join(", ", outside) : "")}", boxes.Count > 5 && overlaps.Count == 0 && outside.Count == 0);
            var cut = _editor.DebugSearchTexts().Where(t => t.isActiveAndEnabled && t.text.Length > 0 && !TextFits(t))
                .Select(t => $"\"{System.Text.RegularExpressions.Regex.Replace(t.text, "<.*?>", "").Replace("\n", " ")}\"").Distinct().ToList();
            report($"{what}: every label fits its box{(cut.Count > 0 ? ": " + string.Join(", ", cut.Take(5)) : "")}", cut.Count == 0);
        }

        /// <summary>Whether a label's text fits its box (at its smallest size when it shrinks to fit).</summary>
        private static bool TextFits(Text t)
        {
            var size = t.rectTransform.rect.size;
            var settings = t.GetGenerationSettings(size);
            settings.resizeTextForBestFit = false;
            if (t.resizeTextForBestFit) settings.fontSize = t.resizeTextMinSize;
            var gen = t.cachedTextGeneratorForLayout;
            float h = gen.GetPreferredHeight(t.text, settings) / t.pixelsPerUnit;
            bool wraps = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            float w = gen.GetPreferredWidth(t.text, settings) / t.pixelsPerUnit;
            return h <= size.y + 1.5f && (wraps || w <= size.x + 1.5f);
        }
    }
}
