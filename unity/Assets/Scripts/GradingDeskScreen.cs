using System;
using System.Collections;
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
    /// Gauge &amp; Gilt's Grading Desk, reached from the Card Shop (Odile Varga, "the Loupe", speaks every result):
    /// <list type="bullet">
    /// <item>Grade: send a Raw copy (or crack a slab and resubmit) for a fee by its value; the odds are shown; standard
    /// is back in 3 duels, express now at twice the fee. Copies at the desk wait in a list until you collect them, and
    /// each comes back with a reveal: the number first, then the label.</item>
    /// <item>Sell: pick copies; each pays a quarter of its value, never more than half of what you paid.</item>
    /// <item>Used: 24 copies of real printings at their value, new every 5 duels, and special orders.</item>
    /// </list>
    /// Purchases and sales arm on the first click and go through on the second, as elsewhere.
    /// </summary>
    public sealed class GradingDeskScreen : MonoBehaviour
    {
        private enum Tab { Grade, Sell, Used }
        private const int PerPage = 10;

        private CampaignState _state = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _list = null!, _detail = null!, _tabs = null!;
        private Text _dp = null!, _status = null!, _pageLabel = null!;
        private GameObject _searchBox = null!, _prev = null!, _next = null!;
        private Tab _tab = Tab.Grade;
        private int _page;
        private string _search = "";
        private CardCopy? _selected;
        private Listing? _listing;
        private bool _express;
        private readonly HashSet<CardCopy> _picks = new HashSet<CardCopy>();
        private string? _armed;
        private float _armedAt;
        private RectTransform? _overlay;
        private readonly Queue<CardCopy> _reveals = new Queue<CardCopy>();

        public event Action? Closed;
        private Profile Profile => _state.Profile!;
        private Collection Collection => Profile.Collection;

        public void Init(CampaignState state, ICardDataProvider cards)
        {
            _state = state;
            _cards = cards;
            Build();
            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Open()
        {
            if (_state.Profile == null) return;
            _root.gameObject.SetActive(true);
            _picks.Clear();
            Say(GradingDesk.Ready(Profile) > 0
                ? $"{GradingDesk.Ready(Profile)} of yours {(GradingDesk.Ready(Profile) == 1 ? "is" : "are")} back from the case room. Collect {(GradingDesk.Ready(Profile) == 1 ? "it" : "them")} below."
                : "Bring me something worth looking at. I grade what's in front of me, not what you hope it is.");
            Show(_tab);
        }

        public void Close()
        {
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
            _root.gameObject.SetActive(false);
            _state.Save();
            Closed?.Invoke();
        }

        private void Say(string line) => _status.text = $"<color=#C9A04A>Odile:</color> <color=#CCD3E0>{line}</color>";

        // ---- Layout -----------------------------------------------------------------------------------

        private void Build()
        {
            _root = Ui.Overlay(transform, "GradingDesk", 15);
            Ui.Image(_root, Ui.Hex("#141821"));
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), Ui.Hex("#0D1017"));
            Ui.Text(Ui.Box(_root, 40, 0, 680, 90), "GAUGE & GILT · GRADING DESK", 34, Ui.Hex("#EFE6D2"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _dp = Ui.Text(Ui.Box(_root, 760, 0, 400, 90), "", 24, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            Ui.BoxButton(_root, 1720, 22, 160, 46, "Back", Close, 18);
            _tabs = Ui.Box(_root, 40, 104, 900, 48);
            _searchBox = Ui.Input(Ui.Box(_root, 40, 168, 820, 44), "Search your copies by name", 16, v => { _search = v; _page = 0; Show(_tab); }).gameObject;
            _list = Ui.Box(_root, 40, 222, 820, PerPage * 70);
            _prev = Ui.BoxButton(_root, 40, 940, 140, 40, "< Prev", () => { _page--; Show(_tab); }).gameObject;
            _pageLabel = Ui.Text(Ui.Box(_root, 190, 940, 480, 40), "", 16, Color.white);
            _next = Ui.BoxButton(_root, 720, 940, 140, 40, "Next >", () => { _page++; Show(_tab); }).gameObject;
            _detail = Ui.Box(_root, 900, 168, 980, 822);
            Ui.Image(_detail, Ui.Hex("#1A1F2B"));
            _status = Ui.Text(Ui.Box(_root, 40, 1010, 1840, 50), "", 16, Color.white, TextAnchor.MiddleLeft);
            _status.supportRichText = true;
        }

        private void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
        }

        private void Show(Tab tab)
        {
            // A different tab (or page) drops anything armed; a redraw of the same screen keeps it, so the second click confirms.
            if (_tab != tab) { _page = 0; _selected = null; _focus = null; _listing = null; _picks.Clear(); _armed = null; }
            _tab = tab;
            _dp.text = $"{Profile.Dp:N0} DP";
            Clear(_tabs);
            int ready = GradingDesk.Ready(Profile);
            var names = new[] { ready > 0 ? $"Grade <color=#7BD88F>({ready})</color>" : "Grade", "Sell", "Used" };
            for (int i = 0; i < 3; i++)
            {
                var t = (Tab)i;
                var button = Ui.BoxButton(_tabs, i * 208, 0, 200, 48, names[i], () => Show(t), 18);
                button.targetGraphic.color = t == tab ? Ui.Hex("#2E3A55") : Ui.Hex("#1E2535");
                button.GetComponentInChildren<Text>().supportRichText = true;
                if (t == tab) Ui.Image(Ui.Box(_tabs, i * 208, 45, 200, 3), Ui.Hex("#C9A04A")).raycastTarget = false;
            }
            Clear(_list);
            Clear(_detail);
            _searchBox.SetActive(tab != Tab.Used);
            _prev.SetActive(tab != Tab.Used);
            _next.SetActive(tab != Tab.Used);
            if (tab == Tab.Used) { ShowMarket(); return; }
            ShowCopies(tab);
        }

        // ---- Copy lists (Grade and Sell) -----------------------------------------------------------------

        private List<CardCopy> CopiesFor(Tab tab)
        {
            var copies = Collection.All.Where(c => !c.AtDesk);
            if (_search.Length > 0)
                copies = copies.Where(c => (_cards.Get(c.Code)?.Name ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            return copies.OrderByDescending(Collection.Value).ThenBy(c => _cards.Get(c.Code)?.Name).ThenBy(c => c.Id).ToList();
        }

        private void ShowCopies(Tab tab)
        {
            var copies = CopiesFor(tab);
            int pages = Math.Max(1, (copies.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = $"{copies.Count} copies · page {_page + 1} of {pages}";
            for (int i = 0; i < PerPage && _page * PerPage + i < copies.Count; i++)
                CopyRow(copies[_page * PerPage + i], i * 70, tab, _page * PerPage + i);
            if (tab == Tab.Grade) GradeDetail();
            else SellDetail();
        }

        private void CopyRow(CardCopy copy, float y, Tab tab, int index)
        {
            var info = _cards.Get(copy.Code);
            var row = Ui.Box(_list, 0, y, 820, 64);
            bool picked = tab == Tab.Grade ? _selected == copy : _picks.Contains(copy);
            Ui.Image(row, picked ? Ui.Hex("#2E3A55") : Ui.Hex("#1E2535"));
            if (picked) Ui.Image(Ui.Box(row, 0, 0, 2, 64), Ui.Hex("#C9A04A")).raycastTarget = false;
            // The Sell tab's ticks are its picks, so the keyboard's highlight is a frame of its own.
            if (tab == Tab.Sell && _keys.Used && copy == _focus) Frame(row, 820, 64);
            float x = 8;
            if (tab == Tab.Sell)
            {
                var box = Ui.Box(row, 8, 18, 28, 28);
                Ui.Image(box, Ui.Hex("#0E1220"));
                Ui.Text(box, picked ? "✓" : "", 20, Ui.Hex("#FFC940"));
                x = 44;
            }
            Thumb(row, new Vector2(x + 20 - 410, 0), new Vector2(40, 58), copy, info);
            var name = Ui.Text(Ui.Box(row, x + 52, 4, 500, 26), info?.Name ?? copy.Code.ToString(), 17, Color.white, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            var line = Ui.Text(Ui.Box(row, x + 52, 32, 520, 26),
                $"{CardFoil.ColoredName(copy.Rarity)} · <color={Grades.Color(copy.Shown)}>{Grades.Glyph(copy.Shown)} {Grades.Describe(copy)}</color>", 13, Color.white, TextAnchor.MiddleLeft);
            line.supportRichText = true;
            line.raycastTarget = false;
            string right;
            if (tab == Tab.Sell)
            {
                var blocked = SellDesk.Blocked(Profile, copy, _state.NeededByDecks);
                right = blocked == null ? $"Pays {Collection.SellPrice(copy):N0} DP\n<size=12><color=#8A8F9A>{SellReason(copy)}</color></size>"
                    : $"<size=13><color=#8A8F9A>{blocked}</color></size>";
            }
            else right = $"{Collection.Value(copy):N0} DP";
            var value = Ui.Text(Ui.Box(row, 600, 0, 208, 64), right, 17, Ui.Hex("#FFC940"), TextAnchor.MiddleRight);
            value.supportRichText = true;
            value.raycastTarget = false;
            Ui.Clickable(row, () =>
            {
                if (tab == Tab.Grade) _selected = copy;
                else if (SellDesk.Blocked(Profile, copy, _state.NeededByDecks) == null && !_picks.Remove(copy)) _picks.Add(copy);
                Show(_tab);
            }, hover: () => _keys.Point(index), style: HoverStyle.Row);
        }

        private string SellReason(CardCopy copy)
        {
            int quarter = Collection.Value(copy) / 4, half = copy.Paid / 2;
            return quarter <= half ? $"25% of {Collection.Value(copy):N0}" : $"capped: half of the {copy.Paid:N0} you paid";
        }

        /// <summary>A copy's card as it looks (rarity, wear), with a slab's rim.</summary>
        private void Thumb(RectTransform parent, Vector2 center, Vector2 size, CardCopy copy, CardInfo? info)
        {
            if (copy.IsSlab) SlabView.Rim(parent, center, size);
            var card = new FieldCard { Code = copy.Code, Type = info?.Type ?? 0, Level = info?.Level ?? 0, Attack = info?.Attack ?? 0, Defense = info?.Defense ?? 0, Position = 0x1, IsPublic = true };
            var view = CardView.Create(parent, center, size, card, info, 0, true, null, offField: true);
            foreach (var art in view.GetComponentsInChildren<RawImage>()) CardFoil.Apply(art, info, copy.Rarity, 0.6f, copy.Look);
            foreach (var g in view.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            if (copy.IsSlab) SlabView.RimGlass(parent, center, size, view);
        }

        // ---- Grade --------------------------------------------------------------------------------------

        private void GradeDetail()
        {
            var copy = _selected;
            if (copy != null && (copy.AtDesk || !Collection.All.Contains(copy))) copy = _selected = null;
            if (copy == null)
                Ui.Text(Ui.Box(_detail, 30, 20, 920, 60), "Pick a copy on the left. A Raw copy goes into a sealed slab with its exact grade; a slab can be cracked and tried again.",
                    17, new Color(0.8f, 0.83f, 0.9f), TextAnchor.UpperLeft);
            else
            {
                var info = _cards.Get(copy.Code);
                if (copy.IsSlab) SlabView.Create(_detail, new Vector2(-490 + 30 + 180, 411 - 20 - 293) , SlabView.Size.Full, copy, info, SetCode(copy)).localScale = Vector3.one * 0.62f;
                else Thumb(_detail, new Vector2(-490 + 30 + 120, 411 - 20 - 175), new Vector2(240, 350), copy, info);
                var facts = Ui.Text(Ui.Box(_detail, 300, 20, 650, 160),
                    $"<b><size=26>{info?.Name}</size></b>\n{CardFoil.ColoredName(copy.Rarity)}\n<color={Grades.Color(copy.Shown)}>{Grades.Glyph(copy.Shown)} {Grades.Describe(copy)}</color>\n"
                    + $"Worth now <color=#FFC940>{Collection.Value(copy):N0} DP</color>", 17, Color.white, TextAnchor.UpperLeft);
                facts.supportRichText = true;

                // What comes back: the odds (a Raw copy's never show its number).
                Ui.Text(Ui.Box(_detail, 300, 190, 600, 20), "WHAT COMES BACK", 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
                var parts = copy.IsSlab
                    ? new[] { (100 - GradingDesk.OddsHigher, $"stays {copy.Slab}: {100 - GradingDesk.OddsHigher}%", "#C9A04A"), (GradingDesk.OddsHigher, $"{Math.Min(10, copy.Slab + 1)}: {GradingDesk.OddsHigher}%", "#7BD88F") }
                    : new[] { (GradingDesk.OddsLower, $"one lower {GradingDesk.OddsLower}%", "#5A6478"), (GradingDesk.OddsSame, $"its true grade {GradingDesk.OddsSame}%", "#C9A04A"), (GradingDesk.OddsHigher, $"one higher {GradingDesk.OddsHigher}%", "#7BD88F") };
                float bx = 300;
                foreach (var (pct, label, color) in parts)
                {
                    float w = 600 * pct / 100f;
                    Ui.Image(Ui.Box(_detail, bx, 214, w - 2, 28), Ui.Hex(color)).raycastTarget = false;
                    var l = Ui.Text(Ui.Box(_detail, bx, 246, Math.Max(w, 120), 20), label, 13, Color.white, TextAnchor.MiddleLeft);
                    l.horizontalOverflow = HorizontalWrapMode.Overflow;
                    bx += w;
                }
                if (copy.IsSlab) Ui.Text(Ui.Box(_detail, 300, 268, 600, 20), $"never lower than {copy.Slab}", 13, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);

                // Standard or express.
                for (int i = 0; i < 2; i++)
                {
                    bool express = i == 1;
                    int fee = GradingDesk.FeeFor(Collection, copy, express);
                    var card = Ui.Box(_detail, 30 + i * 320, 400, 300, 96);
                    Ui.Image(card, _express == express ? Ui.Hex("#C9A04A") : Ui.Hex("#1E2535"));
                    var inner = Ui.Box(card, 2, 2, 296, 92);
                    Ui.Image(inner, _express == express ? Ui.Hex("#2A2418") : Ui.Hex("#1E2535"));
                    var t = Ui.Text(inner, $"<b>{(express ? "Express" : "Standard")}</b> · {fee:N0} DP\n<size=14>{(express ? "back now" : $"back after {GradingDesk.Turnaround} duels")}</size>", 18, Color.white);
                    t.supportRichText = true;
                    Ui.Clickable(card, () => { _express = express; Show(_tab); }, style: HoverStyle.Tile);
                }
                Ui.Text(Ui.Box(_detail, 30, 500, 920, 20), "Fee by value: under 300 → 100 · under 900 → 300 · under 2,000 → 1,000 · 2,000 and up → 2,500", 12, new Color(0.6f, 0.65f, 0.75f), TextAnchor.MiddleLeft);

                var blocked = GradingDesk.Blocked(Profile, copy, _express, _state.NeededByDecks);
                int cost = GradingDesk.FeeFor(Collection, copy, _express);
                var decks = _state.DecksUsing(copy.Code);
                if (blocked == null && decks.Count > 0 && !_express)
                {
                    var warn = Ui.Text(Ui.Box(_detail, 30, 526, 920, 22),
                        $"<color=#E3A21A>▲ {CampaignState.SlugTitle(decks[0])}{(decks.Count > 1 ? $" and {decks.Count - 1} more" : "")} plays another copy while this one's away.</color>", 14, Color.white, TextAnchor.MiddleLeft);
                    warn.supportRichText = true;
                }
                if (blocked != null)
                {
                    var why = Ui.Text(Ui.Box(_detail, 30, 526, 920, 22), $"<color=#FF8A8A>▲ {blocked}</color>", 14, Color.white, TextAnchor.MiddleLeft);
                    why.supportRichText = true;
                }
                var key = $"grade:{copy.Id}:{_express}";
                bool armed = _armed == key && Time.unscaledTime - _armedAt < 5;
                var submit = Ui.BoxButton(_detail, 30, 556, 440, 70,
                    armed ? $"Confirm · {cost:N0} DP" : copy.IsSlab ? $"Crack and resubmit · {cost:N0} DP" : $"Send to Gauge & Gilt · {cost:N0} DP",
                    () => Submit(copy, key), 22);
                submit.targetGraphic.color = blocked != null ? Ui.Hex("#2A3448") : armed ? Ui.Hex("#FFC940") : Ui.Hex("#B8432F");
                if (armed) submit.GetComponentInChildren<Text>().color = Ui.Hex("#161B27");
                if (copy.IsSlab)
                {
                    var crackKey = $"crack:{copy.Id}";
                    bool crackArmed = _armed == crackKey && Time.unscaledTime - _armedAt < 5;
                    Ui.BoxButton(_detail, 490, 556, 300, 70, crackArmed ? "Crack: it becomes Raw again" : "Crack · free", () => Crack(copy, crackKey), 18);
                }
            }
            AtTheDesk();
        }

        private string SetCode(CardCopy copy)
        {
            // The copy's own printing when it's recorded (card versions, phase 2), else its card's first at that rarity.
            if (copy.Set.Length > 0) return copy.Set;
            var printings = _state.Printings;
            if (printings == null) return "";
            var match = printings.Of(copy.Code, _cards.Get(copy.Code)?.Alias ?? 0).FirstOrDefault(p => p.Rarity == copy.Rarity);
            return match.SetCode ?? "";
        }

        /// <summary>Copies at the desk: duels left (pips), or ready to collect.</summary>
        private void AtTheDesk()
        {
            var away = GradingDesk.AtDesk(Profile).OrderBy(c => c.ReadyAt).ToList();
            Ui.Text(Ui.Box(_detail, 30, 650, 920, 20), away.Count == 0 ? "AT THE DESK · nothing yet" : "AT THE DESK", 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            for (int i = 0; i < away.Count && i < 2; i++)
            {
                var copy = away[i];
                var row = Ui.Box(_detail, 30, 676 + i * 62, 920, 56);
                Ui.Image(row, Ui.Hex("#1E2535"));
                var info = _cards.Get(copy.Code);
                Thumb(row, new Vector2(-460 + 24, 0), new Vector2(34, 50), copy, info);
                int left = GradingDesk.DuelsLeft(Profile, copy);
                bool ready = GradingDesk.IsReady(Profile, copy);
                Ui.Text(Ui.Box(row, 52, 0, 520, 56), $"{info?.Name}\n<size=13>{(ready ? "Ready" : $"Standard · back in {left} duel{(left == 1 ? "" : "s")}")}</size>", 16, Color.white, TextAnchor.MiddleLeft).supportRichText = true;
                for (int p = 0; p < GradingDesk.Turnaround; p++)
                    Ui.Image(Ui.Box(row, 600 + p * 20, 21, 14, 14), p < GradingDesk.Turnaround - left ? Ui.Hex("#C9A04A") : Ui.Hex("#3A4256")).raycastTarget = false;
                if (ready) Ui.BoxButton(row, 770, 8, 140, 40, "Collect", () => Collect(copy), 16).targetGraphic.color = Ui.Hex("#B8432F");
            }
            if (away.Count > 2) Ui.Text(Ui.Box(_detail, 30, 800, 920, 20), $"…and {away.Count - 2} more", 13, new Color(0.6f, 0.65f, 0.75f), TextAnchor.MiddleLeft);
        }

        private bool Arm(string key)
        {
            if (_armed == key && Time.unscaledTime - _armedAt < 5) { _armed = null; return true; }
            _armed = key;
            _armedAt = Time.unscaledTime;
            Show(_tab);
            return false;
        }

        private void Submit(CardCopy copy, string key)
        {
            var blocked = GradingDesk.Blocked(Profile, copy, _express, _state.NeededByDecks);
            if (blocked != null) { Say(blocked); Sounds.Play("error"); return; }
            if (!Arm(key)) return;
            var info = _cards.Get(copy.Code);
            int slab = GradingDesk.Submit(Profile, copy, _express, _state.NeededByDecks);
            _state.Save();
            Sounds.Play("click");
            if (slab > 0)
            {
                Say($"Express. Let's see your {info?.Name}.");
                Reveal(copy);
            }
            else
            {
                Say($"Your {info?.Name} goes in the case room. Come back in {GradingDesk.Turnaround} duels.");
                _selected = null;
                Show(_tab);
            }
        }

        private void Crack(CardCopy copy, string key)
        {
            if (!Arm(key)) return;
            GradingDesk.Crack(Profile, copy);
            _state.Save();
            Sounds.Play("damage", 0.5f);
            Say($"Cracked. It's Raw again; a resubmission can't come back under {copy.Floor}.");
            Show(_tab);
        }

        private void Collect(CardCopy copy)
        {
            if (GradingDesk.Collect(Profile, copy) <= 0) return;
            _state.Save();
            Reveal(copy);
        }

        // ---- The reveal: a slab comes back, number first -------------------------------------------------

        private void Reveal(CardCopy copy)
        {
            _reveals.Enqueue(copy);
            if (_overlay == null) StartCoroutine(PlayReveals());
        }

        private IEnumerator PlayReveals()
        {
            while (_reveals.Count > 0)
            {
                var copy = _reveals.Dequeue();
                var info = _cards.Get(copy.Code);
                _overlay = Ui.Stretched("Reveal", _root, Vector2.zero, Vector2.one);
                Ui.Image(_overlay, new Color(0, 0, 0, 0.78f));
                bool skip = false;
                _revealDone = false;
                Ui.Clickable(_overlay, () => skip = true);
                var slab = SlabView.Create(_overlay, new Vector2(0, 40), SlabView.Size.Full, copy, info, SetCode(copy), sealedLabel: true);
                slab.gameObject.AddComponent<CanvasGroup>().alpha = 0;
                Sounds.Play("flip", 0.8f);
                // The slab's own moment (fade, light sweep, the number stamping, the label printing: see SlabReveal); the
                // desk adds the sounds, the number's echo above it, then the worth, the desk's word and the button on.
                var overlay = _overlay;
                yield return SlabReveal.Play(slab, copy, info, SetCode(copy), () => skip, beat =>
                {
                    if (beat == SlabReveal.Beat.Number)
                    {
                        Sounds.Play(copy.Slab >= 10 ? "burst" : "rare", 0.8f);
                        var echo = Ui.Text(Ui.Placed("Echo", overlay, new Vector2(0, 380), new Vector2(300, 140)), copy.Slab.ToString(), 96, Ui.Hex("#F3E3B5"));
                        echo.fontStyle = FontStyle.Bold;
                        StartCoroutine(Fade(echo, 0.5f));
                        return;
                    }
                    Sounds.Play("level_up", 0.5f);
                    var worth = Ui.Text(Ui.Placed("Worth", overlay, new Vector2(0, -300), new Vector2(900, 30)),
                        $"Worth now <color=#FFC940>{Collection.Value(copy):N0} DP</color>", 18, Color.white);
                    worth.supportRichText = true;
                    Say(copy.Slab >= 10 ? "Gem Mint. I don't say that often." : copy.Slab >= 9 ? "Mint. A clean card." : copy.Slab >= 7 ? "Near Mint. Honest wear." : "It is what it is. The slab keeps it that way.");
                    Ui.BoxButton(overlay, 760, 960, 400, 60, _reveals.Count > 0 ? $"Next slab ({_reveals.Count} left)" : "To collection", () => _revealDone = true, 20).targetGraphic.color = Ui.Hex("#B8432F");
                });
                while (!_revealDone && _overlay != null) yield return null;
                if (_overlay == null) yield break;
                Destroy(_overlay.gameObject);
                _overlay = null;
            }
            Show(_tab);
        }

        /// <summary>Fades a text out over <paramref name="seconds"/>, then removes it (the stamped number's echo).</summary>
        private static IEnumerator Fade(Text text, float seconds)
        {
            for (float t = 0; t < seconds && text != null; t += Time.unscaledDeltaTime)
            {
                text.color = new Color(text.color.r, text.color.g, text.color.b, 1 - t / seconds);
                yield return null;
            }
            if (text != null) Destroy(text.gameObject);
        }

        // ---- Sell ---------------------------------------------------------------------------------------

        private void SellDetail()
        {
            var picks = _picks.Where(c => Collection.All.Contains(c)).ToList();
            Ui.Text(Ui.Box(_detail, 30, 20, 920, 24), picks.Count == 0 ? "SELLING NOTHING YET · tick copies on the left" : $"SELLING {picks.Count} COP{(picks.Count == 1 ? "Y" : "IES")}",
                13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            for (int i = 0; i < picks.Count && i < 12; i++)
                Ui.Text(Ui.Box(_detail, 30, 52 + i * 30, 920, 28), $"{_cards.Get(picks[i].Code)?.Name} · {CardFoil.ColoredName(picks[i].Rarity)} · pays <color=#FFC940>{Collection.SellPrice(picks[i]):N0} DP</color>",
                    16, Color.white, TextAnchor.MiddleLeft).supportRichText = true;
            int total = picks.Sum(Collection.SellPrice);
            Ui.Text(Ui.Box(_detail, 30, 520, 920, 40), $"You get {total:N0} DP", 28, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(_detail, 30, 562, 920, 24), $"DP after: {Profile.Dp + total:N0}", 16, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(_detail, 30, 600, 920, 40), "The desk pays 25% of a copy's value, never more than half of what you paid for it. Gifts (your starter, the trader, rewards) aren't for sale; "
                + "neither is a copy one of your decks needs.", 13, new Color(0.54f, 0.56f, 0.6f), TextAnchor.UpperLeft);
            if (picks.Count == 0) return;
            var key = "sell:" + string.Join(",", picks.Select(c => c.Id).OrderBy(x => x));
            bool armed = _armed == key && Time.unscaledTime - _armedAt < 5;
            var sell = Ui.BoxButton(_detail, 30, 720, 440, 70, armed ? $"Confirm: sell {picks.Count}" : $"Sell {picks.Count} cop{(picks.Count == 1 ? "y" : "ies")} · +{total:N0} DP", () => Sell(picks, key), 22);
            sell.targetGraphic.color = armed ? Ui.Hex("#FFC940") : Ui.Hex("#B8432F");
            if (armed) sell.GetComponentInChildren<Text>().color = Ui.Hex("#161B27");
        }

        private void Sell(List<CardCopy> picks, string key)
        {
            if (!Arm(key)) return;
            int got = picks.Sum(c => SellDesk.Sell(Profile, c, _state.NeededByDecks));
            _picks.Clear();
            _state.Save();
            Sounds.Play("dp", 0.7f);
            Say($"{got:N0} DP. I'll find them good homes.");
            Show(_tab);
        }

        // ---- Used market --------------------------------------------------------------------------------

        private void ShowMarket()
        {
            _pageLabel.text = "";
            var listings = _state.MarketListings();
            int refresh = UsedMarket.DuelsToRefresh(Profile);
            var countdown = Ui.Text(Ui.Box(_tabs, 640, 0, 260, 48), $"New listings in {refresh} duel{(refresh == 1 ? "" : "s")}", 16, Color.white, TextAnchor.MiddleLeft);
            countdown.horizontalOverflow = HorizontalWrapMode.Overflow;
            // The grid takes the list's place and more: 6 across, 4 down.
            for (int i = 0; i < listings.Count; i++)
            {
                var l = listings[i];
                var info = _cards.Get(l.Copy.Code);
                var tile = Ui.Box(_list, (i % 6) * 136, (i / 6) * 176 - 54, 130, 170);
                Ui.Image(tile, _listing?.Slot == l.Slot ? Ui.Hex("#2E3A55") : Ui.Hex("#1E2535"));
                var cardAt = new Vector2(0, 30);
                Thumb(tile, cardAt, new Vector2(70, 102), l.Copy, info);
                // A listing printing an original text (spec 4.2): "ORIGINAL TEXT · 2004" over the card, above its stats.
                CardDetail.OriginalBand(tile, l.Copy, 6, 68, 118);
                if (l.Wanted)
                {
                    var star = Ui.Placed("Wanted", tile, cardAt + new Vector2(30, 46), new Vector2(20, 20)).gameObject.AddComponent<RawImage>();
                    star.texture = Icons.For("star");
                    star.color = Ui.Hex("#FFD24A");
                    star.raycastTarget = false;
                }
                var chip = Ui.Text(Ui.Box(tile, 4, 104, 122, 18),
                    l.Copy.IsSlab ? $"<color=#2B2118>{Grades.Glyph(l.Copy.Slab)} {Grades.Label(l.Copy.Slab)}</color>" : $"<color={Grades.Color(l.Copy.Shown)}>{Grades.Glyph(l.Copy.Shown)} {Grades.Word(l.Copy.Shown)}</color>", 11, Color.white);
                chip.supportRichText = true;
                if (l.Copy.IsSlab) Ui.Image(Ui.Box(tile, 24, 104, 82, 18), Ui.Hex("#EFE6D2")).transform.SetSiblingIndex(chip.transform.GetSiblingIndex());
                var name = Ui.Text(Ui.Box(tile, 4, 122, 122, 28), info?.Name ?? "", 11, Color.white, TextAnchor.UpperCenter);
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 8;
                name.resizeTextMaxSize = 11;
                Ui.Text(Ui.Box(tile, 4, 150, 122, 18), l.Sold ? "SOLD" : $"{l.Price:N0} DP", 14, l.Sold ? Ui.Hex("#8A8F9A") : Ui.Hex("#FFC940")).fontStyle = FontStyle.Bold;
                if (l.Sold) tile.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
                var pick = l;
                int at = i;
                Ui.Clickable(tile, () => { _listing = pick; Show(_tab); }, hover: () => _keys.Point(at), style: HoverStyle.Tile);
            }
            MarketDetail(listings);
        }

        private void MarketDetail(List<Listing> listings)
        {
            var l = _listing == null ? null : listings.FirstOrDefault(x => x.Slot == _listing.Slot);
            Ui.BoxButton(_detail, 30, 740, 300, 46, "Special order…", SpecialOrder, 18);
            if (l == null)
            {
                Ui.Text(Ui.Box(_detail, 30, 20, 920, 80), "Second-hand copies of real printings at their value: more of them worn than a pack's, some in slabs. "
                    + "Stars mark cards on your wishlist.", 17, new Color(0.8f, 0.83f, 0.9f), TextAnchor.UpperLeft);
                return;
            }
            var info = _cards.Get(l.Copy.Code);
            if (l.Copy.IsSlab) SlabView.Create(_detail, new Vector2(-490 + 30 + 180, 411 - 20 - 293), SlabView.Size.Full, l.Copy, info, SetCode(l.Copy)).localScale = Vector3.one * 0.62f;
            else Thumb(_detail, new Vector2(-490 + 30 + 120, 411 - 20 - 175), new Vector2(240, 350), l.Copy, info);
            var facts = Ui.Text(Ui.Box(_detail, 300, 20, 650, 200),
                $"<b><size=26>{info?.Name}</size></b>\n{(CardDetail.PrintingLine(l.Copy) is { Length: > 0 } printed ? printed : SetCode(l.Copy))} · {CardFoil.ColoredName(l.Copy.Rarity)}\n"
                + $"<color={Grades.Color(l.Copy.Shown)}>{Grades.Glyph(l.Copy.Shown)} {Grades.Describe(l.Copy)}</color>\nPrice = value <color=#FFC940>{l.Price:N0} DP</color>",
                17, Color.white, TextAnchor.UpperLeft);
            facts.supportRichText = true;
            var blocked = UsedMarket.Blocked(Profile, l);
            if (blocked != null) { Ui.Text(Ui.Box(_detail, 300, 230, 650, 24), $"<color=#FF8A8A>▲ {blocked}</color>", 14, Color.white, TextAnchor.MiddleLeft).supportRichText = true; return; }
            var key = $"buy:{l.Slot}:{UsedMarket.Epoch(Profile)}";
            bool armed = _armed == key && Time.unscaledTime - _armedAt < 5;
            var buy = Ui.BoxButton(_detail, 30, 640, 440, 70, armed ? $"Confirm · {l.Price:N0} DP" : $"Buy · {l.Price:N0} DP", () =>
            {
                if (!Arm(key)) return;
                BuyListing(l);
            }, 22);
            buy.targetGraphic.color = armed ? Ui.Hex("#FFC940") : Ui.Hex("#B8432F");
            if (armed) buy.GetComponentInChildren<Text>().color = Ui.Hex("#161B27");
        }

        /// <summary>Buys a used listing (the Buy button's second click, or a confirmed Enter): UsedMarket.Buy, saved, Gus's word.</summary>
        private void BuyListing(Listing l)
        {
            int sold = UsedMarket.Buy(Profile, l);
            _state.Save();
            Sounds.Play("dp", 0.7f);
            Say(sold > 0 ? $"Yours. Your least valuable Raw copy went to the bin for {sold:N0} DP." : "Yours. Look after it.");
            // A set it completed: Gus frames a card (card unlocks).
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{sets}</color>";
            Show(_tab);
        }

        /// <summary>Special order: search a card, pick a printing (not its commonest), pay its basis plus half.</summary>
        private void SpecialOrder()
        {
            if (_state.Printings == null) return;
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = Ui.Stretched("Order", _root, Vector2.zero, Vector2.one);
            Ui.Image(_overlay, new Color(0, 0, 0, 0.75f));
            var panel = Ui.Box(_overlay, 510, 230, 900, 620);
            Ui.Image(panel, Ui.Hex("#1A1F2B"));
            Ui.Text(Ui.Box(panel, 30, 16, 840, 40), "Special order", 28, Color.white, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(panel, 30, 56, 840, 24), "Any printing but a card's commonest, Raw Near Mint, at its value plus half. It's here as soon as you pay.", 14, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);
            var results = Ui.Box(panel, 30, 140, 840, 400);
            void Find(string text)
            {
                Clear(results);
                if (text.Length < 2) return;
                var matches = _state.MarketPool.Select(c => _cards.Get(c)).OfType<CardInfo>()
                    .Where(c => c.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 && UsedMarket.Orderable(c.Code, _state.Printings!, _cards).Count > 0)
                    .OrderBy(c => c.Name).Take(6).ToList();
                for (int i = 0; i < matches.Count; i++)
                {
                    var info = matches[i];
                    Ui.Text(Ui.Box(results, 0, i * 66, 300, 60), info.Name, 16, Color.white, TextAnchor.MiddleLeft);
                    var options = UsedMarket.Orderable(info.Code, _state.Printings!, _cards).Take(4).ToList();
                    for (int k = 0; k < options.Count; k++)
                    {
                        var rarity = options[k];
                        int price = UsedMarket.OrderPrice(info.Code, rarity, _state.Printings!, _cards);
                        var key = $"order:{info.Code}:{rarity}";
                        bool armed = _armed == key && Time.unscaledTime - _armedAt < 5;
                        var b = Ui.BoxButton(results, 310 + k * 134, i * 66 + 6, 128, 48, armed ? "Confirm" : $"{Rarities.Name(rarity)}\n{price:N0} DP", () =>
                        {
                            if (!(_armed == key && Time.unscaledTime - _armedAt < 5)) { _armed = key; _armedAt = Time.unscaledTime; Find(text); return; }
                            _armed = null;
                            var copy = UsedMarket.Order(Profile, info.Code, rarity, _state.ShopYear, _state.Printings!, _cards);
                            _state.Save();
                            Say(copy != null ? $"One {Rarities.Name(rarity)} {info.Name}, ordered and here." : "I can't place that order: not enough DP, or you have three better copies.");
                            if (copy != null && _state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{sets}</color>";
                            Destroy(_overlay!.gameObject);
                            _overlay = null;
                            Show(_tab);
                        }, 12);
                        b.GetComponentInChildren<Text>().resizeTextForBestFit = true;
                    }
                }
            }
            Ui.Input(Ui.Box(panel, 30, 90, 840, 40), "Search a card the campaign's duelists play", 16, Find);
            Ui.BoxButton(panel, 720, 560, 150, 44, "Close", () => { Destroy(_overlay!.gameObject); _overlay = null; }, 16);
        }

        // ---- The keyboard ------------------------------------------------------------------------------

        // Arrows move the highlight, Enter clicks it (ArrowNav): Grade's copies (up/down across pages, left/right the page)
        // select as a click does; Sell's move a frame and Enter ticks or unticks the copy; the used market's grid goes along
        // its rows and between them. Enter on a listing asks "Buy X for N DP?" (KeyConfirm: a second Enter buys as the Buy
        // button does, Escape cancels); grading and selling stay their buttons with their Confirm.
        private readonly ArrowNav _keys = new ArrowNav();
        private readonly KeyConfirm _confirm = new KeyConfirm();

        /// <summary>For checks: the keyboard's buy question as shown ("" when none is up).</summary>
        public string DebugConfirm => _confirm.Question;
        /// <summary>The Sell tab's highlighted copy (its ticks are the picks).</summary>
        private CardCopy? _focus;

        private void Update()
        {
            if (!IsOpen) { _confirm.Close(); return; }
            ArrowNav.DropButtonFocus(_root); // a clicked Confirm mustn't take Enter
            if (_overlay != null) { _confirm.Close(); return; } // a slab reveal or a special order is up
            if (_confirm.Update()) return;
            var k = _keys.Read(_root, _list);
            if (!k.Any) return;
            if (_tab == Tab.Used) { MarketKeys(k); return; }
            var copies = CopiesFor(_tab);
            if (copies.Count == 0) return;
            var current = _tab == Tab.Grade ? _selected : _focus;
            int at = current == null ? -1 : copies.IndexOf(current);
            if (k.Move != Vector2Int.zero) at = _keys.From(at, copies.Count); // on from the row the mouse last pointed at
            int to = ArrowNav.ListStep(copies.Count, PerPage, _page, at, k.Move, _ => true);
            if (to >= 0)
            {
                _page = to / PerPage;
                if (_tab == Tab.Grade) _selected = copies[to];
                else _focus = copies[to];
                at = to;
            }
            if (k.Enter && at >= 0 && at / PerPage == _page && _tab == Tab.Sell)
            {
                // As a click on the row: tick it (if the desk would buy it), or untick it.
                var copy = copies[at];
                if (SellDesk.Blocked(Profile, copy, _state.NeededByDecks) == null && !_picks.Remove(copy)) _picks.Add(copy);
            }
            Show(_tab);
        }

        private void MarketKeys(ArrowNav.Keys k)
        {
            var listings = _state.MarketListings();
            if (listings.Count == 0) return;
            int at = _listing == null ? -1 : listings.FindIndex(l => l.Slot == _listing.Slot);
            if (k.Move != Vector2Int.zero) at = _keys.From(at, listings.Count); // on from the tile the mouse last pointed at
            bool had = at >= 0;
            if (at < 0) at = 0; // the first press lands on the first tile
            else if (k.Move != Vector2Int.zero)
            {
                // The tiles' layout (y down): 6 across, a row every 176.
                var points = listings.Select((_, i) => new Vector2((i % 6) * 136, (i / 6) * 176)).ToList();
                int next = ArrowNav.Step(points, at, k.Move, 40);
                if (next >= 0) at = next;
            }
            _listing = listings[at];
            Show(_tab);
            // Enter on the highlighted listing: the purchase asked first (a blocked one shows why in its detail).
            var l = listings[at];
            if (k.Enter && had && k.Move == Vector2Int.zero && UsedMarket.Blocked(Profile, l) == null)
                _confirm.Open(_root, $"Buy <b>{_cards.Get(l.Copy.Code)?.Name}</b> ({Grades.Describe(l.Copy)}) for {l.Price:N0} DP?", "Buy", () => BuyListing(l));
        }

        /// <summary>A gold frame around a row (the keyboard's highlight where the fill already means something else).</summary>
        private static void Frame(RectTransform row, float w, float h)
        {
            var gold = Ui.Hex("#FFC940");
            var frame = Ui.Box(row, 0, 0, w, h);
            frame.name = "KeyHighlight";
            frame.gameObject.AddComponent<KeyFrame>(); // hidden while the mouse is in use
            foreach (var (x, y, bw, bh) in new[] { (0f, 0f, w, 2f), (0f, h - 2, w, 2f), (0f, 0f, 2f, h), (w - 2, 0f, 2f, h) })
                Ui.Image(Ui.Box(frame, x, y, bw, bh), gold).raycastTarget = false;
        }

        /// <summary>For checks: the copy the Grade tab has selected, the Sell tab's highlighted one, the listing selected (slot, -1 none), and the Sell tab's picks.</summary>
        public (CardCopy? selected, CardCopy? focus, int listing, int picks) DebugKeys => (_selected, _focus, _listing?.Slot ?? -1, _picks.Count);

        /// <summary>For checks: the tab shown (0 Grade, 1 Sell, 2 Used).</summary>
        public int DebugTab => (int)_tab;

        // ---- For checks ---------------------------------------------------------------------------------

        public void DebugShow(int tab) { Open(); Show((Tab)tab); }

        /// <summary>For checks: grades the most valuable Raw copy (express, armed and confirmed); returns its slab grade, or 0.</summary>
        public int DebugGradeBest()
        {
            var copy = Collection.All.Where(c => !c.IsSlab && !c.AtDesk && GradingDesk.Blocked(Profile, c, true, _state.NeededByDecks) == null)
                .OrderByDescending(Collection.Value).FirstOrDefault();
            if (copy == null) return 0;
            _selected = copy;
            _express = true;
            Show(Tab.Grade);
            var key = $"grade:{copy.Id}:{_express}";
            Submit(copy, key);
            Submit(copy, key);
            return copy.Slab;
        }

        public bool DebugRevealing => _overlay != null;
        private bool _revealDone;

        /// <summary>For checks: "To collection" on the reveal.</summary>
        public void DebugFinishReveal() => _revealDone = true;

        /// <summary>For checks: the ORIGINAL TEXT bands drawn on the used market's listings.</summary>
        public int DebugOriginalBands => _list.GetComponentsInChildren<RectTransform>().Count(r => r.name == "Original");

        /// <summary>For checks: selects the first listing you can buy.</summary>
        public void DebugPickListing()
        {
            _listing = _state.MarketListings().FirstOrDefault(l => UsedMarket.Blocked(Profile, l) == null);
            Show(Tab.Used);
        }

        /// <summary>For checks: selects a listing by its slot.</summary>
        public void DebugPickListing(int slot)
        {
            _listing = _state.MarketListings().FirstOrDefault(l => l.Slot == slot);
            Show(Tab.Used);
        }

        /// <summary>For checks: the detail panel's text (no markup).</summary>
        public string DebugDetailText() =>
            string.Join("\n", _detail.GetComponentsInChildren<Text>().Select(t => System.Text.RegularExpressions.Regex.Replace(t.text, "<.*?>", "")));

        /// <summary>For checks: ticks the first copy the desk would buy.</summary>
        public int DebugPickSale()
        {
            var copy = Collection.All.FirstOrDefault(c => SellDesk.Blocked(Profile, c, _state.NeededByDecks) == null);
            if (copy != null) _picks.Add(copy);
            Show(Tab.Sell);
            return copy == null ? 0 : Collection.SellPrice(copy);
        }
        public string DebugStatus => _status.text;
    }
}
