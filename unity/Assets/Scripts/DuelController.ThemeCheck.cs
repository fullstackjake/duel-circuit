using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-themecheck &lt;folder&gt;` (with -mods &lt;empty folder&gt;): the shell screens in both themes (POST-GAME-PLAN step 8):
    /// Main Street, then the Back Alley, a screenshot of each screen in each ("theme-&lt;theme&gt;-&lt;screen&gt;.png"). The
    /// campaign (circuits and the Library), the Card Shop, the Card Trader, the deck editor, a versus screen and a results
    /// screen; and the Back Alley's (ALLEY-SPEC section 13): Dumpster Row's panel, Moth's van's three tabs, Baz's chalkboard,
    /// Nyx's Table and a Midnight Open bracket of 16 with its final's pips (drawn, never entered); the headliners' show
    /// over the board (the banner, the meter with the payoff stamp, the fizzle: DuelController.ShowCheck). Checks that
    /// switching the theme raises <see cref="UiTheme.Changed"/> and that each screen takes it. Then the story screen's backdrops,
    /// a place at a time (DuelController.BackdropCheck).
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator ThemeCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"theme check: {(ok ? "ok  " : "FAIL")} {what}");
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"theme-{name}.png"));
                yield return null;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            profile.LibraryOpen = true;
            DebugSetDp(profile, 50_000);
            profile.Tokens = 3;
            // The alley open, its scenes marked seen (they'd close the screens under test), and a card of yours on Moth's rail.
            bool alley = _campaign.AlleyReady;
            if (alley)
            {
                profile.AlleyOpen = true;
                foreach (var trigger in BackAlley.Triggers(_campaign.AlleyData).Append(BackAlley.HeldEvent))
                    foreach (var scene in _story.Pending(profile, new[] { trigger })) Story.MarkSeen(profile, scene);
                // Ines's card still on the rail (the HELD row shows until held-card-home is seen).
                profile.Seen.Remove(BackAlley.HeldScene);
                profile.Seen.Remove(BackAlley.HeldHomeScene);
                if (profile.Collection.All.OrderByDescending(profile.Collection.Value).FirstOrDefault() is CardCopy railed && profile.Collection.Remove(railed))
                    profile.Rail.Add(new RailEntry { Copy = railed, Price = AlleyEconomy.RailPrice(profile.Collection.Value(railed)), ExpiresAt = profile.DuelsPlayed + AlleyEconomy.RailDuels });
            }
            _campaign.Save();
            int changes = 0;
            UiTheme.Changed += () => changes++;
            foreach (var (name, theme) in new[] { ("main-street", UiTheme.MainStreet), ("back-alley", UiTheme.BackAlley) })
            {
                UiTheme.Current = theme;
                _campaignScreen.ThemeOverride = theme;
                _campaignScreen.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{name}-circuits");
                _campaignScreen.DebugShowLibrary(0);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{name}-library");
                _campaignScreen.Close();
                _shop.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{name}-shop");
                _shop.Close();
                _trader.Open();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{name}-trader");
                _trader.Close();
                _campaignScreen.Close(); // (closing the trader brings it back, over the editor)
                _editor.Open(profile.Deck, profile.Collection);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-editor");
                _editor.Close();
                ShowVersus(0, 0);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-versus");
                _scenes.Close();
                // Original texts: the versus line at a circuit past CED's errata, and the editor's note (DuelController.TextsCheck).
                yield return TextsThemeShots(name, Shot, Report);
                // A results screen as a won circuit match shows it (sample numbers, nothing recorded).
                var r = new CampaignScenes.Results
                {
                    Won = true, Headline = "Victory!", Speaker = "Ryo", Quote = "Okay. Okay! Rematch. Now.",
                    Circuit = "Beginner Circuit · Match 1 against Ryo, the Rookie", TotalDp = profile.Dp,
                    Progress = new List<bool> { true, false, false }, Record = "Against Ryo: <b>1</b> win, <b>0</b> losses",
                    Exp = (120, profile.Level, 40, 200),
                };
                r.Earnings.Add(("Win", 150));
                r.Earnings.Add(("First-win bonus", 200));
                r.Unlocks.Add("Recipe unlocked: Ryo's Classic. Find it in the Recipe Book.");
                _scenes.ShowResults(r, () => _scenes.Close(), () => _scenes.Close(), () => _scenes.Close());
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-results");
                _scenes.Close();
                Report($"the {name} theme draws the campaign, Library, shop, trader, deck editor, versus and results", UiTheme.Current == theme);
                if (!alley) { Report("the alley's data, the Traditional list and printings are installed", false); continue; }
                // The Back Alley: Dumpster Row's panel, then the van's three tabs, in this theme.
                _campaignScreen.DebugShowAlley(_campaign.AlleyData.IndexOf("dumpster-row"));
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-alley");
                bool panel = _campaignScreen.IsOpen && _campaignScreen.InAlley && _campaignScreen.DebugBase == theme.Base && _campaignScreen.AlleySeats.Count == 4;
                // Back up to Circuits, so the next theme's first shot is the circuit map again.
                _campaignScreen.DebugShowCircuits();
                _campaignScreen.Close();
                _van.ThemeOverride = theme;
                var tabs = new[] { "shelf", "used", "sell" };
                bool van = true;
                for (int tab = 0; tab < tabs.Length; tab++)
                {
                    _van.DebugShow(tab);
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot($"{name}-van-{tabs[tab]}");
                    van &= _van.IsOpen && UiTheme.Current == theme;
                }
                var plate = _van.DebugPlate;
                van &= plate.plate == "UNDER THE COUNTER" && plate.plateColor == theme.Sign && plate.signColor == UiTheme.NeonCyan;
                _van.Hide();
                _van.ThemeOverride = null;
                Report($"the {name} theme draws the alley panel and the van's three tabs (plate \"{plate.plate}\" under \"{plate.sign}\")", panel && van);
                // The headliners' show over the board: the banner, the meter with the payoff, the fizzle (DuelController.ShowCheck).
                yield return ShowThemeShots(name, Shot, Report);
                // A hovered button, map node, shop row and van slab (DuelController.HoverCheck).
                yield return HoverThemeShots(name, theme, Shot, Report);

                // Phase C: Baz's chalkboard and Nyx's Table (the tables they wait on cleared for the shots, then as they were).
                var data = _campaign.AlleyData;
                var cleared = profile.AlleyTables.ToDictionary(p => p.Key, p => p.Value.Cleared);
                foreach (var id in new[] { "dumpster-row", "basement", "black-market" })
                {
                    if (!profile.AlleyTables.TryGetValue(id, out var st)) profile.AlleyTables[id] = st = new AlleyTableState();
                    st.Cleared = true;
                }
                _campaignScreen.DebugShowAlley(_campaignScreen.DebugChalkboard);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-chalkboard");
                bool chalkboard = _campaignScreen.IsOpen && _campaignScreen.DebugBase == theme.Base && _campaignScreen.ChalkboardRows.Count
                    // (The Invitational stays off the board until the wall opens: NL-2's chalkboard rule.)
                    == data.Brackets.Count(x => x.Kind != Brackets.InvitationalKind || profile.Seen.Contains(BackAlley.WallEvent));
                _campaignScreen.DebugShowAlley(data.IndexOf("nyx"));
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-nyx");
                bool nyx = _campaignScreen.IsOpen && _campaignScreen.AlleySeats.Count == 4;
                // No Limits (NL-1): the table and its builds ready in this run's data, Nyx cleared and the wall open for the shot.
                var undoNoLimits = ForceNoLimitsReady(out int noLimitsTable);
                var seenBefore = new HashSet<string>(profile.Seen);
                if (!profile.AlleyTables.TryGetValue("nyx", out var nyxState)) profile.AlleyTables["nyx"] = nyxState = new AlleyTableState();
                bool nyxCleared = nyxState.Cleared;
                nyxState.Cleared = true;
                profile.Seen.Add(BackAlley.ClosingScene);
                profile.Seen.Add(BackAlley.WallEvent);
                _campaignScreen.DebugShowAlley(noLimitsTable);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot($"{name}-no-limits");
                var nlSeats = _campaignScreen.AlleySeats;
                bool noLimits = noLimitsTable >= 0 && _campaignScreen.IsOpen && _campaignScreen.InNoLimits && _campaignScreen.DebugBase == theme.Base
                    && nlSeats.Count == 4 && nlSeats[3].Tag == CampaignScreen.EndChairTag && nlSeats.All(s => s.Unbanned.StartsWith("UNBANNED: "));
                Report($"the {name} theme draws the No Limits panel ({nlSeats.Count} seats, seat 4 \"{nlSeats.ElementAtOrDefault(3)?.Tag}\", \"{_campaignScreen.AlleyRulesLine}\")", noLimits);
                nyxState.Cleared = nyxCleared;
                profile.Seen.IntersectWith(seenBefore);
                foreach (var back in undoNoLimits) back();
                _campaignScreen.DebugShowCircuits();
                _campaignScreen.Close();
                // A Midnight Open of 16, drawn (not entered): you through to the final, the other rounds settled, game 1 yours.
                var bracketBefore = profile.Bracket;
                bool sixteen = false;
                if (_campaign.AlleyContext is AlleyContext ctx && data.Brackets.Any(x => x.Kind == Brackets.MidnightKind))
                {
                    var b = Brackets.CreateAlley(data, Brackets.MidnightKind, profile, ctx, 0x7E5Du, profile.Deck);
                    profile.Bracket = b;
                    for (int round = 0; round < b.Rounds - 1; round++)
                    {
                        foreach (var m in b.MatchesIn(round).Where(b.IsYours)) Brackets.Decide(b, m, b.Pair(m).a == b.YourSlot ? 0 : 1);
                        Brackets.SimulateRound(b, round, _campaign.Matchups);
                    }
                    if (b.Pair(b.Final).a == b.YourSlot) b.FinalGamesA = 1; else b.FinalGamesB = 1;
                    b.FinalLastLoser = b.Pair(b.Final).a == b.YourSlot ? 1 : 0;
                    UiTheme.Current = theme;
                    _bracketScreen.Open();
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot($"{name}-bracket-16");
                    var (boxes, overlaps) = _bracketScreen.DebugOverlaps();
                    sixteen = _bracketScreen.IsOpen && boxes == 31 && overlaps.Count == 0 && _bracketScreen.DebugPips.a.Length > 0;
                    _bracketScreen.Close();
                }
                profile.Bracket = bracketBefore;
                foreach (var id in new[] { "dumpster-row", "basement", "black-market" })
                    if (cleared.TryGetValue(id, out var was)) profile.AlleyTables[id].Cleared = was; else profile.AlleyTables.Remove(id);
                _campaign.Save();
                Report($"the {name} theme draws Baz's chalkboard, Nyx's Table and a Midnight Open bracket of 16 with its pips", chalkboard && nyx && sixteen);
            }
            // The story's backdrops: one per place, the epilogue's crossfade, Reduced motion's swap (DuelController.BackdropCheck).
            _campaignScreen.Close();
            yield return StoryBackdropShots(Shot, Report);
            Report($"switching themes raises UiTheme.Changed ({changes})", changes >= 1);
            _campaignScreen.ThemeOverride = null;
            UiTheme.Current = UiTheme.MainStreet;
            Debug.Log("theme check: done");
            Application.Quit();
        }
    }
}
