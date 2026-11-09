using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Presentation;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Draws the duel mat, both players' cards, a live log, card details, and spectator controls.
    /// Player 0 sits at the bottom, player 1 at the top (mirrored, as if across the table).
    /// </summary>
    public sealed partial class BoardView : MonoBehaviour
    {
        private const float Column = 128f; // wide enough for a sideways (defense) card
        private const float Row = 140f;
        private const float HandY = 2 * Row + 150f;
        private const int MaxLogLines = 34;
        private static readonly Vector2 HandCardSize = new Vector2(90, 126);

        private ICardDataProvider _cards = null!;
        private string[] _names = { "", "" };

        /// <summary>A short word for whose card it is: YOU / OPP when you play, else the duelist's first name.</summary>
        private string OwnerTag(int controller)
        {
            if (HumanSeat >= 0) return controller == HumanSeat ? "YOU" : "OPP";
            var name = controller >= 0 && controller < _names.Length ? _names[controller].Split(' ', '&')[0] : "";
            return (name.Length > 6 ? name.Substring(0, 6) : name).ToUpperInvariant();
        }
        private string[] _deckNames = { "", "" };
        private Text _bannerQuote = null!;
        private RectTransform _bannerRoot = null!;
        private RectTransform _field = null!;
        private RectTransform _cardLayer = null!;
        private Text _header = null!;
        private Text[] _lifePoints = new Text[2];
        private Text _log = null!;
        private CardDetail _detail = null!;
        private RawImage _phaseDisc = null!, _phaseHalo = null!;
        private readonly RectTransform[] _plates = new RectTransform[2];

        /// <summary>The poster for your plate (null: the emblem); asked each time your plate is drawn.</summary>
        public Func<Poster?>? YourPoster { get; set; }

        /// <summary>Shows a duelist's portrait by their LP ("" or "you": your poster or the emblem).</summary>
        public void SetPortrait(int player, string characterId)
        {
            var plate = _plates[player];
            for (int i = plate.childCount - 1; i >= 0; i--) Destroy(plate.GetChild(i).gameObject);
            _portraitId[player] = characterId;
            _portraitBase[player] = null;
            // The mood light sits behind the character: a glow that flashes on a hit or a big play.
            var mood = Ui.Placed("Mood", plate, new Vector2(0, -20), new Vector2(340, 340)).gameObject.AddComponent<RawImage>();
            mood.texture = BoardFx.Textures.Glow;
            mood.color = new Color(0, 0, 0, 0);
            mood.raycastTarget = false;
            _mood[player] = mood;
            if (characterId == "you")
            {
                // Your poster (picked in the Locker), else the emblem.
                // Smaller than in the Locker and raised, so its foot ends behind the name tab and the LP stays on the dark plate.
                if (YourPoster?.Invoke() is Poster poster) PosterView.Create(plate, poster, new Vector2(14, 34), tagline: false).localScale = Vector3.one * 0.85f;
                else Emblem(plate);
            }
            else if (string.IsNullOrEmpty(characterId) || !Portraits.Place(plate, characterId, mirror: player == 1))
                Ui.Text(Ui.Placed("You", plate, new Vector2(0, -10), new Vector2(300, 120)), "?", 64, new Color(1, 1, 1, 0.16f)).fontStyle = FontStyle.Bold;
            // The portrait just placed is the last child (the old one is only destroyed at the end of the frame).
            else _portraitBase[player] = plate.GetChild(plate.childCount - 1).GetComponent<RawImage>();
            _moodUntil[player] = 0;
        }

        /// <summary>
        /// Your plate's crest where a duelist has a portrait: a gold ring around the card back's sparkle, on a soft glow.
        /// You're the player, so the plate doesn't pick a face for you.
        /// </summary>
        private static void Emblem(RectTransform plate)
        {
            RawImage Layer(string name, Texture texture, Vector2 size, Color color)
            {
                var image = Ui.Placed(name, plate, new Vector2(0, -6), size).gameObject.AddComponent<RawImage>();
                image.texture = texture;
                image.color = color;
                image.raycastTarget = false;
                return image;
            }
            Layer("EmblemGlow", BoardFx.Textures.Glow, new Vector2(260, 260), new Color(1f, 0.79f, 0.25f, 0.18f));
            Layer("EmblemRing", BoardFx.Textures.Ring, new Vector2(170, 170), Ui.Hex("#C8901E"));
            Layer("EmblemSparkle", CardView.Sparkle, new Vector2(96, 96), Ui.Hex("#FFC940"));
        }

        // ---- Stat changes: "-800 ATK" over a monster whose ATK or DEF just changed --------------------------------

        private readonly Dictionary<(int, int), (uint code, int atk, int def)> _stats = new Dictionary<(int, int), (uint, int, int)>();

        private void StatChanges(BoardState board)
        {
            for (int p = 0; p < 2; p++)
                for (int z = 0; z < board.Players[p].Monsters.Count; z++)
                {
                    var key = (p, z);
                    if (board.Players[p].Monsters[z] is not FieldCard m || !m.IsFaceUp) { _stats.Remove(key); continue; }
                    if (_stats.TryGetValue(key, out var was) && was.code == m.Code)
                    {
                        var at = MonsterZone(p, z);
                        // A lesson's pointer framing this card would hide the numbers (it's drawn over effects): beside it.
                        if (_pointer != null && _pointer.gameObject.activeInHierarchy && Vector2.Distance(_pointer.anchoredPosition, at) < 30)
                            at += new Vector2(CardView.FieldSize.x / 2 + 82, -30);
                        int da = m.Attack - was.atk, dd = m.Defense - was.def;
                        if (da != 0) Fx.Floater(at + new Vector2(0, 8), $"{(da > 0 ? "+" : "")}{da} ATK", da > 0 ? Ui.Hex("#6FC3FF") : Ui.Hex("#FF7070"));
                        if (dd != 0 && (m.Type & CardType.Link) == 0) Fx.Floater(at - new Vector2(0, 22), $"{(dd > 0 ? "+" : "")}{dd} DEF", dd > 0 ? Ui.Hex("#6FC3FF") : Ui.Hex("#FF7070"));
                    }
                    _stats[key] = (m.Code, m.Attack, m.Defense);
                }
        }

        // ---- The chain panel: every link, who played it, what it targets, in resolution order -----------------

        private RectTransform? _chainPanel;
        private string _chainKey = "";

        /// <summary>
        /// Draws the chain at the top right of the field: the newest link on top (the first to resolve), each with its
        /// owner's color, its card, its target or effect line, and its state (resolving, resolved, negated) as a word
        /// and a symbol, not just a color. Hidden while there's no chain.
        /// </summary>
        /// <summary>How many links the chain panel shows (0: no chain going).</summary>
        public int ChainLinksShown { get; private set; }

        public void ShowChain(IReadOnlyList<Ygo.Engine.Presentation.TrackedLink> links)
        {
            var key = string.Join(";", links.Select(l => $"{l.Number}:{l.Code}:{l.State}:{l.Targets.Count}"));
            ChainLinksShown = links.Count;
            _chainTopCode = links.Count == 0 ? 0 : links.OrderByDescending(l => l.Number).First().Code;
            if (key == _chainKey) return;
            _chainKey = key;
            if (_chainPanel != null) Destroy(_chainPanel.gameObject);
            _chainPanel = null;
            if (links.Count == 0) return;
            if (links.Count >= 2) ChainShown?.Invoke(links.Count);
            const float rowH = 46, tallRowH = 60, width = 360, textW = 186, gap = 4;
            var ordered = links.OrderByDescending(l => l.Number).ToList();
            // Each row's second line first (its targets, else for the newest link what the effect does): a row grows to two
            // lines when the line needs them, so a second target is never cut off after its comma.
            var lines = ordered.Select((l, i) => ChainLine(l, i == 0)).ToList();
            float height = 28;
            var panel = Ui.Placed("ChainPanel", _field, Vector2.zero, new Vector2(width, 100));
            _chainPanel = panel;
            // Under the effect callouts at the top right, a size up from its rows (UpdateCallouts keeps it there).
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.localScale = Vector3.one * ChainPanelScale;
            panel.anchoredPosition = new Vector2(-ColumnMargin + _columnDodge, -(_chainY < 0 ? CalloutTop() : _chainY));
            // Opaque, the panel and its rows: nothing under them (a callout sliding by, the board) shows through.
            Ui.Image(panel, new Color(0.04f, 0.06f, 0.1f, 1f)).raycastTarget = false;
            var head = Ui.Text(Ui.Placed("Head", panel, Vector2.zero, new Vector2(width - 16, 24)),
                $"CHAIN  <color=#8A93A6><size=11>resolves top to bottom</size></color>", 13, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            head.supportRichText = true;
            head.raycastTarget = false;
            var rows = new List<(RectTransform row, float h)>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var l = ordered[i];
                var row = Ui.Placed($"Link{l.Number}", panel, Vector2.zero, new Vector2(width - 8, rowH - gap));
                bool resolving = l.State == LinkState.Resolving;
                Ui.Image(row, resolving ? new Color(0.25f, 0.2f, 0.06f, 1f) : new Color(0.1f, 0.13f, 0.2f, 1f)).raycastTarget = false;
                var info = _cards.Get(l.Code);
                bool negated = l.State == LinkState.Negated || l.State == LinkState.EffectNegated;
                // The line under the name: one line at 11 down to 9, else two lines (the row grows) at 10, trimmed to fit.
                var sub = Ui.Text(Ui.Placed("Line", row, Vector2.zero, new Vector2(textW, 18)), "", 11, new Color(0.78f, 0.82f, 0.9f), TextAnchor.MiddleLeft);
                sub.raycastTarget = false;
                var (lineText, lineSize, twoLines) = FitChainLine(sub, lines[i].text, lines[i].targets, textW);
                sub.text = lineText;
                sub.fontSize = lineSize;
                float h = twoLines ? tallRowH : rowH;
                float inner = h - gap;
                row.sizeDelta = new Vector2(width - 8, inner);
                var subRt = sub.rectTransform;
                subRt.sizeDelta = new Vector2(textW, twoLines ? 28 : 18);
                subRt.anchoredPosition = new Vector2(12, twoLines ? -inner / 2 + 17 : -10 - (inner - (rowH - gap)) / 2);
                var edge = Ui.Placed("Owner", row, new Vector2(-(width - 8) / 2 + 2, 0), new Vector2(4, inner));
                Ui.Image(edge, l.Controller == HumanSeat || (HumanSeat < 0 && l.Controller == 0) ? Ui.Hex("#4FA3FF") : Ui.Hex("#FF5A5A")).raycastTarget = false;
                var num = Ui.Text(Ui.Placed("N", row, new Vector2(-(width - 8) / 2 + 20, 5), new Vector2(28, 24)), l.Number.ToString(), 18, Ui.Hex("#FFC940"));
                num.fontStyle = FontStyle.Bold;
                num.raycastTarget = false;
                // Whose link it is in words too, under its number (the stripe's color isn't the only sign).
                var who = Ui.Text(Ui.Placed("Who", row, new Vector2(-(width - 8) / 2 + 20, -13), new Vector2(34, 12)), OwnerTag(l.Controller), 8, new Color(0.8f, 0.84f, 0.92f));
                who.fontStyle = FontStyle.Bold;
                who.raycastTarget = false;
                var thumb = Ui.Placed("Art", row, new Vector2(-(width - 8) / 2 + 52, 0), new Vector2(27, 39));
                var art = info != null ? CardView.Art?.Get(l.Code) : null;
                if (art != null) { var raw = thumb.gameObject.AddComponent<RawImage>(); raw.texture = art; raw.raycastTarget = false; }
                else Ui.Image(thumb, info != null ? CardView.FrameColor(info.Type) : CardView.BackFill).raycastTarget = false;
                // The name on one line: a size down to 9, then shortened with "…" (never a second, cut-off line).
                var name = Ui.Text(Ui.Placed("Name", row, new Vector2(12, inner / 2 - 12), new Vector2(textW, 20)), info?.Name ?? "a card", 13,
                    negated ? new Color(1, 1, 1, 0.45f) : Color.white, TextAnchor.MiddleLeft);
                name.fontStyle = FontStyle.Bold;
                name.raycastTarget = false;
                (name.text, name.fontSize) = FitOneLine(name, name.text, textW, 13, 9);
                var (mark, color) = l.State switch
                {
                    LinkState.Resolving => ("▶ resolving", "#FFC940"),
                    LinkState.Resolved => ("✓ done", "#7BD88F"),
                    LinkState.Negated => ("✗ negated", "#FF7070"),
                    LinkState.EffectNegated => ("✗ effect\nnegated", "#FF7070"),
                    LinkState.Activating => ("activating", "#9FD8FF"),
                    _ => ("waiting", "#8A93A6"),
                };
                var state = Ui.Text(Ui.Placed("State", row, new Vector2((width - 8) / 2 - 36, 0), new Vector2(68, 36)), mark, 11, Ui.Hex(color));
                state.fontStyle = FontStyle.Bold;
                state.raycastTarget = false;
                rows.Add((row, h));
                height += h;
            }
            // Stacked from the top, each row its own height.
            panel.sizeDelta = new Vector2(width, height);
            ((RectTransform)head.transform).anchoredPosition = new Vector2(0, height / 2 - 14);
            float top = height / 2 - 28;
            foreach (var (row, h) in rows)
            {
                row.anchoredPosition = new Vector2(0, top - h / 2);
                top -= h;
            }
            panel.SetAsLastSibling();
        }

        /// <summary>The newest link's card while a chain is going (0: none).</summary>
        private uint _chainTopCode;

        /// <summary>
        /// What a chain prompt answers, for its dialog: the newest chain link, else this turn's attack or summon when that
        /// is the very last thing that happened; "" when the board can't tell.
        /// </summary>
        private string RespondingTo()
        {
            if (ChainLinksShown > 0 && _chainTopCode != 0 && _cards.Get(_chainTopCode) is { } link) return $"Responding to: {link.Name}";
            var last = _history?.Entries.LastOrDefault(e => e.Kind != HistoryKind.Turn);
            if (last == null || _lastBoard == null || last.Turn != _lastBoard.Turn) return "";
            return last.Kind is HistoryKind.Attack or HistoryKind.Summon ? last.Text : "";
        }

        private static string FirstLine(string text)
        {
            var line = text.Split('\n')[0];
            return line.Length > 90 ? line.Substring(0, 88) + "…" : line;
        }

        /// <summary>
        /// A link's second line: "→ " and its targets (and the target names themselves, kept apart: a name can hold a comma,
        /// "Lilith, Lady of Lament"), else (the newest link) what its effect does; "" for neither.
        /// </summary>
        private (string text, List<string> targets) ChainLine(Ygo.Engine.Presentation.TrackedLink l, bool newest)
        {
            var targets = l.Targets.Select(t => t.Code != 0 ? _cards.Get(t.Code)?.Name ?? "a card" : (t.Location & 0x7f) == Location.Hand ? "a card in hand" : "a set card").ToList();
            if (targets.Count > 0) return ("→ " + string.Join(", ", targets), targets);
            var info = _cards.Get(l.Code);
            if (!newest || info == null) return ("", targets);
            return (Ygo.Engine.Interactive.DuelText.Describe(l.Description, _cards) is string d && d.Length > 0 ? d : FirstLine(info.Text), targets);
        }

        /// <summary>
        /// The chain row's second line fitted to <paramref name="width"/>: one line at 11 down to 9; else two lines at 10,
        /// a list of targets cut to whole names and "+N more", other text cut with "…". Never a line the box truncates.
        /// </summary>
        private static (string text, int size, bool twoLines) FitChainLine(Text t, string line, List<string> targets, float width)
        {
            if (line.Length == 0) return ("", 11, false);
            for (int size = 11; size >= 9; size--)
                if (CardView.TextWidth(t, line, size) <= width) return (line, size, false);
            const int two = 10;
            float room = 2 * (CardView.TextHeight(t, "Ag", two, width) + 1);
            bool Fits(string s) => CardView.TextHeight(t, s, two, width) <= room;
            if (Fits(line)) return (line, two, true);
            if (targets.Count > 1)
            {
                for (int keep = targets.Count - 1; keep >= 1; keep--)
                {
                    var cut = "→ " + string.Join(", ", targets.Take(keep)) + $" +{targets.Count - keep} more";
                    if (Fits(cut)) return (cut, two, true);
                }
            }
            var shorter = line;
            while (shorter.Length > 4 && !Fits(shorter + "…")) shorter = shorter.Substring(0, shorter.Length - 1);
            return (shorter.TrimEnd(' ', ',') + "…", two, true);
        }

        /// <summary>One line of <paramref name="text"/> in <paramref name="width"/>: the size from max down to min that fits, else min with "…".</summary>
        private static (string text, int size) FitOneLine(Text t, string text, float width, int max, int min)
        {
            for (int size = max; size >= min; size--)
                if (CardView.TextWidth(t, text, size) <= width) return (text, size);
            var shorter = text;
            while (shorter.Length > 2 && CardView.TextWidth(t, shorter + "…", min) > width) shorter = shorter.Substring(0, shorter.Length - 1);
            return (shorter.TrimEnd() + "…", min);
        }

        /// <summary>Raised when a negated card or a just-set card is on the field (for automated screenshots).</summary>
        public event Action? NegatedShown, SetThisTurnShown;

        /// <summary>Raised when the chain panel shows two or more links (for automated screenshots).</summary>
        public event Action<int>? ChainShown;

        /// <summary>For checks: each chain row's second line as drawn, top to bottom.</summary>
        public List<string> ChainLines => _chainPanel == null ? new List<string>()
            : _chainPanel.GetComponentsInChildren<Text>().Where(t => t.name == "Line").Select(t => t.text).ToList();

        /// <summary>
        /// For checks: what's wrong with the chain panel as drawn: a name or line its box cuts off (or that ends on a comma),
        /// a row over another or outside the panel. Empty when every row reads whole.
        /// </summary>
        public List<string> ChainProblems
        {
            get
            {
                var problems = new List<string>();
                if (_chainPanel == null) return problems;
                float panelTop = _chainPanel.sizeDelta.y / 2, panelBottom = -panelTop, lastBottom = panelTop - 24;
                foreach (Transform child in _chainPanel)
                {
                    if (!child.name.StartsWith("Link")) continue;
                    var row = (RectTransform)child;
                    float top = row.anchoredPosition.y + row.sizeDelta.y / 2, bottom = row.anchoredPosition.y - row.sizeDelta.y / 2;
                    if (top > lastBottom + 0.5f || bottom < panelBottom - 0.5f) problems.Add($"{row.name} overlaps ({top:0}..{bottom:0}, the one above ends at {lastBottom:0})");
                    lastBottom = bottom;
                    foreach (var t in row.GetComponentsInChildren<Text>())
                    {
                        if (t.name != "Name" && t.name != "Line" && t.name != "State") continue;
                        var rt = t.rectTransform;
                        float need = CardView.TextHeight(t, t.text, t.fontSize, rt.sizeDelta.x);
                        if (t.text.Length > 0 && need > rt.sizeDelta.y + 0.5f) problems.Add($"{row.name} {t.name} \"{t.text}\" needs {need:0} px of {rt.sizeDelta.y:0}");
                        if (t.text.TrimEnd().EndsWith(",")) problems.Add($"{row.name} {t.name} ends on a comma: \"{t.text}\"");
                        float tTop = rt.anchoredPosition.y + rt.sizeDelta.y / 2, tBottom = rt.anchoredPosition.y - rt.sizeDelta.y / 2;
                        if (tTop > row.sizeDelta.y / 2 + 0.5f || tBottom < -row.sizeDelta.y / 2 - 0.5f) problems.Add($"{row.name} {t.name} sticks out of its row");
                    }
                }
                return problems;
            }
        }

        /// <summary>For checks: the chain panel's rows, top to bottom.</summary>
        public List<string> ChainRows => _chainPanel == null ? new List<string>()
            : _chainPanel.GetComponentsInChildren<Text>().Where(t => t.name == "Name" || t.name == "State").Select(t => t.text).ToList();

        // ---- Reactions -------------------------------------------------------------------------------------------

        private readonly string[] _portraitId = { "", "" };
        private readonly RawImage?[] _portraitBase = new RawImage?[2];
        private readonly Graphic?[] _mood = new Graphic?[2];
        private readonly float[] _moodUntil = new float[2];
        private readonly string[] _moodKind = { "", "" };
        private readonly RectTransform?[] _bubble = new RectTransform?[2];

        /// <summary>Who is shown on a plate right now (the active duelist in a tag duel).</summary>
        public string PortraitId(int player) => _portraitId[player];

        /// <summary>The expression a reaction shows: summon/strike are smug, hit is hurt, danger is desperate.</summary>
        public static string Expression(string kind) => kind switch { "summon" or "strike" or "combo" => "smug", "hit" or "fizzle" => "hurt", "danger" => "desperate", _ => "" };

        /// <summary>
        /// A duelist reacts, <paramref name="delay"/> seconds from now: their portrait changes expression (when there is
        /// art for it: portraits/&lt;id&gt;_smug.png, _hurt.png, _desperate.png) and flashes in the mood's color, and their
        /// line appears in a speech bubble beside their plate.
        /// </summary>
        public void React(int player, string kind, string line, float delay = 0)
        {
            StartCoroutine(ReactLater(player, kind, line, delay));
        }

        public string LastReaction { get; private set; } = "";
        /// <summary>Raised when a duelist reacts ("summon", "hit"...), for automated screenshots.</summary>
        public event Action<string>? Reacted;

        private System.Collections.IEnumerator ReactLater(int player, string kind, string line, float delay)
        {
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            LastReaction = $"{player}:{kind}:{line}";
            Reacted?.Invoke(kind);
            var id = _portraitId[player];
            var face = Portraits.Get($"{id}_{Expression(kind)}");
            if (_portraitBase[player] is RawImage portrait && portrait != null)
                portrait.texture = face ?? Portraits.Get(id);
            _moodKind[player] = kind;
            _moodUntil[player] = Time.unscaledTime + 2.8f;
            if (line.Length > 0) ShowBubble(player, line);
        }

        private void ShowBubble(int player, string line)
        {
            if (_bubble[player] != null) Destroy(_bubble[player]!.gameObject);
            // Beside the plate, level with the portrait; the tail points back at the speaker.
            var y = _plateHome[player].y + 76;
            var bubble = Ui.Placed("Bubble", _field, new Vector2(-404, y), new Vector2(270, 86));
            _bubble[player] = bubble;
            var group = bubble.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var tail = Ui.Placed("Tail", bubble, new Vector2(-135, 0), new Vector2(18, 18));
            tail.localRotation = Quaternion.Euler(0, 0, 45);
            Ui.Image(tail, new Color(0.97f, 0.96f, 0.92f, 0.97f));
            Ui.Image(bubble, new Color(0.97f, 0.96f, 0.92f, 0.97f));
            var edge = bubble.gameObject.AddComponent<Outline>();
            edge.effectColor = _plateAccent[player];
            edge.effectDistance = new Vector2(2, -2);
            var text = Ui.Text(bubble, line, 17, Ui.Hex("#1A1D26"));
            text.fontStyle = FontStyle.BoldAndItalic;
            text.rectTransform.offsetMin = new Vector2(12, 6);
            text.rectTransform.offsetMax = new Vector2(-12, -6);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = 17;
            bubble.SetAsLastSibling();
            StartCoroutine(BubbleLife(bubble, group));
        }

        private System.Collections.IEnumerator BubbleLife(RectTransform bubble, CanvasGroup group)
        {
            float start = Time.unscaledTime;
            while (bubble != null)
            {
                float t = Time.unscaledTime - start;
                if (t > 3.2f) { Destroy(bubble.gameObject); yield break; }
                group.alpha = t < 0.15f ? t / 0.15f : t > 2.8f ? (3.2f - t) / 0.4f : 1;
                bubble.localScale = Vector3.one * (t < 0.2f ? Mathf.Lerp(0.6f, 1f, 1 - Mathf.Pow(1 - t / 0.2f, 3)) : 1);
                yield return null;
            }
        }

        /// <summary>The portrait's mood: a colored flash that fades (gold when smug, red when hurt), a red pulse in danger.</summary>
        private void UpdateMood(int player)
        {
            var mood = _mood[player];
            if (mood == null) return;
            bool danger = _lpTarget[player] > 0 && _lpTarget[player] <= 1000;
            float left = _moodUntil[player] - Time.unscaledTime;
            var portrait = _portraitBase[player];
            if (left > 0)
            {
                float k = Mathf.Clamp01(left / 2.8f);
                var kind = _moodKind[player];
                var color = kind is "summon" or "strike" or "combo" ? new Color(1f, 0.82f, 0.3f) : new Color(1f, 0.15f, 0.15f);
                float flash = kind is "hit" or "fizzle" ? k * k : 0.5f * k * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8));
                mood.color = new Color(color.r, color.g, color.b, 0.45f * flash);
                if (portrait != null)
                {
                    // Hurt: the portrait shakes; smug: it leans in a little.
                    var rt = portrait.rectTransform;
                    rt.anchoredPosition = new Vector2(kind is "hit" or "fizzle" ? Mathf.Sin(Time.unscaledTime * 60) * 6 * k * k : 0, 4);
                    float zoom = kind is "summon" or "strike" or "combo" ? 1 + 0.06f * k : 1;
                    rt.localScale = new Vector3(player == 1 ? -zoom : zoom, zoom, 1);
                }
            }
            else
            {
                float beat = danger ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(Time.unscaledTime * 6.5f)), 6) : 0;
                mood.color = new Color(1f, 0.1f, 0.1f, danger ? 0.12f + 0.2f * beat : 0);
                if (portrait != null)
                {
                    // Back to their resting face: desperate while in danger (if there's art for it), else normal.
                    var id = _portraitId[player];
                    var rest = (danger ? Portraits.Get($"{id}_desperate") : null) ?? Portraits.Get(id);
                    if (portrait.texture != rest) portrait.texture = rest;
                    portrait.rectTransform.anchoredPosition = new Vector2(0, 4);
                    portrait.rectTransform.localScale = new Vector3(player == 1 ? -1 : 1, 1, 1);
                }
            }
        }

        private readonly Text[] _plateName = new Text[2], _plateDeck = new Text[2];
        private Text _phaseLabel = null!;
        private Text _speedLabel = null!;
        private Text _pauseLabel = null!;
        private Text _banner = null!;
        private readonly Queue<string> _logLines = new Queue<string>();

        public bool RevealHands { get; set; } = true;
        /// <summary>Seat whose hand and face-down cards are drawn as card backs (the human's opponent), or -1.</summary>
        public int HiddenSeat { get; set; } = -1;

        /// <summary>An alley duel's last turn (the turn cap), or 0: from turn 30 the counter reads "Turn 34 / 40".</summary>
        public int TurnCapLast { get; set; }

        private string TurnText(int turn, bool compact = false) =>
            TurnCapLast > 0 && turn >= Ygo.Engine.Campaign.AlleyEconomy.TurnCounterFrom ? (compact ? $"Turn {Math.Min(turn, TurnCapLast)}/{TurnCapLast}" : $"Turn {Math.Min(turn, TurnCapLast)} / {TurnCapLast}") : $"Turn {turn}";
        /// <summary>The seat you play (0), or -1 when watching.</summary>
        public int HumanSeat => HiddenSeat == 1 ? 0 : -1;
        public string PlayerName(int p)
        {
            var n = p >= 0 && p < _names.Length ? _names[p] : "";
            var amp = n.IndexOf(" & ", StringComparison.Ordinal);
            return amp > 0 ? n.Substring(0, amp) : n;
        }
        /// <summary>Life points after the latest redraw.</summary>
        public int LifePoints(int p) => _lastBoard?.Players[p].LifePoints ?? 8000;

        /// <summary>Human play: the decision panel. Its Answered event carries the response bytes.</summary>
        public PromptPanel Prompt { get; private set; } = null!;
        private RectTransform _promptRoot = null!;
        private RectTransform _logBg = null!;
        private BoardState? _lastBoard;
        // Where each card was drawn (this render and the one before), so effects can fly cards between places.
        // Key: (controller, location, sequence); piles use sequence -1.
        private Dictionary<(int, uint, int), RectTransform> _views = new Dictionary<(int, uint, int), RectTransform>();
        private Dictionary<(int, uint, int), Vector2> _positions = new Dictionary<(int, uint, int), Vector2>();
        private Dictionary<(int, uint, int), Vector2> _previousPositions = new Dictionary<(int, uint, int), Vector2>();
        private RectTransform _fxLayer = null!;
        private AttackArrow _arrow = null!;
        // Life points roll toward their new value instead of jumping.
        private readonly float[] _lpShown = { 8000, 8000 };
        private readonly int[] _lpTarget = { 8000, 8000 };
        private readonly float[] _lpRollRate = { 0, 0 };
        /// <summary>The LP a full bar stands for: each duelist's starting LP (8000 unless the duel says otherwise).</summary>
        private readonly float[] _lpScale = { 8000, 8000 };
        // Life points with drama: the damage trail, a jolt when hit, and a heartbeat at 1000 LP or less.
        private readonly float[] _lpTrail = { 8000, 8000 };
        private readonly float[] _lpTrailHold = { 0, 0 };
        private readonly RectTransform[] _plateRoot = new RectTransform[2];
        private readonly Vector2[] _plateHome = new Vector2[2];
        /// <summary>A duelist panel's size, and its LP bar's width.</summary>
        private static readonly Vector2 PanelSize = new Vector2(400, 400);
        private readonly Outline[] _plateFrame = new Outline[2];
        private readonly Color[] _plateAccent = new Color[2];
        private readonly float[] _jolt = { 0, 0 };
        private bool _snapLifePoints = true;

        /// <summary>Duel effects (moving cards, spotlight, damage...), drawn over the cards.</summary>
        public BoardFx Fx { get; private set; } = null!;
        /// <summary>How long a life point change takes to roll (seconds); 0 jumps.</summary>
        public float LifePointRollTime { get; set; } = 0.7f;
        // Cards (and piles) the current decision refers to, so they can be outlined and clicked.
        private readonly HashSet<(int controller, uint location, uint sequence)> _actionable = new HashSet<(int, uint, uint)>();
        private readonly HashSet<(int controller, uint location)> _actionablePiles = new HashSet<(int, uint)>();

        public event Action? PauseClicked;
        /// <summary>Whether the duel is paused (your own duels say so on the status line).</summary>
        public Func<bool>? IsPaused;
        /// <summary>A screen over the board (a story scene, the menu) has the keyboard: the board's shortcuts stay off.</summary>
        public Func<bool>? KeysElsewhere;
        public event Action? StepClicked;
        public event Action<int>? SpeedChanged;
        public event Action? RematchClicked;
        /// <summary>Matchup picker: (seat, +1/-1) to cycle the character or deck for that seat.</summary>
        public event Action<int, int>? CharacterCycled;
        public event Action<int, int>? DeckCycled;
        public event Action? StartClicked;
        public event Action? RandomClicked;
        public event Action? DecksClicked;
        /// <summary>Replays: save this duel, open the replay list; while watching, step back, skip a turn, restart, copy the code.</summary>
        public event Action? SaveReplayClicked, ReplaysClicked, BackClicked, NextTurnClicked, RestartClicked, CopyCodeClicked;
        public event Action? MenuClicked;
        private GameObject _liveRow = null!, _replayRow = null!;
        private bool _replayMode;
        // Campaign layout: the side panel keeps only what a player needs (see SetCampaignMode).
        private GameObject _picker = null!, _spectatorButtons = null!, _logButton = null!, _campaignTitleRoot = null!;
        private Text _campaignTitle = null!, _logButtonLabel = null!;
        private RectTransform _infoRt = null!, _speedGroup = null!;
        private bool _playLayout, _humanMode, _logOpen;
        private GameObject _responsesRoot = null!;
        private Text _momentsLabel = null!;
        /// <summary>Replay controls: open the key moments list.</summary>
        public event Action? MomentsClicked;
        /// <summary>A moment was picked in the list (its replay position).</summary>
        public event Action<int>? MomentPicked;
        private RectTransform? _momentsPanel, _lessonBanner;

        /// <summary>Whether the replay's moments are ready (the button says so while they're being found).</summary>
        public void SetMomentsReady(bool ready) => _momentsLabel.text = ready ? "Moments" : "Moments…";

        /// <summary>
        /// The replay's key moments, grouped by turn: click one to jump to just before it (the board then plays it).
        /// The lesson's key turn, if any, is marked.
        /// </summary>
        public void ShowMoments(List<Ygo.Engine.Replays.Moment> moments, int currentTick, int lessonTurn = 0)
        {
            CloseMoments();
            var root = Ui.Stretched("Moments", _field, Vector2.zero, Vector2.one);
            _momentsPanel = root;
            Ui.Clickable((RectTransform)Ui.Image(root, new Color(0, 0, 0, 0.7f)).transform, CloseMoments);
            var panel = Ui.Placed("Panel", root, Vector2.zero, new Vector2(900, 900));
            Ui.Image(panel, Ui.Hex("#161B27"));
            Ui.Text(Ui.Placed("Title", panel, new Vector2(-150, 420), new Vector2(560, 40)), "KEY MOMENTS", 24, Color.white, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Button(panel, "Close", new Vector2(370, 420), new Vector2(110, 38), CloseMoments);
            Ui.Text(Ui.Placed("Hint", panel, new Vector2(0, 388), new Vector2(840, 24)),
                "Click a moment to jump to just before it; press Pause / Step or wait to see it happen.", 13, new Color(0.75f, 0.78f, 0.86f), TextAnchor.MiddleLeft);
            var viewport = Ui.Placed("View", panel, new Vector2(0, -40), new Vector2(860, 780));
            Ui.Image(viewport, new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var list = Ui.Rect("List", viewport);
            list.anchorMin = new Vector2(0, 1);
            list.anchorMax = new Vector2(1, 1);
            list.pivot = new Vector2(0.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = list;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            float y = 0;
            void Row(string text, int tick, Color color, bool heading, bool here)
            {
                var row = Ui.Box(list, heading ? 0 : 24, y, heading ? 860 : 836, heading ? 38 : 32);
                Ui.Image(row, here ? Ui.Hex("#2E3A55") : heading ? Ui.Hex("#1E2535") : new Color(0, 0, 0, 0.001f));
                var t = Ui.Text(row, text, heading ? 16 : 14, color, TextAnchor.MiddleLeft);
                t.supportRichText = true;
                t.rectTransform.offsetMin = new Vector2(12, 0);
                if (heading) t.fontStyle = FontStyle.Bold;
                Ui.Clickable(row, () => { CloseMoments(); MomentPicked?.Invoke(tick); });
                y += heading ? 42 : 34;
            }
            Row("Start of the duel", 0, Ui.Hex("#FFC940"), true, currentTick == 0);
            foreach (var m in moments)
            {
                bool lesson = m.Kind == Ygo.Engine.Replays.MomentKind.Turn && m.Turn == lessonTurn;
                var (icon, color) = m.Kind switch
                {
                    Ygo.Engine.Replays.MomentKind.Turn => ("", lesson ? Ui.Hex("#7BD88F") : Ui.Hex("#FFC940")),
                    Ygo.Engine.Replays.MomentKind.Chain => ("⛓ ", Ui.Hex("#9FD8FF")),
                    Ygo.Engine.Replays.MomentKind.Hit => ("✦ ", Ui.Hex("#FF8A8A")),
                    Ygo.Engine.Replays.MomentKind.Summon => ("★ ", Ui.Hex("#FFE38A")),
                    _ => ("■ ", Color.white),
                };
                Row(icon + m.Text + (lesson ? "   <b>← the lesson's key turn</b>" : ""), m.Tick, color, m.Kind == Ygo.Engine.Replays.MomentKind.Turn, false);
            }
            list.sizeDelta = new Vector2(0, y);
            root.SetAsLastSibling();
        }

        public void CloseMoments()
        {
            if (_momentsPanel != null) Destroy(_momentsPanel.gameObject);
            _momentsPanel = null;
        }

        public bool MomentsOpen => _momentsPanel != null;

        /// <summary>A Replay Lesson's banner at the top of the field (null: none), with a way back.</summary>
        public void SetLesson(string? title, string? summary, Action? back)
        {
            if (_lessonBanner != null) Destroy(_lessonBanner.gameObject);
            _lessonBanner = null;
            if (title == null) { PointAt(null); return; }
            // At the top right, right of the opponent's hand (so it never covers it) and left of the recent-actions strip;
            // effect callouts and messages stack under it. The hint is the main instruction, so it's big.
            const float width = 620, height = 104;
            var banner = Ui.Placed("Lesson", _field, LessonSpot(new Vector2(width, height)), new Vector2(width, height));
            _lessonBanner = banner;
            Ui.Image(banner, new Color(0.08f, 0.2f, 0.12f, 0.94f));
            var t = Ui.Text(Ui.Placed("T", banner, new Vector2(-72, 32), new Vector2(452, 26)), $"<b>LESSON</b>  {title}", 17, Color.white, TextAnchor.MiddleLeft);
            t.supportRichText = true;
            var s = Ui.Text(Ui.Placed("S", banner, new Vector2(-72, -14), new Vector2(452, 62)), summary ?? "", 20, new Color(0.86f, 0.94f, 0.88f), TextAnchor.MiddleLeft);
            s.resizeTextForBestFit = true;
            s.resizeTextMinSize = 14;
            s.resizeTextMaxSize = 20;
            // Leaves the lesson (it isn't a step back within it).
            if (back != null) Ui.Button(banner, "Leave lesson", new Vector2(width / 2 - 78, 0), new Vector2(136, 44), back);
        }
        /// <summary>Where the lesson banner goes: 8 px under the field's top, its right edge at the callout column's.</summary>
        private Vector2 LessonSpot(Vector2 size) =>
            new Vector2(_field.rect.width / 2 - ColumnMargin - size.x / 2, _field.rect.height / 2 - 8 - size.y / 2);

        private Image _responsesBg = null!;
        private Text _responsesLabel = null!, _responsesHelp = null!;
        private ResponseMode _responses = ResponseMode.Auto;
        private bool _holding;
        private Text _waiting = null!;

        /// <summary>For checks: the waiting label.</summary>
        public string WaitingText => _waiting.text;
        /// <summary>Clicked: cycle Auto / On / Off.</summary>
        public event Action? ResponsesClicked;
        /// <summary>Whether the ask key is held (Shift / LT), to show it on the board.</summary>
        public Func<bool>? HoldingAsk;

        /// <summary>Shows the chain prompt mode (only when you play).</summary>
        public void SetResponseMode(bool human, ResponseMode mode)
        {
            _responses = mode;
            _responsesRoot.SetActive(human);
            DrawResponses();
        }

        /// <summary>For checks: the pill's text.</summary>
        public string ResponsesText => _responsesRoot.activeSelf ? $"{_responsesLabel.text} / {_responsesHelp.text}" : "";

        private void DrawResponses()
        {
            var color = _responses == ResponseMode.On ? "#7BD88F" : _responses == ResponseMode.Off ? "#FF8A8A" : "#FFC940";
            _responsesLabel.text = _holding ? "<color=#7FD3FF><b>ASKING</b></color>" : $"Chain: <color={color}><b>{_responses.ToString().ToUpperInvariant()}</b></color>";
            _responsesHelp.text = _holding ? "you'll be asked next time" : _responses switch
            {
                ResponseMode.On => "asks at every chance",
                ResponseMode.Off => "only your own triggers",
                _ => "asks when they play",
            };
        }
        private RectTransform _side = null!, _headerRt = null!;
        private Image _sideBg = null!, _infoBg = null!, _promptBg = null!;

        private readonly Text[] _pickName = new Text[2];
        private readonly Text[] _pickDeck = new Text[2];
        private readonly Text[] _pickStyle = new Text[2];

        public void Build(ICardDataProvider cards)
        {
            _cards = cards;

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio
            var canvas = canvasGo.transform;

            if (FindAnyObjectByType<EventSystem>() == null)
            {
#if ENABLE_INPUT_SYSTEM
                // The Input System's UI module handles mouse, keyboard, and the controller's virtual mouse.
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
                eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
#else
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            }

            Ui.Image(Ui.Stretched("Background", canvas, Vector2.zero, Vector2.one), Ui.Hex("#14171F"));

            // Left 72%: the mat. Right 28%: info, log, controls.
            _field = Ui.Stretched("Field", canvas, new Vector2(0, 0), new Vector2(0.72f, 0.955f));
            var side = Ui.Stretched("Side", canvas, new Vector2(0.72f, 0), new Vector2(1, 1));
            _side = side;
            _sideBg = Ui.Image(side, Ui.Hex("#1B1F2A"));

            var headerRt = Ui.Stretched("Header", canvas, new Vector2(0, 0.955f), new Vector2(0.72f, 1));
            _headerRt = headerRt;
            Ui.Image(headerRt, Ui.Hex("#0F1117"));
            _header = Ui.Text(headerRt, "", 22, Color.white);
            var menuButton = Ui.Button(headerRt, "", new Vector2(-620, 0), new Vector2(46, 38), () => MenuClicked?.Invoke());
            menuButton.name = "Menu";
            var menuIcon = Ui.Placed("Icon", (RectTransform)menuButton.transform, Vector2.zero, new Vector2(24, 24)).gameObject.AddComponent<RawImage>();
            menuIcon.texture = Icons.For("menu");
            menuIcon.raycastTarget = false;

            BuildMat();
            _cardLayer = Ui.Stretched("Cards", _field, Vector2.zero, Vector2.one);
            // Where attacks are going: above the cards, below the effects.
            _arrow = Ui.Stretched("Arrows", _field, Vector2.zero, Vector2.one).gameObject.AddComponent<AttackArrow>();
            _fxLayer = Ui.Stretched("Effects", _field, Vector2.zero, Vector2.one);
            // Nothing the effects draw is ever clicked: banners, cut-ins and the effect text (even while still invisible,
            // waiting for their delay) must never sit between the pointer and a card.
            var fxGroup = _fxLayer.gameObject.AddComponent<CanvasGroup>();
            fxGroup.blocksRaycasts = false;
            fxGroup.interactable = false;
            Fx = new BoardFx(this, _fxLayer, _cardLayer, cards);
            // The duel doesn't step (nor redraw the cards) while a press on a card is held, so the click isn't lost.
            Fx.HoldWhile = CardPressHeld;

            // Phase button (Master Duel style): turn and phase on a disc at the field's right edge; on your turn,
            // clicking it moves to the next phase.
            // A glowing ring on the center line, right of the board: blue on your turn, red on theirs. In the play layout
            // it stays 12 px clear of the right column (card detail, text log), which starts 310 px from the edge.
            var phaseRt = Ui.Placed("PhaseButton", _field, new Vector2(570, 0), new Vector2(136, 136));
            _phaseRt = phaseRt;
            _phaseHalo = Ui.Placed("Halo", phaseRt, Vector2.zero, new Vector2(190, 190)).gameObject.AddComponent<RawImage>();
            _phaseHalo.texture = BoardFx.Textures.Glow;
            _phaseHalo.raycastTarget = false;
            var phaseCore = Ui.Placed("Core", phaseRt, Vector2.zero, new Vector2(112, 112)).gameObject.AddComponent<RawImage>();
            phaseCore.texture = BoardFx.Textures.Glow;
            phaseCore.color = new Color(0.03f, 0.06f, 0.14f, 1);
            phaseCore.raycastTarget = false;
            _phaseDisc = Ui.Stretched("Ring", phaseRt, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            _phaseDisc.texture = BoardFx.Textures.Ring;
            _phaseDisc.color = Ui.Hex("#4FA3FF");
            var innerRing = Ui.Placed("InnerRing", phaseRt, Vector2.zero, new Vector2(118, 118)).gameObject.AddComponent<RawImage>();
            innerRing.texture = BoardFx.Textures.Ring;
            innerRing.color = new Color(1, 1, 1, 0.35f);
            innerRing.raycastTarget = false;
            _phaseLabel = Ui.Text(Ui.Stretched("Label", phaseRt, Vector2.zero, Vector2.one), "", 20, Color.white);
            _phaseLabel.supportRichText = true;
            Ui.Clickable(phaseRt, OpenPhasePicker);

            // Chain prompts (Master Duel's Auto / On / Off): under the phase button, with what it means.
            // Under your Banished pile, beside the Deck (that slot is empty): clear of the pile and the phase disc.
            var responses = Ui.Placed("Responses", _field, new Vector2(Column * 4 + 6, -2 * Row - 6), new Vector2(150, 60));
            _responsesBg = Ui.Image(responses, new Color(0.06f, 0.08f, 0.13f, 0.9f));
            var edge = responses.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(1f, 0.79f, 0.25f, 0.6f);
            edge.effectDistance = new Vector2(1, -1);
            _responsesLabel = Ui.Text(Ui.Placed("Mode", responses, new Vector2(0, 14), new Vector2(146, 26)), "", 16, Color.white);
            _responsesLabel.supportRichText = true;
            _responsesHelp = Ui.Text(Ui.Placed("Help", responses, new Vector2(0, -14), new Vector2(146, 26)), "", 14, new Color(0.78f, 0.82f, 0.9f));
            _responsesHelp.resizeTextForBestFit = true;
            _responsesHelp.resizeTextMinSize = 12;
            _responsesHelp.resizeTextMaxSize = 14;
            Ui.Clickable(responses, () => { Sounds.Play("click"); ResponsesClicked?.Invoke(); });
            _responsesRoot = responses.gameObject;
            // Watching (replays, bot duels): whose hands show, in the same place.
            var hands = Ui.Placed("Hands", _field, new Vector2(580, 130), new Vector2(150, 54));
            Ui.Image(hands, new Color(0.06f, 0.08f, 0.13f, 0.9f));
            var handsEdge = hands.gameObject.AddComponent<Outline>();
            handsEdge.effectColor = new Color(0.37f, 0.88f, 1f, 0.6f);
            handsEdge.effectDistance = new Vector2(1, -1);
            _handsLabel = Ui.Text(Ui.Placed("Mode", hands, new Vector2(0, 11), new Vector2(146, 26)), "", 15, Color.white);
            _handsLabel.supportRichText = true;
            Ui.Text(Ui.Placed("Help", hands, new Vector2(0, -13), new Vector2(146, 24)), "click to change", 11, new Color(0.78f, 0.82f, 0.9f));
            Ui.Clickable(hands, () => { Sounds.Play("click"); CycleHands(); });
            _handsRoot = hands.gameObject;
            DrawHands();
            // While you wait: who the game is waiting on (the opponent, or effects resolving), never blank.
            _waiting = Ui.Text(Ui.Placed("Waiting", _field, new Vector2(Column * 4 + 18, -2 * Row - 50), new Vector2(220, 24)), "", 15, new Color(0.85f, 0.88f, 0.95f));
            _waiting.fontStyle = FontStyle.Italic;
            _waiting.raycastTarget = true;
            _waiting.supportRichText = true;
            // A paused duel of yours says so, and a click resumes it (your own duels have no Pause button).
            Ui.Clickable(_waiting.rectTransform, () => { if (IsPaused?.Invoke() == true) PauseClicked?.Invoke(); });

            // Duelist panels (BoardView.Panel): an angular HUD frame per duelist at the field's left edge.
            BuildPanels();

            var pickerRt = Ui.Stretched("Picker", side, new Vector2(0, 0.78f), new Vector2(1, 1));
            BuildPicker(pickerRt);
            _picker = pickerRt.gameObject;

            // Campaign: where this duel sits (circuit, match) in place of the picker.
            var titleRt = Ui.Stretched("CampaignTitle", side, new Vector2(0, 0.93f), new Vector2(1, 1));
            Ui.Image(titleRt, Ui.Hex("#161A23"));
            _campaignTitle = Ui.Text(titleRt, "", 18, Ui.Hex("#FFC940"));
            _campaignTitle.supportRichText = true;
            _campaignTitleRoot = titleRt.gameObject;
            _campaignTitleRoot.SetActive(false);

            var infoRt = Ui.Stretched("Info", side, new Vector2(0, 0.6f), new Vector2(1, 0.78f));
            _infoRt = infoRt;
            // A backing for the card detail when it floats over the mat (the full-width campaign board).
            var infoBg = Ui.Stretched("InfoBg", side, new Vector2(0, 0.6f), new Vector2(1, 0.78f));
            _infoBg = Ui.Image(infoBg, new Color(0.05f, 0.06f, 0.1f, 0));
            _infoBg.raycastTarget = false;
            infoBg.SetSiblingIndex(infoRt.GetSiblingIndex()); // behind the card detail
            infoRt.offsetMin = new Vector2(16, 8);
            infoRt.offsetMax = new Vector2(-16, -16);
            // The hovered card, Master Duel style (picture, stats with icons, type bar, scrolling text).
            // The Details / Pin buttons get a strip of their own under the text, in every layout.
            _detail = new CardDetail(infoRt) { BottomReserve = 30 };
            // Pin: keep this card in the panel; cards you hover then show in a second panel (to compare).
            var pin = Ui.Rect("Pin", infoRt);
            pin.anchorMin = pin.anchorMax = new Vector2(1, 0);
            pin.pivot = new Vector2(1, 0);
            pin.anchoredPosition = new Vector2(0, -2);
            pin.sizeDelta = new Vector2(84, 26);
            _pinImage = Ui.Image(pin, new Color(0.16f, 0.22f, 0.34f, 0.95f));
            _pinLabel = Ui.Text(pin, "Pin (P)", 13, Color.white);
            Ui.Clickable(pin, () => { Sounds.Play("click"); TogglePin(); });
            // Details: the shown card in the full-screen reader (as a click on its picture or text does).
            var details = Ui.Rect("Details", infoRt);
            details.anchorMin = details.anchorMax = new Vector2(1, 0);
            details.pivot = new Vector2(1, 0);
            details.anchoredPosition = new Vector2(-92, -2);
            details.sizeDelta = new Vector2(104, 26);
            Ui.Image(details, new Color(0.16f, 0.22f, 0.34f, 0.95f));
            Ui.Text(details, "Details (D)", 13, Color.white);
            Ui.Clickable(details, () => { Sounds.Play("click"); OpenReader(_detail.Shown); });
            _detail.Clicked += OpenReader;
            var second = Ui.Stretched("Hovered", side, new Vector2(0, 0.1f), new Vector2(1, 0.5f));
            second.offsetMin = new Vector2(16, 8);
            second.offsetMax = new Vector2(-16, -8);
            _hoverBg = Ui.Image(second, new Color(0.05f, 0.07f, 0.12f, 0.95f));
            _hoverBg.raycastTarget = false;
            var inner = Ui.Stretched("Card", second, Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(6, 6);
            inner.offsetMax = new Vector2(-6, -6);
            _hoverDetail = new CardDetail(inner);
            _hoverDetail.Clicked += OpenReader;
            _hoverRoot = second;
            second.gameObject.SetActive(false);

            _promptRoot = Ui.Stretched("Prompt", side, new Vector2(0, 0.26f), new Vector2(1, 0.59f));
            _promptRoot.offsetMin = new Vector2(10, 0);
            _promptRoot.offsetMax = new Vector2(-10, 0);
            Prompt = new PromptPanel(_promptRoot);
            _promptBg = _promptRoot.GetComponent<Image>();
            Prompt.Answered += _ => ShowChoices(null, false);
            // The monster you chose to attack with: the arrow to the target you're choosing starts there.
            Prompt.ChoiceTaken += (set, i) =>
            {
                if (set.Prompt is BattlePrompt && set.Choices[i].Verb == "Attack" && set.Choices[i].Card is { } c) _attacker = c;
            };
            // After a cancel, the cursor goes back to the card you were using once the next decision shows.
            // (Only a cancel soon after using the card: picking its target or zone, not a "Don't chain" turns later.)
            Prompt.Cancelled += () => _focusPending = _focusCard != null && _promptsSinceFocus <= 2;
            Dialogs = new PromptDialogs(_field, Prompt, cards, LiveCard, ShowInfo) { CheckField = ToggleCheckField, Responding = RespondingTo };
            // A toggled pick redraws the board so its checkmark shows.
            Prompt.Refreshed += () => { if (_lastBoard != null && Prompt.Current != null && Dialogs.Kind == "field") Render(_lastBoard); };
            // The panel shows itself on every redraw (a pick toggled, a card ordered): the play layout hides it again
            // when the dialog on the board owns the decision (the order dialog's side list was showing a second copy).
            Prompt.Refreshed += UpdatePromptList;
            _promptRoot.gameObject.SetActive(false);

            var logBg = Ui.Stretched("LogBg", side, new Vector2(0, 0.1f), new Vector2(1, 0.59f));
            logBg.offsetMin = new Vector2(10, 0);
            logBg.offsetMax = new Vector2(-10, 0);
            _logBg = logBg;
            Ui.Image(logBg, Ui.Hex("#11141B"));
            var logRt = Ui.Stretched("Log", logBg, Vector2.zero, Vector2.one);
            logRt.offsetMin = new Vector2(10, 8);
            logRt.offsetMax = new Vector2(-10, -8);
            _log = Ui.Text(logRt, "", 14, new Color(0.78f, 0.8f, 0.84f), TextAnchor.LowerLeft);
            _log.verticalOverflow = VerticalWrapMode.Truncate;

            _controlsRt = Ui.Stretched("Controls", side, new Vector2(0, 0), new Vector2(1, 0.1f));
            BuildControls(_controlsRt);
            // The log opens over the side panel in the campaign; it stays above the info and prompt.
            _logBg.SetAsLastSibling();

            var bannerRt = Ui.Placed("Banner", _field, Vector2.zero, new Vector2(900, 170));
            Ui.Image(bannerRt, new Color(0, 0, 0, 0.8f)).raycastTarget = false; // an announcement: clicks go through to the cards
            // The headline sits on the middle line and the quote just under it (one block, not two far apart).
            _banner = Ui.Text(Ui.Stretched("Text", bannerRt, new Vector2(0, 0.48f), new Vector2(1, 0.96f)), "", 44, Color.white, TextAnchor.LowerCenter);
            _bannerQuote = Ui.Text(Ui.Stretched("Quote", bannerRt, new Vector2(0, 0.04f), new Vector2(1, 0.46f)), "", 20, new Color(0.85f, 0.87f, 0.9f), TextAnchor.UpperCenter);
            _bannerQuote.fontStyle = FontStyle.Italic;
            bannerRt.gameObject.SetActive(false);
            _bannerRoot = bannerRt;
            BuildPlayChrome(canvas, side, infoBg, infoRt, second);
#if ENABLE_INPUT_SYSTEM
            // The controller's button hints keep left of the side panel (developer layout) or the watch bar.
            ControllerSupport.ReservedRight = () =>
            {
                if (!isActiveAndEnabled || KeysElsewhere?.Invoke() == true || ControllerSupport.Legend != null) return 0;
                if (!_playLayout) return ((RectTransform)_canvas).rect.width * (1 - _side.anchorMin.x);
                return WatchBarShowing ? _controlsRt.sizeDelta.x + 12 : 0;
            };
#endif
        }

        /// <summary>The living mat (painted per duelist, Field Spell art) under framed zone slots.</summary>
        public Playmat Mat { get; private set; } = null!;

        private void BuildMat()
        {
            Mat = Playmat.Create(_field);
            // The mat runs under the (see-through) top bar; grown at both ends so its seam stays at the field's middle.
            var matRt = (RectTransform)Mat.transform;
            matRt.offsetMin = new Vector2(0, -52);
            matRt.offsetMax = new Vector2(0, 52);
            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < 5; i++)
                {
                    Zone(MonsterZone(p, i), p, "");
                    Zone(SpellZone(p, i), p, "");
                }
                Zone(SpellZone(p, 5), p, "zone-field");
            }
            Zone(new Vector2(-Column, 0), -1, "zone-extra");
            Zone(new Vector2(Column, 0), -1, "zone-extra");
        }

        /// <summary>
        /// A zone slot: a pane of glass in its side's accent color (blue across the table, gold on yours with the space
        /// mats): a soft light around it, a faint tint inside and a thin glowing edge.
        /// </summary>
        private void Zone(Vector2 center, int side, string label)
        {
            var rt = Ui.Placed("Zone", _field, center, CardView.FieldSize);
            var violet = new Color(0.72f, 0.6f, 1f, 1);
            Image Layer(string name, RectTransform parent, Sprite sprite, Vector2 grow, float alpha)
            {
                var img = Ui.Image(Ui.Placed(name, parent, Vector2.zero, CardView.FieldSize + grow), new Color(1, 1, 1, alpha));
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;
                if (side >= 0) Mat.AddFrame(img, side);
                else img.color = new Color(violet.r, violet.g, violet.b, alpha * 1.6f);
                return img;
            }
            Layer("Glow", rt, HudShapes.RoundedGlow, new Vector2(24, 24), 0.18f);
            Layer("Pane", rt, HudShapes.RoundedFill, Vector2.zero, 0.09f);
            Ui.Image(Ui.Placed("Depth", rt, Vector2.zero, CardView.FieldSize - new Vector2(4, 4)), new Color(0.01f, 0.02f, 0.05f, 0.22f)).raycastTarget = false;
            Layer("Frame", rt, HudShapes.RoundedEdge, Vector2.zero, 0.8f);
            // The zone's mark (Field Zone, Extra Monster Zone), faint, instead of a word.
            if (label.Length > 0)
            {
                var icon = Ui.Placed("Mark", rt, Vector2.zero, new Vector2(50, 50)).gameObject.AddComponent<RawImage>();
                icon.texture = Icons.For(label);
                icon.color = new Color(1, 1, 1, side >= 0 ? 0.3f : 0.36f);
                icon.raycastTarget = false;
            }
        }

        private void BuildControls(RectTransform bar)
        {
            float y = 26;
            var live = Ui.Stretched("LiveRow", bar, Vector2.zero, Vector2.one);
            Ui.Button(live, "Save replay", new Vector2(-130, -26), new Vector2(200, 40), () => SaveReplayClicked?.Invoke());
            _duelTools.Add(Ui.Button(live, "Replays", new Vector2(105, -26), new Vector2(200, 40), () => ReplaysClicked?.Invoke()));
            _liveRow = live.gameObject;
            var replay = Ui.Stretched("ReplayRow", bar, Vector2.zero, Vector2.one);
            Ui.Button(replay, "< Back", new Vector2(-215, -26), new Vector2(80, 40), () => BackClicked?.Invoke());
            Ui.Button(replay, "Next turn", new Vector2(-120, -26), new Vector2(100, 40), () => NextTurnClicked?.Invoke());
            var moments = Ui.Button(replay, "Moments", new Vector2(-15, -26), new Vector2(100, 40), () => MomentsClicked?.Invoke());
            _momentsLabel = moments.GetComponentInChildren<Text>();
            Ui.Button(replay, "Copy code", new Vector2(95, -26), new Vector2(110, 40), () => CopyCodeClicked?.Invoke());
            Ui.Button(replay, "Replays", new Vector2(208, -26), new Vector2(106, 40), () => ReplaysClicked?.Invoke());
            _replayRow = replay.gameObject;
            _replayRow.SetActive(false);
            var spectator = Ui.Stretched("SpectatorButtons", bar, Vector2.zero, Vector2.one);
            _spectatorButtons = spectator.gameObject;
            var pause = Ui.Button(spectator, "Pause", new Vector2(-200, y), new Vector2(90, 40), () => PauseClicked?.Invoke());
            _pauseLabel = pause.GetComponentInChildren<Text>();
            Ui.Button(spectator, "Step", new Vector2(-100, y), new Vector2(80, 40), () => StepClicked?.Invoke());
            _duelTools.Add(Ui.Button(spectator, "Rematch", new Vector2(200, y), new Vector2(120, 40), () => RematchClicked?.Invoke()));
            var logButton = Ui.Button(bar, "Duel log", new Vector2(-150, 0), new Vector2(150, 44), ToggleLog);
            _logButtonLabel = logButton.GetComponentInChildren<Text>();
            _logButton = logButton.gameObject;
            _logButton.SetActive(false);
            var speed = Ui.Stretched("Speed", bar, Vector2.zero, Vector2.one);
            _speedGroup = speed;
            Ui.Button(speed, "-", new Vector2(-25, y), new Vector2(44, 40), () => SpeedChanged?.Invoke(-1));
            _speedLabel = Ui.Text(Ui.Placed("Speed", speed, new Vector2(35, y), new Vector2(70, 40)), "", 16, Color.white);
            Ui.Button(speed, "+", new Vector2(95, y), new Vector2(44, 40), () => SpeedChanged?.Invoke(+1));
        }

        /// <summary>Two rows (top seat first, like the mat) of: &lt; character &gt;  &lt; deck &gt;, then Start / Random.</summary>
        private void BuildPicker(RectTransform panel)
        {
            Ui.Image(panel, Ui.Hex("#161A23"));
            var title = Ui.Placed("Title", panel, new Vector2(0, 95), new Vector2(500, 24));
            Ui.Text(title, "MATCHUP", 14, new Color(1, 1, 1, 0.45f));
            Text? foilsLabel = null;
            var foils = Ui.Button(panel, "", new Vector2(180, 95), new Vector2(130, 24), () =>
            {
                CardFoil.CycleMode();
                foilsLabel!.text = CardFoil.ModeLabel;
                if (_lastBoard != null) Render(_lastBoard);
            });
            foilsLabel = foils.GetComponentInChildren<Text>();
            foilsLabel.text = CardFoil.ModeLabel;
            foilsLabel.fontSize = 13;

            int[] seatsTopFirst = { 1, 0 };
            for (int row = 0; row < 2; row++)
            {
                int seat = seatsTopFirst[row];
                float y = 58 - row * 64;
                var color = seat == 0 ? Ui.Hex("#4FA3FF") : Ui.Hex("#FF5A5A");
                _duelTools.Add(Ui.Button(panel, "<", new Vector2(-238, y), new Vector2(32, 34), () => CharacterCycled?.Invoke(seat, -1)));
                _pickName[seat] = Ui.Text(Ui.Placed($"Name{seat}", panel, new Vector2(-128, y), new Vector2(180, 34)), "", 18, color);
                _pickName[seat].fontStyle = FontStyle.Bold;
                _duelTools.Add(Ui.Button(panel, ">", new Vector2(-18, y), new Vector2(32, 34), () => CharacterCycled?.Invoke(seat, +1)));
                _duelTools.Add(Ui.Button(panel, "<", new Vector2(28, y), new Vector2(32, 34), () => DeckCycled?.Invoke(seat, -1)));
                _pickDeck[seat] = Ui.Text(Ui.Placed($"Deck{seat}", panel, new Vector2(122, y), new Vector2(150, 34)), "", 16, Color.white);
                // Deck titles ("Chaos / Yata-Lock") run longer than the file names did.
                _pickDeck[seat].resizeTextForBestFit = true;
                _pickDeck[seat].resizeTextMinSize = 11;
                _pickDeck[seat].resizeTextMaxSize = 16;
                _duelTools.Add(Ui.Button(panel, ">", new Vector2(218, y), new Vector2(32, 34), () => DeckCycled?.Invoke(seat, +1)));
                _pickStyle[seat] = Ui.Text(Ui.Placed($"Style{seat}", panel, new Vector2(0, y - 26), new Vector2(500, 18)), "", 12,
                    new Color(0.75f, 0.78f, 0.84f));
            }
            _duelTools.Add(Ui.Button(panel, "Start Duel", new Vector2(-165, -88), new Vector2(160, 36), () => StartClicked?.Invoke()));
            _duelTools.Add(Ui.Button(panel, "Random", new Vector2(-10, -88), new Vector2(130, 36), () => RandomClicked?.Invoke()));
            _duelTools.Add(Ui.Button(panel, "Deck Editor", new Vector2(155, -88), new Vector2(180, 36), () => DecksClicked?.Invoke()));
            // Why they're off, when they are (a campaign duel, a story scene: BoardView.DevTools).
            _duelToolsNote = Ui.Text(Ui.Placed("ToolsNote", panel, new Vector2(0, -114), new Vector2(520, 18)), "", 12, Ui.Hex("#FFC940"));
        }

        /// <summary>Shows the picker's current choice for one seat (not necessarily the duel in progress).</summary>
        public void SetPick(int seat, string name, string style, string deck)
        {
            _pickName[seat].text = name;
            _pickStyle[seat].text = style;
            _pickDeck[seat].text = deck;
        }

        /// <summary>Human play shows the decision panel and shortens the log; spectating gives the log the space.</summary>
        /// <summary>Watching a replay: both hands shown, replay controls instead of save.</summary>
        public void SetReplayMode(bool on)
        {
            _replayMode = on;
            _replayRow.SetActive(on);
            ApplyLayout();
        }

        public void SetHumanMode(bool on)
        {
            _humanMode = on;
            _handsRoot.SetActive(!on);
            _promptRoot.gameObject.SetActive(on);
            if (!on) ShowChoices(null, false);
            ApplyLayout();
        }

        /// <summary>
        /// The duel's title for the top bar (the circuit and match, a lesson, "Free Duel"...), and the layout again.
        /// Every duel uses the full-width play layout (see BoardView.Play.cs) unless the developer panel is on.
        /// </summary>
        public void SetDuelTitle(string title = "")
        {
            _campaignTitle.text = title;
            _logOpen = false;
            ApplyLayout();
        }

        /// <summary>
        /// An alley duel (set before <see cref="SetDuelTitle"/>): the top bar reads the alley's theme (docs/dev/ALLEY-SPEC-2026-11-01.md
        /// section 13), a panel band in its colors with the title in its text color and the buttons in its iron. Gameplay
        /// colors and the rest of the board are unchanged. Off for every other duel.
        /// </summary>
        public bool AlleyBar { get; set; }

        /// <summary>The top bar's buttons' own fills, to put back after an alley duel.</summary>
        private readonly Dictionary<Graphic, Color> _barButtons = new Dictionary<Graphic, Color>();

        /// <summary>The top bar in the alley's roles, or back to the board's own (after <see cref="ApplyPlayChrome"/> has set it).</summary>
        private void ApplyAlleyBar()
        {
            // The alley's theme itself, not UiTheme.Current: the menu over a duel switches Current to main street.
            var T = UiTheme.BackAlley;
            if (AlleyBar) _headerRt.GetComponent<Image>().color = T.Deep.WithAlpha(_playLayout ? 0.82f : 1);
            if (_headerTitle != null) _headerTitle.color = AlleyBar ? T.Text : Ui.Hex("#FFC940");
            foreach (var g in _headerRt.GetComponentsInChildren<Button>(true).Select(b => b.targetGraphic).Where(g => g != null))
            {
                if (!_barButtons.ContainsKey(g)) _barButtons[g] = g.color;
                g.color = AlleyBar ? T.Button : _barButtons[g];
            }
        }

        /// <summary>For checks: the top bar's band and title colors.</summary>
        public (Color band, Color title) DebugBar => (_headerRt.GetComponent<Image>().color, _headerTitle.color);

        /// <summary>The old side panel with the matchup picker, decision list, log and test buttons (F12).</summary>
        public bool DevPanel
        {
            get => _devPanel;
            set { _devPanel = value; _duelToolsApplied = false; ApplyLayout(); }
        }
        private bool _devPanel;

        /// <summary>For checks: whether the full-width play layout is showing.</summary>
        public bool PlayLayout => _playLayout;

        public bool LogOpen => _logOpen;

        public void ToggleLog()
        {
            _logOpen = !_logOpen;
            ApplyLayout();
        }

        private void ApplyLayout()
        {
            _playLayout = !_devPanel;
            _picker.SetActive(!_playLayout);
            _campaignTitleRoot.SetActive(false); // the title is in the top bar now
            _spectatorButtons.SetActive(!_playLayout);
            _liveRow.SetActive(!_playLayout && !_replayMode);
            _logButton.SetActive(_playLayout);
            _logButtonLabel.text = _logOpen ? "Close log" : "Log";
            // One row of buttons in the campaign (log, speed); two rows otherwise.
            _speedGroup.anchoredPosition = _playLayout ? new Vector2(20, -26) : Vector2.zero;
            ((RectTransform)_logButton.transform).anchoredPosition = _playLayout ? new Vector2(-100, 0) : new Vector2(-150, 0);
            ((RectTransform)_logButton.transform).sizeDelta = new Vector2(_playLayout ? 90 : 150, 44);
            // The campaign board is full width: the mat and cards fill the screen, and a narrow column at the right
            // floats over the mat with the card detail, the controls, and the decision list only when it's needed.
            var fieldRight = _playLayout ? 1f : 0.72f;
            _field.anchorMax = new Vector2(fieldRight, _field.anchorMax.y);
            _headerRt.anchorMax = new Vector2(fieldRight, 1);
            // The duelist panels hug the field's left edge (smaller in the developer layout, whose field is narrower).
            for (int p = 0; p < 2; p++)
            {
                if (_plateRoot[p] == null) continue;
                float scale = _playLayout ? 1 : 0.6f;
                _plateRoot[p].localScale = Vector3.one * scale;
                _plateHome[p] = _playLayout ? new Vector2(-760, p == 0 ? -295 : 295) : new Vector2(-691 + PanelSize.x * scale / 2, p == 0 ? -260 : 340);
                _plateRoot[p].anchoredPosition = _plateHome[p];
            }
            _side.anchorMin = new Vector2(_playLayout ? 1 - 310f / 1920 : 0.72f, 0);
            _side.anchorMax = new Vector2(1, _playLayout ? 0.955f : 1);
            _sideBg.color = _playLayout ? new Color(0, 0, 0, 0) : Ui.Hex("#1B1F2A");
            _infoBg.color = new Color(0.05f, 0.06f, 0.1f, _playLayout ? 0.95f : 0);
            if (_promptBg != null) _promptBg.color = _playLayout ? new Color(0.06f, 0.08f, 0.13f, 0.94f) : Ui.Hex("#161A23");
            if (_playLayout)
            {
                // Card detail on top; the decision list (when needed) below it; the log is a drawer over both.
                _infoRt.anchorMin = new Vector2(0, 0.45f);
                _infoRt.anchorMax = new Vector2(1, 0.95f);
                _promptRoot.anchorMin = new Vector2(0, 0.04f);
                _promptRoot.anchorMax = new Vector2(1, 0.43f);
                _logBg.anchorMin = new Vector2(0, 0.1f);
                _logBg.anchorMax = new Vector2(1, 0.92f);
                _logBg.gameObject.SetActive(_logOpen);
            }
            else
            {
                // The card detail gets room for three whole lines of text above its Details / Pin strip; the decision
                // list (its full height kept) and the log move down to make it.
                _infoRt.anchorMin = new Vector2(0, 0.56f);
                _infoRt.anchorMax = new Vector2(1, 0.78f);
                _promptRoot.anchorMin = new Vector2(0, 0.22f);
                _promptRoot.anchorMax = new Vector2(1, 0.55f);
                _logBg.anchorMin = new Vector2(0, 0.1f);
                _logBg.anchorMax = new Vector2(1, _humanMode ? 0.215f : 0.55f);
                _logBg.gameObject.SetActive(true);
            }
            var bg = (RectTransform)_infoBg.transform;
            bg.anchorMin = _infoRt.anchorMin;
            bg.anchorMax = _infoRt.anchorMax;
            bg.offsetMin = _infoRt.offsetMin - new Vector2(8, 8);
            bg.offsetMax = _infoRt.offsetMax + new Vector2(8, 8);
            UpdatePromptList();
            ApplyPlayChrome();
            ApplyAlleyBar();
            if (_lessonBanner != null) _lessonBanner.anchoredPosition = LessonSpot(_lessonBanner.sizeDelta);
            PlaceComboMeter();
            Canvas.ForceUpdateCanvases();
            _detail?.Layout();
        }

        /// <summary>
        /// The decision list: always there in free play; on the full-width campaign board only for decisions the board
        /// can't show (declaring a number, a Type or an Attribute, rock-paper-scissors...).
        /// </summary>
        private void UpdatePromptList()
        {
            if (!_humanMode) { _promptRoot.gameObject.SetActive(false); return; }
            if (!_playLayout) { _promptRoot.gameObject.SetActive(true); return; }
            var set = Prompt.Current;
            bool onBoard = set == null || Dialogs.Kind.Length > 0 || set.Prompt is IdlePrompt || set.Prompt is BattlePrompt
                           || set.Title.StartsWith("Choose a zone");
            _promptRoot.gameObject.SetActive(!onBoard);
        }

        /// <summary>For checks: whether the decision list is showing.</summary>
        public bool PromptListShown => _promptRoot.gameObject.activeSelf;

        /// <summary>Displays a decision (or "waiting" when null) and outlines the cards it involves.</summary>
        public void ShowChoices(ChoiceSet? set, bool previousRejected)
        {
            if (set != Prompt.Current)
            {
                if (set != null) _promptsSinceFocus++;
                Prompt.Show(set, previousRejected);
                // A card dragged onto a zone goes to that zone when the game then asks where.
                if (set != null && TakeDropZone(set)) return;
            }
            CloseActionMenu();
            if (_pileViewer != null && _pileViewer.name == "PilePicker") ClosePileViewer(); // its choices are gone
            if (_arrow.Preview && set?.Title.StartsWith("Select the attack target") != true) HideAttackArrow();
            // A new Battle or Main Phase decision: any attack is over, however it ended.
            if (!_arrow.Preview && (set?.Prompt is BattlePrompt || set?.Prompt is IdlePrompt)) HideAttackArrow();
            ClosePhasePicker();
            Dialogs.Show(set);
            UpdatePromptList();
            _actionable.Clear();
            _actionablePiles.Clear();
            if (set != null)
            {
                foreach (var choice in set.Choices)
                {
                    if (choice.Card is not { } c) continue;
                    var loc = c.Location & 0x7f;
                    _actionable.Add((c.Controller, loc, c.Sequence));
                    _actionablePiles.Add((c.Controller, loc));
                }
            }
            if (_lastBoard != null) Render(_lastBoard);
            if (set != null && _focusPending)
            {
                _focusPending = false;
                StartCoroutine(FocusCardSoon());
            }
        }

        // ---- Controller focus -----------------------------------------------------------------------------

        /// <summary>The card you last acted on (clicked, or opened the menu of), for returning focus to it.</summary>
        private (int controller, uint location, int sequence)? _focusCard;
        private bool _focusPending;
        private int _promptsSinceFocus;

        /// <summary>For checks: where focus goes back to (screen pixels), or null.</summary>
        public Vector2? FocusPoint => _focusCard is { } f && ViewAt(f.controller, f.location, f.sequence) is RectTransform v
            ? RectTransformUtility.WorldToScreenPoint(null, v.TransformPoint(v.rect.center)) : null;

        private System.Collections.IEnumerator FocusCardSoon()
        {
            yield return null; // after the layout settles
            FocusCard();
        }

        private void FocusCard()
        {
            if (FocusPoint is Vector2 point) ControllerSupport.Instance?.FocusAt(point);
        }

        public bool ActionMenuOpen => _actionMenu != null;

        /// <summary>
        /// A card or pile was clicked: if it has exactly one matching choice, take it;
        /// if several (e.g. Summon or Set), narrow the list to that card's choices.
        /// </summary>
        private void CardClicked(int controller, uint location, int sequence, bool user = true)
        {
            var set = Prompt.Current;
            CloseActionMenu();
            // A card you may read always offers Details, even when there's nothing to do with it.
            var readable = ReadableAt(controller, location & 0x7f, sequence);
            var at = ViewAt(controller, location, sequence) as RectTransform;
            if (set == null)
            {
                if (readable != null && at != null) ShowActionMenu(at, null, new List<int>(), readable);
                return;
            }
            _focusCard = (controller, location & 0x7f, sequence);
            _promptsSinceFocus = 0;
            var matches = new List<int>();
            for (int i = 0; i < set.Choices.Count; i++)
            {
                if (set.Choices[i].Card is not { } c) continue;
                if (c.Controller == controller && (c.Location & 0x7f) == location && (sequence < 0 || c.Sequence == sequence))
                    matches.Add(i);
            }
            if (matches.Count == 0)
            {
                if (readable != null && at != null) ShowActionMenu(at, set, matches, readable);
                return;
            }
            // Your turn's actions (and responding with a card that has several effects): the card's own menu, right at
            // the card, like Master Duel. Picking targets or materials: the click is the choice.
            bool menu = set.Prompt is IdlePrompt || set.Prompt is BattlePrompt || (set.IsChain && matches.Count > 1);
            if (menu && at != null) { ShowActionMenu(at, set, matches, readable); return; }
            if (matches.Count == 1)
            {
                // A pick that toggles (materials, targets): a second click on it within a long frame or two is the same
                // click repeated, not a change of mind; it would untick what was just ticked.
                if (user && set.Mode != ChoiceMode.Single && ClickGuard.Repeat(set, matches[0])) return; // Multi, Order, Incremental
                Prompt.ClickChoice(matches[0]);
            }
            else Prompt.Filter(matches);
        }

        /// <summary>Decisions shown on the board (card picker, chain and yes/no dialogs).</summary>
        public PromptDialogs Dialogs { get; private set; } = null!;

        /// <summary>The live state of a card on the field (stats, face-up or not), for the dialogs.</summary>
        public FieldCard? LiveCard(CardRef c)
        {
            // An Xyz material shares its monster's zone; it isn't that monster.
            if (_lastBoard == null || c.Controller > 1 || (c.Location & Location.Overlay) != 0) return null;
            var player = _lastBoard.Players[c.Controller];
            var list = (c.Location & 0x7f) == Location.MZone ? player.Monsters : (c.Location & 0x7f) == Location.SZone ? player.SpellTraps : null;
            return list != null && c.Sequence < list.Count ? list[(int)c.Sequence] : null;
        }

        private RectTransform? _actionMenu;

        /// <summary>The phase button: Battle Phase if you can, then Main Phase 2, then end the turn.</summary>
        private void NextPhase()
        {
            var set = Prompt.Current;
            if (set == null) return;
            foreach (var wanted in new[] { "Go to the Battle Phase", "Go to Main Phase 2", "End your turn" })
            {
                var i = set.Choices.FindIndex(c => c.Card == null && c.Label == wanted);
                if (i >= 0) { CloseActionMenu(); Prompt.ClickChoice(i); return; }
            }
        }

        private static uint FieldSpell(BoardState board, int p)
        {
            var zones = board.Players[p].SpellTraps;
            return zones.Count > 5 && zones[5] is FieldCard f && f.IsFaceUp ? f.Code : 0;
        }

        private void UpdatePhaseButton(BoardState board)
        {
            var phase = board.Phase == 0 ? "" : Phase.Name(board.Phase).Replace("Main Phase ", "Main ").Replace(" Phase", "");
            var set = Prompt.Current;
            bool canAdvance = set != null && set.Choices.Any(c => c.Card == null && (c.Label == "Go to the Battle Phase" || c.Label == "Go to Main Phase 2" || c.Label == "End your turn"));
            _phaseLabel.text = $"<size=17>{TurnText(board.Turn, compact: true)}</size>\n<b>{phase}</b>";
            // The bottom seat's turn is blue (yours, or the near duelist's when watching), the far seat's red.
            bool mine = board.TurnPlayer == (HumanSeat >= 0 ? HumanSeat : 0);
            // Brightest when a click on it changes phase; dimmer blue while you wait on your turn; red on theirs.
            var ring = canAdvance ? Ui.Hex("#7CC4FF") : mine ? Ui.Hex("#3D7FE0") : Ui.Hex("#D2424A");
            _phaseDisc.color = ring;
            _phaseHalo.color = new Color(ring.r, ring.g, ring.b, canAdvance ? 0.55f : 0.35f);
        }

        public void CloseActionMenu(bool returnFocus = false)
        {
            if (_actionMenu != null) Destroy(_actionMenu.gameObject);
            _actionMenu = null;
            if (returnFocus) FocusCard();
        }

        /// <summary>A row of buttons above a card: one per thing you can do with it, then Details (to read it) and Cancel.</summary>
        private void ShowActionMenu(RectTransform card, ChoiceSet? set, List<int> choices, CardInfo? details = null)
        {
            CloseActionMenu();
            var root = Ui.Stretched("ActionMenu", _field, Vector2.zero, Vector2.one);
            _actionMenu = root;
            // A click anywhere else closes the menu.
            var catcher = Ui.Image(root, new Color(0, 0, 0, 0.001f));
            Ui.Clickable((RectTransform)catcher.transform, () => CloseActionMenu());
            // Round icon buttons in a row above the card (Master Duel style), kept on the field.
            const float spacing = 130;
            var cardPos = card.anchoredPosition;
            float y = Mathf.Min(cardPos.y + card.sizeDelta.y / 2 + 70, 420);
            float x0 = Mathf.Clamp(cardPos.x - (choices.Count - 1) * spacing / 2, -560, 560 - (choices.Count - 1) * spacing);
            int discs = choices.Count + (details != null ? 2 : 1); // the card's actions, Details, then Cancel
            x0 = Mathf.Clamp(cardPos.x - (discs - 1) * spacing / 2, -560, 560 - (discs - 1) * spacing);
            for (int k = 0; k < choices.Count; k++)
            {
                int index = choices[k];
                var choice = set!.Choices[index];
                var label = choice.Verb.Length > 0 ? choice.Verb : choice.Label;
                LabelPill(Icons.Disc(root, new Vector2(x0 + k * spacing, y), label, () => { CloseActionMenu(); Prompt.ClickChoice(index); }, primary: k == 0));
            }
            int next = choices.Count;
            if (details != null) LabelPill(Icons.Disc(root, new Vector2(x0 + next++ * spacing, y), "Details", () => OpenReader(details), size: 72));
            LabelPill(Icons.Disc(root, new Vector2(x0 + next * spacing, y), "Cancel", () => CloseActionMenu(true), size: 72));
            root.SetAsLastSibling();
        }

        /// <summary>
        /// A dark pill behind an action disc's label, so the word reads cleanly over whatever is under it (a pile's count
        /// and name, a card).
        /// </summary>
        private static void LabelPill(RectTransform disc)
        {
            if (disc.Find("Label") is not RectTransform label) return;
            var text = label.GetComponent<Text>();
            float width = text != null ? Mathf.Min(text.preferredWidth + 22, label.sizeDelta.x) : label.sizeDelta.x;
            var pill = Ui.Placed("LabelBack", disc, label.anchoredPosition, new Vector2(width, 26));
            var image = Ui.Image(pill, new Color(0.03f, 0.04f, 0.08f, 0.88f));
            image.sprite = HudShapes.RoundedFill;
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            pill.SetSiblingIndex(label.GetSiblingIndex()); // just behind the label
        }

        // ---- Piles: double-click to look; a click on one you can use asks what you want -------------------------

        /// <summary>When a pile menu or viewer opened, so the second click of a double-click doesn't close it again.</summary>
        private float _pileOpenedAt = -10;
        private const float DoubleClickTime = 0.4f;
        private bool JustOpened => Time.unscaledTime - _pileOpenedAt < DoubleClickTime;

        /// <summary>Piles you may look through: both Graveyards and banished piles, and your own Extra Deck.</summary>
        private bool Viewable(int owner, uint location) =>
            location == Location.Grave || location == Location.Removed || (location == Location.Extra && owner != HiddenSeat);

        /// <summary>
        /// A pile was clicked. If it holds cards you can use right now (your turn, or a chance to respond), a menu at the
        /// pile: View, Summon or Activate, and Cancel. Picking cards from it (a target in the GY): the usual choice.
        /// Otherwise it opens to browse. A double-click always opens it to browse.
        /// </summary>
        private void PileClicked(int owner, uint location)
        {
            var set = Prompt.Current;
            var matches = set == null ? new List<int>() : Enumerable.Range(0, set.Choices.Count)
                .Where(i => set.Choices[i].Card is { } c && c.Controller == owner && (c.Location & 0x7f) == location).ToList();
            bool menu = set != null && (set.Prompt is IdlePrompt || set.Prompt is BattlePrompt || set.IsChain);
            if (matches.Count > 0 && !menu) { CardClicked(owner, location, -1); return; }
            if (matches.Count == 0)
            {
                if (Viewable(owner, location)) ViewPile(owner, location);
                return;
            }
            if (ViewAt(owner, location, -1) is RectTransform pile) ShowPileMenu(pile, owner, location, set!, matches);
        }

        /// <summary>The pile's own menu: View (when you may look), the action, and Cancel. A second click soon after views it.</summary>
        private void ShowPileMenu(RectTransform pile, int owner, uint location, ChoiceSet set, List<int> matches)
        {
            CloseActionMenu();
            var root = Ui.Stretched("ActionMenu", _field, Vector2.zero, Vector2.one);
            _actionMenu = root;
            _pileOpenedAt = Time.unscaledTime;
            var catcher = Ui.Image(root, new Color(0, 0, 0, 0.001f));
            Ui.Clickable((RectTransform)catcher.transform, () =>
            {
                bool doubleClick = JustOpened;
                CloseActionMenu();
                if (doubleClick && Viewable(owner, location)) ViewPile(owner, location);
            });
            // "Summon" when every choice is a summon (Extra Deck monsters); an effect that summons (Glow-Up Bulb) is an activation.
            bool summonsOnly = matches.All(i => set.Choices[i].Label.StartsWith("Special Summon") || set.Choices[i].Label.StartsWith("Summon"));
            string verb = summonsOnly ? "Summon" : "Activate";
            string where = location switch { Location.Extra => "Extra Deck", Location.Grave => "Graveyard", Location.Removed => "banished cards", _ => "Deck" };
            var buttons = new List<(string label, Action act, bool primary)>();
            if (Viewable(owner, location)) buttons.Add(("View", () => { CloseActionMenu(); ViewPile(owner, location); }, false));
            buttons.Add((verb, () => { CloseActionMenu(); ShowPilePicker($"{verb} from your {where}", set, matches); }, true));
            buttons.Add(("Cancel", () => CloseActionMenu(), false));
            const float spacing = 130;
            var at = pile.anchoredPosition;
            float y = Mathf.Min(at.y + pile.sizeDelta.y / 2 + 70, 420);
            // Kept clear of the turn and phase disc at the right edge of the field.
            float x0 = Mathf.Clamp(at.x - (buttons.Count - 1) * spacing / 2, -560, 450 - (buttons.Count - 1) * spacing);
            _menuButtons.Clear();
            for (int k = 0; k < buttons.Count; k++)
            {
                var (label, act, primary) = buttons[k];
                LabelPill(Icons.Disc(root, new Vector2(x0 + k * spacing, y), label, act, size: primary ? 92 : 72, primary: primary));
                _menuButtons[label] = act;
            }
            root.SetAsLastSibling();
        }

        /// <summary>The open pile menu's buttons by label (for checks).</summary>
        private readonly Dictionary<string, Action> _menuButtons = new Dictionary<string, Action>();

        /// <summary>For checks: clicks a pile; returns the menu's labels, or "viewer N" / "picker N" for what opened.</summary>
        public List<string> DebugPileClick(int owner, uint location)
        {
            PileClicked(owner, location);
            return DebugOverlay();
        }

        /// <summary>For checks: presses a button of the open pile menu (or Cancel on a picker), then reports as above.</summary>
        public List<string> DebugPress(string label)
        {
            if (_actionMenu != null && _menuButtons.TryGetValue(label, out var act)) act();
            else if (label == "Cancel") ClosePileViewer();
            return DebugOverlay();
        }

        /// <summary>For checks: a second click on the overlay just opened (the second click of a double-click).</summary>
        public List<string> DebugSecondClick(int owner, uint location)
        {
            if (_actionMenu != null)
            {
                bool doubleClick = JustOpened;
                CloseActionMenu();
                if (doubleClick && Viewable(owner, location)) ViewPile(owner, location);
            }
            else if (_pileViewer != null && !JustOpened) ClosePileViewer();
            return DebugOverlay();
        }

        private List<string> DebugOverlay()
        {
            if (_actionMenu != null) return _actionMenu.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
            if (_pileViewer == null) return new List<string>();
            var views = _pileViewer.GetComponentsInChildren<CardView>();
            return new List<string> { $"{(_pileViewer.name == "PilePicker" ? "picker" : "viewer")} {views.Length}" };
        }

        /// <summary>For checks: how many cards in the open viewer show their face.</summary>
        public int DebugViewerFaceUp() => _pileViewer == null ? 0 : _pileViewer.GetComponentsInChildren<CardView>().Count(v => v.ShowsFace);

        /// <summary>
        /// The cards in a pile you can use right now, one tile per thing you can do (a card with two effects shows twice),
        /// each labelled. Pick one, or Cancel to go back to what you were doing.
        /// </summary>
        private void ShowPilePicker(string title, ChoiceSet set, List<int> matches)
        {
            ClosePileViewer();
            var root = Ui.Stretched("PilePicker", _field, Vector2.zero, Vector2.one);
            _pileViewer = root;
            _pileOpenedAt = Time.unscaledTime;
            var dim = Ui.Image(root, new Color(0, 0, 0, 0.78f));
            Ui.Clickable((RectTransform)dim.transform, () => { if (!JustOpened) ClosePileViewer(); });
            Ui.Text(Ui.Placed("Title", root, new Vector2(0, 420), new Vector2(1200, 50)), title, 28, Color.white).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Placed("Hint", root, new Vector2(0, 382), new Vector2(1200, 30)), "Click a card to use it · hover to read it · Cancel to go back", 15, new Color(1, 1, 1, 0.55f));
            var view = Ui.Placed("View", root, new Vector2(0, -10), new Vector2(1240, 680));
            Ui.Image(view, new Color(0, 0, 0, 0.001f));
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Cards", view);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            const int perRow = 8;
            var size = new Vector2(120, 175);
            int rows = Math.Max(1, (matches.Count + perRow - 1) / perRow);
            content.sizeDelta = new Vector2(0, rows * (size.y + 46) + 10);
            for (int k = 0; k < matches.Count; k++)
            {
                int index = matches[k];
                var choice = set.Choices[index];
                var c = choice.Card!.Value;
                var info = _cards.Get(c.Code);
                int inRow = Math.Min(perRow, matches.Count - k / perRow * perRow);
                var center = new Vector2(-(inRow - 1) / 2f * (size.x + 26) + (k % perRow) * (size.x + 26),
                    content.sizeDelta.y / 2 - (k / perRow) * (size.y + 46) - size.y / 2 - 6);
                // The pile's own copy of the card (its Link Rating and stats as the board knows them), shown face-up.
                var live = PileCard(c);
                var shown = new FieldCard
                {
                    Code = c.Code, Type = live?.Type ?? info?.Type ?? 0, Level = live?.Level ?? info?.Level ?? 0, Rank = OrPrinted(live?.Rank ?? 0, info),
                    LinkRating = OrPrinted(live?.LinkRating ?? 0, info), Attack = live?.Attack ?? info?.Attack ?? 0, Defense = live?.Defense ?? info?.Defense ?? 0,
                    Position = Ygo.Engine.Position.FaceUpAttack, IsPublic = true,
                };
                CardView.Create(content, center, size, shown, info, HumanSeat, true, ShowInfo, offField: true,
                    onClick: () => { ClosePileViewer(); Prompt.ClickChoice(index); }, highlight: true, onRightClick: () => PinCard(info));
                var label = choice.Verb.Length > 0 ? choice.Verb : choice.Label;
                var text = Ui.Text(Ui.Placed("Action", content, center + new Vector2(0, -size.y / 2 - 18), new Vector2(size.x + 24, 34)), label, 14, Ui.Hex("#FFC940"));
                text.fontStyle = FontStyle.Bold;
            }
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            Ui.Button(root, "Cancel", new Vector2(0, -400), new Vector2(220, 52), ClosePileViewer);
            root.SetAsLastSibling();
        }

        /// <summary>
        /// A Rank or Link Rating the board doesn't know (0: cards in the Extra Deck aren't queried for it), else the printed
        /// one: the database keeps both in the card's level (as the deck editor shows them).
        /// </summary>
        private static uint OrPrinted(uint value, CardInfo? info) => value > 0 ? value : (info?.Level ?? 0) & 0xff;

        /// <summary>The board's copy of a card in a pile (GY, banished, Extra Deck), or null.</summary>
        private FieldCard? PileCard(CardRef c)
        {
            if (_lastBoard == null || c.Controller > 1) return null;
            var pb = _lastBoard.Players[c.Controller];
            var list = (c.Location & 0x7f) switch { Location.Grave => pb.Graveyard, Location.Removed => pb.Banished, Location.Extra => pb.ExtraDeck, _ => null };
            return list != null && c.Sequence < list.Count ? list[(int)c.Sequence] : null;
        }

        /// <summary>Opens a pile to browse (Graveyards, banished piles, your Extra Deck).</summary>
        private void ViewPile(int owner, uint location)
        {
            if (_lastBoard == null) return;
            var pb = _lastBoard.Players[owner];
            // Cards banished face-down stay face-down for everyone, yours included.
            string whose = _names[owner] == "You" ? "Your" : $"{_names[owner]}'s";
            if (location == Location.Grave) ShowPile($"{whose} Graveyard", pb.Graveyard, owner, true);
            else if (location == Location.Removed) ShowPile($"{whose} banished cards", pb.Banished, owner, false);
            else if (location == Location.Extra && owner != HiddenSeat) ShowPile($"{whose} Extra Deck", pb.ExtraDeck, owner, true);
        }

        /// <summary>Closes whatever the board has open over the field (a card or pile menu, a pile viewer or picker).</summary>
        public bool CloseOverlays()
        {
            bool any = _actionMenu != null || _pileViewer != null || _phasePicker != null || _concedeDialog != null || _checkSticky || _skipRecap != null || _reader != null;
            CloseReader();
            CloseSkipRecap();
            _checkSticky = false;
            CloseActionMenu(true);
            ClosePileViewer();
            ClosePhasePicker();
            CloseConcede();
            return any;
        }

        public List<string> DebugClickCard(int controller, uint location, int sequence)
        {
            CardClicked(controller, location, sequence, user: false);
            return _actionMenu == null ? new List<string>() : _actionMenu.GetComponentsInChildren<Text>().Select(t => t.text).ToList();
        }

        private RectTransform? _pileViewer;

        /// <summary>The cards in a pile, newest first; hover one to read it. Face-down banished cards stay hidden from you.</summary>
        private void ShowPile(string title, List<FieldCard?> cards, int owner, bool revealFaceDown)
        {
            if (_pileViewer != null) Destroy(_pileViewer.gameObject);
            var root = Ui.Stretched("PileViewer", _field, Vector2.zero, Vector2.one);
            _pileViewer = root;
            _pileOpenedAt = Time.unscaledTime;
            var dim = Ui.Image(root, new Color(0, 0, 0, 0.78f));
            // The second click of a double-click lands here: it doesn't close what the first one opened.
            Ui.Clickable((RectTransform)dim.transform, () => { if (!JustOpened) ClosePileViewer(); });
            Ui.Text(Ui.Placed("Title", root, new Vector2(0, 420), new Vector2(1200, 50)), $"{title}  ({cards.Count})", 28, Color.white).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Placed("Hint", root, new Vector2(0, 382), new Vector2(1200, 30)), "Hover a card to read it · scroll for more · click outside or press Esc to close", 15, new Color(1, 1, 1, 0.55f));
            var view = Ui.Placed("View", root, new Vector2(0, -30), new Vector2(1240, 760));
            Ui.Image(view, new Color(0, 0, 0, 0.001f));
            view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Rect("Cards", view);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            const int perRow = 10;
            var size = new Vector2(110, 160);
            var list = cards.Where(c => c != null).Select(c => c!).Reverse().ToList();
            int rows = Math.Max(1, (list.Count + perRow - 1) / perRow);
            content.sizeDelta = new Vector2(0, rows * (size.y + 16) + 10);
            for (int i = 0; i < list.Count; i++)
            {
                var card = list[i];
                bool shown = card.IsFaceUp || revealFaceDown || (card.Position & 0x0f) == 0;
                int inRow = Math.Min(perRow, list.Count - i / perRow * perRow);
                var center = new Vector2(-(inRow - 1) / 2f * (size.x + 12) + (i % perRow) * (size.x + 12),
                    content.sizeDelta.y / 2 - (i / perRow) * (size.y + 16) - size.y / 2 - 6);
                var shownInfo = _cards.Get(card.Code);
                var shownCard = new FieldCard
                {
                    Code = card.Code, Type = card.Type, Level = card.Level, Rank = OrPrinted(card.Rank, shownInfo),
                    LinkRating = OrPrinted(card.LinkRating, shownInfo), Attack = card.Attack, Defense = card.Defense,
                    Position = Ygo.Engine.Position.FaceUpAttack, IsPublic = true,
                };
                if (shown)
                {
                    CardView.Create(content, center, size, shownCard, shownInfo, owner, true, ShowInfo, offField: true, onRightClick: () => PinCard(shownInfo));
                }
                else
                {
                    // A card banished face-down: its back, for everyone.
                    var back = CardView.Create(content, center, size, card, null, owner, false, null, offField: true);
                    CardView.DrawBack(Ui.Stretched("Back", back.transform, Vector2.zero, Vector2.one));
                    Ui.Text(Ui.Placed("FaceDown", back.transform, new Vector2(0, -size.y / 2 + 14), new Vector2(size.x, 22)), "face-down", 12,
                        new Color(1, 1, 1, 0.75f)).fontStyle = FontStyle.Italic;
                }
            }
            if (list.Count == 0)
                Ui.Text(Ui.Placed("Empty", root, new Vector2(0, 100), new Vector2(600, 60)), "Empty.", 22, new Color(1, 1, 1, 0.6f));
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            root.SetAsLastSibling();
        }

        /// <summary>For automated checks: opens a pile's viewer and returns how many cards it shows.</summary>
        public void ClosePileViewer()
        {
            if (_pileViewer != null) Destroy(_pileViewer.gameObject);
            _pileViewer = null;
        }

        public int DebugBrowse(int owner, uint location)
        {
            PileClicked(owner, location);
            return _pileViewer == null ? -1 : _pileViewer.GetComponentsInChildren<CardView>().Length;
        }

        public void SetPlayers(string[] names, string[] deckNames)
        {
            _snapLifePoints = true;
            _stats.Clear();
            Fx.Clear();
            _names = names;
            _deckNames = deckNames;
            DrawHands();
            _banner.text = "";
            _bannerQuote.text = "";
        }

        /// <summary>Tag duels: the team's label after its duelists swap (the one in play first).</summary>
        public void SetPlayerName(int player, string name, string deck)
        {
            if (player < 0 || player >= _names.Length) return;
            _names = (string[])_names.Clone();
            _deckNames = (string[])_deckNames.Clone();
            _names[player] = name;
            _deckNames[player] = deck;
        }

        public void SetResult(string headline, string quote, string reward = "")
        {
            _banner.text = headline;
            _bannerQuote.text = (quote.Length > 0 ? $"\u201C{quote}\u201D" : "") + (reward.Length > 0 ? $"\n<color=#FFC940>{reward}</color>" : "");
            _bannerQuote.supportRichText = true;
        }

        public void SetControls(bool paused, float eventsPerSecond)
        {
            _pauseLabel.text = paused ? "Play" : "Pause";
            _speedLabel.text = $"{eventsPerSecond:0.#}/s";
        }

        // ---- Prized copies arriving face-up -------------------------------------------------------------

        private HashSet<uint> _faceUpBefore = new HashSet<uint>(), _faceUpNow = new HashSet<uint>();
        private bool _arrivalsArmed;

        /// <summary>Prized copies (by cover) that have had their moment this duel: once each.</summary>
        public readonly HashSet<uint> Prized = new HashSet<uint>();

        /// <summary>For checks: prized arrivals noticed this run.</summary>
        public int ArrivalsSeen { get; private set; }

        /// <summary>Prized copies of yours that turned face-up on the field since the last redraw, for BoardFx to celebrate.</summary>
        public readonly List<(uint cover, int controller, uint location, int sequence, CopyLook look)> Arrivals =
            new List<(uint, int, uint, int, CopyLook)>();

        /// <summary>A new duel (or a replay): nothing has arrived or sparkled yet, and the first board is the starting point.</summary>
        public void ResetPrizes()
        {
            _faceUpBefore.Clear();
            Prized.Clear();
            Arrivals.Clear();
            CardFoil.Prizes.Clear();
            _arrivalsArmed = false;
        }

        /// <summary>
        /// Which of your stamped cards are face-up on the field now, and which of them just arrived and are prized. Arrivals
        /// wait until BoardFx plays them (a prompt can redraw the board in between).
        /// </summary>
        private void NoteArrivals(BoardState board)
        {
            _faceUpNow.Clear();
            for (int p = 0; p < 2; p++)
            {
                var pb = board.Players[p];
                void Look(FieldCard? card, uint location, int sequence)
                {
                    if (card == null || card.Cover == 0 || !card.IsFaceUp) return;
                    _faceUpNow.Add(card.Cover);
                    if (!_arrivalsArmed || _faceUpBefore.Contains(card.Cover) || Prized.Contains(card.Cover) || Arrivals.Any(a => a.cover == card.Cover)) return;
                    if (CardFoil.DuelLooks?.Of(card) is CopyLook look && CardFoil.PrizeOf(look) != CardFoil.PrizeKind.None)
                    {
                        Arrivals.Add((card.Cover, p, location, sequence, look));
                        ArrivalsSeen++;
                    }
                }
                for (int i = 0; i < pb.Monsters.Count; i++) Look(pb.Monsters[i], Location.MZone, i);
                for (int i = 0; i < pb.SpellTraps.Count && i < 6; i++) Look(pb.SpellTraps[i], Location.SZone, i);
            }
            (_faceUpBefore, _faceUpNow) = (_faceUpNow, _faceUpBefore);
            _arrivalsArmed = true;
        }

        public void Render(BoardState board)
        {
            // A press held on a card: redrawing now would destroy the pressed card and lose the click. Drawn on release.
            if (DeferRenders && CardPressHeld())
            {
                _lastBoard = board;
                _renderPending = board;
                return;
            }
            _renderPending = null;
            _boardVersion++;
            _lastBoard = board;
            NoteArrivals(board);
            StatChanges(board);
            if (board.Players.Any(pl => pl.Monsters.Concat(pl.SpellTraps).Any(c => c != null && c.IsNegated))) NegatedShown?.Invoke();
            if (board.Players.Any(pl => pl.SpellTraps.Any(c => c != null && c.SetThisTurn && !c.IsFaceUp))) SetThisTurnShown?.Invoke();
            for (int i = _cardLayer.childCount - 1; i >= 0; i--)
                Destroy(_cardLayer.GetChild(i).gameObject);
            (_previousPositions, _positions) = (_positions, _previousPositions);
            _positions.Clear();
            _views.Clear();

            var phase = board.Phase == 0 ? "" : Phase.Name(board.Phase);
            _header.text = (_replayMode ? "REPLAY  -  " : "")
                           + (board.Turn == 0 ? "Starting duel..." : $"{TurnText(board.Turn)}  -  {_names[board.TurnPlayer]}  -  {phase}");
            UpdatePhaseButton(board);
            // Face-up Field Spells take over their side of the mat.
            Mat.SetFieldSpells(new[] { FieldSpell(board, 0), FieldSpell(board, 1) });

            SetActivePanel(board.Turn == 0 ? -1 : board.TurnPlayer);
            for (int p = 0; p < 2; p++)
            {
                var pb = board.Players[p];
                SetLifePoints(p, pb.LifePoints, pb.LastLpChange);

                bool reveal = p != HiddenSeat;
                for (int i = 0; i < pb.Monsters.Count; i++)
                    if (pb.Monsters[i] is FieldCard m) Card(m, MonsterZone(p, i), p, reveal, Location.MZone, i);
                for (int i = 0; i < pb.SpellTraps.Count && i < 6; i++)
                    if (pb.SpellTraps[i] is FieldCard s) Card(s, SpellZone(p, i), p, reveal, Location.SZone, i);

                // The hand fans out in an arc, like cards held in a hand; cards overlap when there are many.
                var count = pb.Hand.Count;
                var spacing = Mathf.Min(HandCardSize.x * 0.86f, 760f / Mathf.Max(1, count));
                float spread = Mathf.Min(5f, 36f / Mathf.Max(1, count));
                for (int i = 0; i < count; i++)
                {
                    if (pb.Hand[i] is not FieldCard h) continue;
                    var offset = i - (count - 1) / 2f;
                    var x = offset * spacing;
                    var y = -HandY - Mathf.Abs(offset) * Mathf.Abs(offset) * 2.2f;
                    int owner = p, seq = i;
                    // Cards you can play sit a little higher in your hand (with the gold border and ▶ tab).
                    if (p == HumanSeat && _actionable.Contains((p, Location.Hand, (uint)i))) y += 16;
                    var handInfo = HandShown(p) && reveal ? _cards.Get(h.Code) : null;
                    var view = CardView.Create(_cardLayer, Mirror(p, new Vector2(x, y)), HandCardSize, h, _cards.Get(h.Code), p,
                        HandShown(p) && reveal, ShowInfo, offField: true,
                        onClick: () => CardClicked(owner, Location.Hand, seq),
                        highlight: _actionable.Contains((p, Location.Hand, (uint)i)), flip: p == 1,
                        onRightClick: () => PinCard(handInfo), hand: true);
                    if (handInfo != null && _pinned != null && handInfo.Code == _pinned.Code) PinBadge(view.transform, HandCardSize);
                    if (handInfo != null) HandLevel(view.transform, handInfo);
                    var rt = (RectTransform)view.transform;
                    rt.localRotation *= Quaternion.Euler(0, 0, -offset * spread);
                    if (p == HumanSeat)
                    {
                        var lift = view.gameObject.AddComponent<HandLift>();
                        lift.Dropped = at => HandDrop(seq, at);
                        lift.Layer = _cardLayer;
                    }
                    Register(p, Location.Hand, i, rt);
                }

                // Empty zones you can choose (placing a card): a glowing outline to click.
                for (int i = 0; i < 7; i++)
                    if (_actionable.Contains((p, Location.MZone, (uint)i)) && (i >= pb.Monsters.Count || pb.Monsters[i] == null))
                        ZoneTarget(MonsterZone(p, i), p, Location.MZone, i);
                for (int i = 0; i < 6; i++)
                    if (_actionable.Contains((p, Location.SZone, (uint)i)) && (i >= pb.SpellTraps.Count || pb.SpellTraps[i] == null))
                        ZoneTarget(SpellZone(p, i), p, Location.SZone, i);

                Pile(p, "GY", Mirror(p, new Vector2(Column * 3, -Row)), pb.Graveyard, Location.Grave);
                Pile(p, "Banished", Mirror(p, new Vector2(Column * 4, -Row)), pb.Banished, Location.Removed);
                int pileOwner = p;
                Register(p, Location.Deck, -1, CardView.Pile(_cardLayer, "Deck", Mirror(p, new Vector2(Column * 3, -2 * Row)), pb.DeckCount, null, null, p, ShowInfo,
                    () => DeckClicked(pileOwner), _actionablePiles.Contains((p, Location.Deck))));
                Register(p, Location.Extra, -1, CardView.Pile(_cardLayer, "Extra", Mirror(p, new Vector2(-Column * 3, -2 * Row)), pb.ExtraDeck.Count, null, null, p, ShowInfo,
                    () => PileClicked(pileOwner, Location.Extra), _actionablePiles.Contains((p, Location.Extra))));
            }

            MarkFieldPicks();
            MarkChanges();

            if (board.Winner >= 0)
            {
                // Gold when you win (or for the winner when watching), red when you lose, white for a draw.
                bool decided = board.Winner is 0 or 1;
                _banner.color = !decided ? Color.white : HumanSeat < 0 || board.Winner == HumanSeat ? Ui.Hex("#FFC940") : Ui.Hex("#FF8A8A");
                _bannerRoot.gameObject.SetActive(true);
                _bannerRoot.SetAsLastSibling();
            }
            else
            {
                _bannerRoot.gameObject.SetActive(false);
            }
        }

        public void ClearLog()
        {
            _logLines.Clear();
            _log.text = "";
        }

        public void AppendLog(string line)
        {
            foreach (var part in line.Split('\n'))
            {
                if (part.Length == 0) continue;
                _logLines.Enqueue(LogStyled(part));
                while (_logLines.Count > MaxLogLines) _logLines.Dequeue();
            }
            _log.text = string.Join("\n", _logLines);
            _log.fontSize = FxSettings.Scaled(14);
        }

        private static readonly System.Text.RegularExpressions.Regex LogTurn =
            new System.Text.RegularExpressions.Regex(@"^=== Turn (\d+): (.+?)\s+\(LP (\d+) / (\d+)\) ===$");
        private static readonly System.Text.RegularExpressions.Regex LogResult =
            new System.Text.RegularExpressions.Regex(@"^\*\*\* (.+?)(?: \(reason \d+\))? \*\*\*$");

        /// <summary>
        /// The narrator's plain-text markers (shared with the harness's console logs) as the board shows them: a gold
        /// "Turn 3 · You" heading with the LP after it, and the result in gold without its stars or reason code.
        /// </summary>
        private static string LogStyled(string line)
        {
            var turn = LogTurn.Match(line);
            if (turn.Success)
                return $"<b><color=#FFC940>Turn {turn.Groups[1].Value} · {turn.Groups[2].Value}</color></b>   LP {turn.Groups[3].Value} / {turn.Groups[4].Value}";
            var result = LogResult.Match(line);
            return result.Success ? $"<b><color=#FFC940>{result.Groups[1].Value}</color></b>" : line;
        }

        public void ShowInfo(CardInfo info)
        {
            DetailShown();
            if (_pinned == null) { _detail.Show(info); return; }
            // Pinned: the pinned card stays; the hovered one shows in the second panel.
            _hoverRoot.gameObject.SetActive(true);
            _hoverRoot.SetAsLastSibling();
            _hoverDetail.Show(info);
            Canvas.ForceUpdateCanvases();
            _hoverDetail.Layout();
        }

        private CardInfo? _pinned;
        private CardDetail _hoverDetail = null!;
        private RectTransform _hoverRoot = null!;
        private Image _pinImage = null!, _hoverBg = null!;
        private Text _pinLabel = null!;

        /// <summary>
        /// Right-click a card: pin it (its detail stays while you point at others), or unpin it if it's the pinned one.
        /// No moving the mouse to the Pin button over other cards.
        /// </summary>
        public void PinCard(CardInfo? info)
        {
            if (info == null) return;
            bool same = _pinned != null && _pinned.Code == info.Code;
            if (_pinned != null) TogglePin(); // unpin
            if (same) return;
            _detail.Show(info);
            DetailShown();
            TogglePin();
        }

        /// <summary>
        /// A monster in your hand shows its Level (or Rank) big on its left edge, the part still in view when the hand
        /// fans out (Link Evolution): you see what you could Tribute Summon or Xyz at a glance.
        /// </summary>
        private static void HandLevel(Transform card, CardInfo info)
        {
            if ((info.Type & CardType.Monster) == 0 || (info.Type & CardType.Link) != 0) return;
            uint level = info.Level & 0xff;
            if (level == 0) return;
            var rt = Ui.Placed("Level", card, new Vector2(-HandCardSize.x / 2 + 17, -HandCardSize.y / 2 + 42), new Vector2(34, 40));
            bool rank = (info.Type & CardType.Xyz) != 0;
            var text = Ui.Text(rt, level.ToString(), FxSettings.Scaled(30), rank ? Ui.Hex("#D8DCE4") : Color.white);
            text.fontStyle = FontStyle.BoldAndItalic;
            text.raycastTarget = false;
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = rank ? new Color(0.1f, 0.1f, 0.12f, 0.95f) : new Color(0.35f, 0.15f, 0, 0.95f);
            outline.effectDistance = new Vector2(2, -2);
            text.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -3);
        }

        /// <summary>A small gold pin on the pinned card, wherever it is on the board.</summary>
        private static void PinBadge(Transform card, Vector2 size)
        {
            var badge = Ui.Placed("Pinned", card, new Vector2(-size.x / 2 + 12, size.y / 2 - 12), new Vector2(24, 24));
            var disc = badge.gameObject.AddComponent<RawImage>();
            disc.texture = Icons.Round;
            disc.color = Ui.Hex("#B8862B");
            disc.raycastTarget = false;
            Ui.Text(Ui.Stretched("P", badge, Vector2.zero, Vector2.one), "📌", 13, Color.white).raycastTarget = false;
        }

        /// <summary>Whether a text field has the keyboard (so shortcut keys stay out of its way).</summary>
        private static bool TypingSomewhere()
        {
            // Unity-null checks, not ?.: the selected object is often a prompt button that was just destroyed.
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            var selected = events.currentSelectedGameObject;
            if (selected == null) return false;
            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        // ---- Where an attack is going -------------------------------------------------------------------------

        /// <summary>The monster chosen to attack with (for the arrow while you choose its target).</summary>
        private CardRef? _attacker;

        /// <summary>The attack arrow (for checks): whether it's up, a preview, and its readout.</summary>
        public (bool showing, bool preview, string readout) AttackArrowState => (_arrow.Showing, _arrow.Preview, _arrow.Readout);

        /// <summary>
        /// The arrow from an attacker to its target (to the player for a direct attack, To.Location 0), with what the
        /// battle compares: "1800 vs 1500", green when the attacker wins, red when it loses.
        /// </summary>
        public void ShowAttackArrow(CardRef from, CardRef to, bool preview = false)
        {
            var attackerView = ViewAt(from.Controller, Location.MZone, (int)from.Sequence);
            if (attackerView == null) return;
            bool direct = (to.Location & 0x7f) == 0;
            var target = direct ? PlayerSide(1 - from.Controller)
                : ViewAt(to.Controller, to.Location & 0x7f, (int)to.Sequence)?.anchoredPosition ?? PreviousPosition(to.Controller, to.Location & 0x7f, (int)to.Sequence);
            int atk = LiveCard(from)?.Attack ?? 0;
            string readout;
            Color color;
            if (direct)
            {
                readout = $"{atk} damage";
                color = Ui.Hex("#FFD24A");
            }
            else
            {
                var defender = LiveCard(new CardRef(to.Code, to.Controller, Location.MZone, to.Sequence, 0));
                if (defender == null || !defender.IsFaceUp)
                {
                    readout = $"{atk} vs ?";
                    color = Color.white;
                }
                else
                {
                    bool defense = defender.IsDefense;
                    int stat = defense ? defender.Defense : defender.Attack;
                    readout = $"{atk} vs {stat}{(defense ? " DEF" : "")}";
                    color = atk > stat ? Ui.Hex("#7BE07B") : atk == stat ? Color.white : Ui.Hex("#FF7A6A");
                }
            }
            _arrow.Show(attackerView.anchoredPosition, target, from.Controller == HumanSeat, readout, color, preview);
            _arrowEnds = (from, direct ? (CardRef?)null : to);
        }

        public void HideAttackArrow() => _arrow.Hide();

        /// <summary>The attack under way (its arrow showing, not a preview): attacker and target (null: a direct attack).</summary>
        public (CardRef from, CardRef? to)? AttackShowing => _arrow.Showing && !_arrow.Preview ? _arrowEnds : ((CardRef, CardRef?)?)null;

        /// <summary>The attacker and its target (null for a direct attack) of the arrow showing.</summary>
        private (CardRef from, CardRef? to) _arrowEnds;

        /// <summary>
        /// A card left a Monster Zone: if it was the attacker or its target, the attack is cut short (banished by
        /// Dimensional Prison, destroyed by Mirror Force, bounced) and its arrow goes.
        /// </summary>
        public void MonsterLeft(CardRef from)
        {
            if (!_arrow.Showing || _arrow.Preview || (from.Location & 0x7f) != Location.MZone) return;
            bool Same(CardRef r) => r.Controller == from.Controller && (r.Location & 0x7f) == Location.MZone && r.Sequence == from.Sequence;
            if (Same(_arrowEnds.from) || (_arrowEnds.to is { } to && Same(to))) HideAttackArrow();
        }

        /// <summary>For checks: the status line under the board ("Opponent is playing...", "Resolving...").</summary>
        public string StatusLine => _waiting != null ? _waiting.text : "";

        /// <summary>The controller's Card terms button: the next explained term of the card being read. Its title, or null.</summary>
        public string? NextTerm() => (_hoverRoot.gameObject.activeSelf ? _hoverDetail : _detail).ShowNextTerm();

        /// <summary>For checks: the card detail's tags (Spell Speed first, then genres).</summary>
        public List<string> DetailTags => _detail.ShownTags;

        /// <summary>Unpins whatever is pinned (a new duel starts with nothing pinned).</summary>
        public void Unpin()
        {
            if (_pinned != null) TogglePin();
        }

        /// <summary>Pins the card in the detail panel (or unpins it).</summary>
        public void TogglePin()
        {
            _pinned = _pinned == null ? _detail.Shown : null;
            _pinLabel.text = _pinned != null ? "Unpin (P)" : "Pin (P)";
            if (_lastBoard != null) Render(_lastBoard); // the pin badge on the card
            _pinImage.color = _pinned != null ? Ui.Hex("#B8862B") : new Color(0.16f, 0.22f, 0.34f, 0.95f);
            if (_pinned == null) _hoverRoot.gameObject.SetActive(false);
            // The second panel takes the decision list's place on the campaign board, the picker's in free play.
            var rt = _hoverRoot;
            if (_playLayout) { rt.anchorMin = new Vector2(0, 0.0f); rt.anchorMax = new Vector2(1, 0.43f); }
            else { rt.anchorMin = new Vector2(0, 0.78f); rt.anchorMax = new Vector2(1, 1f); }
        }

        /// <summary>For checks: the pinned card's name, and the second panel's.</summary>
        public (string? pinned, string? hovered) PinState => (_pinned?.Name, _hoverRoot.gameObject.activeSelf ? _hoverDetail.Shown?.Name : null);

        /// <summary>The card detail panel (for checks).</summary>
        public CardDetail Detail => _detail;
        /// <summary>The second (compare) panel shown while a card is pinned (for checks).</summary>
        public CardDetail HoverDetail => _hoverDetail;

        private void ZoneTarget(Vector2 center, int owner, uint location, int sequence)
        {
            var rt = Ui.Placed("ZoneTarget", _cardLayer, center, CardView.FieldSize);
            // A solid gold slot with dark words: it reads on the gold mat as well as the blue one.
            var glow = Ui.Image(rt, new Color(1f, 0.79f, 0.25f, 0.75f));
            var outline = glow.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.79f, 0.25f, 0.95f);
            outline.effectDistance = new Vector2(3, -3);
            Ui.Text(Ui.Stretched("Label", rt, Vector2.zero, Vector2.one), "Place\nhere", 15, Ui.Hex("#161A23")).fontStyle = FontStyle.Bold;
            Ui.Clickable(rt, () => CardClicked(owner, location, sequence));
            Register(owner, location, sequence, rt);
        }

        private void Card(FieldCard card, Vector2 center, int owner, bool reveal, uint location, int sequence)
        {
            var readable = card.IsFaceUp || reveal ? _cards.Get(card.Code) : null;
            var view = CardView.Create(_cardLayer, center, CardView.FieldSize, card, _cards.Get(card.Code), owner, reveal, ShowInfo,
                onClick: () => CardClicked(owner, location, sequence),
                highlight: _actionable.Contains((owner, location, (uint)sequence)), flip: owner == 1,
                onRightClick: () => PinCard(readable));
            if (readable != null && _pinned != null && readable.Code == _pinned.Code) PinBadge(view.transform, CardView.FieldSize);
            Register(owner, location, sequence, (RectTransform)view.transform);
        }

        private void Pile(int owner, string label, Vector2 center, List<FieldCard?> cards, uint location)
        {
            var top = cards.Count > 0 ? cards[cards.Count - 1] : null;
            // A card banished face-down shows its back on top of the pile too (for everyone).
            if (top != null && location == Location.Removed && !top.IsFaceUp && (top.Position & 0x0f) != 0) top = null;
            Register(owner, location, -1, CardView.Pile(_cardLayer, label, center, cards.Count, top, top == null ? null : _cards.Get(top.Code), owner, ShowInfo,
                () => PileClicked(owner, location), _actionablePiles.Contains((owner, location))));
        }

        /// <summary>
        /// Picking cards on the field: the cards you can pick glow (a soft pulse behind them) and picked ones get a gold
        /// checkmark.
        /// </summary>
        private void MarkFieldPicks()
        {
            var set = Prompt.Current;
            if (set == null || Dialogs.Kind != "field") return;
            for (int i = 0; i < set.Choices.Count; i++)
            {
                if (set.Choices[i].Card is not { } c || ViewAt(c.Controller, c.Location, (int)c.Sequence) is not RectTransform view) continue;
                var glow = Ui.Placed("PickGlow", _cardLayer, view.anchoredPosition, CardView.FieldSize * 1.7f).gameObject.AddComponent<RawImage>();
                glow.texture = BoardFx.Textures.Glow;
                glow.color = new Color(1f, 0.85f, 0.3f, 0.6f);
                glow.raycastTarget = false;
                glow.transform.SetAsFirstSibling();
                glow.gameObject.AddComponent<Pulse>();
                // Choosing what to attack: pointing at a target draws the arrow to it, with the ATK comparison.
                if (_attacker is { } attacker && set.Title.StartsWith("Select the attack target"))
                {
                    var target = c;
                    var hover = view.gameObject.AddComponent<HoverTip>();
                    hover.Entered = () => ShowAttackArrow(attacker, target, preview: true);
                    hover.Exited = () => { if (_arrow.Preview) HideAttackArrow(); };
                }
                if (!Prompt.IsSelected(i) && !set.Choices[i].AlreadySelected) continue;
                var at = view.anchoredPosition + new Vector2(CardView.FieldSize.x / 2 - 8, CardView.FieldSize.y / 2 - 8);
                var badge = Ui.Placed("Picked", _cardLayer, at, new Vector2(34, 34));
                badge.localRotation = Quaternion.Euler(0, 0, 45);
                Ui.Image(badge, Ui.Hex("#13203A")).raycastTarget = false;
                var mark = Ui.Text(Ui.Placed("Check", _cardLayer, at, new Vector2(34, 34)), "✓", 24, CardView.Actionable);
                mark.raycastTarget = false;
            }
        }

        /// <summary>Breathes a glow's alpha.</summary>
        private sealed class Pulse : MonoBehaviour
        {
            private RawImage? _image;
            private float _base;
            private void Start() { _image = GetComponent<RawImage>(); _base = _image.color.a; }
            private void Update()
            {
                if (_image == null) return;
                var c = _image.color;
                c.a = _base * (0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4));
                _image.color = c;
            }
        }

        // ---- Drag to play -------------------------------------------------------------------------------------

        /// <summary>Where a dragged card was dropped, so the zone choice that follows can take that zone.</summary>
        private (uint location, int sequence)? _dropZone;

        /// <summary>
        /// A hand card dropped on the field: play it the obvious way (monsters are Summoned, or Set when dropped on the
        /// Spell &amp; Trap row; Spells and Traps are activated, or Set). When it could go several ways, the card's own
        /// menu opens. Returns false when the drop does nothing (the card goes back to the hand).
        /// </summary>
        public bool HandDrop(int sequence, Vector2 at)
        {
            var set = Prompt.Current;
            if (set == null || at.y < -HandY + HandCardSize.y * 0.6f) return false;
            var matches = new List<int>();
            for (int i = 0; i < set.Choices.Count; i++)
                if (set.Choices[i].Card is { } c && c.Controller == HumanSeat && (c.Location & 0x7f) == Location.Hand && c.Sequence == sequence) matches.Add(i);
            if (matches.Count == 0) return false;

            var zone = NearestZone(at);
            bool spellRow = zone?.location == Location.SZone;
            var info = set.Choices[matches[0]].Card is { } card ? _cards.Get(card.Code) : null;
            bool monster = info != null && (info.Type & CardType.Monster) != 0;
            var preferred = monster
                ? (spellRow ? new[] { "Set", "Summon", "Special Summon" } : new[] { "Summon", "Special Summon", "Set" })
                : (spellRow || zone == null ? new[] { "Activate", "Set" } : new[] { "Activate", "Set" });
            int pick = -1;
            foreach (var verb in preferred)
            {
                pick = matches.Where(i => set.Choices[i].Verb == verb).DefaultIfEmpty(-1).First();
                if (pick >= 0) break;
            }
            if (pick < 0 && matches.Count == 1) pick = matches[0];
            _dropZone = zone;
            if (pick >= 0) { Prompt.ClickChoice(pick); return true; }
            // Several ways to play it and none obvious: the card's menu, as if it were clicked.
            if (ViewAt(HumanSeat, Location.Hand, sequence) is RectTransform view) ShowActionMenu(view, set, matches, ReadableAt(HumanSeat, Location.Hand, sequence));
            return true;
        }

        /// <summary>The zone of yours nearest a point on the field, if one is close.</summary>
        private (uint location, int sequence)? NearestZone(Vector2 at)
        {
            (uint, int)? best = null;
            float bestDistance = 90;
            for (int i = 0; i < 7; i++)
            {
                var d = Vector2.Distance(at, MonsterZone(HumanSeat, i));
                if (d < bestDistance) { bestDistance = d; best = (Location.MZone, i); }
            }
            for (int i = 0; i < 6; i++)
            {
                var d = Vector2.Distance(at, SpellZone(HumanSeat, i));
                if (d < bestDistance) { bestDistance = d; best = (Location.SZone, i); }
            }
            return best;
        }

        /// <summary>After a drop: a zone choice takes the zone the card was dropped on, when it's one of the options.</summary>
        private bool TakeDropZone(ChoiceSet set)
        {
            if (_dropZone is not { } zone) return false;
            if (set.Prompt is IdlePrompt || set.Prompt is BattlePrompt) { _dropZone = null; return false; }
            if (!set.Title.StartsWith("Choose a zone")) return false;
            _dropZone = null;
            for (int i = 0; i < set.Choices.Count; i++)
                if (set.Choices[i].Card is { } c && c.Controller == HumanSeat && (c.Location & 0x7f) == zone.location && c.Sequence == zone.sequence)
                {
                    Prompt.ClickChoice(i);
                    return true;
                }
            return false;
        }

        /// <summary>For checks: shows a life point change as if it happened in the duel (jolt, trail, heartbeat).</summary>
        public void DebugLifePoints(int player, int value) => SetLifePoints(player, value);
        /// <summary>For checks: an LP change on a panel as the duel would make it (with its reason).</summary>
        public void DebugLifePointChange(int player, int value, LpChange why) => SetLifePoints(player, value, why);

        /// <summary>For checks: the pointer over hand card <paramref name="sequence"/> (it lifts out of the fan).</summary>
        public void DebugHoverHandCard(int sequence)
        {
            if (ViewAt(HumanSeat, Location.Hand, sequence)?.GetComponent<HandLift>() is HandLift lift) lift.OnPointerEnter(null!);
        }

        /// <summary>For checks: drops hand card <paramref name="sequence"/> on one of your zones, as a drag would.</summary>
        public bool DebugDropHandCard(int sequence, uint location, int zone) =>
            HandDrop(sequence, location == Location.SZone ? SpellZone(HumanSeat, zone) : MonsterZone(HumanSeat, zone));

        private void Register(int owner, uint location, int sequence, RectTransform view)
        {
            _views[(owner, location, sequence)] = view;
            _positions[(owner, location, sequence)] = view.anchoredPosition;
        }

        private static bool IsPile(uint location) =>
            location == Location.Deck || location == Location.Extra || location == Location.Grave || location == Location.Removed;

        /// <summary>The card (or pile) drawn at a place in the latest render, if any.</summary>
        // ---- Watching: whose hands show, and what changed after a jump --------------------------------------

        private GameObject _handsRoot = null!;
        private Text _handsLabel = null!;
        /// <summary>Watching: 0 shows both hands, 1 only the bottom player's, 2 only the top player's.</summary>
        public int HandsMode { get; private set; }

        private bool HandShown(int seat) => RevealHands && (_humanMode || HandsMode == 0 || HandsMode - 1 == seat);

        public void CycleHands()
        {
            HandsMode = (HandsMode + 1) % 3;
            DrawHands();
            if (_lastBoard != null) Render(_lastBoard);
        }

        private void DrawHands()
        {
            string First(int seat) { var n = seat < _names.Length ? _names[seat].Split(' ', '&')[0] : ""; return n.Length > 9 ? n.Substring(0, 9) : n; }
            _handsLabel.text = HandsMode == 0 ? "Hands: <b><color=#5FE1FF>BOTH</color></b>" : $"Hand: <b><color=#5FE1FF>{First(HandsMode - 1).ToUpperInvariant()}</color></b>";
        }

        /// <summary>For checks: the hands pill's text.</summary>
        public string HandsText => _handsRoot.activeSelf ? _handsLabel.text : "";

        private BoardChanges? _changes;
        private static readonly Color ChangeColor = Ui.Hex("#5FE1FF");

        /// <summary>Replays: marks what changed since the position the viewer jumped from (null clears it).</summary>
        public void ShowChanges(BoardChanges? changes)
        {
            if (changes == null && _changes == null) return;
            _changes = changes;
            if (_lastBoard != null) Render(_lastBoard);
        }

        /// <summary>For checks: how many change marks are on the board.</summary>
        public int ChangeMarks { get; private set; }

        /// <summary>
        /// A cyan glow and a NEW / CHANGED tag on cards that arrived or changed, a LEFT outline where a card was, and
        /// "+2" / "-2400 LP" tags by the piles and Life Points: words with the color, so it isn't color alone.
        /// </summary>
        private void MarkChanges()
        {
            ChangeMarks = 0;
            if (_changes == null) return;
            foreach (var (p, location, sequence, kind, _) in _changes.Field)
            {
                var at = location == Location.MZone ? MonsterZone(p, sequence) : SpellZone(p, sequence);
                if (kind == ChangeKind.Left)
                {
                    var ghost = Ui.Placed("Left", _cardLayer, at, CardView.FieldSize - new Vector2(10, 10));
                    var fill = Ui.Image(ghost, new Color(ChangeColor.r, ChangeColor.g, ChangeColor.b, 0.08f));
                    fill.raycastTarget = false;
                    var line = ghost.gameObject.AddComponent<Outline>();
                    line.effectColor = new Color(ChangeColor.r, ChangeColor.g, ChangeColor.b, 0.7f);
                    line.effectDistance = new Vector2(2, -2);
                }
                else
                {
                    var glow = Ui.Placed("Changed", _cardLayer, at, CardView.FieldSize * 1.6f).gameObject.AddComponent<RawImage>();
                    glow.texture = BoardFx.Textures.Glow;
                    glow.color = new Color(ChangeColor.r, ChangeColor.g, ChangeColor.b, 0.55f);
                    glow.raycastTarget = false;
                    glow.transform.SetAsFirstSibling();
                    glow.gameObject.AddComponent<Pulse>();
                }
                // The tag goes on the open side: monster zones toward the middle of the field, Spell/Trap zones toward the
                // player's own edge.
                float side = (location == Location.MZone ? 1 : -1) * (p == 0 ? 1 : -1);
                ChangeTag(at + new Vector2(0, side * (CardView.FieldSize.y / 2 + 10)), kind == ChangeKind.Arrived ? "NEW" : kind == ChangeKind.Changed ? "CHANGED" : "LEFT");
            }
            foreach (var pile in _changes.Piles)
            {
                var (p, location) = pile.Key;
                if (location is not (Location.Grave or Location.Removed or Location.Extra) || ViewAt(p, location, -1) is not RectTransform view) continue;
                // Above your piles, below the opponent's (under their count).
                ChangeTag(view.anchoredPosition + new Vector2(0, p == 0 ? CardView.FieldSize.y / 2 + 10 : -CardView.FieldSize.y / 2 - 32),
                    pile.Value > 0 ? $"+{pile.Value}" : $"−{-pile.Value}");
            }
            for (int p = 0; p < 2; p++)
            {
                int delta = _changes.LifeAfter[p] - _changes.LifeBefore[p];
                if (delta == 0) continue;
                // On the plate itself: its change chip beside the LP (a tag floating outside the plate read as a stray).
                ShowChange(p, delta, LpChange.None);
                ChangeMarks++;
            }
        }

        private void ChangeTag(Vector2 at, string text, float width = 0)
        {
            var tag = Ui.Placed("ChangeTag", _cardLayer, at, new Vector2(width > 0 ? width : Mathf.Max(38, text.Length * 10.5f + 16), 22));
            Ui.Image(tag, new Color(0.02f, 0.08f, 0.12f, 0.92f)).raycastTarget = false;
            var edge = tag.gameObject.AddComponent<Outline>();
            edge.effectColor = ChangeColor;
            edge.effectDistance = new Vector2(1, -1);
            var t = Ui.Text(tag, text, 14, ChangeColor);
            t.fontStyle = FontStyle.Bold;
            t.raycastTarget = false;
            ChangeMarks++;
        }

        public RectTransform? ViewAt(int owner, uint location, int sequence)
        {
            location &= 0x7f;
            return _views.TryGetValue((owner, location, IsPile(location) ? -1 : sequence), out var v) && v != null ? v : null;
        }

        /// <summary>Where a place was in the render before the latest one (falls back to the latest, then the mat layout).</summary>
        public Vector2 PreviousPosition(int owner, uint location, int sequence)
        {
            location &= 0x7f;
            var key = (owner, location, IsPile(location) ? -1 : sequence);
            if (_previousPositions.TryGetValue(key, out var p)) return p;
            if (_positions.TryGetValue(key, out p)) return p;
            return location switch
            {
                Location.MZone => MonsterZone(owner, sequence),
                Location.SZone => SpellZone(owner, sequence),
                Location.Hand => Mirror(owner, new Vector2(0, -HandY)),
                Location.Grave => Mirror(owner, new Vector2(Column * 3, -Row)),
                Location.Removed => Mirror(owner, new Vector2(Column * 4, -Row)),
                Location.Extra => Mirror(owner, new Vector2(-Column * 3, -2 * Row)),
                _ => Mirror(owner, new Vector2(Column * 3, -2 * Row)), // deck
            };
        }

        /// <summary>The middle of a player's side of the mat (for damage numbers and direct attacks).</summary>
        public static Vector2 PlayerSide(int player) => Mirror(player, new Vector2(0, -Row * 1.5f));

        /// <summary>Hand size in the latest render.</summary>
        public int HandCount(int player) => _lastBoard?.Players[player].Hand.Count ?? 0;

        /// <summary>Deck size in the latest render.</summary>
        public int DeckCount(int player) => _lastBoard?.Players[player].DeckCount ?? 0;

        public bool IsHidden(int owner) => owner == HiddenSeat;

        private void SetLifePoints(int p, int value, LpChange why = LpChange.None)
        {
            if (!_snapLifePoints) ShowChange(p, value - _lpTarget[p], why); // not when a duel starts or a replay jumps
            // The LP bar is scaled to the duel's starting LP (a lesson's 2000 fills it), learned when the duel starts.
            if (_snapLifePoints && value > 0) _lpScale[p] = value;
            if (_snapLifePoints || LifePointRollTime <= 0) { _lpShown[p] = value; _lpTrail[p] = value; }
            else if (value != _lpTarget[p])
            {
                _lpRollRate[p] = Mathf.Max(400f, Mathf.Abs(value - _lpShown[p]) / LifePointRollTime);
                if (value < _lpTarget[p])
                {
                    // Hit: the plate jolts (harder for bigger hits) and the trail waits before draining.
                    _jolt[p] = Mathf.Clamp01((_lpTarget[p] - value) / 3000f) * 0.6f + 0.4f;
                    _lpTrailHold[p] = Time.unscaledTime + 0.45f;
                }
                else _lpTrail[p] = value;
            }
            _lpTarget[p] = value;
            if (_snapLifePoints && p == 1) _snapLifePoints = false;
            DrawLifePoints(p);
        }

        private void DrawLifePoints(int p)
        {
            var shown = Mathf.RoundToInt(_lpShown[p]);
            var falling = shown > _lpTarget[p];
            var rising = shown < _lpTarget[p];
            var lp = falling ? $"<color=#FF6B6B>{shown}</color>" : rising ? $"<color=#7CE38B>{shown}</color>" : shown.ToString();
            DrawPanel(p, lp);
            _plateDeck[p].text = _deckNames[p];
        }

        private void Update()
        {
            try { UpdateClicks(); }
            catch (Exception e) { Debug.LogException(e); }
            // The animation clock runs whatever else goes wrong this frame: if it stops, the board stops changing.
            try { UpdateKeys(); }
            catch (Exception e) { Debug.LogException(e); }
            UpdatePendingPhase();
            try { UpdateCheckField(); UpdateColumnGuide(); UpdateSkip(); UpdateDuelTools(); }
            catch (Exception e) { Debug.LogException(e); }
            UpdatePlayChrome();
            UpdatePanels();
            for (int p = 0; p < 2; p++)
            {
                if (_plateRoot[p] == null) continue;
                UpdateMood(p);
                // The trail drains after its pause.
                if (_lpTrail[p] > _lpShown[p] && Time.unscaledTime > _lpTrailHold[p])
                {
                    _lpTrail[p] = Mathf.MoveTowards(_lpTrail[p], _lpShown[p], 5000 * Time.unscaledDeltaTime);
                    DrawLifePoints(p);
                }
                // A jolt when hit: the plate shakes and flashes red, settling quickly.
                if (_jolt[p] > 0)
                {
                    _jolt[p] = Mathf.Max(0, _jolt[p] - Time.unscaledDeltaTime * 2.2f);
                    float k = _jolt[p];
                    // (With reduced motion the panel flashes but doesn't shake.)
                    _plateRoot[p].anchoredPosition = FxSettings.ReducedMotion ? _plateHome[p]
                        : _plateHome[p] + new Vector2(Mathf.Sin(Time.unscaledTime * 70) * 9 * k, Mathf.Cos(Time.unscaledTime * 55) * 5 * k);
                }
                else _plateRoot[p].anchoredPosition = _plateHome[p];
                // At 1000 LP or less the plate beats like a heart: the frame glows red and the number pulses.
                bool danger = _lpTarget[p] > 0 && _lpTarget[p] <= 1000;
                float beat = danger ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(Time.unscaledTime * 6.5f)), 6) : 0;
                var red = new Color(1f, 0.2f, 0.2f, 1);
                var frameColor = Color.Lerp(_plateAccent[p], red, Mathf.Max(_jolt[p], danger ? 0.5f + 0.5f * beat : 0));
                _plateFrame[p].effectColor = new Color(frameColor.r, frameColor.g, frameColor.b, 0.85f);
                _plateFrame[p].effectDistance = Vector2.one * (2 + 3 * Mathf.Max(beat, _jolt[p])) * new Vector2(1, -1);
                _lifePoints[p].rectTransform.localScale = Vector3.one * (1 + 0.15f * beat + 0.2f * _jolt[p]);
                if (Mathf.Approximately(_lpShown[p], _lpTarget[p])) continue;
                _lpShown[p] = Mathf.MoveTowards(_lpShown[p], _lpTarget[p], _lpRollRate[p] * Time.unscaledDeltaTime);
                DrawLifePoints(p);
            }
            if (_waiting != null)
            {
                var board = _lastBoard;
                bool waiting = _humanMode && Prompt.Current == null && board != null && board.Winner < 0;
                var dots = new string('.', 1 + (int)(Time.unscaledTime * 2) % 3);
                _waiting.text = !waiting ? ""
                    : IsPaused?.Invoke() == true ? "<color=#FFC940><b>Paused</b></color> · click to resume"
                    : Skipping ? $"<color=#8CC8FF>Skipping to your turn{dots}</color>"
                    : board!.TurnPlayer != HumanSeat ? $"Opponent is playing{dots}"
                    // "Resolving" only while a chain is actually resolving (not at the start, or in a quiet End Phase).
                    : ChainLinksShown > 0 ? $"Resolving{dots}" : "";
            }
            bool holding = _humanMode && HoldingAsk?.Invoke() == true;
            if (holding != _holding) { _holding = holding; DrawResponses(); }
            if (_pointAt.Length > 0 || _pointer != null) UpdatePointer();
            Fx.Tick();
        }

        private void UpdateKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (keys?.escapeKey.wasPressedThisFrame == true) CloseOverlays();
            if (_humanMode && keys != null && !TypingSomewhere() && KeysElsewhere?.Invoke() != true)
            {
                if (keys.pKey.wasPressedThisFrame) TogglePin();                // pin (or unpin) the card the detail shows
                if (keys.dKey.wasPressedThisFrame) ToggleReader();             // read the card the detail shows, full screen
                if (keys.bKey.wasPressedThisFrame) GoToPhase("battle");
                if (keys.mKey.wasPressedThisFrame) GoToPhase("main2-direct"); // from the Battle Phase to Main Phase 2 (never starts a Battle Phase)
                if (keys.eKey.wasPressedThisFrame) GoToPhase("end");
                if (keys.sKey.wasPressedThisFrame) RequestSkip();
            }
#else
            if (Input.GetKeyDown(KeyCode.Escape)) CloseOverlays();
#endif
        }

        /// <summary>Main Monster Zones 0-4 left to right from the owner's view; 5-6 are the shared Extra Monster Zones.</summary>
        private static Vector2 MonsterZone(int owner, int seq)
        {
            if (seq >= 5) return Mirror(owner, new Vector2(seq == 5 ? -Column : Column, 0));
            return Mirror(owner, new Vector2((seq - 2) * Column, -Row));
        }

        private static Vector2 SpellZone(int owner, int seq) =>
            seq == 5
                ? Mirror(owner, new Vector2(-Column * 3, -Row)) // Field Zone sits left of the monster row
                : Mirror(owner, new Vector2((seq - 2) * Column, -2 * Row));

        /// <summary>Layout is written for player 0 (bottom); player 1 is rotated 180 degrees around the center.</summary>
        private static Vector2 Mirror(int owner, Vector2 bottomPosition) =>
            owner == 0 ? bottomPosition : -bottomPosition;
    }
}

namespace YgoCampaign.Game
{
    /// <summary>
    /// A card in your hand: under the pointer it straightens, lifts and grows; dragged, it follows the pointer and can
    /// be dropped on the field to play it (<see cref="Dropped"/> says whether the drop did anything, else it goes home).
    /// </summary>
    public sealed class HandLift : UnityEngine.MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler,
        UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        public System.Func<UnityEngine.Vector2, bool>? Dropped;
        public UnityEngine.RectTransform? Layer;
        private UnityEngine.Vector2 _home;
        private UnityEngine.Quaternion _homeRotation;
        private float _target, _amount;
        private bool _started, _dragging;

        /// <summary>
        /// The card's resting footprint, kept as a clear hit area while it lifts: the pointer that entered near the card's
        /// bottom edge stays "on" it as the card rises away, so it doesn't drop back under the pointer, rise again and
        /// flicker (a click there would land on a moving card).
        /// </summary>
        private UnityEngine.RectTransform? _homeHit;

        /// <summary>For checks: whether the resting footprint is kept (off: the old lift, for comparison).</summary>
        public static bool KeepHomeHit = true;

        /// <summary>For checks: pointer enters and exits on hand cards since the last reset.</summary>
        public static int Enters, Exits;

        private void Remember()
        {
            // Home is wherever the card rests now (draw animations may have just moved it in).
            var rt = (UnityEngine.RectTransform)transform;
            _home = rt.anchoredPosition;
            _homeRotation = rt.localRotation;
            _started = true;
            if (_homeHit == null && KeepHomeHit)
            {
                _homeHit = Ui.Rect("HomeHit", rt);
                _homeHit.anchorMin = _homeHit.anchorMax = _homeHit.pivot = rt.pivot;
                _homeHit.sizeDelta = rt.rect.size;
                _homeHit.SetAsFirstSibling();
                var image = _homeHit.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.color = new UnityEngine.Color(0, 0, 0, 0);
                image.canvasRenderer.cullTransparentMesh = true;
            }
            PlaceHomeHit();
        }

        /// <summary>Where the card's centre rests in the fan (world space), lifted or not.</summary>
        public UnityEngine.Vector3 RestingCenter()
        {
            if (_homeHit != null) return _homeHit.TransformPoint(_homeHit.rect.center);
            var rt = (UnityEngine.RectTransform)transform;
            return rt.TransformPoint(rt.rect.center);
        }

        /// <summary>Holds the hit area on the card's home rect, whatever the card's own lift, turn and size.</summary>
        private void PlaceHomeHit()
        {
            if (_homeHit == null) return;
            var rt = (UnityEngine.RectTransform)transform;
            float s = UnityEngine.Mathf.Max(0.01f, rt.localScale.x);
            var undo = UnityEngine.Quaternion.Inverse(rt.localRotation);
            _homeHit.localRotation = undo * _homeRotation;
            _homeHit.localScale = UnityEngine.Vector3.one / s;
            _homeHit.localPosition = undo * (UnityEngine.Vector3)(_home - rt.anchoredPosition) / s;
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e != null) Enters++;
            if (_amount <= 0 && !_dragging) Remember();
            _target = 1;
            transform.SetAsLastSibling();
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e != null) Exits++;
            _target = 0;
        }

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (Dropped == null || Layer == null) return;
            if (!_started) Remember();
            _dragging = true;
            if (_homeHit != null) _homeHit.gameObject.SetActive(false); // the card is in the hand of the pointer now
            transform.SetAsLastSibling();
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!_dragging || Layer == null) return;
            if (UnityEngine.RectTransformUtility.ScreenPointToLocalPointInRectangle(Layer, e.position, e.pressEventCamera, out var local))
            {
                var rt = (UnityEngine.RectTransform)transform;
                rt.anchoredPosition = local;
                rt.localRotation = UnityEngine.Quaternion.identity;
                rt.localScale = UnityEngine.Vector3.one * 1.15f;
            }
        }

        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            var at = ((UnityEngine.RectTransform)transform).anchoredPosition;
            if (Dropped?.Invoke(at) == true) return; // the board redraws with the card played (its footprint stays off)
            if (_homeHit != null) _homeHit.gameObject.SetActive(true);
            _amount = 1;
            _target = 0;
        }

        private void Update()
        {
            if (!_started || _dragging || UnityEngine.Mathf.Approximately(_amount, _target)) return;
            _amount = UnityEngine.Mathf.MoveTowards(_amount, _target, UnityEngine.Time.unscaledDeltaTime * 8);
            var rt = (UnityEngine.RectTransform)transform;
            // Lift along the card's own "up", so it rises out of the fan, and straighten as it rises.
            rt.anchoredPosition = _home + (UnityEngine.Vector2)(_homeRotation * new UnityEngine.Vector3(0, 46 * _amount, 0));
            rt.localRotation = UnityEngine.Quaternion.Slerp(_homeRotation, UnityEngine.Quaternion.identity, _amount);
            rt.localScale = UnityEngine.Vector3.one * (1 + 0.28f * _amount);
            PlaceHomeHit();
        }
    }
}
