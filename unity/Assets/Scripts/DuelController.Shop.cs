using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-shopcheck &lt;absolute folder&gt;` (with -mods &lt;empty folder&gt;): the card shop rework (SHOP-REWORK-SPEC-2026-11-01,
    /// chunk B), screenshotted step by step ("shop-&lt;n&gt;-&lt;what&gt;.png"): a fresh save's first visit (the plate, the
    /// default pick, the Beginner Circuit's shelf, the collapsed back shelves, the opening-stock line); the back shelves
    /// open (every set on sale once); a rarity set after forced levels; a house pack; a seeded pack with its tags; a seeded
    /// box; New arrivals coming and going; the Recipe Book from the shop; a bracket entry's Prize Pack odds and a Prize
    /// Pack's reveal; and the save matching the game. Reports "shop check: ok / FAIL" lines like the other checks.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator ShopCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"shop check: {(ok ? "ok  " : "FAIL")} {what}"); }
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"shop-{name}.png"));
                yield return null; // taken at the end of this frame
                yield return null;
            }
            // A visit from the campaign: closing the shop opens the campaign screen, which is closed again.
            System.Collections.IEnumerator Visit(float wait = 0.6f)
            {
                if (_shop.IsOpen) _shop.Close();
                _campaignScreen.Close();
                _shop.Open();
                yield return new WaitForSecondsRealtime(wait);
            }
            List<ShelfRow> Section(ShelfSection section) =>
                _shop.DebugRows.Where(r => r.Section == section && r.Kind == ShelfRowKind.Set).ToList();
            System.Collections.IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
#if ENABLE_INPUT_SYSTEM
            // The keyboard steps (1b, 5c, 8): a keyboard of the check's own, its keys pressed and let go a few frames apart.
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("CheckKeys");
            keyboard.MakeCurrent(); // the screens that read Keyboard.current (the menu) hear it too
            System.Collections.IEnumerator Press(UnityEngine.InputSystem.Key key)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                yield return Frames(3);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return Frames(3);
            }
            var Down = UnityEngine.InputSystem.Key.DownArrow;
            var Up = UnityEngine.InputSystem.Key.UpArrow;
            var Left = UnityEngine.InputSystem.Key.LeftArrow;
            var Right = UnityEngine.InputSystem.Key.RightArrow;
            var Enter = UnityEngine.InputSystem.Key.Enter;
#endif

            yield return new WaitForSecondsRealtime(0.5f);
            UiTheme.Current = UiTheme.MainStreet;
            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            var circuits = _campaign.Circuits;
            var catalog = _campaign.Shop;
            if (catalog == null || catalog.Sets.Count == 0)
            {
                Report("the shop has its card-set data (printings.bin)", false);
                Application.Quit(1);
                yield break;
            }
            bool Real(PackSet s) => !SelectionPacks.IsHousePack(s) && !ImportShelf.IsImport(s);
            // Forced levels: the EXP for each (the level is worked out from EXP).
            void AddLevels(int n)
            {
                for (int target = profile.Level + n; profile.Level < target;) profile.Exp += Leveling.ExpToNext(profile.Level);
            }

            // 1. A fresh save's first visit.
            yield return Visit();
            yield return Shot("1-fresh");
            Report($"the plate reads {_shop.DebugPlate}", _shop.DebugPlate == "GUS'S CARDS");
            Report($"the default pick is {_shop.DebugSelected?.Info.Code}", _shop.DebugSelected?.Info.Code == "DB2");
            var rows = _shop.DebugRows;
            var featured = Section(ShelfSection.Featured);
            var live = featured.Where(r => !r.Locked).Select(r => r.Set!.Info.Code).ToList();
            var soon = featured.Where(r => r.Locked).ToList();
            Report($"{rows.FirstOrDefault()?.Header}: {string.Join(", ", live)}, then {string.Join(", ", soon.Select(r => $"{r.Set!.Info.Code} (soon, level {r.OpensAtLevel})"))}",
                rows.FirstOrDefault()?.Header == "FOR THE BEGINNER CIRCUIT" && live.SequenceEqual(new[] { "DB2", "DB1", "DR1", "FET" })
                && soon.Count >= 1 && featured.SkipWhile(r => !r.Locked).All(r => r.Locked) && soon.All(r => r.OpensAtLevel > profile.Level));
            Report($"the back shelves are one collapsed row ({rows.FirstOrDefault(r => r.Kind == ShelfRowKind.BackToggle)?.Header})",
                rows.Count(r => r.Kind == ShelfRowKind.BackToggle) == 1 && !rows.Any(r => r.Section == ShelfSection.BackCatalog && r.Kind == ShelfRowKind.Set));
            Report($"the footer is the opening-stock line ({_shop.DebugStatus})", _shop.DebugStatus.Contains("from 2005 or before"));
            Report($"the shelf's line is under its header", _shop.DebugListText().Contains("Ryo built a whole deck"));
            var detail = _shop.DebugDetailText();
            Report("the pick's detail has its why, set progress, odds, box and condition lines",
                detail.Contains("WHY THIS SET") && detail.Contains("Finish it and Gus frames") && detail.Contains("ODDS PER PACK")
                && detail.Contains("Buy a box · 24 packs") && detail.Contains("A box of 24") && detail.Contains("Condition when pulled: Mint"));
            Report($"house packs: the Beginner Circuit's and the Rookie Cup's decks ({string.Join(", ", Section(ShelfSection.House).Select(r => r.Set!.Info.Name))})",
                Section(ShelfSection.House).Count >= 2 && Section(ShelfSection.House).All(r => r.Set!.Info.Name.StartsWith("House Pack:")));

#if ENABLE_INPUT_SYSTEM
            // 1b. The keyboard (overnight 2026-11-06): the arrows move the highlight through the shelves (the detail with it),
            // Enter clicks it (the back shelves' row opens them), and nothing the keys do buys anything.
            {
                int dpKeys = profile.Dp, packsKeys = profile.PacksOpened;
                var keyRows = _shop.DebugRows.ToList();
                bool Stop(int i) => keyRows[i].Kind == ShelfRowKind.Set || keyRows[i].Kind == ShelfRowKind.BackToggle;
                var from = _shop.DebugSelected;
                int at = keyRows.FindIndex(r => r.Kind == ShelfRowKind.Set && r.Set == from);
                int next = Enumerable.Range(at + 1, Mathf.Max(0, keyRows.Count - at - 1)).Where(Stop).DefaultIfEmpty(-1).First();
                yield return Press(Down);
                bool movedOk = next >= 0 && (keyRows[next].Kind == ShelfRowKind.BackToggle ? _shop.DebugBackSelected
                    : _shop.DebugSelected == keyRows[next].Set && _shop.DebugDetailText().Contains(keyRows[next].Set!.Info.Name));
                Report($"Down moves the highlight from {from?.Info.Code} to the next row ({(next >= 0 ? keyRows[next].Set?.Info.Code ?? keyRows[next].Header : "none")}), its detail with it",
                    movedOk);
                yield return Shot("1b-keys-down");
                yield return Press(Up);
                Report($"Up moves it back ({_shop.DebugSelected?.Info.Code}), the detail too", _shop.DebugSelected == from && from != null && _shop.DebugDetailText().Contains(from.Info.Name));
                int presses = 0;
                while (!_shop.DebugBackSelected && presses++ < keyRows.Count) yield return Press(Down);
                Report($"Down through the shelves reaches the back shelves' row ({presses} presses), its detail Gus's back-shelves panel",
                    _shop.DebugBackSelected && _shop.DebugDetailText().Contains("Back shelves"));
                yield return Press(Enter);
                bool backOpened = _shop.DebugRows.Any(r => r.Section == ShelfSection.BackCatalog && r.Kind == ShelfRowKind.Set);
                Report("Enter on that row opens the back shelves, as a click does", backOpened && _shop.DebugBackSelected);
                var openRows = _shop.DebugRows.ToList();
                int toggle = openRows.FindIndex(r => r.Kind == ShelfRowKind.BackToggle);
                var firstBack = openRows.Skip(toggle + 1).FirstOrDefault(r => r.Kind == ShelfRowKind.Set)?.Set;
                yield return Press(Down);
                Report($"Down from it goes into the back shelves ({_shop.DebugSelected?.Info.Code}, the first: {firstBack?.Info.Code})",
                    firstBack != null && _shop.DebugSelected == firstBack && _shop.DebugDetailText().Contains(firstBack.Info.Name));
                int page = _shop.DebugPage;
                yield return Press(Right);
                int pageRight = _shop.DebugPage;
                var pageFirst = openRows.Skip(pageRight * 11).Take(11).FirstOrDefault(r => r.Kind == ShelfRowKind.Set)?.Set;
                Report($"Right turns the page ({page + 1} -> {pageRight + 1}) and highlights its first set ({_shop.DebugSelected?.Info.Code})",
                    pageRight == page + 1 && pageFirst != null && _shop.DebugSelected == pageFirst && _shop.DebugDetailText().Contains(pageFirst.Info.Name));
                yield return Press(Left);
                Report($"Left turns it back ({_shop.DebugPage + 1})", _shop.DebugPage == page);

                // A text field has the keyboard: the arrows are the field's.
                var before = _shop.DebugSelected;
                _shop.DebugTypeInSearch(true);
                yield return Frames(3);
                bool typing = _shop.DebugTyping;
                yield return Press(Down);
                Report($"while the search field has the keyboard (focused: {typing}), Down leaves the highlight alone", typing && _shop.DebugSelected == before);
                _shop.DebugTypeInSearch(false);
                yield return Frames(3);

                // A Buy button the mouse clicked doesn't keep the UI's focus, so Enter can't buy a second time.
                _shop.DebugBack(false);
                _shop.DebugSelectCode("DB2");
                yield return Frames(2);
                bool focused = _shop.DebugFocusBuy();
                yield return Frames(3);
                var held = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.3f);
                var asked = _shop.DebugConfirm;
                Report($"a clicked Buy button lets go of the UI's focus ({(held == null ? "nothing" : held.name)} kept), and Enter on the highlighted set only asks (\"{StripTags(asked)}\"): nothing bought",
                    focused && held == null && asked.Contains("Buy a pack of") && profile.Dp == dpKeys && profile.PacksOpened == packsKeys && !_shop.DebugOpeningShown);
                yield return Shot("1b-keys-confirm");
                yield return Press(UnityEngine.InputSystem.Key.Escape);
                Report($"Escape cancels it ({(_shop.DebugConfirm.Length == 0 ? "closed" : "still up")}), nothing bought ({dpKeys} -> {profile.Dp} DP)",
                    _shop.DebugConfirm.Length == 0 && profile.Dp == dpKeys && profile.PacksOpened == packsKeys && !_shop.DebugOpeningShown);
                // Enter, then Enter again: one pack bought, as the "Buy a pack" button buys it (Buy: Profile.Spend).
                var keyPick = _shop.DebugSelected;
                yield return Press(Enter);
                bool askedAgain = _shop.DebugConfirm.Length > 0 && profile.Dp == dpKeys;
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.5f);
                Report($"Enter asks again ({askedAgain}) and a second Enter buys one pack ({dpKeys} -> {profile.Dp} DP, price {keyPick?.Price}; {packsKeys} -> {profile.PacksOpened} opened, the reveal up: {_shop.DebugOpeningShown})",
                    askedAgain && keyPick != null && profile.Dp == dpKeys - keyPick.Price && _shop.DebugOpeningShown && _shop.DebugConfirm.Length == 0);
                yield return _shop.DebugRevealAll();
                _shop.DebugBackToShop();
                yield return Frames(3);
                dpKeys = profile.Dp;
                packsKeys = profile.PacksOpened;

                // The Card Trader over the shop: its keys, not the shop's.
                var shopPick = _shop.DebugSelected;
                _trader.Open();
                var found = _trader.DebugSearch("dragon");
                yield return Frames(3);
                var traderFrom = _trader.DebugSelected;
                yield return Press(Down);
                Report($"with the Card Trader over the shop, Down moves the trader's highlight ({traderFrom} -> {_trader.DebugSelected}) and not the shop's",
                    found.Count > 1 && _trader.DebugSelected == found[1].Name && _shop.DebugSelected == shopPick);
                yield return Press(Right);
                Report($"Right turns the trader's page (now {_trader.DebugSelected}, the 13th card {(found.Count > 12 ? found[12].Name : "-")})",
                    found.Count > 12 && _trader.DebugSelected == found[12].Name);
                yield return Shot("1b-keys-trader");
                // Enter on the highlighted card asks first (a card that can't be traded for only shows why); Escape cancels.
                // (Two Card Tokens lent for the step, so there's a card to trade for.)
                int tokensKeys = profile.Tokens;
                profile.Tokens += 2;
                var tradeFor = _trader.DebugSelectTradable();
                yield return Frames(2);
                yield return Press(Enter);
                var tradeAsk = StripTags(_trader.DebugConfirm);
                yield return Press(UnityEngine.InputSystem.Key.Escape);
                Report($"Enter on the trader's card ({tradeFor}) asks (\"{tradeAsk}\"), Escape cancels: no token spent ({tokensKeys + 2} -> {profile.Tokens})",
                    tradeFor != null && tradeAsk.StartsWith("Trade ") && tradeAsk.Contains(tradeFor) && _trader.DebugConfirm.Length == 0 && profile.Tokens == tokensKeys + 2);
                profile.Tokens = tokensKeys;
                _trader.DebugSearch("");
                _trader.Close();
                yield return Frames(3);

                // The Grading Desk: its copies (Grade), and the used market's grid (Used).
                _gradingDesk.DebugShow(0);
                yield return Frames(3);
                yield return Press(Down);
                var first = _gradingDesk.DebugKeys.selected;
                yield return Press(Down);
                var second = _gradingDesk.DebugKeys.selected;
                Report($"at the Grading Desk, Down selects the copies in turn ({(first == null ? "-" : _cardPack!.Get(first.Code)?.Name)}, then {(second == null ? "-" : _cardPack!.Get(second.Code)?.Name)}), the detail with them",
                    first != null && second != null && first != second && _gradingDesk.DebugDetailText().Contains(StripTags(_cardPack!.Get(second.Code)?.Name ?? "?")));
                _gradingDesk.DebugShow(2);
                yield return Frames(3);
                var listings = _campaign.MarketListings();
                yield return Press(Right);
                int l0 = _gradingDesk.DebugKeys.listing;
                yield return Press(Right);
                int l1 = _gradingDesk.DebugKeys.listing;
                yield return Press(Down);
                int l7 = _gradingDesk.DebugKeys.listing;
                yield return Shot("1b-keys-market");
                Report($"the used market's grid: the first arrow lands on the first tile, Right the next, Down the one under it (slots {l0}, {l1}, {l7})",
                    listings.Count > 7 && l0 == listings[0].Slot && l1 == listings[1].Slot && l7 == listings[7].Slot
                    && _gradingDesk.DebugDetailText().Contains(StripTags(_cardPack!.Get(listings[7].Copy.Code)?.Name ?? "?"))); // the detail's text comes tag-stripped ("Maliss <P> March Hare")
                _gradingDesk.DebugShow(1);
                yield return Frames(3);
                yield return Press(Down);
                var focus = _gradingDesk.DebugKeys.focus;
                int picks = _gradingDesk.DebugKeys.picks;
                yield return Press(Enter);
                bool sellable = focus != null && SellDesk.Blocked(profile, focus, _campaign.NeededByDecks) == null;
                Report($"the Sell tab: Down highlights a copy ({(focus == null ? "-" : _cardPack!.Get(focus.Code)?.Name)}), Enter ticks it as a click would ({(sellable ? "for sale" : "not for sale")}: picks {picks} -> {_gradingDesk.DebugKeys.picks}) and sells nothing",
                    focus != null && _gradingDesk.DebugKeys.picks == picks + (sellable ? 1 : 0) && profile.Dp == dpKeys);
                _gradingDesk.Close();
                yield return Frames(3);

                // The Recipe Book from the shop.
                _shop.DebugRecipeBook();
                yield return new WaitForSecondsRealtime(0.3f);
                var deckFrom = _recipeBook.DebugSelectedDeck;
                yield return Press(Down);
                var deckDown = _recipeBook.DebugSelectedDeck;
                yield return Press(Up);
                Report($"the Recipe Book: Down and Up move between recipes ({deckFrom} -> {deckDown} -> {_recipeBook.DebugSelectedDeck}) and the shop under it stays put",
                    deckDown != deckFrom && _recipeBook.DebugSelectedDeck == deckFrom && _shop.DebugSelected?.Info.Code == "DB2");
                yield return Press(Right);
                var deckPage = _recipeBook.DebugSelectedDeck;
                int pageRows = _recipeBook.DebugSelectedIndex;
                yield return Press(Left);
                Report($"Right turns the Recipe Book's page ({deckFrom} -> {deckPage}, recipe {pageRows + 1}), Left turns it back ({_recipeBook.DebugSelectedDeck})",
                    pageRows > 1 && _recipeBook.DebugSelectedDeck == deckFrom);
                _recipeBook.Close();
                yield return new WaitForSecondsRealtime(0.3f);
                Report($"after the shop's screens, still nothing bought ({profile.Dp} DP)", profile.Dp == dpKeys && profile.PacksOpened == packsKeys);
            }
#endif

            // 2. The back shelves open: every real set on sale once across the sections.
            _shop.DebugBack(true);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("2-back-open");
            var shown = _shop.DebugRows.Where(r => r.Kind == ShelfRowKind.Set && !r.Locked && Real(r.Set!)).Select(r => r.Set!.Info.Code).ToList();
            int year = circuits.ShopYear(profile);
            var onSale = catalog.Sets.Where(s => int.TryParse(s.Year, out var y) && y <= year).Select(s => s.Info.Code).ToList();
            Report($"with the back shelves open, every real set on sale appears once ({shown.Count} rows, {onSale.Count} sets on sale up to {year})",
                shown.Count == onSale.Count && shown.Distinct().Count() == shown.Count && onSale.All(shown.Contains));
            var years = _shop.DebugRows.Where(r => r.Kind == ShelfRowKind.YearHeader).Select(r => r.Header).ToList();
            Report($"the back shelves go by year, newest first ({string.Join(", ", years.Take(4))}...)", years.Count > 0 && years.SequenceEqual(years.OrderByDescending(y => y)));
            _shop.DebugBack(false);

            // 3. A rarity set: Hidden Arsenal 2, after levels that open its year (the visit records those years as arrived).
            if (!profile.BandStartLevel.ContainsKey(circuits.BandEra(profile))) profile.BandStartLevel[circuits.BandEra(profile)] = profile.Level;
            var ha = catalog.Sets.FirstOrDefault(s => s.Info.Name == "Hidden Arsenal 2" && PackCatalog.IsRaritySet(s))
                     ?? catalog.Sets.Where(s => PackCatalog.IsRaritySet(s) && int.TryParse(s.Year, out var y) && y <= circuits.NextEra(profile)).OrderBy(s => s.Year).FirstOrDefault();
            if (ha != null && int.TryParse(ha.Year, out var haYear) && haYear > year) AddLevels(haYear - year);
            _campaign.Save();
            yield return Visit();
            var arrivalLine = _shop.DebugStatus;
            Report($"levels open the shop to {circuits.ShopYear(profile)}; the visit's footer is its arrival line ({arrivalLine})",
                arrivalLine.Contains(circuits.ShopYear(profile).ToString()));
            Report($"those years' sets are New arrivals ({string.Join(", ", Section(ShelfSection.NewArrivals).Select(r => $"{r.Set!.Info.Code} {r.Set.Year}"))})",
                Section(ShelfSection.NewArrivals).Count > 0 && Section(ShelfSection.NewArrivals).All(r => r.New && int.Parse(r.Set!.Year) > 2005));
            if (ha == null) Report("there's a rarity set to look at", false);
            else
            {
                Report($"{ha.Info.Name} ({ha.Info.Code}) is in the shop", _shop.DebugSelectCode(ha.Info.Code));
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("3-rarity-set");
                var baseRarity = PackCatalog.BaseRarity(ha);
                var odds = _shop.DebugOddsRows(ha);
                detail = _shop.DebugDetailText();
                Report($"its row has the RARITY SET chip, the detail its badge line, and the odds only above its base {baseRarity} ({string.Join("; ", odds)})",
                    _shop.DebugListText().Contains("RARITY SET") && detail.Contains("A rarity set: ") && odds.Count > 0
                    && odds.All(o => !o.StartsWith(Rarities.Name(baseRarity) + " ")) && (baseRarity >= Rarity.SecretRare || odds.Any(o => o.StartsWith("Secret Rare"))));
            }

            // 4. A house pack: the name, no year, the description, its odds.
            var house = Section(ShelfSection.House).FirstOrDefault()?.Set;
            if (house == null) Report("a house pack is on sale", false);
            else
            {
                _shop.DebugSelectCode(house.Info.Code);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("4-house-pack");
                detail = _shop.DebugDetailText();
                var deck = house.Info.Code.Substring(3);
                Report($"{house.Info.Name}: no year, \"{detail.Split('\n').Skip(1).FirstOrDefault()}\", {house.Price} DP, an odds block",
                    house.Info.Name.StartsWith("House Pack: ") && house.Info.Date.Length == 0 && detail.Contains("repack")
                    && house.Price == SelectionPacks.PriceFor(deck, circuits) && detail.Contains("ODDS PER PACK") && _shop.DebugOddsRows(house).Count > 0
                    && _shop.DebugListText().Contains("House Pack · "));
            }

            // 5. A seeded pack of DB1 with a wishlist card in it: a Wanted tag, a "plays this" tag, the summary's set line.
            var db1 = catalog.Find("DB1")!;
            var context = _campaign.Tags()!;
            int seed = -1;
            uint wish = 0;
            for (int s = 1; s <= 400 && seed < 0; s++)
            {
                var pulls = PackCatalog.Open(db1, new System.Random(s));
                bool Keeps(uint c) => profile.Collection.Count(c) + pulls.Count(p => p.Code == c) <= Collection.MaxCopies;
                var played = pulls.Where(p => Keeps(p.Code) && PackTags.For(p.Code, context).PlayedBy.Count > 0).ToList();
                var plain = pulls.Where(p => Keeps(p.Code) && PackTags.For(p.Code, context).PlayedBy.Count == 0 && played.All(q => q.Code != p.Code)).ToList();
                if (played.Count > 0 && plain.Count > 0) { seed = s; wish = plain[0].Code; }
            }
            if (seed < 0) Report("a DB1 seed with a card someone plays and one nobody does", false);
            else
            {
                profile.Wishlist[wish] = Collection.MaxCopies;
                profile.WishlistFor = profile.Deck;
                _campaign.Save();
                _shop.Open(visit: false);
                var before = _campaign.SetProgress(db1);
                yield return _shop.DebugSeeded("DB1", seed, box: false);
                yield return _shop.DebugRevealAll();
                yield return Shot("5-pack");
                var tags = _shop.DebugTagLines();
                var now = _campaign.SetProgress(db1);
                var summary = _shop.DebugSummary;
                Report($"a seeded DB1 pack (seed {seed}, {_cardPack!.Get(wish)?.Name} wished for) tags its kept cards: {string.Join("; ", tags)}",
                    tags.Any(t => t.StartsWith("Wanted for")) && tags.Any(t => t.EndsWith(" plays this") || t.EndsWith(" play this")));
                Report($"its summary has the set line ({summary})", summary.Contains($"Set {now.owned}/{now.total}") && (now.owned == before.owned || summary.Contains($"(+{now.owned - before.owned})")));
                Report($"its line under the summary: \"{_shop.DebugVoice}\"", true);
                var (nameToTag, room) = _shop.DebugCaptions();
                Report($"each tag line sits under its card's name ({nameToTag:0} apart) with room above the next row and the summary ({room:0})", nameToTag <= 4 && room >= 12);
                _shop.DebugBackToShop();
            }

            // 5b. Card versions (phase 2): a seeded DB1 pack with a card printing its original text (an old set): the reveal tags
            // it "ORIGINAL TEXT · <year>" and the copy kept carries the text and its printing.
            if (_campaign.Printings is Ygo.Engine.Data.PrintingIndex printings)
            {
                int origSeed = -1;
                uint origCode = 0;
                for (int s = 1; s <= 400 && origSeed < 0; s++)
                    foreach (var pull in PackCatalog.Open(db1, new System.Random(s)))
                    {
                        var (setCode, printed) = Profile.PackPrint(db1, pull, printings);
                        if (setCode.Length > 0 && _campaign.Texts.PrintedText(pull.Code, printed) != 0 && profile.Collection.Owned(pull.Code) == 0) { origSeed = s; origCode = pull.Code; break; }
                    }
                if (origSeed < 0) Report("a DB1 seed with a pull printing an original text", false);
                else
                {
                    _shop.Open(visit: false);
                    yield return _shop.DebugSeeded("DB1", origSeed, box: false);
                    yield return _shop.DebugRevealAll();
                    yield return Shot("5b-original-pull");
                    var (origTags, originals) = _shop.DebugOriginalTags;
                    // The side panel shows the last card turned over: the original pull's text when it's that one.
                    var panel = _shop.DebugCardInfo;
                    var copy = profile.Collection.CopiesOf(origCode).FirstOrDefault();
                    var origInfo = copy == null ? null : _cardPack!.Get(copy.Text);
                    Report($"a seeded DB1 pack (seed {origSeed}) with {_cardPack!.Get(origCode)?.Name}: the reveal tags {origTags} pull(s) ORIGINAL TEXT ({originals} kept copies carry one); "
                           + $"the copy is {copy?.Set} {copy?.Printed}, text {copy?.Text} ({origInfo?.Name}); the side panel {(panel.Contains(origInfo?.Name ?? "-") ? "reads its original" : "shows another card")}",
                        origTags >= 1 && origTags == originals && copy != null && CardDetail.CarriesOriginal(copy) && copy.Set.StartsWith("DB1") && db1.Info.Date.StartsWith(copy.Printed));
                    _shop.DebugBackToShop();
                }
            }

#if ENABLE_INPUT_SYSTEM
            // 5c. The keyboard on a pack's reveal: the first arrow shows the highlight on the first card, the arrows move it
            // along a row and between the rows (staying put at an edge), Enter turns the highlighted card over, the side panel
            // reads the highlighted card, and after Reveal all the highlight goes with the card the panel reads. Nothing bought.
            {
                _shop.Open(visit: false);
                yield return _shop.DebugSeeded("DB1", 3, box: false);
                int dpPack = profile.Dp, packsPack = profile.PacksOpened;
                var h = _shop.DebugHighlight;
                Report($"a fresh pack has no keyboard highlight (cursor {h.cursor}, frame {h.framed})", h.cursor == -1 && !h.framed && h.revealed.All(r => !r));
                yield return Press(Right);
                h = _shop.DebugHighlight;
                Report($"the first arrow shows it on the first card (cursor {h.cursor}, frame {h.framed}), the panel saying it's face down ({_shop.DebugCardInfo.Split('\n')[0]})",
                    h.cursor == 0 && h.framed && _shop.DebugCardInfo.Contains("Card 1 of") && _shop.DebugCardInfo.Contains("Face down"));
                yield return Press(Right);
                h = _shop.DebugHighlight;
                Report($"Right moves it along the row (cursor {h.cursor})", h.cursor == 1 && _shop.DebugCardInfo.Contains("Card 2 of"));
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(1.2f);
                h = _shop.DebugHighlight;
                Report($"Enter turns the highlighted card over ({_shop.DebugPackName(1)}) and the panel reads it",
                    h.revealed[1] && h.revealed.Count(r => r) == 1 && h.cursor == 1 && _shop.DebugCardInfo.Contains(_shop.DebugPackName(1)));
                yield return Press(Down);
                int down = _shop.DebugHighlight.cursor;
                Report($"Down goes to the second row (cursor {down}), the panel with it", down >= 5 && _shop.DebugCardInfo.Contains($"Card {down + 1} of"));
                yield return Press(Down);
                Report($"Down on the last row stays put (cursor {_shop.DebugHighlight.cursor})", _shop.DebugHighlight.cursor == down);
                yield return Press(Up);
                int up = _shop.DebugHighlight.cursor;
                Report($"Up goes back to the first row (cursor {up})", up >= 0 && up < 5);
                yield return Press(Left);
                yield return Press(Left);
                yield return Press(Left);
                Report($"Left along the row stops at its first card (cursor {_shop.DebugHighlight.cursor})", _shop.DebugHighlight.cursor == 0);
                // The menu over the reveal: the Enter that picks its first item (closing it) doesn't also turn the
                // highlighted card over, whichever screen reads the key first.
                int faceUp = _shop.DebugHighlight.revealed.Count(r => r);
                _menu.Open();
                yield return Frames(3);
                bool menuUp = _menu.IsOpen;
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.6f);
                h = _shop.DebugHighlight;
                Report($"with the menu over the reveal, Enter picks the menu's item and closes it (open before {menuUp}, after {_menu.IsOpen}) and turns no card over (face up {faceUp} -> {h.revealed.Count(r => r)}, card {h.cursor + 1} {(h.cursor >= 0 && h.revealed[h.cursor] ? "face up" : "face down")})",
                    menuUp && !_menu.IsOpen && h.revealed.Count(r => r) == faceUp && h.cursor == 0 && !h.revealed[0] && _shop.DebugOpeningShown);
                _campaignScreen.Close(); // the menu's first item opened it, under the shop
                yield return Frames(3);
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(1.2f);
                Report($"and the next Enter is the reveal's again (card 1 {(_shop.DebugHighlight.revealed[0] ? "turned over" : "still face down")})", _shop.DebugHighlight.revealed[0]);
                yield return Shot("5c-pack-keys");
                yield return _shop.DebugRevealAll();
                h = _shop.DebugHighlight;
                Report($"after Reveal all the highlight is on the card the panel reads (cursor {h.cursor}: {(h.cursor >= 0 ? _shop.DebugPackName(h.cursor) : "-")})",
                    h.cursor >= 0 && h.revealed.All(r => r) && _shop.DebugCardInfo.Contains(_shop.DebugPackName(h.cursor)));
                int lastAt = h.cursor;
                yield return Press(lastAt == 0 || lastAt == 5 ? Right : Left); // not off a row's first card
                h = _shop.DebugHighlight;
                Report($"face up, an arrow moves it (cursor {lastAt} -> {h.cursor}) and the panel reads that card ({_shop.DebugPackName(h.cursor)})",
                    h.cursor != lastAt && _shop.DebugCardInfo.Contains(_shop.DebugPackName(h.cursor)));
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.3f);
                Report($"Enter on a face-up card does nothing, and no key bought a pack ({dpPack} -> {profile.Dp} DP, packs opened {packsPack} -> {profile.PacksOpened})",
                    profile.Dp == dpPack && profile.PacksOpened == packsPack);
                yield return Shot("5c-pack-keys-revealed");
                _shop.DebugBackToShop();
            }
#endif

            // Buy 5 packs, open one, and go Back to shop: the other four are paid for, so they go straight into the collection.
            {
                int opened5 = profile.PacksOpened, total5 = profile.Collection.Total;
                bool bought = _shop.DebugBuyPacks("DB1", 5);
                yield return new WaitForSecondsRealtime(0.5f);
                int left = _shop.DebugPacksLeft;
                _shop.DebugBackToShop();
                yield return new WaitForSecondsRealtime(0.3f);
                var status = _shop.DebugStatus;
                Report($"Back to shop with {left} paid packs left opens them into the collection ({status})",
                    bought && left == 4 && _shop.DebugPacksLeft == 0 && profile.PacksOpened == opened5 + 5 && profile.Collection.Total > total5
                    && status.StartsWith("Opened the other 4 packs of Dark Beginning 1 into your collection"));
                var savedPacks = Profile.Load(_campaign.SavePath);
                Report("and the save has them", savedPacks != null && savedPacks.PacksOpened == profile.PacksOpened && savedPacks.Collection.Total == profile.Collection.Total);
            }

            // 6. A seeded box of DB2 (a featured set): the DP, the collection, the promise, the packs opened.
            var db2 = catalog.Find("DB2")!;
            int price = ShopPrices.Box(db2);
            DebugAddDp(profile, price);
            _campaign.Save();
            int dp = profile.Dp, total = profile.Collection.Total, opened = profile.PacksOpened;
            yield return _shop.DebugSeeded("DB2", 7, box: true);
            yield return Shot("6-box-summary");
            var box = _shop.DebugBox;
            if (box == null || !box.Opened) Report($"a box of DB2 ({price} DP) opened", false);
            else
            {
                int kept = box.Pulls.Count(p => p.added.Kept && p.added.Replaced == null) + _shop.DebugUnlockCopies;
                var promise = ShopPrices.BoxTop(db2);
                Report($"a box of DB2 costs {price:N0} DP less its {box.Refund} DP refund ({dp} -> {profile.Dp})", profile.Dp == dp - price + box.Refund && box.Refund <= price / 2);
                Report($"its {box.Pulls.Count} cards are in the collection ({kept} copies kept: {total} -> {profile.Collection.Total})",
                    profile.Collection.Total == total + kept && box.Pulls.Count == ShopPrices.BoxPacks * PackCatalog.PackSize);
                Report($"its promise is a {promise}: {_cardPack!.Get(box.Topper.Code)?.Name} ({box.Topper.Rarity}{(box.Upgraded ? ", upgraded" : "")})",
                    promise == null || box.Topper.Rarity == promise);
                Report($"it counts {ShopPrices.BoxPacks} packs opened ({opened} -> {profile.PacksOpened})", profile.PacksOpened == opened + ShopPrices.BoxPacks);
                // The summary's parts add up: kept, upgrades and extra copies sold are every card of the box.
                int boxKept = box.Pulls.Count(p => p.added.Kept && p.added.Replaced == null), boxUp = box.Pulls.Count(p => p.added.Kept && p.added.Replaced != null);
                int boxSold = box.Pulls.Count(p => !p.added.Kept);
                var boxSummary = _shop.DebugSummary;
                Report($"the box summary adds up ({boxSummary})", boxKept + boxUp + boxSold == box.Pulls.Count && boxSummary.Contains($"{boxKept} copies kept")
                    && (boxUp == 0 || boxSummary.Contains($"{boxUp} upgrade")) && (boxSold == 0 || boxSummary.Contains($"{boxSold} extra cop")));
                var shownBox = _shop.DebugPack;
                Report($"the box shows its {shownBox.Count} best pulls face down, the promise last, and Gus's box line (\"{_shop.DebugVoice}\")",
                    shownBox.Count == 10 && shownBox[shownBox.Count - 1].code == box.Topper.Code && _shop.DebugVoice.Length > 0);
                yield return _shop.DebugRevealAll();
                yield return Shot("6-box-revealed");
                var (boxGap, boxRoom) = _shop.DebugCaptions();
                var (boxButtons, boxOff) = _shop.DebugButtons();
                Report($"the box's captions sit under their names ({boxGap:0} apart, {boxRoom:0} clear), and its one button ({string.Join(", ", boxButtons)}) is centered ({boxOff:0} off)",
                    boxGap <= 4 && boxRoom >= 12 && boxButtons.Count == 1 && boxOff <= 1);
                Report($"turning the promise over plays box-best (\"{_shop.DebugVoice}\")", _shop.DebugVoice.Contains("the box promised"));
                var saved = Profile.Load(_campaign.SavePath);
                Report($"the box is saved ({saved?.Dp} DP, {saved?.Collection.Total} cards)", saved != null && saved.Dp == profile.Dp && saved.Collection.Total == profile.Collection.Total);
            }
            _shop.DebugBackToShop();

            // 7. A forced level-up: that year's sets arrive with its line; three visits later they're not new, and Gus makes small talk.
            int yearBefore = circuits.ShopYear(profile);
            AddLevels(1);
            _campaign.Save();
            int newYear = circuits.ShopYear(profile);
            yield return Visit();
            yield return Shot("7-arrivals");
            var arrived = _shop.DebugStatus;
            var fresh = Section(ShelfSection.NewArrivals);
            Report($"level {profile.Level} opens {newYear} (from {yearBefore}): NEW ARRIVALS leads with its sets ({string.Join(", ", fresh.Select(r => $"{r.Set!.Info.Code} {r.Set.Year}"))})",
                newYear > yearBefore && _shop.DebugRows.Any(r => r.Kind == ShelfRowKind.Header && r.Header == ShopShelves.NewHeader)
                && fresh.Any(r => r.Set!.Year == newYear.ToString()) && fresh.Count <= Arrivals.MaxRows);
            Report($"and the footer is {newYear}'s arrival line ({arrived})", arrived.Contains(newYear.ToString()));
            for (int v = 0; v < Arrivals.Visits; v++) yield return Visit(0.3f);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("7-arrivals-gone");
            Report($"{Arrivals.Visits} visits later nothing is new ({Section(ShelfSection.NewArrivals).Count} rows) and the footer is a footer line ({_shop.DebugStatus})",
                Section(ShelfSection.NewArrivals).Count == 0 && !_shop.DebugRows.Any(r => r.Header == ShopShelves.NewHeader)
                && _shop.DebugStatus.Length > 0 && _shop.DebugStatus != arrived && !_shop.DebugStatus.Contains(newYear.ToString()));

            // The Recipe Book from the shop's header, and back to the shop.
            _shop.DebugRecipeBook();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("7-recipe-book");
            bool bookOpen = _recipeBook.IsOpen;
            _recipeBook.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            Report("the shop's Recipe Book button opens the Recipe Book, and closing it goes back to the shop", bookOpen && _shop.IsOpen && !_campaignScreen.IsOpen);

            // Build from the shop's Recipe Book (a recipe unlocked for it): the editor shows with nothing over it, and closing
            // it goes back to the shop.
            var unlockMatch = circuits.All[0].Matches[0];
            var unlockKey = Profile.MatchKey(0, 0);
            var deckBefore = profile.Deck;
            profile.MatchWins[unlockKey] = 1;
            _campaign.Save();
            _shop.DebugRecipeBook();
            yield return new WaitForSecondsRealtime(0.4f);
            bool unlocked = _recipeBook.DebugSelect(unlockMatch.Deck);
            _recipeBook.DebugBuild();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("7-recipe-build");
            Report($"Build from the shop's Recipe Book ({unlockMatch.Deck}, built as {profile.Deck}) shows the editor with the shop and the book out of the way",
                unlocked && _editor.IsOpen && _editor.CurrentDeckName == profile.Deck && !_shop.IsOpen && !_recipeBook.IsOpen);
            _editor.Close();
            yield return new WaitForSecondsRealtime(0.4f);
            Report("closing that editor goes back to the shop, not the campaign", _shop.IsOpen && !_editor.IsOpen && !_campaignScreen.IsOpen);
            profile.Deck = deckBefore;
            profile.MatchWins.Remove(unlockKey);
            _campaign.Save();

            // 8. A bracket entry: the Prize Pack odds before the entry is paid; a Prize Pack's reveal (its slab odds, Hollis's line).
            _shop.Close();
            _campaignScreen.Close();
            var lib = _campaign.Library;
            profile.LibraryOpen = true;
            for (int m = 0; m < lib.Cups[0].Matches.Count; m++) profile.LibraryWins[lib.Key(0, m)] = 1;
            DebugAtLeastDp(profile, Brackets.LocalsEntry + 500);
            _campaign.Save();
            _bracketScreen.ShowEntry(0);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("8-prize-odds");
            var entry = _bracketScreen.DebugEntryText;
            var oddsLines = entry.Split('\n').ToList();
            int mixAt = oddsLines.FindIndex(t => t.Contains("cards from the field's decks"));
            var mix = mixAt >= 0 && mixAt + 1 < oddsLines.Count ? oddsLines[mixAt + 1].Replace('\u00A0', ' ') : "none";
            Report($"the bracket entry shows the Prize Pack odds ({mix}), each rarity and its share held together",
                mixAt >= 0 && mix.Contains("%") && entry.Contains($"Grade 10 {PrizePacks.SlabTenPercent}%") && oddsLines[mixAt + 1].Contains("Rare\u00A0"));
            _bracketScreen.ControllerBack();
            if (_campaign.Prizes is PrizePacks prizes)
            {
                var b = Brackets.Create(lib, 0, _campaign.Ratings, 0x5E0Bu, profile.Deck);
                var pack = PrizePacks.RevealOrder(prizes.Give(profile, b, lib.Cups[0].Pool, 0)).ToList();
                _campaign.Save();
                bool done = false;
                _shop.OpenPrizes($"{b.Name} · Prize Pack", new[] { pack }, () => done = true, b.Seed);
#if ENABLE_INPUT_SYSTEM
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(1.2f);
                var ph = _shop.DebugHighlight;
                Report($"on a Prize Pack, Enter turns the first card over ({_shop.DebugPackName(0)}) and the panel reads it",
                    ph.cursor == 0 && ph.revealed[0] && ph.revealed.Count(r => r) == 1 && _shop.DebugCardInfo.Contains(_shop.DebugPackName(0)));
#endif
                yield return _shop.DebugRevealAll();
                yield return Shot("8-prize-pack");
                var text = _shop.DebugOpeningText();
                Report($"a Prize Pack's subtitle gives its slab's odds, and Hollis speaks over the slab (\"{_shop.DebugVoice}\")",
                    text.Contains($"Prize Pack 1 of 1 · slab: Grade 9 {100 - PrizePacks.SlabTenPercent}%, Grade 10 {PrizePacks.SlabTenPercent}%") && _shop.DebugVoice.Length > 0);
                var (prizeButtons, prizeOff) = _shop.DebugButtons();
                Report($"face up, its summary says what it gave ({_shop.DebugSummary}), and Done is centered ({prizeOff:0} off)",
                    !_shop.DebugSummary.Contains("Click a card") && _shop.DebugSummary.Contains("sealed slab") && prizeButtons.SequenceEqual(new[] { "Done" }) && prizeOff <= 1);
                _shop.DebugFinishPrizes();
                Report("Done finishes the Prize Packs", done);
            }

#if ENABLE_INPUT_SYSTEM
            // 10. Moth's van's keys (2026-11-06b, confirms 2026-11-06c): the arrows move the highlight over the Shelf's slabs and
            // the Used tab, picking what they land on (its detail with it); Enter asks "Buy X for N DP?", Escape cancels, a second
            // Enter buys (through the Buy button's path). On the Sell tab they go up and down the copies and Enter asks "Sell X?".
            // One press never buys or sells, a clicked (armed) Buy included.
            {
                if (_shop.IsOpen) _shop.Close();
                _campaignScreen.Close();
                var slots = _van.DebugShelfSlots;
                DebugAtLeastDp(profile, 2_000_000); // so the Buy button is there to be armed
                _campaign.Save();
                _van.Open();
                _van.DebugShow(0);
                yield return new WaitForSecondsRealtime(0.5f);
                int dpVan = profile.Dp, cardsVan = profile.Collection.Total;
                yield return Press(Down);
                var k0 = _van.DebugKeys;
                Report($"the van's Shelf: the first arrow highlights the first slab and picks it (slot {k0.shelfSlot} of {string.Join(",", slots)}, frame {k0.framed})",
                    slots.Count > 0 && k0.cursor == 0 && k0.framed && k0.shelfSlot == slots[0]);
                yield return Press(Right);
                var k1 = _van.DebugKeys;
                yield return Press(Down);
                var k2 = _van.DebugKeys;
                yield return Shot("10-van-keys");
                Report($"Right picks the next slab (slot {k1.shelfSlot}), Down the one under it (slot {k2.shelfSlot}), the detail with each",
                    slots.Count >= 5 && k1.shelfSlot == slots[1] && k2.shelfSlot == slots[4] && k2.framed);
                yield return Press(Enter);
                var vanAsk = StripTags(_van.DebugConfirm);
                yield return Shot("10-van-confirm");
                yield return Press(UnityEngine.InputSystem.Key.Escape);
                Report($"Enter on a slab only asks (\"{vanAsk}\"), Escape cancels ({(_van.DebugConfirm.Length == 0 ? "closed" : "still up")}): nothing bought ({dpVan} -> {profile.Dp} DP)",
                    vanAsk.StartsWith("Buy ") && vanAsk.Contains(" DP?") && _van.DebugConfirm.Length == 0 && profile.Dp == dpVan && profile.Collection.Total == cardsVan);
                bool armed = _van.DebugArmBuy();
                yield return Frames(3);
                var heldVan = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.2f);
                bool askedNotBought = _van.DebugConfirm.Length > 0 && profile.Dp == dpVan;
                yield return Press(UnityEngine.InputSystem.Key.Escape);
                Report($"after a click armed its Buy, Enter still only asks ({(armed ? "armed" : "no Buy")}; {(heldVan == null ? "no button" : heldVan.name)} kept the focus; {dpVan} -> {profile.Dp} DP)",
                    armed && heldVan == null && askedNotBought && profile.Dp == dpVan && profile.Collection.Total == cardsVan);
                // Enter, Enter: the slab bought (MothsVan.Buy, as Buy's second click), at its price.
                int slabPrice = _campaign.VanShelf().FirstOrDefault(l => l.Slot == slots[4])?.Price ?? -1;
                yield return Press(Enter);
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.2f);
                Report($"Enter, then Enter again, buys the slab ({dpVan} -> {profile.Dp} DP at {slabPrice:N0}; {cardsVan} -> {profile.Collection.Total} cards)",
                    slabPrice > 0 && profile.Dp == dpVan - slabPrice && profile.Collection.Total >= cardsVan && _van.DebugConfirm.Length == 0);
                dpVan = profile.Dp;
                cardsVan = profile.Collection.Total;
                _van.DebugShow(1);
                yield return Frames(2);
                yield return Press(Down);
                var ku = _van.DebugKeys;
                Report($"the Used tab: the first arrow highlights its first row (cursor {ku.cursor}, frame {ku.framed}; used slot {ku.usedSlot}, rail {ku.rail})",
                    ku.cursor == 0 && ku.framed);
                _van.DebugShow(2);
                yield return Frames(2);
                yield return Press(Down);
                var s0 = _van.DebugKeys;
                yield return Press(Down);
                var s1 = _van.DebugKeys;
                // The first copy down the list Moth would buy (the first two may be gifts).
                int guard = 0;
                while (s1.copy != null && SellDesk.Blocked(profile, s1.copy, _campaign.NeededByDecks) != null && guard++ < 10) { yield return Press(Down); s1 = _van.DebugKeys; }
                var copy = s1.copy;
                int pays = copy != null ? profile.Collection.SellPrice(copy) : 0;
                yield return Press(Enter);
                var sellAsk = StripTags(_van.DebugConfirm);
                yield return Shot("10-van-sell-confirm");
                yield return Press(UnityEngine.InputSystem.Key.Escape);
                bool sellKept = profile.Dp == dpVan && profile.Collection.Total == cardsVan;
                yield return Press(Enter);
                yield return Press(Enter);
                yield return new WaitForSecondsRealtime(0.2f);
                Report($"the Sell tab: Down, Down highlight the first copies (cursor {s0.cursor}, then {s1.cursor}); Enter asks (\"{sellAsk}\"), Escape keeps it, "
                       + $"Enter, Enter sells it ({dpVan} -> {profile.Dp} DP, pays {pays}; {cardsVan} -> {profile.Collection.Total} cards)",
                    s0.cursor == 0 && s1.cursor >= 1 && copy != null && sellAsk.StartsWith("Sell ") && sellKept
                    && profile.Dp == dpVan + pays && profile.Collection.Total == cardsVan - 1 && _van.DebugConfirm.Length == 0);
                dpVan = profile.Dp;
                cardsVan = profile.Collection.Total;
                var s2 = _van.DebugKeys;
                int pageBefore = s2.page;
                yield return Press(Right);
                var s3 = _van.DebugKeys;
                Report($"Right turns the page ({pageBefore + 1} -> {s3.page + 1}) and highlights its first copy (cursor {s3.cursor})",
                    profile.Collection.Total <= 10 || s3.page == pageBefore + 1 && s3.cursor == (pageBefore + 1) * 10);
                // The controller: the d-pad drives the same highlight, A asks as Enter does, B cancels (and doesn't leave the van).
                var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("CheckVanPad");
                pad.MakeCurrent();
                System.Collections.IEnumerator Pad(UnityEngine.InputSystem.LowLevel.GamepadButton button)
                {
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(button));
                    yield return Frames(3);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                    yield return Frames(3);
                }
                int padFrom = _van.DebugKeys.cursor;
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                int padTo = _van.DebugKeys.cursor;
                bool highlight = ControllerSupport.HighlightMode;
                // On to a copy Moth would buy, so A has something to ask about.
                for (int g = 0; g < 10 && _van.DebugKeys.copy is CardCopy c && SellDesk.Blocked(profile, c, _campaign.NeededByDecks) != null; g++)
                    yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                var padAsk = StripTags(_van.DebugConfirm);
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
                Report($"with a controller the d-pad moves the same highlight (cursor {padFrom} -> {padTo}, highlight mode {highlight}), A asks (\"{padAsk}\"), "
                       + $"B cancels and the van stays open ({_van.IsOpen}; {dpVan} -> {profile.Dp} DP)",
                    padTo == padFrom + 1 && highlight && padAsk.StartsWith("Sell ") && _van.DebugConfirm.Length == 0 && _van.IsOpen
                    && profile.Dp == dpVan && profile.Collection.Total == cardsVan);
                // A again: asked, then A confirms (the copy sold, through the Sell button's path).
                for (int g = 0; g < 10 && _van.DebugKeys.copy is CardCopy c && SellDesk.Blocked(profile, c, _campaign.NeededByDecks) != null; g++)
                    yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                var padCopy = _van.DebugKeys.copy;
                int padPays = padCopy != null ? profile.Collection.SellPrice(padCopy) : 0;
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                bool padAsked = _van.DebugConfirm.Length > 0 && profile.Dp == dpVan;
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                yield return Frames(2);
                Report($"A asks ({padAsked}) and a second A sells ({dpVan} -> {profile.Dp} DP, pays {padPays}; {cardsVan} -> {profile.Collection.Total} cards)",
                    padAsked && padCopy != null && profile.Dp == dpVan + padPays && profile.Collection.Total == cardsVan - 1 && _van.DebugConfirm.Length == 0);
                dpVan = profile.Dp;
                cardsVan = profile.Collection.Total;
                // Aimed with the stick, A on Cancel cancels (it's a click on the button, never a yes).
                for (int g = 0; g < 10 && _van.DebugKeys.copy is CardCopy c && SellDesk.Blocked(profile, c, _campaign.NeededByDecks) != null; g++)
                    yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                bool askedForCancel = _van.DebugConfirm.Length > 0;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = new Vector2(0.6f, 0) });
                yield return Frames(2);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return Frames(2);
                bool stickMode = !ControllerSupport.HighlightMode;
                var cancelAt = _van.DebugConfirmButton(false);
                if (cancelAt is Vector2 cancelPoint) _controller.DebugMoveTo(cancelPoint);
                yield return Frames(3);
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                yield return Frames(2);
                Report($"with the stick's cursor on Cancel (highlight off: {stickMode}), A cancels: the question closed ({_van.DebugConfirm.Length == 0}), nothing sold ({dpVan} -> {profile.Dp} DP)",
                    askedForCancel && stickMode && cancelAt != null && _van.DebugConfirm.Length == 0 && profile.Dp == dpVan && profile.Collection.Total == cardsVan);
                // Holding A down for a while in highlight mode: one question, never a buy or a click.
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                for (int g = 0; g < 10 && _van.DebugKeys.copy is CardCopy c && SellDesk.Blocked(profile, c, _campaign.NeededByDecks) != null; g++)
                    yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadDown);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
                var heldDp = new List<int>();
                for (int f = 0; f < 12; f++) { yield return null; heldDp.Add(profile.Dp); }
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                yield return Frames(4);
                bool stillAsking = _van.DebugConfirm.Length > 0;
                Report($"A held for 12 frames in highlight mode asks once and does nothing else (the question up: {stillAsking}; DP each frame {string.Join(",", heldDp.Distinct())}, now {profile.Dp})",
                    stillAsking && heldDp.All(d => d == dpVan) && profile.Dp == dpVan && profile.Collection.Total == cardsVan);
                yield return Pad(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
                Report($"and B cancels it ({_van.DebugConfirm.Length == 0}), the van still open ({_van.IsOpen})", _van.DebugConfirm.Length == 0 && _van.IsOpen && profile.Dp == dpVan);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                keyboard.MakeCurrent();
                _van.Close();
                yield return Frames(2);
                _campaignScreen.Close();
                UiTheme.Current = UiTheme.MainStreet;
            }
#endif

            // 9. The save after the check: the same DP, cards and shop memory as the game.
            var final = Profile.Load(_campaign.SavePath);
            Report($"the save matches the game: {final?.Dp} DP, {final?.Collection.Total} cards, {final?.ShopVisits} visits, shop year seen {final?.ShopYearSeen}, "
                   + $"{final?.Arrivals.Count} arrivals, {final?.VoiceCounts.Count} voice counts",
                final != null && final.Dp == profile.Dp && final.Collection.Total == profile.Collection.Total && final.PacksOpened == profile.PacksOpened
                && final.ShopVisits == profile.ShopVisits && final.ShopYearSeen == profile.ShopYearSeen
                && final.Arrivals.OrderBy(a => a.Key).SequenceEqual(profile.Arrivals.OrderBy(a => a.Key))
                && final.VoiceCounts.OrderBy(v => v.Key).SequenceEqual(profile.VoiceCounts.OrderBy(v => v.Key))
                && profile.Seen.All(final.Seen.Contains));
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
#endif
            Debug.Log($"shop check: done ({bad} FAIL)");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
