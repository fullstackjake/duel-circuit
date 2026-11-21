using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Ygo.Engine.Campaign;
using UiText = UnityEngine.UI.Text;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The climb's screens (docs/dev/THE-CLIMB-SPEC-2026-11-20.md, Part U): the circuit final billed on its row and its versus
    /// (a fixture's <c>finalline:</c>, and the fallback without one), rematch pay as the rows and results show it, deck-strength
    /// stars on circuit and Tag Cup rows, guests and the Recipe Book, a former recipe kept by a save, the duplicate-recipe note
    /// and the starter ribbon. Run inside <c>-campaigncheck</c> (its last part) or alone as <c>-climbcheck &lt;folder&gt;</c>
    /// (absolute; with -mods &lt;empty folder&gt;). Each starts its own fresh save. Screenshots under <c>climb-*</c>.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The results' first DP line for a win: "Rematch" (a win in a match already won), its loaner half, or "Win".</summary>
        private static string WinLabel(DuelReward reward, bool loaner) =>
            reward.Repeat ? (loaner ? "Rematch (half: loaner deck)" : "Rematch")
            : loaner ? (reward.FirstWin ? "Win and first win (half: loaner deck)" : "Win (half: loaner deck)")
            : "Win";

        /// <summary>`-climbcheck &lt;folder&gt;`: <see cref="ClimbCampaignCheck"/> on its own ("climb check: ok/FAIL" lines).</summary>
        private IEnumerator ClimbCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"climb check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{name}.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (it starts a save of its own)", false);
                Application.Quit();
                yield break;
            }
            // It starts over: never on a save that's there already (a player's real one).
            if (_campaign.Profile != null || File.Exists(_campaign.SavePath))
            {
                Report($"needs -mods <empty folder>: {_campaign.SavePath} holds a save, and this check starts over", false);
                Application.Quit();
                yield break;
            }
            Debug.Log($"climb check: window {Screen.width}x{Screen.height}");
            _title.Close();
            _menu.Close();
            yield return ClimbCampaignCheck(Report, Shot);
            Debug.Log($"climb check: done, {bad} failure(s)");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private IEnumerator ClimbCampaignCheck(Action<string, bool> report, Func<string, IEnumerator> shot)
        {
            string Plain(string s) => Regex.Replace(s, "<.*?>", "");
            IEnumerator Settle(float seconds = 0.3f) { yield return null; yield return new WaitForSecondsRealtime(seconds); }
            List<UiText> Texts(Component under) => under.GetComponentsInChildren<UiText>().Where(t => t.gameObject.activeInHierarchy).ToList();
            // The gold stars in a text (StrengthText): the run of ★ inside the gold colour tag, and every ★ in it.
            int Gold(string text, UiTheme theme)
            {
                var m = Regex.Match(text, $"<color={Regex.Escape(theme.GoldHex)}>(★+)</color>");
                return m.Success ? m.Groups[1].Value.Length : 0;
            }
            int AllStars(string text) => Plain(text).Count(ch => ch == StrengthText.Star);

            // Step 9's record: every text this build adds, its size and whether it fits its box (measured as drawn).
            var sizeProblems = new List<string>();
            var sizedKinds = new HashSet<string>();
            void Size(string kind, UiText t, bool wraps = false)
            {
                sizedKinds.Add(kind);
                var box = t.rectTransform.rect;
                float w = t.preferredWidth, h = t.preferredHeight;
                bool ok = t.fontSize >= 14 && !t.resizeTextForBestFit && h <= box.height + 0.5f && (wraps || w <= box.width + 0.5f);
                if (!ok) sizeProblems.Add($"{kind} \"{Plain(t.text).Replace("\n", " / ")}\": {t.fontSize} px, needs {w:0}x{h:0} in {box.width:0}x{box.height:0}");
            }

            // A line whose start must show whatever its end does: the width of its text up to the end of the stars.
            void StarsFirst(string kind, UiText t)
            {
                sizedKinds.Add(kind);
                int end = t.text.IndexOf("  ·  ", StringComparison.Ordinal); // the stars end where the first separator starts
                var prefix = end >= 0 ? t.text.Substring(0, end) : t.text;
                var settings = t.GetGenerationSettings(new Vector2(100000, t.rectTransform.rect.height));
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = new TextGenerator().GetPreferredWidth(prefix, settings) / t.pixelsPerUnit, box = t.rectTransform.rect.width;
                bool ok = t.fontSize >= 14 && Plain(t.text).StartsWith("Strength ★") && w <= box + 0.5f;
                if (!ok) sizeProblems.Add($"{kind} \"{Plain(t.text)}\": {t.fontSize} px, stars end at {w:0} of {box:0}");
            }
            // The Recipe Book's "by" line shown whole: stars first, at most maxLines lines (each break at a separator), nothing
            // truncated, and the next text below it (the ban line) starting under it.
            void WholeLine(string kind, int maxLines)
            {
                var shown = Texts(_recipeBook);
                var by = shown.FirstOrDefault(t => t.fontSize == UiTheme.Size(TextRole.Small) && t.text.Contains("'s deck  ·  "));
                if (by == null) { sizedKinds.Add(kind); sizeProblems.Add($"{kind}: not found"); return; }
                StarsFirst(kind, by);
                var settings = by.GetGenerationSettings(new Vector2(by.rectTransform.rect.width, 0));
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                var gen = new TextGenerator();
                gen.Populate(by.text, settings);
                int lines = gen.lineCount, breaks = by.text.Count(ch => ch == '\n') + 1;
                float Top(UiText t) => -t.rectTransform.anchoredPosition.y;
                float bottom = Top(by) + by.rectTransform.rect.height;
                var below = shown.Where(t => t != by && t.rectTransform.parent == by.rectTransform.parent && Top(t) > Top(by) + 1
                                             && Mathf.Abs(t.rectTransform.anchoredPosition.x - by.rectTransform.anchoredPosition.x) < 1).OrderBy(Top).FirstOrDefault();
                bool ok = lines <= maxLines && lines == breaks && by.preferredHeight <= by.rectTransform.rect.height + 0.5f && below != null && Top(below) >= bottom - 0.5f;
                Debug.Log($"climb check: note  {kind}: {lines} line(s), needs {by.preferredHeight:0} of {by.rectTransform.rect.height:0} px, next text at {(below != null ? Top(below) : -1):0} (line ends {bottom:0})");
                if (!ok) sizeProblems.Add($"{kind} \"{Plain(by.text).Replace("\n", " / ")}\": {lines} line(s) (breaks {breaks - 1}, at most {maxLines}), needs {by.preferredHeight:0} of {by.rectTransform.rect.height:0} px, next at {(below != null ? Top(below) : -1):0} under {bottom:0}");
            }

            // ---- A fresh save: the starter screen first ----------------------------------------------------------------
            if (_campaign.Profile != null) _campaign.StartOver();
            _campaignScreen.Open();
            yield return Settle(0.5f);
            var starterTexts = Texts(_campaignScreen);
            var ribbons = starterTexts.Where(t => t.text == "BEST FIRST DECK").ToList();
            string TileTitle(UiText t)
            {
                // A tile's texts: its ribbon (a text on a child of the ribbon box) or its tag sit beside the title.
                for (var p = t.transform.parent; p != null; p = p.parent)
                    if (p.GetComponentsInChildren<UiText>().FirstOrDefault(x => CampaignState.Starters.Any(s => s.Title == x.text)) is UiText title) return title.text;
                return "";
            }
            var mekkTag = starterTexts.FirstOrDefault(t => t.text == "Modern · makes the early circuits easy");
            report($"8. the starter ribbon is on {string.Join(", ", ribbons.Select(TileTitle))} ({ribbons.Count} ribbon(s)); Mekk-Knight's tag reads \"{(mekkTag != null ? mekkTag.text : "(not found)")}\"",
                ribbons.Count == 1 && TileTitle(ribbons[0]) == "Warrior's Charge" && mekkTag != null && TileTitle(mekkTag) == "Mekk-Knight Invoked Evolved");
            if (mekkTag != null) Size("starter tag", mekkTag);
            yield return shot("climb-starters");

            _campaign.Start(CampaignState.Starters[1]); // Warrior's Charge: Ryo's Warriors is its own deck (Q5)
            var profile = _campaign.Profile!;
            var circuits = _campaign.Circuits;
            var c0 = circuits.All[0];
            int last = c0.Matches.Count - 1;
            var finalist = c0.Matches[last].Character;
            _pickDeck[0] = profile.Deck;

            // ---- 1. The final's row: "Final: ...", the fallback quote, then a fixture's final line ------------------------
            var originalLine = c0.FinalLine;
            var originalFormer = c0.Former.ToList();
            const string fixtureLine = "Fixture final line: one duel left, the whole circuit on it, so bring the best deck you have.";
            var owner = c0.Matches[0].Character;
            var allRecipes = RecipeBook.All(circuits);
            var formerDeck = CampaignState.Starters.Select(s => s.Deck)
                .FirstOrDefault(d => _library.Contains(d) && allRecipes.All(r => r.Key != RecipeBook.Key(owner.Id, d))) ?? "";
            var fixtureDir = Path.Combine(_campaign.ModsRoot, "climb-check");
            Directory.CreateDirectory(fixtureDir);
            var fixturePath = Path.Combine(fixtureDir, "circuits.txt");
            File.WriteAllText(fixturePath, "# The climb check's fixture: the first circuit, with a final line and a former match.\n"
                + $"circuit: {c0.Name}\nband: {c0.Band}\n" + string.Concat(c0.Matches.Select(m => $"match: {m.Character.Id} {m.Deck}\n"))
                + $"finalline: {fixtureLine}\nwas: 1 {owner.Id} {formerDeck}\nwas: {c0.Matches.Count + 1} {owner.Id} {formerDeck}\n");
            var fixture = Circuits.Load(fixturePath, _roster);
            var fc = fixture.All.FirstOrDefault();
            bool fixtureParsed = fc != null && fc.FinalLine == fixtureLine && fc.Former.Count == 1 && fc.Former[0].Match == 0 && fc.Former[0].Was.Character.Id == owner.Id && formerDeck.Length > 0;
            Debug.Log($"climb check: note  the fixture ({fixturePath}) parses: final line \"{fc?.FinalLine}\", former {string.Join(", ", fc?.Former.Select(f => $"{f.Match}:{f.Was.Character.Id}/{f.Was.Deck}") ?? new string[0])} (its bad match number skipped): {fixtureParsed}");

            (List<UiText> titles, List<UiText> quotes, List<UiText> rewards, UiText? header) CircuitTexts()
            {
                var all = Texts(_campaignScreen);
                return (all.Where(t => t.fontSize == UiTheme.Size(TextRole.Subhead) && (t.text.StartsWith("Match ") || t.text.StartsWith("Final: "))).ToList(),
                    all.Where(t => t.fontSize == 14 && t.font == Typography.Font(Face.Italic) && t.text.StartsWith("“")).ToList(),
                    all.Where(t => t.fontSize == 16 && (t.text.Contains("First win: +") || t.text.Contains("Won ×"))).ToList(),
                    all.FirstOrDefault(t => Plain(t.text).StartsWith("Cleared.") || Plain(t.text).StartsWith("Clear all ")));
            }

            c0.FinalLine = ""; // the fallback: no final line
            _campaignScreen.DebugShowCircuit(0);
            yield return Settle();
            var (titles, quotes, _, _) = CircuitTexts();
            var introWanted = $"“{finalist.IntroFor(c0.Matches.Take(last).Any(e => e.Character.Id == finalist.Id))}”";
            bool otherTitles = titles.Take(last).Select((t, m) => t.text == $"Match {m + 1}: {c0.Matches[m].Character.DisplayName}").All(x => x);
            report($"1. without a final line the last row reads \"{titles.LastOrDefault()?.text}\" and quotes the intro ({quotes.LastOrDefault()?.text}); the others \"Match N: ...\"",
                titles.Count == c0.Matches.Count && titles[last].text == $"Final: {finalist.DisplayName}" && otherTitles && quotes.LastOrDefault()?.text == introWanted);
            yield return shot("climb-final-row-intro");

            c0.FinalLine = fc?.FinalLine ?? fixtureLine;
            _campaignScreen.DebugShowCircuit(0);
            yield return Settle();
            (titles, quotes, _, _) = CircuitTexts();
            report($"1. with the fixture's final line (parsed: {fixtureParsed}) the last row reads \"{titles.LastOrDefault()?.text}\" / {quotes.LastOrDefault()?.text}",
                fixtureParsed && titles.Count == c0.Matches.Count && titles[last].text == $"Final: {finalist.DisplayName}" && quotes.LastOrDefault()?.text == $"“{fixtureLine}”");
            if (titles.Count > 0) Size("final title", titles[last]);
            if (quotes.Count > 0) Size("final line", quotes[quotes.Count - 1], wraps: true);
            yield return shot("climb-final-row");

            // ---- 2. Every circuit's rows: no "skill", stars as the deck's rating -------------------------------------------
            var starProblems = new List<string>();
            int rated = 0, rows = 0, skillLines = 0;
            for (int ci = 0; ci < circuits.All.Count; ci++)
            {
                _campaignScreen.DebugShowCircuit(ci);
                yield return null;
                var all = Texts(_campaignScreen);
                skillLines += all.Count(t => t.text.StartsWith("Deck: ") && t.text.Contains("skill"));
                var rewards = all.Where(t => t.fontSize == 16 && (t.text.Contains("First win: +") || t.text.Contains("Won ×"))).ToList();
                if (rewards.Count != circuits.All[ci].Matches.Count) { starProblems.Add($"{circuits.All[ci].Name}: {rewards.Count} reward texts"); continue; }
                for (int m = 0; m < rewards.Count; m++)
                {
                    rows++;
                    int want = DeckStrength.StarsFor(_campaign.Ratings, circuits.All[ci].Matches[m].Deck);
                    if (want > 0) rated++;
                    int gold = Gold(rewards[m].text, UiTheme.Current), every = AllStars(rewards[m].text);
                    if (gold != want || every != (want > 0 ? DeckStrength.MaxStars : 0) || !rewards[m].supportRichText || !Plain(rewards[m].text).StartsWith(want > 0 ? "★" : "First win"))
                        starProblems.Add($"{circuits.All[ci].Name} match {m + 1} ({circuits.All[ci].Matches[m].Deck}): {gold} gold of {every}, want {want}");
                    Size("circuit reward", rewards[m]);
                    if (rewards[m].fontSize != 16) starProblems.Add($"{circuits.All[ci].Name} match {m + 1}: reward at {rewards[m].fontSize} px");
                }
            }
            report($"2. {rows} circuit rows: {skillLines} deck lines say \"skill\"; stars match the deck ratings ({rated} rated){(starProblems.Count > 0 ? ": " + string.Join("; ", starProblems.Take(4)) : "")}",
                rows == circuits.All.Sum(c => c.Matches.Count) && rows > 0 && skillLines == 0 && starProblems.Count == 0 && rated > 0);

            // ---- 3. The final's versus: "Circuit final · Match N of N", their final line; match 1 isn't billed --------------
            ShowVersus(0, last);
            yield return Settle(0.5f);
            var subtitle = Texts(_scenes).FirstOrDefault(t => t.text.Contains($"Match {last + 1} of {c0.Matches.Count}"));
            var quote = _scenes.LastVersus?.them.Quote ?? "";
            if (subtitle != null) Size("versus subtitle", subtitle);
            var finalSubtitle = subtitle != null ? subtitle.text : ""; // the next versus destroys this text
            yield return shot("climb-final-versus");
            ShowVersus(0, 0);
            yield return Settle();
            var firstSubtitle = Texts(_scenes).FirstOrDefault(t => t.text.Contains($"Match 1 of {c0.Matches.Count}"));
            report($"3. the final's versus subtitle \"{finalSubtitle}\", their quote \"{quote}\"; match 1's \"{firstSubtitle?.text}\"",
                finalSubtitle == $"Circuit final · Match {last + 1} of {c0.Matches.Count}" && quote == fixtureLine
                && firstSubtitle != null && firstSubtitle.text == $"Match 1 of {c0.Matches.Count}");
            _scenes.Close();
            c0.FinalLine = originalLine;

            // ---- 4, 5 and Q5. Wins, rematches and their results ---------------------------------------------------------
            var none = new List<CampaignState.QueuedStory>();
            int Wallet() => profile.Dp;
            (string label, int dp) FirstEarning() => _scenes.LastResults?.Earnings.FirstOrDefault() ?? ("(none)", -1);
            string UnlockLine() => _scenes.LastResults?.Unlocks.FirstOrDefault(u => u.StartsWith("Recipe unlocked:")) ?? "(none)";

            // A first win in 0.0 (Ryo's Legendary Dragons recipe; not a Warrior's Charge collection's cards).
            int before = Wallet();
            var first = circuits.Record(profile, 0, 0, 0);
            ShowCampaignResults(0, 0, first, circuits.ShopYear(profile), 0, false, none);
            var firstRecipe = RecipeBook.All(circuits).First(r => r.Key == RecipeBook.Key(c0.Matches[0].Character.Id, c0.Matches[0].Deck));
            bool firstOwned = _campaign.Printings is Ygo.Engine.Data.PrintingIndex px0 && RecipeBook.OwnsAll(_library.Load(firstRecipe.Deck), profile, px0, _cardPack!);
            bool firstRight = first.FirstWin && !first.Repeat && first.Dp == circuits.WinDp(0) + Circuits.FirstWinBonus && FirstEarning() == ("Win", circuits.WinDp(0))
                && UnlockLine().EndsWith(firstOwned ? "You already own every card in it." : "Find it in the Recipe Book.")
                && profile.Seen.Contains(RecipeBook.SeenKey(firstRecipe)) == firstOwned;
            Debug.Log($"climb check: note  a first win in 0.0 pays {first.Dp} (wallet +{Wallet() - before}), the results say {FirstEarning()}, its recipe line \"{UnlockLine()}\" (owns all: {firstOwned}): {firstRight}");
            _scenes.Close();

            // 5. The label's every case.
            var cases = new[]
            {
                (WinLabel(new DuelReward { Repeat = true }, false), "Rematch"),
                (WinLabel(new DuelReward { Repeat = true }, true), "Rematch (half: loaner deck)"),
                (WinLabel(new DuelReward { FirstWin = true }, false), "Win"),
                (WinLabel(new DuelReward { FirstWin = true }, true), "Win and first win (half: loaner deck)"),
                (WinLabel(new DuelReward(), true), "Win (half: loaner deck)"),
            };
            before = Wallet();
            var again = circuits.Record(profile, 0, 0, 0);
            int paid = Wallet() - before;
            ShowCampaignResults(0, 0, again, circuits.ShopYear(profile), 0, false, none);
            yield return Settle();
            report($"4, 5. a rematch in 0.0 pays {again.Dp} (wallet +{paid}, \"{again.Summary}\"); the results say {FirstEarning()}; the first win was right ({firstRight}); WinLabel: {string.Join(", ", cases.Select(x => $"\"{x.Item1}\""))}",
                firstRight && cases.All(x => x.Item1 == x.Item2) && again.Repeat && !again.FirstWin && again.Dp == Circuits.RepeatWinDp && paid == Circuits.RepeatWinDp && FirstEarning() == ("Rematch", paid));
            yield return shot("climb-rematch-results");
            _scenes.Close();

            // Rookie Cup's first match: a circuit whose win pays more than a rematch (so the row and results can't show the win's DP).
            circuits.Record(profile, 1, 0, 0);
            before = Wallet();
            var again1 = circuits.Record(profile, 1, 0, 0);
            paid = Wallet() - before;
            ShowCampaignResults(1, 0, again1, circuits.ShopYear(profile), 0, false, none);
            report($"4. a rematch in 1.0 (a win there pays {circuits.WinDp(1)}) pays {again1.Dp} (wallet +{paid}); the results say {FirstEarning()}",
                again1.Repeat && paid == Circuits.RepeatWinDp && FirstEarning() == ("Rematch", paid) && circuits.WinDp(1) != Circuits.RepeatWinDp);
            _scenes.Close();


            // Q5: the final's first win (Ryo's Warriors, the starter's own deck): you own every card, so it isn't NEW.
            var finalRecipe = RecipeBook.All(circuits).First(r => r.Key == RecipeBook.Key(finalist.Id, c0.Matches[last].Deck));
            bool finalOwned = _campaign.Printings is Ygo.Engine.Data.PrintingIndex px1 && RecipeBook.OwnsAll(_library.Load(finalRecipe.Deck), profile, px1, _cardPack!);
            var finalWin = circuits.Record(profile, 0, last, 0);
            ShowCampaignResults(0, last, finalWin, circuits.ShopYear(profile), 0, false, none);
            yield return Settle();
            report($"Q5. the final's first win (own every card: {finalOwned}): \"{UnlockLine()}\", seen {profile.Seen.Contains(RecipeBook.SeenKey(finalRecipe))}",
                finalOwned && UnlockLine() == $"Recipe unlocked: {finalRecipe.Owner.Name}'s {DeckTitle(finalRecipe.Deck)}. You already own every card in it."
                && profile.Seen.Contains(RecipeBook.SeenKey(finalRecipe)));
            yield return shot("climb-unlock-results");
            _scenes.Close();

            // The rows: 0.0 and 1.0 won twice say "Rematch: +150 DP"; the Beginner Circuit cleared says what a rematch pays.
            _campaignScreen.DebugShowCircuit(0);
            yield return Settle();
            var (_, _, rewards0, header0) = CircuitTexts();
            bool clearedYet = circuits.IsCleared(0, profile);
            for (int m = 0; m < c0.Matches.Count; m++) if (!profile.HasWon(0, m)) circuits.Record(profile, 0, m, 0);
            _campaignScreen.DebugShowCircuit(0);
            yield return Settle();
            var (_, _, rewardsCleared, headerCleared) = CircuitTexts();
            report($"4. 0.0's row \"{Plain(rewards0.FirstOrDefault()?.text ?? "").Replace("\n", " / ")}\"; cleared ({!clearedYet} -> {circuits.IsCleared(0, profile)}) header \"{Plain(headerCleared?.text ?? "")}\"",
                rewards0.Count > 0 && Plain(rewards0[0].text).EndsWith($"Won ×2\nRematch: +{Circuits.RepeatWinDp} DP")
                && circuits.IsCleared(0, profile) && headerCleared != null && Plain(headerCleared.text) == $"Cleared. A rematch pays {Circuits.RepeatWinDp} DP.");
            if (headerCleared != null) Size("cleared header", headerCleared);
            foreach (var r in rewardsCleared) Size("circuit reward", r);
            yield return shot("climb-rematch-row");
            _campaignScreen.DebugShowCircuit(1);
            yield return Settle();
            var (_, _, rewards1, _) = CircuitTexts();
            report($"4. 1.0's row (a win there pays {circuits.WinDp(1)}): \"{Plain(rewards1.FirstOrDefault()?.text ?? "").Replace("\n", " / ")}\"",
                rewards1.Count > 0 && Plain(rewards1[0].text).EndsWith($"Won ×2\nRematch: +{Circuits.RepeatWinDp} DP"));
            foreach (var r in rewards1) Size("circuit reward", r);

            // ---- Tag Cup rows: the pair's stars; a rematch's row and results -------------------------------------------------
            if (circuits.TagCups.Count > 0)
            {
                var cup = circuits.TagCups[0];
                List<UiText> TagRewards() => Texts(_campaignScreen).Where(t => t.fontSize == 16 && (t.text.Contains("First win: +") || t.text.Contains("Won ×"))).ToList();
                _campaignScreen.DebugShowTagCup(0);
                yield return Settle();
                var tagRows = TagRewards();
                var tagStars = tagRows.Select((t, m) => (shown: Gold(t.text, UiTheme.Current), all: AllStars(t.text),
                    want: DeckStrength.StarsFor(_campaign.Ratings, new[] { cup.Matches[m].lead.Deck, cup.Matches[m].partner.Deck }))).ToList();
                report($"tag rows' stars (shown/want): {string.Join(", ", tagStars.Select(s => $"{s.shown}/{s.want}"))}",
                    tagRows.Count == cup.Matches.Count && tagStars.All(s => s.shown == s.want && s.all == (s.want > 0 ? DeckStrength.MaxStars : 0)) && tagStars.Any(s => s.want > 0));
                circuits.RecordTag(profile, 0, 0, 0);
                before = Wallet();
                var tagAgain = circuits.RecordTag(profile, 0, 0, 0);
                paid = Wallet() - before;
                var heldTag = _tag;
                _tag = null; // your own deck, no loaner
                ShowTagResults(0, 0, tagAgain, 0, "", none);
                _tag = heldTag;
                report($"tag rematch (a win there pays {circuits.WinDp(cup.After)}) pays {tagAgain.Dp} (wallet +{paid}); the results say {FirstEarning()}",
                    tagAgain.Repeat && paid == Circuits.RepeatWinDp && FirstEarning() == ("Rematch", paid));
                _scenes.Close();
                _campaignScreen.DebugShowTagCup(0);
                yield return Settle();
                tagRows = TagRewards();
                report($"tag row after the rematch: \"{Plain(tagRows.FirstOrDefault()?.text ?? "").Replace("\n", " / ")}\"",
                    tagRows.Count > 0 && Plain(tagRows[0].text).EndsWith($"Won ×2\nRematch: +{Circuits.RepeatWinDp} DP"));
                foreach (var r in tagRows) Size("tag reward", r);
                yield return shot("climb-tag-row");
            }

            // ---- 6. Guests: Seraph open (the Advanced band cleared), stars and "Best after" ------------------------------
            int advancedLast = Enumerable.Range(0, circuits.All.Count).LastOrDefault(i => circuits.All[i].Band == "Advanced");
            for (int ci = 0; ci <= advancedLast; ci++)
                for (int m = 0; m < circuits.All[ci].Matches.Count; m++)
                    if (!profile.HasWon(ci, m)) profile.MatchWins[Profile.MatchKey(ci, m)] = 1;
            _campaign.Save();
            _campaignScreen.Close();
            ShowGuests();
            yield return Settle(0.5f);
            var seraph = _campaign.Guests.All.First(g => g.Character.Id == "seraph");
            var seraphRow = _scenes.LastGuestRows.FirstOrDefault(r => r.Id == "seraph");
            var wantTail = " · " + StrengthText.Stars(4, UiTheme.Current) + " · Best after the Master band";
            // The band part only while that band isn't cleared, on every open guest; none on a locked one.
            var guestProblems = new List<string>();
            foreach (var g in _campaign.Guests.All)
            {
                var row = _scenes.LastGuestRows.FirstOrDefault(r => r.Id == g.Character.Id);
                if (row == null) { guestProblems.Add($"{g.Character.Id}: no row"); continue; }
                bool hasStars = row.Decks.Contains(StrengthText.Star);
                if (!row.Unlocked) { if (hasStars) guestProblems.Add($"{g.Character.Id}: locked but starred"); continue; }
                double elo = DeckStrength.Elo(_campaign.Ratings, g.Decks, strongest: true);
                var after = DeckStrength.BestAfterBand(circuits, _campaign.Ratings, elo);
                var inIt = Enumerable.Range(0, circuits.All.Count).Where(i => circuits.All[i].Band == after || circuits.All[i].Name == after).ToList();
                bool open = inIt.Any(i => !circuits.IsCleared(i, profile));
                if (Gold(row.Decks, UiTheme.Current) != DeckStrength.Stars(elo)) guestProblems.Add($"{g.Character.Id}: {Gold(row.Decks, UiTheme.Current)} gold, want {DeckStrength.Stars(elo)}");
                if (row.Decks.Contains("Best after") != open) guestProblems.Add($"{g.Character.Id}: best after {after} (open {open}) shown {row.Decks.Contains("Best after")}");
            }
            report($"6. every guest row: stars on open ones only, \"Best after\" only while that band isn't cleared{(guestProblems.Count > 0 ? ": " + string.Join("; ", guestProblems) : "")}",
                guestProblems.Count == 0);
            var seraphLine = Texts(_scenes).FirstOrDefault(t => seraphRow != null && t.text == seraphRow.Decks);
            report($"6. Seraph's decks line: \"{Plain(seraphRow?.Decks ?? "(no row)")}\" ({(seraphLine != null ? $"drawn, rich text {seraphLine.supportRichText}, {seraphLine.fontSize} px" : "not drawn")})",
                seraphRow != null && seraphRow.Unlocked && seraphRow.Decks.EndsWith(wantTail) && seraphLine != null && seraphLine.supportRichText);
            if (seraphLine != null) Size("guest decks line", seraphLine);
            foreach (var t in Texts(_scenes).Where(t => t.text.Contains(StrengthText.Star))) Size("guest decks line", t);
            yield return shot("climb-guests");
            _scenes.Close();

            // A guest rematch: Seraph's prize win first, then 150.
            GuestList.Record(profile, circuits, seraph, false, 0);
            before = Wallet();
            var guestAgain = GuestList.Record(profile, circuits, seraph, false, 0);
            paid = Wallet() - before;
            ShowGuestResults((seraph, false, ""), guestAgain, 0, none);
            report($"guest rematch pays {guestAgain.Dp} (wallet +{paid}); the results say {FirstEarning()}",
                guestAgain.Repeat && paid == Circuits.RepeatWinDp && FirstEarning() == ("Rematch", paid));
            _scenes.Close();

            // ---- 7. The Recipe Book: stars on open rows and the detail; a former recipe only once it's yours --------------
            c0.Former.Clear();
            c0.Former.AddRange(originalFormer);
            if (fc != null) c0.Former.AddRange(fc.Former);
            var formerRecipe = RecipeBook.All(circuits).FirstOrDefault(r => r.Former && r.Key == RecipeBook.Key(owner.Id, formerDeck));
            // The live data's former recipes: circuits.txt's own "was:" pairings that no circuit match or Tag Cup still plays
            // (one still met, as Gage's Geargia in the Novice Tag Cup, is a current recipe). Each must be listed as former.
            var metNow = new HashSet<string>();
            foreach (var circuit in circuits.All)
                foreach (var m in circuit.Matches)
                    if (m.Deck.Length > 0) metNow.Add(RecipeBook.Key(m.Character.Id, m.Deck));
            foreach (var cup in circuits.TagCups)
                foreach (var (lead, partner) in cup.Matches)
                    foreach (var m in new[] { lead, partner })
                        if (m.Deck.Length > 0) metNow.Add(RecipeBook.Key(m.Character.Id, m.Deck));
            var dataWas = circuits.All.SelectMany((c, i) => (i == 0 ? originalFormer : c.Former).Select(f => f.Was)).ToList();
            var formerOnly = dataWas.Where(w => w.Deck.Length > 0).Select(w => RecipeBook.Key(w.Character.Id, w.Deck))
                .Where(k => !metNow.Contains(k)).Distinct().ToList();
            var liveFormers = RecipeBook.All(circuits).Where(r => r.Former && r.Key != formerRecipe?.Key).ToList();
            var unlisted = formerOnly.Where(k => liveFormers.All(r => r.Key != k)).ToList();
            var liveFormer = formerOnly.Select(k => liveFormers.FirstOrDefault(r => r.Key == k)).FirstOrDefault(r => r != null);
            _recipeBook.Open();
            yield return Settle(0.5f);
            bool listedBefore = _recipeBook.DebugRecipes.Any(r => r.Former && r.Key == formerRecipe?.Key);
            bool liveListedBefore = liveFormer != null && _recipeBook.DebugRecipes.Any(r => r.Key == liveFormer.Key);
            var openRated = _recipeBook.DebugRecipes.Where(r => RecipeBook.IsUnlocked(r, circuits, profile) && DeckStrength.StarsFor(_campaign.Ratings, r.Deck) > 0).ToList();
            var ownerLines = Texts(_recipeBook).Where(t => t.fontSize == 14 && t.text.Contains(StrengthText.Star)).ToList();
            bool linesRight = openRated.Count > 0 && ownerLines.Count == openRated.Count
                && ownerLines.Select((t, i) => Gold(t.text, UiTheme.Current) == DeckStrength.StarsFor(_campaign.Ratings, openRated[i].Deck) && Plain(t.text).StartsWith(openRated[i].Owner.Name)
                    // (R2: a line cut with "…" would lose stars or its end: it must be whole.)
                    && !Plain(t.text).TrimEnd().EndsWith("…") && Plain(t.text).Count(c => c == StrengthText.Star) == DeckStrength.MaxStars).All(x => x);
            report($"7. {ownerLines.Count} open rated recipe rows show stars ({openRated.Count} open and rated); the former recipe ({formerRecipe?.Owner.Name}'s {formerDeck}) isn't listed without its key ({!listedBefore})",
                linesRight && formerRecipe != null && !listedBefore);
            foreach (var t in ownerLines) Size("recipe owner line", t);
            _recipeBook.Close();

            if (formerRecipe != null) profile.Unlocks.Add(RecipeBook.FormerKey(formerRecipe));
            if (liveFormer != null) profile.Unlocks.Add(RecipeBook.FormerKey(liveFormer));
            _recipeBook.Open();
            yield return Settle(0.5f);
            bool selected = formerRecipe != null && _recipeBook.DebugSelectKey(formerRecipe.Key);
            yield return Settle();
            int wantStars = DeckStrength.StarsFor(_campaign.Ratings, formerDeck);
            var byLine = Texts(_recipeBook).FirstOrDefault(t => t.fontSize == UiTheme.Size(TextRole.Small) && t.text.Contains("'s deck  ·  "));
            report($"7. with formerrecipe:{formerRecipe?.Key} it's listed and selected ({selected}); its detail: \"{Plain(byLine?.text ?? "(none)")}\" ({wantStars} stars)",
                selected && byLine != null && Plain(byLine.text).Contains(formerRecipe!.Where)
                && byLine.text.StartsWith("Strength " + StrengthText.Stars(wantStars, UiTheme.Current) + "  ·  ") && wantStars > 0);
            WholeLine("recipe strength line", 1);
            yield return shot("climb-recipes");
            if (liveFormer != null)
            {
                bool liveSelected = _recipeBook.DebugSelectKey(liveFormer.Key);
                yield return Settle();
                var liveBy = Texts(_recipeBook).FirstOrDefault(t => t.fontSize == UiTheme.Size(TextRole.Small) && t.text.Contains("'s deck  ·  "));
                report($"7. the live data's former recipe ({liveFormer.Owner.Name}'s {liveFormer.Deck}): hidden without its key ({!liveListedBefore}), listed with it ({liveSelected}); \"{Plain(liveBy?.text ?? "(none)")}\"",
                    !liveListedBefore && liveSelected && liveBy != null);
                WholeLine("recipe strength line (former, live)", 2);
                yield return shot("climb-recipes-long");
            }
            else if (formerOnly.Count == 0)
                Debug.Log($"climb check: note  circuits.txt's {dataWas.Count} \"was:\" line(s) ({string.Join(", ", dataWas.Select(w => RecipeBook.Key(w.Character.Id, w.Deck)))}) "
                          + "are all still met in a circuit or Tag Cup: no former recipe from the data; the fixture's covers step 7");
            if (formerOnly.Count > 0)
                report($"7. every \"was:\" pairing met nowhere else in the map is listed as a former recipe ({string.Join(", ", formerOnly)})"
                       + (unlisted.Count > 0 ? $": not listed {string.Join(", ", unlisted)}" : ""), unlisted.Count == 0);
            // The longest real "by" line: the recipe met in the circuit with the longest name.
            var longest = _recipeBook.DebugRecipes.Where(r => DeckStrength.StarsFor(_campaign.Ratings, r.Deck) > 0).OrderByDescending(r => r.Where.Length + r.Owner.DisplayName.Length).FirstOrDefault();
            if (longest != null)
            {
                // Unlocked for the look (a scratch save): its detail shows only once it's yours.
                profile.Unlocks.Add(RecipeBook.FormerKey(longest));
                _recipeBook.Close();
                _recipeBook.Open();
                yield return Settle(0.4f);
                _recipeBook.DebugSelectKey(longest.Key);
                yield return Settle();
                var longBy = Texts(_recipeBook).FirstOrDefault(t => t.fontSize == UiTheme.Size(TextRole.Small) && t.text.Contains("'s deck  ·  "));
                report($"9. the longest recipe line ({longest.Owner.Name}, {longest.Where}): \"{Plain(longBy?.text ?? "(none)")}\"", longBy != null && Plain(longBy.text).StartsWith("Strength ★"));
                WholeLine("recipe strength line (longest)", 2);
                yield return shot("climb-recipes-longest");
            }
            _recipeBook.Close();
            c0.Former.Clear();
            c0.Former.AddRange(originalFormer);

            // ---- 9. Every text this build adds: at least 14 px at 1080, inside its box ------------------------------------
            var kinds = new[] { "starter tag", "final title", "final line", "circuit reward", "versus subtitle", "cleared header", "tag reward", "guest decks line", "recipe owner line", "recipe strength line", "recipe strength line (longest)" };
            var missing = kinds.Where(k => !sizedKinds.Contains(k)).ToList();
            report($"9. at {Screen.width}x{Screen.height}: the added texts ({sizedKinds.Count} kinds) are at least 14 px and fit their boxes"
                   + (missing.Count > 0 ? $"; not found: {string.Join(", ", missing)}" : "") + (sizeProblems.Count > 0 ? ": " + string.Join("; ", sizeProblems.Take(6)) : ""),
                missing.Count == 0 && sizeProblems.Count == 0);
            _campaignScreen.Close();
        }
    }
}
