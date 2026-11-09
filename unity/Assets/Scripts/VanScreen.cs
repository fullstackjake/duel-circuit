using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Moth's van, Under the Counter (docs/dev/ALLEY-SPEC-2026-11-01.md section 10), opened from the alley map's van node.
    /// Theme-native: built on <see cref="UiTheme.Current"/> (the BackAlley theme there), the shop's layout (a list on the
    /// left, the detail on the right), "UNDER THE COUNTER" on the plate in <see cref="UiTheme.Sign"/> under the van's
    /// sign in <see cref="UiTheme.NeonCyan"/>, Moth's portrait slot (the hooded silhouette until a portrait exists), the
    /// whole screen framed in brick. Three tabs:
    /// <list type="bullet">
    /// <item>Shelf: six slabs of contraband (<see cref="MothsVan.Shelf"/>), each authenticated by the Loupe, at 1.5× value.</item>
    /// <item>Used &amp; graded: your cards on Moth's rail first (buy one back at its tag before its clock runs out), then the
    /// used contraband at its value, and at the end Ines's card, HELD, face down and not for sale.</item>
    /// <item>Sell: the Sell desk exactly (<see cref="MothsVan.Sell"/>): a quarter of a copy's value, never more than half
    /// of what you paid; won and gifted copies pay nothing.</item>
    /// </list>
    /// Moth speaks on the status line (campaign/van.txt). Purchases and sales arm on the first click and go through on the
    /// second, as at the Grading Desk. Contraband Packs aren't sold here: they're bracket prizes. The keyboard's arrows move
    /// a highlight as in the shop (<see cref="ArrowNav"/>); Enter clicks it and never buys or sells.
    /// </summary>
    public sealed class VanScreen : MonoBehaviour
    {
        private enum Tab { Shelf, Used, Sell }
        private const int PerPage = 10;

        private CampaignState _state = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _list = null!, _detail = null!, _tabs = null!;
        private Text _dp = null!, _status = null!, _pageLabel = null!, _sign = null!, _plate = null!;
        private GameObject _searchBox = null!, _prev = null!, _next = null!;
        private UiTheme? _builtTheme;
        private Tab _tab = Tab.Shelf;
        private int _page;
        private string _search = "";
        private Listing? _shelfPick, _usedPick;
        private RailEntry? _railPick;
        private readonly HashSet<CardCopy> _picks = new HashSet<CardCopy>();
        private string? _armed;
        private float _armedAt;

        public event Action? Closed;
        /// <summary>Story events the van raises ("buyback"), with their placeholders; the controller plays them over the van.</summary>
        public event Action<string[], Dictionary<string, string>>? StoryEvents;

        /// <summary>-themecheck only: draws the van in this theme (null: the alley's).</summary>
        public UiTheme? ThemeOverride;

        private Profile Profile => _state.Profile!;
        private Collection Collection => Profile.Collection;

        public bool IsOpen => _builtTheme != null && _root.gameObject.activeSelf;

        public void Init(CampaignState state, ICardDataProvider cards)
        {
            _state = state;
            _cards = cards;
        }

        /// <summary>Opens the van; <paramref name="visit"/>: Moth greets you (not after a scene played over it).</summary>
        public void Open(bool visit = true)
        {
            if (_state.Profile == null) return;
            UiTheme.Current = ThemeOverride ?? UiTheme.BackAlley;
            if (_builtTheme != UiTheme.Current) Build();
            _root.gameObject.SetActive(true);
            if (visit)
            {
                _picks.Clear();
                _armed = null;
                _cursor = -1;
                _usedScrollY = 0;
                _keys.Used = false;
                Greet();
            }
            Show(_tab);
        }

        /// <summary>Hides the van for a scene over it (it comes back with <see cref="Open"/>(false)).</summary>
        public void Hide() => _root.gameObject.SetActive(false);

        public void Close()
        {
            _root.gameObject.SetActive(false);
            _state.Save();
            Closed?.Invoke();
        }

        /// <summary>
        /// Moth's hello: the claim on Ines's card paid (once, after held-card-home), his first-visit line, or the evening's
        /// footer.
        /// </summary>
        private void Greet()
        {
            const string paidSeen = "van:held-paid";
            if (!MothsVan.HeldShown(Profile) && Profile.Seen.Add(paidSeen)) Say("van:held-paid");
            else if (Profile.Seen.Add("van:hello")) Say("van:hello");
            else Say("van:footer");
            _state.Save();
        }

        /// <summary>Moth's line for a hook on the status line (nothing when the pool has none).</summary>
        private void Say(string hook, IReadOnlyDictionary<string, string>? fill = null)
        {
            if (_state.VanLine(hook, fill) is { } line) Speak(line.speaker, line.text);
        }

        private void Speak(string speaker, string text)
        {
            var T = UiTheme.Current;
            _status.text = speaker.Length > 0 ? $"<color={T.GoldSoftHex}>{speaker}:</color> <color={T.TextSoftHex}>{text}</color>" : $"<color={T.TextSoftHex}>{text}</color>";
        }

        // ---- Layout -----------------------------------------------------------------------------------

        private void Build()
        {
            bool open = _builtTheme != null && _root.gameObject.activeSelf;
            if (_builtTheme != null) Destroy(_root.parent != null ? _root.parent.gameObject : _root.gameObject);
            var T = UiTheme.Current;
            _builtTheme = T;
            _root = Ui.Overlay(transform, "Van", 13);
            Ui.Image(_root, T.Base);
            // The van's frame: brick showing around the edges, the screen's ground inside.
            var inner = AlleyMaterials.Frame(_root, 0, 0, 1920, 1080, 12, T.Base);
            inner.gameObject.name = "Inside";
            Ui.Image(Ui.Box(_root, 12, 12, 1896, 92), T.Deep).raycastTarget = false;
            // Moth's portrait slot: his portrait once there is one, the hooded figure until then.
            var face = Ui.Box(_root, 28, 18, 80, 80);
            Ui.Image(face, T.Well);
            if (!Portraits.Place(face, "moth")) AlleyMaterials.Silhouette(face, T.TextDim);
            // The van's sign (neon is for signs), and the plate under it.
            _sign = Ui.Text(Ui.Box(_root, 126, 16, 600, 28), "MOTH'S VAN", 18, UiTheme.NeonCyan, TextAnchor.MiddleLeft);
            T.Title(_sign);
            _sign.gameObject.AddComponent<Outline>().effectColor = UiTheme.NeonCyan.WithAlpha(0.35f);
            _plate = Ui.Text(Ui.Box(_root, 126, 42, 640, 56), "UNDER THE COUNTER", 34, T.Sign, TextAnchor.MiddleLeft);
            T.Title(_plate);
            _dp = Ui.Text(Ui.Box(_root, 780, 12, 900, 92), "", 24, T.Gold, TextAnchor.MiddleLeft);
            _dp.supportRichText = true;
            Ui.BoxButton(_root, 1720, 34, 160, 46, "Back", Close, 18).targetGraphic.color = T.Button;
            _tabs = Ui.Box(_root, 40, 118, 1840, 48);
            _searchBox = Ui.Input(Ui.Box(_root, 40, 178, 820, 44), "Search your copies by name", 16, v => { _search = v; _page = 0; Show(_tab); }).gameObject;
            _list = Ui.Box(_root, 40, 180, 820, 760);
            _prev = Ui.BoxButton(_root, 40, 950, 140, 40, "< Prev", () => { _page--; Show(_tab); }).gameObject;
            _pageLabel = Ui.Text(Ui.Box(_root, 190, 950, 480, 40), "", 16, T.TextMuted);
            _next = Ui.BoxButton(_root, 720, 950, 140, 40, "Next >", () => { _page++; Show(_tab); }).gameObject;
            _detail = Ui.Box(_root, 900, 180, 980, 810);
            Ui.Image(_detail, T.Panel);
            _status = Ui.Text(Ui.Box(_root, 40, 1004, 1840, 54), "", 17, T.TextSoft, TextAnchor.MiddleLeft);
            _status.supportRichText = true;
            _root.gameObject.SetActive(open);
        }

        private static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
        }

        private void Show(Tab tab)
        {
            var T = UiTheme.Current;
            // A different tab drops anything armed or picked; a redraw of the same one keeps it, so the second click confirms.
            if (_tab != tab) { _page = 0; _shelfPick = _usedPick = null; _railPick = null; _picks.Clear(); _armed = null; }
            bool changed = _tab != tab;
            // The keyboard's highlight is redrawn with the tab (a redraw of the Used tab keeps its scroll).
            if (_usedContent != null) _usedScrollY = _usedContent.anchoredPosition.y;
            _usedContent = null;
            if (changed) { _cursor = -1; _usedScrollY = 0; }
            if (_mark != null) Destroy(_mark.gameObject);
            _mark = null;
            _spots.Clear();
            _tab = tab;
            int held = Profile.AnteLive?.Held ?? 0;
            _dp.text = $"{Profile.Dp:N0} DP" + (held > 0 ? $" <size=18><color={T.TextDimHex}>({held:N0} held)</color></size>" : "")
                       + $"   <size=17><color={T.TextDimHex}>{Collection.Total:N0} cards</color></size>";
            Clear(_tabs);
            var names = new[] { "Shelf", "Used & graded", "Sell" };
            for (int i = 0; i < names.Length; i++)
            {
                var t = (Tab)i;
                var button = Ui.BoxButton(_tabs, i * 248, 0, 240, 48, names[i], () => Show(t), 18);
                button.targetGraphic.color = t == tab ? T.TabOn : T.ButtonOff;
                var label = button.GetComponentInChildren<Text>();
                label.color = t == tab ? T.Gold : T.TextDim;
                label.fontStyle = t == tab ? FontStyle.Bold : FontStyle.Normal;
                if (t == tab) Ui.Image(Ui.Box(_tabs, i * 248, 45, 240, 3), T.Highlight).raycastTarget = false;
            }
            Clear(_list);
            Clear(_detail);
            bool sell = tab == Tab.Sell;
            _searchBox.SetActive(sell);
            _prev.SetActive(sell);
            _next.SetActive(sell);
            _pageLabel.text = "";
            _list.anchoredPosition = new Vector2(40, sell ? -232 : -180);
            if (changed) Say(tab == Tab.Shelf ? "van:shelf" : tab == Tab.Sell ? "van:sell" : Profile.Rail.Count > 0 ? "van:used"
                : !MothsVan.HeldShown(Profile) && Profile.Seen.Contains("van:held-paid") ? "van:held-gone" : "van:empty");
            if (tab == Tab.Shelf) ShowShelf();
            else if (tab == Tab.Used) ShowUsed();
            else ShowSell();
            if (tab != Tab.Sell)
            {
                // The highlight follows what's picked (a click moves it too); else it stays where the keys left it.
                int picked = _spots.FindIndex(sp => sp.Picked);
                if (picked >= 0) _cursor = picked;
                else if (_cursor >= _spots.Count) _cursor = _spots.Count - 1;
                MarkSpot();
            }
        }

        // ---- Shared pieces --------------------------------------------------------------------------------

        /// <summary>A copy's card as it looks (rarity, wear), with a slab's rim; face down for the HELD row.</summary>
        private void Thumb(RectTransform parent, Vector2 center, Vector2 size, CardCopy copy, bool faceDown = false)
        {
            var info = _cards.Get(copy.Code);
            if (copy.IsSlab && !faceDown) SlabView.Rim(parent, center, size);
            var card = new FieldCard
            {
                Code = copy.Code, Type = info?.Type ?? 0, Level = info?.Level ?? 0, Attack = info?.Attack ?? 0, Defense = info?.Defense ?? 0,
                Position = faceDown ? 0x2u : 0x1u, IsPublic = !faceDown,
            };
            var view = CardView.Create(parent, center, size, card, faceDown ? null : info, 0, !faceDown, null, offField: true);
            if (!faceDown) foreach (var art in view.GetComponentsInChildren<RawImage>()) CardFoil.Apply(art, info, copy.Rarity, 0.6f, copy.Look);
            foreach (var g in view.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            if (copy.IsSlab && !faceDown) SlabView.RimGlass(parent, center, size, view);
        }

        /// <summary>The detail's big view of a copy: a slab at 62%, or the card.</summary>
        private void Showcase(CardCopy copy)
        {
            var info = _cards.Get(copy.Code);
            if (copy.IsSlab) SlabView.Create(_detail, new Vector2(-490 + 30 + 180, 405 - 20 - 293), SlabView.Size.Full, copy, info, SetCode(copy)).localScale = Vector3.one * 0.62f;
            else Thumb(_detail, new Vector2(-490 + 30 + 120, 405 - 20 - 175), new Vector2(240, 350), copy);
        }

        /// <summary>
        /// The HELD row's words with its printing's year (spec 4.2: Ines's Scapegoat is its earliest printing by April 2005,
        /// display only): "HELD · not for sale · 2004 printing"; the plain label without the card-set data.
        /// </summary>
        private string HeldRowLabel =>
            _state.Printings is PrintingIndex p && MothsVan.HeldPrinting(p, _cards) is Printing held && CopyPrints.YearOf(p.DateOf(held)) is int year && year > 0
                ? $"{MothsVan.HeldLabel} · {year} printing" : MothsVan.HeldLabel;

        /// <summary>For checks: the HELD row's label as drawn.</summary>
        public string DebugHeldLabel => HeldRowLabel;

        private string SetCode(CardCopy copy)
        {
            // The copy's own printing when it's recorded (card versions, phase 2), else its card's first at that rarity.
            if (copy.Set.Length > 0) return copy.Set;
            var printings = _state.Printings;
            if (printings == null) return "";
            return printings.Of(copy.Code, _cards.Get(copy.Code)?.Alias ?? 0).FirstOrDefault(p => p.Rarity == copy.Rarity).SetCode ?? "";
        }

        private Text Facts(float y, float h, string text, int size = 17)
        {
            var t = Ui.Text(Ui.Box(_detail, 300, y, 650, h), text, size, UiTheme.Current.Text, TextAnchor.UpperLeft);
            t.supportRichText = true;
            return t;
        }

        private void Blocked(float y, string why) =>
            Ui.Text(Ui.Box(_detail, 30, y, 920, 24), $"<color={UiTheme.Current.BadHex}>▲ {why}</color>", 15, UiTheme.Current.Text, TextAnchor.MiddleLeft).supportRichText = true;

        /// <summary>The armed-twice action button: red, then gold to confirm within 5 seconds.</summary>
        private void ActionButton(float x, float y, float w, string label, string confirm, string key, Action act)
        {
            var T = UiTheme.Current;
            bool armed = _armed == key && Time.unscaledTime - _armedAt < 5;
            var button = Ui.BoxButton(_detail, x, y, w, 70, armed ? confirm : label, () =>
            {
                if (!(_armed == key && Time.unscaledTime - _armedAt < 5))
                {
                    _armed = key;
                    _armedAt = Time.unscaledTime;
                    Show(_tab);
                    return;
                }
                _armed = null;
                act();
            }, 22);
            button.targetGraphic.color = armed ? T.Highlight : T.Primary;
            if (armed) button.GetComponentInChildren<Text>().color = T.OnAccent;
        }

        /// <summary>The tag on a restored shelf slot (<see cref="Listing.Restored"/>).</summary>
        public const string RestoredTag = "RESTORED";

        private static string GradeLine(CardCopy c) => $"<color={Grades.Color(c.Shown)}>{Grades.Glyph(c.Shown)} {Grades.Describe(c)}</color>";

        /// <summary>A listing's tile in a grid: the card, its grade chip, its name, its price (SOLD dimmed).</summary>
        private RectTransform Tile(RectTransform parent, float x, float y, float w, float h, Listing l, bool picked, string note, Action click)
        {
            var T = UiTheme.Current;
            var info = _cards.Get(l.Copy.Code);
            var tile = Ui.Box(parent, x, y, w, h);
            Ui.Image(tile, picked ? T.RowSelected : T.RowA);
            if (picked) Ui.Image(Ui.Box(tile, 0, 0, w, 3), T.Highlight).raycastTarget = false;
            // A note over the card (the shelf's "Authenticated by the Loupe"), in italics.
            float top = note.Length > 0 ? 26 : 8, cardH = h - 68 - top, cardW = cardH * 0.69f;
            if (note.Length > 0) Ui.Text(Ui.Box(tile, 4, 5, w - 8, 18), note, 12, T.TextQuiet, l.Restored ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter).fontStyle = FontStyle.Italic;
            // A restored slot (a peak build's own banned card, once the wall is open): RESTORED, in No Limits' sign ink (as UNBANNED).
            if (l.Restored)
            {
                var tag = Ui.Text(Ui.Box(tile, 4, 5, w - 8, 18), RestoredTag, 12, T.Sign, TextAnchor.MiddleRight);
                tag.fontStyle = FontStyle.Bold;
                tag.gameObject.name = RestoredTag;
            }
            Thumb(tile, new Vector2(0, h / 2 - top - cardH / 2), new Vector2(cardW, cardH), l.Copy);
            // A listing printing an original text (spec 4.2): "ORIGINAL TEXT · 2004" over the card, above its stats.
            CardDetail.OriginalBand(tile, l.Copy, 4, top + cardH - 38, w - 8);
            var chip = Ui.Text(Ui.Box(tile, 4, h - 70, w - 8, 18), l.Copy.IsSlab ? $"{Grades.Glyph(l.Copy.Slab)} Grade {l.Copy.Slab}" : $"<color={Grades.Color(l.Copy.Shown)}>{Grades.Glyph(l.Copy.Shown)} {Grades.Word(l.Copy.Shown)}</color>",
                12, T.Text);
            chip.supportRichText = true;
            var name = Ui.Text(Ui.Box(tile, 4, h - 52, w - 8, 28), info?.Name ?? "", 12, T.Text, TextAnchor.UpperCenter);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 8;
            name.resizeTextMaxSize = 12;
            var price = Ui.Text(Ui.Box(tile, 4, h - 24, w - 8, 20), l.Sold ? "SOLD" : $"{l.Price:N0} DP", 14, l.Sold ? T.TextDim : T.Gold);
            price.fontStyle = FontStyle.Bold;
            price.supportRichText = true;
            if (l.Sold) tile.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
            Ui.Clickable(tile, click, style: HoverStyle.Tile);
            return tile;
        }

        // ---- Shelf --------------------------------------------------------------------------------------

        private void ShowShelf()
        {
            var T = UiTheme.Current;
            var shelf = _state.VanShelf();
            int refresh = MothsVan.DuelsToRefresh(Profile, shelf: true);
            Ui.Text(Ui.Box(_tabs, 1460, 0, 380, 48), $"New stock in {refresh} alley duel{(refresh == 1 ? "" : "s")}", 16, T.TextMuted, TextAnchor.MiddleRight);
            for (int i = 0; i < shelf.Count; i++)
            {
                var l = shelf[i];
                var pick = l;
                // Odile's word on each.
                bool picked = _shelfPick != null && SameListing(l, _shelfPick);
                Action click = () => { _shelfPick = pick; Show(_tab); };
                var tile = Tile(_list, (i % 3) * 276, (i / 3) * 384, 264, 372, l, picked, "Authenticated by the Loupe", click);
                Spot(tile, click, picked, () => AskBuy(pick, shelf: true));
            }
            if (shelf.Count == 0) Ui.Text(Ui.Box(_list, 0, 0, 820, 60), "The freezer's empty tonight.", 18, T.TextMuted, TextAnchor.UpperLeft);
            // The pick only while it's still that slab on this shelf (the wall opening or a restock re-draws it: MothsVan.Buy
            // refuses a listing that's gone), so a stale tile is never offered for sale.
            var l2 = _shelfPick == null ? null : shelf.FirstOrDefault(x => SameListing(x, _shelfPick));
            if (l2 == null) _shelfPick = null;
            if (l2 == null)
            {
                Ui.Text(Ui.Box(_detail, 30, 20, 920, 120), $"Slabs of cards banned somewhere, sometime: grade 9 or 10, Secret Rare and up, worth {AlleyEconomy.ShelfMinValue:N0} DP or more. "
                    + $"Moth's price is {AlleyEconomy.ShelfMarkup:0.#}× the value, rounded up to 500. New stock every {AlleyEconomy.ShelfEvery} alley duels."
                    + (shelf.Any(x => x.Restored) ? $" The {RestoredTag} slots are the peak builds' own banned cards, off the depot." : ""),
                    17, T.TextSoft, TextAnchor.UpperLeft);
                return;
            }
            ListingDetail(l2, shelf: true);
        }

        /// <summary>The same shelf listing: its slot, its slab (card, printing, grade), restored or not, and its price, in the same period.</summary>
        private static bool SameListing(Listing a, Listing b) =>
            a.Slot == b.Slot && a.Epoch == b.Epoch && a.Restored == b.Restored && a.Price == b.Price
            && a.Copy.Code == b.Copy.Code && a.Copy.Rarity == b.Copy.Rarity && a.Copy.Slab == b.Copy.Slab;

        /// <summary>A shelf or used listing's detail: the copy, its facts, Odile's word (the shelf), and Buy.</summary>
        private void ListingDetail(Listing l, bool shelf)
        {
            var T = UiTheme.Current;
            var info = _cards.Get(l.Copy.Code);
            int value = Collection.Value(l.Copy);
            Showcase(l.Copy);
            Facts(20, 230, $"<b><size=26>{info?.Name}</size></b>\n{(CardDetail.PrintingLine(l.Copy) is { Length: > 0 } printed ? printed : SetCode(l.Copy))} · {CardFoil.ColoredName(l.Copy.Rarity)}\n{GradeLine(l.Copy)}\n"
                + (shelf ? $"Worth <color={T.GoldHex}>{value:N0} DP</color> · Moth's price <color={T.GoldHex}>{l.Price:N0} DP</color>"
                         : $"Price = value <color={T.GoldHex}>{l.Price:N0} DP</color>")
                + $"\n<size=14><color={T.TextDimHex}>The Sell desk would pay {Math.Min(value / 4, l.Price / 2):N0} DP for it.</color></size>"
                // A restored slot (Listing.Restored): the tag, in No Limits' sign ink, and where it's from.
                + (shelf && l.Restored ? $"\n<color={T.SignHex}><b>{RestoredTag}</b></color> · a card a list took, off the depot" : ""));
            if (shelf) Facts(250, 60, $"<color={T.GoldSoftHex}>Odile:</color> <i>“Authenticated by the Loupe.”</i>", 16);
            var blocked = MothsVan.Blocked(Profile, l);
            if (blocked != null) { Blocked(640, blocked); return; }
            // The arm key names the slab (and a shelf slot's restored flag), so a slot re-drawn with another slab never arrives armed.
            var key = $"{(shelf ? "shelf" : "used")}:{l.Slot}:{(shelf ? MothsVan.ShelfEpoch(Profile) : MothsVan.UsedEpoch(Profile))}"
                      + $":{MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.IsSlab ? l.Copy.Slab : l.Copy.Grade)}{(shelf && l.Restored ? ":restored" : "")}";
            ActionButton(30, 680, 440, $"Buy · {l.Price:N0} DP", $"Confirm · {l.Price:N0} DP", key, () => BuyListing(l, shelf));
        }

        /// <summary>Buys a shelf or used listing (Buy's second click, or a confirmed Enter): MothsVan.Buy, saved, Moth's word.</summary>
        private void BuyListing(Listing l, bool shelf)
        {
            var T = UiTheme.Current;
            int refund = MothsVan.Buy(Profile, l, shelf);
            _state.Save();
            if (refund < 0)
            {
                Sounds.Play("error");
                Speak(_state.CastName("moth"), "That one's gone. New stock came in while you were looking.");
                _shelfPick = _usedPick = null;
                Show(_tab);
                return;
            }
            Sounds.Play("dp", 0.7f);
            Speak(_state.CastName("moth"), refund > 0 ? $"Yours. As-is. Your least valuable copy went in the bin for {refund:N0} DP." : "Yours. As-is.");
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={T.GoldSoftHex}>{sets}</color>";
            Show(_tab);
        }

        // ---- Used & graded ------------------------------------------------------------------------------

        /// <summary>A rail tag as the van's row says it: "Your Pot of Greed · Grade 9 slab · 4,500 DP · 3 duels left".</summary>
        private string RailLine(RailEntry r)
        {
            int left = Ante.DuelsLeft(Profile, r);
            return $"Your {_cards.Get(r.Copy.Code)?.Name ?? r.Copy.Code.ToString()} · {(r.Copy.IsSlab ? $"Grade {r.Copy.Slab} slab" : Grades.RawLabel(r.Copy.Grade))} · "
                   + $"{r.Price:N0} DP · {left} duel{(left == 1 ? "" : "s")} left";
        }

        private void ShowUsed()
        {
            var T = UiTheme.Current;
            var listings = _state.VanUsed();
            int refresh = MothsVan.DuelsToRefresh(Profile, shelf: false);
            Ui.Text(Ui.Box(_tabs, 1460, 0, 380, 48), $"New listings in {refresh} duel{(refresh == 1 ? "" : "s")}", 16, T.TextMuted, TextAnchor.MiddleRight);
            // One scrolling column: the rail, the used contraband, the HELD row.
            var rail = Profile.Rail.OrderBy(r => r.ExpiresAt).ToList();
            bool heldRow = MothsVan.HeldShown(Profile);
            const float railRow = 68, tileW = 132, tileH = 176;
            int gridRows = (listings.Count + 5) / 6;
            float height = 30 + Math.Max(1, rail.Count) * railRow + 40 + gridRows * (tileH + 6) + (heldRow ? 30 + railRow : 0);
            var content = Scroll(_list, 820, 760, height);
            _usedContent = content;
            content.anchoredPosition = new Vector2(0, Mathf.Clamp(_usedScrollY, 0, Math.Max(0, height - 760)));
            float y = 0;
            Ui.Text(Ui.Box(content, 0, y, 820, 26), "MOTH'S RAIL · your cards, until their clocks run out", 13, T.Caption, TextAnchor.MiddleLeft);
            y += 30;
            if (rail.Count == 0)
            {
                Ui.Text(Ui.Box(content, 0, y, 820, railRow - 6), "Nothing of yours on the hooks.", 16, T.TextMuted, TextAnchor.MiddleLeft);
                y += railRow;
            }
            foreach (var r in rail)
            {
                var entry = r;
                bool picked = _railPick == r;
                var row = Ui.Box(content, 0, y, 820, railRow - 6);
                Ui.Image(row, picked ? T.RowSelected : T.RowB);
                Ui.Image(Ui.Box(row, 0, 0, 4, railRow - 6), picked ? T.Highlight : T.Brass).raycastTarget = false;
                Thumb(row, new Vector2(-410 + 34, 0), new Vector2(38, 56), r.Copy);
                var line = Ui.Text(Ui.Box(row, 66, 0, 740, railRow - 6), RailLine(r), 17, T.Text, TextAnchor.MiddleLeft);
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 12;
                line.resizeTextMaxSize = 17;
                Action pickRail = () =>
                {
                    _railPick = entry;
                    _usedPick = null;
                    Say("van:rail", MothsVan.RailFill(Profile, entry, _cards));
                    Show(_tab);
                };
                Ui.Clickable(row, pickRail, style: HoverStyle.Row);
                Spot(row, pickRail, picked, () => AskBuyBack(entry));
                y += railRow;
            }
            y += 10;
            Ui.Text(Ui.Box(content, 0, y, 820, 26), $"USED CONTRABAND · at value, slabs up to {AlleyEconomy.VanUsedSlabMax}", 13, T.Caption, TextAnchor.MiddleLeft);
            y += 30;
            for (int i = 0; i < listings.Count; i++)
            {
                var pick = listings[i];
                bool picked = _usedPick?.Slot == listings[i].Slot && _railPick == null;
                Action click = () => { _usedPick = pick; _railPick = null; Show(_tab); };
                Spot(Tile(content, (i % 6) * (tileW + 5), y + (i / 6) * (tileH + 6), tileW, tileH, listings[i], picked, "", click), click, picked, () => AskBuy(pick, shelf: false));
            }
            y += gridRows * (tileH + 6);
            if (heldRow)
            {
                // Ines's card at the end of the rail: face down, no price, not for sale; Moth won't talk about it.
                y += 24;
                var row = Ui.Box(content, 0, y, 820, railRow - 6);
                Ui.Image(row, T.Well);
                Ui.Image(Ui.Box(row, 0, 0, 4, railRow - 6), T.Locked).raycastTarget = false;
                Thumb(row, new Vector2(-410 + 34, 0), new Vector2(38, 56), new CardCopy { Code = MothsVan.HeldCode, Grade = MothsVan.HeldGrade }, faceDown: true);
                Ui.Text(Ui.Box(row, 66, 0, 740, railRow - 6), HeldRowLabel, 17, T.TextDim, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Action held = () => Say("van:held");
                Ui.Clickable(row, held, style: HoverStyle.Row);
                Spot(row, held, false);
            }
            UsedDetail(listings);
        }

        private void UsedDetail(List<Listing> listings)
        {
            var T = UiTheme.Current;
            if (_railPick != null && Profile.Rail.Contains(_railPick)) { RailDetail(_railPick); return; }
            var l = _usedPick == null ? null : listings.FirstOrDefault(x => x.Slot == _usedPick.Slot);
            if (l == null)
            {
                Ui.Text(Ui.Box(_detail, 30, 20, 920, 120), $"Your cards on the rail first: a card you lost at Orvel's Table hangs here at {AlleyEconomy.RailMarkup:0.#}× its value "
                    + $"for {AlleyEconomy.RailDuels} duels, then Moth sells it. Below, second-hand contraband at its value: Raw at the used odds, slabs up to {AlleyEconomy.VanUsedSlabMax}.",
                    17, T.TextSoft, TextAnchor.UpperLeft);
                return;
            }
            ListingDetail(l, shelf: false);
        }

        /// <summary>A card of yours on the rail: its tag, its clock, and Buy back (the same copy comes back).</summary>
        private void RailDetail(RailEntry r)
        {
            var T = UiTheme.Current;
            var info = _cards.Get(r.Copy.Code);
            int left = Ante.DuelsLeft(Profile, r);
            Showcase(r.Copy);
            Facts(20, 260, $"<b><size=26>{info?.Name}</size></b>\n{SetCode(r.Copy)} · {CardFoil.ColoredName(r.Copy.Rarity)}\n{GradeLine(r.Copy)}\n"
                + $"Moth's tag <color={T.GoldHex}>{r.Price:N0} DP</color> ({AlleyEconomy.RailMarkup:0.#}× its value)\n"
                + $"{left} duel{(left == 1 ? "" : "s")} left on the clock, then he sells it.\n<size=14><color={T.TextDimHex}>Bought back, it's the same card: "
                + "its grade, its wear, its slab. The tag is added to what you paid for it.</color></size>");
            var blocked = Ante.BuyBackBlocked(Profile, r);
            if (blocked != null) { Blocked(640, blocked); return; }
            ActionButton(30, 680, 440, $"Buy back · {r.Price:N0} DP", $"Confirm · {r.Price:N0} DP", $"rail:{r.Copy.Id}", () => BuyBack(r));
        }

        /// <summary>Buys back a railed card (Buy back's second click, or a confirmed Enter): Ante.BuyBack, saved, its scene.</summary>
        private void BuyBack(RailEntry r)
        {
            var fill = MothsVan.RailFill(Profile, r, _cards);
            if (!Ante.BuyBack(Profile, r)) { Sounds.Play("error"); Show(_tab); return; }
            _state.Save();
            Sounds.Play("dp", 0.7f);
            _railPick = null;
            Say("van:buyback", fill);
            Show(_tab);
            // Its scene the first time ("buyback": callers raise it once Ante.BuyBack succeeds).
            StoryEvents?.Invoke(new[] { "buyback" }, fill);
        }

        /// <summary>A vertical scroll area inside <paramref name="parent"/>; returns the content to lay out from its top (the Unlisted binder's pages use it too).</summary>
        internal static RectTransform Scroll(RectTransform parent, float w, float h, float height)
        {
            var viewport = Ui.Box(parent, 0, 0, w, h);
            Ui.Image(viewport, UiTheme.Current.Scrim.WithAlpha(0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, Math.Max(h, height));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return content;
        }

        // ---- Sell ---------------------------------------------------------------------------------------

        private List<CardCopy> Copies()
        {
            var copies = Collection.All.Where(c => !c.AtDesk);
            if (_search.Length > 0) copies = copies.Where(c => (_cards.Get(c.Code)?.Name ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            return copies.OrderByDescending(Collection.Value).ThenBy(c => _cards.Get(c.Code)?.Name).ThenBy(c => c.Id).ToList();
        }

        private void ShowSell()
        {
            var T = UiTheme.Current;
            var copies = Copies();
            int pages = Math.Max(1, (copies.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = $"{copies.Count} copies · page {_page + 1} of {pages}";
            for (int i = 0; i < PerPage && _page * PerPage + i < copies.Count; i++)
            {
                var copy = copies[_page * PerPage + i];
                var info = _cards.Get(copy.Code);
                bool picked = _picks.Contains(copy);
                var row = Ui.Box(_list, 0, i * 70, 820, 64);
                Ui.Image(row, picked ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                var box = Ui.Box(row, 8, 18, 28, 28);
                Ui.Image(box, T.Well);
                Ui.Text(box, picked ? "✓" : "", 20, T.Gold);
                Thumb(row, new Vector2(44 + 20 - 410, 0), new Vector2(40, 58), copy);
                Ui.Text(Ui.Box(row, 96, 4, 500, 26), info?.Name ?? copy.Code.ToString(), 17, T.Text, TextAnchor.MiddleLeft);
                Ui.Text(Ui.Box(row, 96, 32, 500, 26), $"{CardFoil.ColoredName(copy.Rarity)} · {GradeLine(copy)}", 13, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
                var blocked = SellDesk.Blocked(Profile, copy, _state.NeededByDecks);
                var right = Ui.Text(Ui.Box(row, 560, 0, 248, 64),
                    blocked == null ? $"Pays {Collection.SellPrice(copy):N0} DP" : $"<size=13><color={T.TextDimHex}>{blocked}</color></size>", 17, T.Gold, TextAnchor.MiddleRight);
                right.supportRichText = true;
                right.resizeTextForBestFit = true;
                right.resizeTextMinSize = 10;
                right.resizeTextMaxSize = 17;
                int index = _page * PerPage + i;
                if (_keys.Used && index == _cursor) MarkRow(row);
                Ui.Clickable(row, () => TogglePick(copy, index), hover: () => _keys.Point(index), style: HoverStyle.Row);
            }
            SellDetail();
        }

        /// <summary>A Sell row's click (or Enter on it): ticks the copy, or says why it isn't for sale. Never sells.</summary>
        private void TogglePick(CardCopy copy, int index)
        {
            _cursor = index;
            if (SellDesk.Blocked(Profile, copy, _state.NeededByDecks) is string why)
            {
                // Won at a table: Moth doesn't buy back his own (the desk's words, then his).
                // A No Limits pilot's restored card has its own refusal ("That one's got somewhere to be").
                if (copy.Source == CopySource.Ante)
                {
                    bool restored = _state.AlleyReady && _state.AlleyData.Builds.Any(b => b.Restored.Contains(copy.Code));
                    if (!restored || _state.VanLine("van:sell-refused:restored") is not { } r) Say("van:sell-refused");
                    else Speak(r.speaker, r.text);
                }
                else Speak("", why);
                Sounds.Play("error");
                return;
            }
            if (!_picks.Remove(copy)) _picks.Add(copy);
            Show(_tab);
        }

        private void SellDetail()
        {
            var T = UiTheme.Current;
            var picks = _picks.Where(c => Collection.All.Contains(c)).ToList();
            Ui.Text(Ui.Box(_detail, 30, 20, 920, 24), picks.Count == 0 ? "SELLING NOTHING YET · tick copies on the left" : $"SELLING {picks.Count} COP{(picks.Count == 1 ? "Y" : "IES")}",
                13, T.Caption, TextAnchor.MiddleLeft);
            for (int i = 0; i < picks.Count && i < 12; i++)
                Ui.Text(Ui.Box(_detail, 30, 52 + i * 30, 920, 28), $"{_cards.Get(picks[i].Code)?.Name} · {CardFoil.ColoredName(picks[i].Rarity)} · pays <color={T.GoldHex}>{Collection.SellPrice(picks[i]):N0} DP</color>",
                    16, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
            int total = picks.Sum(Collection.SellPrice);
            Ui.Text(Ui.Box(_detail, 30, 520, 920, 40), $"You get {total:N0} DP", 28, T.Gold, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(_detail, 30, 562, 920, 24), $"DP after: {Profile.Dp + total:N0}", 16, T.TextSoft, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(_detail, 30, 600, 920, 60), "Moth pays what the Sell desk pays: 25% of a copy's value, never more than half of what you paid for it. "
                + "Cards won at a table, gifts and prizes pay nothing; a copy one of your decks needs isn't for sale.", 13, T.TextQuiet, TextAnchor.UpperLeft);
            if (picks.Count == 0) return;
            var key = "sell:" + string.Join(",", picks.Select(c => c.Id).OrderBy(x => x));
            ActionButton(30, 700, 440, $"Sell {picks.Count} cop{(picks.Count == 1 ? "y" : "ies")} · +{total:N0} DP", $"Confirm: sell {picks.Count}", key, () => SellCopies(picks));
        }

        /// <summary>Sells copies (Sell's second click, or a confirmed Enter on one): MothsVan.Sell each, saved, Moth's word.</summary>
        private void SellCopies(List<CardCopy> copies)
        {
            var T = UiTheme.Current;
            int got = copies.Sum(c => MothsVan.Sell(Profile, c, _state.NeededByDecks));
            _picks.RemoveWhere(c => copies.Contains(c) || !Collection.All.Contains(c));
            _state.Save();
            Sounds.Play("dp", 0.7f);
            Say("van:sell");
            _status.text += $"  <color={T.GoldHex}>+{got:N0} DP</color>";
            Show(_tab);
        }

        // ---- The keyboard -------------------------------------------------------------------------------

        // As in the shop (ArrowNav): the arrows (or the d-pad) move a highlight. The Shelf's slabs and the Used tab's rail rows,
        // tiles and HELD row go by where they sit, and the highlight picks what it lands on (its detail shows), as a click
        // would; Enter on it asks "Buy X for N DP?" (or "Buy back your X?"). The Sell tab's copies go up and down (across
        // pages; left/right turn the page) and Enter asks "Sell X for N DP?". A second Enter does it (the same path as the
        // buttons' second click), Escape cancels, and one press never buys or sells (KeyConfirm). Clicks tick copies as before.

        /// <summary>Something the arrows stop on in the tab shown: its rect, its click, whether it's the one picked.</summary>
        private sealed class KeySpot
        {
            public RectTransform Rt = null!;
            public Action Click = null!;
            public bool Picked;
            /// <summary>Enter on it once picked: its purchase asked (KeyConfirm); null where Enter is just the click (the HELD row).</summary>
            public Action? Ask;
        }

        private readonly ArrowNav _keys = new ArrowNav();
        private readonly KeyConfirm _confirm = new KeyConfirm();

        /// <summary>For checks: the keyboard's buy or sell question as shown ("" when none is up).</summary>
        public string DebugConfirm => _confirm.Question;

        /// <summary>For checks: the screen point of the confirm's yes (or Cancel) button; null when none is up.</summary>
        public Vector2? DebugConfirmButton(bool yes) => _confirm.ButtonPoint(yes);
        private readonly List<KeySpot> _spots = new List<KeySpot>();
        /// <summary>The highlight: an index into the tab's spots (Shelf, Used) or into its copies (Sell); -1 for none yet.</summary>
        private int _cursor = -1;
        private RectTransform? _mark, _usedContent;
        private float _usedScrollY;

        private void Spot(RectTransform rt, Action click, bool picked, Action? ask = null)
        {
            // The mouse over it moves the keyboard's position there (ArrowNav.Point), so the arrows go on from it.
            int index = _spots.Count;
            if (rt.GetComponent<Clickable>() is Clickable c && c.Hover == null) c.Hover = () => _keys.Point(index);
            _spots.Add(new KeySpot { Rt = rt, Click = click, Picked = picked, Ask = ask });
        }

        /// <summary>Enter on a picked listing: "Buy X for N DP?", then bought as Buy's second click buys it; a blocked one shows why in its detail.</summary>
        private void AskBuy(Listing l, bool shelf)
        {
            if (MothsVan.Blocked(Profile, l) != null) return;
            _confirm.Open(_root, $"Buy <b>{_cards.Get(l.Copy.Code)?.Name}</b> ({Grades.Describe(l.Copy)}) for {l.Price:N0} DP?", "Buy", () => BuyListing(l, shelf));
        }

        /// <summary>Enter on a picked rail entry: "Buy back your X for N DP?", then as Buy back's second click.</summary>
        private void AskBuyBack(RailEntry r)
        {
            if (!Profile.Rail.Contains(r) || Ante.BuyBackBlocked(Profile, r) != null) return;
            _confirm.Open(_root, $"Buy back your <b>{_cards.Get(r.Copy.Code)?.Name}</b> for {r.Price:N0} DP?", "Buy back", () => BuyBack(r));
        }

        /// <summary>Enter on a Sell row: "Sell X for N DP?", then sold as Sell's second click sells; one Moth won't take says why.</summary>
        private void AskSell(CardCopy copy, int index)
        {
            _cursor = index;
            if (SellDesk.Blocked(Profile, copy, _state.NeededByDecks) != null) { TogglePick(copy, index); return; }
            _confirm.Open(_root, $"Sell <b>{_cards.Get(copy.Code)?.Name}</b> ({CardFoil.ColoredName(copy.Rarity)}, {Grades.Describe(copy)}) for {Collection.SellPrice(copy):N0} DP?",
                "Sell", () => SellCopies(new List<CardCopy> { copy }));
        }

        private void Update()
        {
            if (!IsOpen) { _confirm.Close(); return; }
            // A clicked button keeps the UI's selection, and Enter would press it again (a Buy confirmed).
            ArrowNav.DropButtonFocus(_root);
            if (_confirm.Update()) return; // "Buy X?" / "Sell X?" is up: Enter does it, Escape cancels
            var k = _keys.Read(_root, _list);
            if (!k.Any) return;
            if (_tab == Tab.Sell) SellKeys(k);
            else SpotKeys(k);
        }

        private void SpotKeys(ArrowNav.Keys k)
        {
            if (_spots.Count == 0) return;
            if (k.Move != Vector2Int.zero)
            {
                _cursor = _keys.From(_cursor, _spots.Count); // on from where the mouse last pointed
                // The first press lands on the first thing (or on what's picked already).
                int to = _cursor < 0 || _cursor >= _spots.Count ? 0
                    : ArrowNav.Step(_spots.Select(sp => new Vector2(sp.Rt.anchoredPosition.x + sp.Rt.sizeDelta.x / 2, -sp.Rt.anchoredPosition.y + sp.Rt.sizeDelta.y / 2)).ToList(),
                        _cursor, k.Move, 20);
                if (to >= 0)
                {
                    _cursor = to;
                    _spots[to].Click(); // picks it (a redraw), or Moth's line for the HELD row
                    MarkSpot();
                }
                if (!k.Enter) return;
            }
            if (k.Enter && _cursor >= 0 && _cursor < _spots.Count)
            {
                // Enter picks the highlighted thing (as a click) and asks to buy it; the HELD row is just Moth's line.
                var sp = _spots[_cursor];
                if (sp.Ask == null) { sp.Click(); MarkSpot(); return; }
                if (!sp.Picked) { sp.Click(); sp = _cursor < _spots.Count ? _spots[_cursor] : null!; }
                MarkSpot();
                sp?.Ask?.Invoke();
            }
        }

        private void SellKeys(ArrowNav.Keys k)
        {
            var copies = Copies();
            if (copies.Count == 0) return;
            if (_cursor >= copies.Count) _cursor = copies.Count - 1;
            if (k.Move != Vector2Int.zero) _cursor = _keys.From(_cursor, copies.Count); // on from where the mouse last pointed
            int to = ArrowNav.ListStep(copies.Count, PerPage, _page, _cursor, k.Move, _ => true);
            if (to >= 0)
            {
                _cursor = to;
                _page = to / PerPage;
                Show(_tab);
            }
            if (k.Enter && _cursor >= 0 && _cursor / PerPage == _page) AskSell(copies[_cursor], _cursor);
        }

        /// <summary>The gold frame on the highlighted spot (once the keys are in use), scrolled into view on the Used tab.</summary>
        private void MarkSpot()
        {
            var target = _keys.Used && _cursor >= 0 && _cursor < _spots.Count && _spots[_cursor].Rt != null ? _spots[_cursor].Rt : null;
            ArrowNav.Mark(ref _mark, _list, target, 4);
            if (target == null || _tab != Tab.Used || _usedContent == null || target.parent != _usedContent) return;
            float top = -target.anchoredPosition.y - 8, bottom = -target.anchoredPosition.y + target.sizeDelta.y + 8, view = 760;
            float y = _usedContent.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            y = Mathf.Clamp(y, 0, Math.Max(0, _usedContent.sizeDelta.y - view));
            _usedContent.anchoredPosition = new Vector2(_usedContent.anchoredPosition.x, y);
            _usedScrollY = y;
        }

        private void MarkRow(RectTransform row) => ArrowNav.Mark(ref _mark, _list, row, 3);

        // ---- For checks ---------------------------------------------------------------------------------

        /// <summary>Opens (or redraws) a tab: 0 Shelf, 1 Used &amp; graded, 2 Sell.</summary>
        public void DebugShow(int tab)
        {
            if (!IsOpen) Open();
            Show((Tab)tab);
        }

        /// <summary>The tabs' labels as drawn.</summary>
        public List<string> DebugTabs() => _tabs.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text).ToList();

        /// <summary>For checks: picks a used listing by its slot (its detail shows).</summary>
        public void DebugPickShelf(int slot)
        {
            _shelfPick = _state.VanShelf().FirstOrDefault(l => l.Slot == slot);
            Show(Tab.Shelf);
        }

        public void DebugPickUsed(int slot)
        {
            _usedPick = _state.VanUsed().FirstOrDefault(l => l.Slot == slot);
            Show(Tab.Used);
        }

        /// <summary>For checks: the ORIGINAL TEXT bands drawn on the listings.</summary>
        /// <summary>The shelf tiles tagged RESTORED as last drawn (for the checks).</summary>
        public int DebugRestoredTags => _list.GetComponentsInChildren<RectTransform>().Count(r => r.name == RestoredTag);
        public int DebugOriginalBands => _list.GetComponentsInChildren<RectTransform>().Count(r => r.name == "Original");
        public string DebugListText() => string.Join("\n", _list.GetComponentsInChildren<Text>().Select(t => Strip(t.text)));
        public string DebugDetailText() => string.Join("\n", _detail.GetComponentsInChildren<Text>().Select(t => Strip(t.text)));
        public string DebugStatus => Strip(_status.text);
        /// <summary>The van's sign and plate, and their colors.</summary>
        public (string sign, Color signColor, string plate, Color plateColor) DebugPlate => (_sign.text, _sign.color, _plate.text, _plate.color);

        /// <summary>Picks a rail entry and presses Buy back twice (armed, then confirmed); true when it came back.</summary>
        public bool DebugBuyBack(RailEntry entry)
        {
            Show(Tab.Used);
            _railPick = entry;
            Show(Tab.Used);
            for (int i = 0; i < 2 && Profile.Rail.Contains(entry); i++)
                _detail.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>().text.StartsWith(i == 0 ? "Buy back" : "Confirm"))?.onClick.Invoke();
            return !Profile.Rail.Contains(entry);
        }

        /// <summary>Ticks copies on the Sell tab and presses Sell twice; returns the DP it paid.</summary>
        public int DebugSell(IEnumerable<CardCopy> copies)
        {
            Show(Tab.Sell);
            _picks.Clear();
            foreach (var c in copies) _picks.Add(c);
            Show(Tab.Sell);
            int before = Profile.Dp;
            for (int i = 0; i < 2; i++)
                _detail.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>().text.StartsWith(i == 0 ? "Sell" : "Confirm"))?.onClick.Invoke();
            return Profile.Dp - before;
        }

        /// <summary>
        /// For checks: the keyboard's highlight (an index into the tab's spots, or the Sell tab's copies), whether its frame
        /// shows, the Shelf's and Used tab's picks (slot, -1 for none; whether a rail entry is picked), the Sell tab's ticks
        /// and page, and the highlighted copy there.
        /// </summary>
        public (int cursor, bool framed, int shelfSlot, int usedSlot, bool rail, int picks, int page, CardCopy? copy) DebugKeys
        {
            get
            {
                var copies = _tab == Tab.Sell ? Copies() : null;
                return (_cursor, _mark != null && _mark.gameObject.activeInHierarchy, _shelfPick?.Slot ?? -1, _usedPick?.Slot ?? -1, _railPick != null,
                    _picks.Count, _page, copies != null && _cursor >= 0 && _cursor < copies.Count ? copies[_cursor] : null);
            }
        }

        /// <summary>For checks: the Shelf's listings' slots in the order drawn.</summary>
        public List<int> DebugShelfSlots => _state.VanShelf().Select(l => l.Slot).ToList();

        /// <summary>
        /// For checks: clicks the detail's Buy (arming it, as a first click does) and leaves the UI's selection on the button
        /// that's there after, as a mouse click would; false when there's no Buy to press.
        /// </summary>
        public bool DebugArmBuy()
        {
            var buy = _detail.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>().text.StartsWith("Buy"));
            if (buy == null) return false;
            buy.onClick.Invoke();
            var confirm = _detail.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>().text.StartsWith("Confirm"));
            if (confirm != null) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(confirm.gameObject);
            return confirm != null;
        }

        /// <summary>For checks: the detail's buttons' labels.</summary>
        public List<string> DebugDetailButtons() => _detail.GetComponentsInChildren<Button>().Select(b => b.GetComponentInChildren<Text>().text).ToList();

        private static string Strip(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<.*?>", "");
    }
}
