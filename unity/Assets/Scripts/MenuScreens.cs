using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>The game's name, version, and the fan-project notice shown on the title and menu.</summary>
    public static class GameInfo
    {
        public const string Title = "DUEL CIRCUIT";
        public const string Subtitle = "a solo dueling campaign";
        public static string Version => $"v{Application.version}";
        /// <summary>The one-line notice under the title and the menus (AGPL 5(d): copyright, no warranty, where the license is).</summary>
        public const string Notice =
            "Copyright (C) 2026 Jake Joseph and the Duel Circuit authors. Free software under the GNU AGPL v3 or later, with NO WARRANTY. "
            + "An unofficial fan game, not made or approved by Konami. Licenses, credits and source code: About, in the main menu.";
    }

    /// <summary>
    /// Title screen: drifting card pictures (some foiled), the logo, and a glowing "press any button". After a while
    /// idle it fades into attract mode, letting a bot duel play behind the logo, until any button brings you to the menu.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        public const float AttractAfter = 45f;

        private RectTransform _root = null!, _drift = null!, _logo = null!;
        private Image _shade = null!;
        private Text _press = null!;
        private RawImage _logoGlow = null!, _pressGlow = null!;
        private CanvasGroup _driftGroup = null!;
        private readonly List<RectTransform> _cards = new List<RectTransform>();
        private float _shown, _attractBlend;
        private ICardDataProvider _data = null!;

        /// <summary>Any button pressed: go to the main menu.</summary>
        public event Action? Continue;
        /// <summary>Attract mode turned on (true) or off (false): a duel should play behind the title.</summary>
        public event Action<bool>? Attract;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool InAttract { get; private set; }

        public void Init(ICardDataProvider data)
        {
            _data = data;
            Build();
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _shown = Time.unscaledTime;
            SetAttract(false);
            _attractBlend = 0;
        }

        public void Close()
        {
            SetAttract(false);
            _root.gameObject.SetActive(false);
        }

        private void Build()
        {
            _root = Ui.Overlay(transform, "Title", 40);
            _shade = Ui.Image(_root, Ui.Hex("#07090E"));

            // Card pictures drifting slowly up in tilted columns.
            _drift = Ui.Stretched("Drift", _root, Vector2.zero, Vector2.one);
            _driftGroup = _drift.gameObject.AddComponent<CanvasGroup>();
            _driftGroup.blocksRaycasts = false;
            var art = CardView.Art;
            var installed = art?.Installed ?? (IReadOnlyList<uint>)Array.Empty<uint>();
            var rng = new System.Random(Environment.TickCount);
            var tilt = Ui.Placed("Tilt", _drift, Vector2.zero, new Vector2(2600, 2000));
            tilt.localRotation = Quaternion.Euler(0, 0, 12);
            var rarities = new[] { Rarity.UltraRare, Rarity.SecretRare, Rarity.StarlightRare, Rarity.SuperRare, Rarity.PrismaticSecretRare, Rarity.GhostRare };
            for (int col = 0; col < 9; col++)
                for (int row = 0; row < 6; row++)
                {
                    var rt = Ui.Placed("Card", tilt, new Vector2((col - 4) * 270, (row - 2.5f) * 380 + (col % 2) * 190), new Vector2(236, 344));
                    var image = rt.gameObject.AddComponent<RawImage>();
                    image.raycastTarget = false;
                    if (installed.Count > 0)
                    {
                        var code = installed[rng.Next(installed.Count)];
                        image.texture = art!.Get(code);
                        var info = _data.Get(code);
                        // About a third in foil; the rest still go through Apply, for their rounded corners.
                        bool foiled = rng.NextDouble() < 0.35;
                        CardFoil.Apply(image, info, foiled ? rarities[rng.Next(rarities.Length)] : Rarity.Common, 0.8f);
                    }
                    else image.color = Ui.Hex("#2A2016");
                    _cards.Add(rt);
                }
            Ui.Image(Ui.Stretched("Darken", _drift, Vector2.zero, Vector2.one), new Color(0.02f, 0.03f, 0.06f, 0.72f)).raycastTarget = false;

            _logo = Ui.Placed("Logo", _root, new Vector2(0, 110), new Vector2(1600, 300));
            float subtitleY = -80;
            if (GameLogo.Texture is Texture2D logoArt)
            {
                // The logo art (art/logo/logo.png): its red and blue halves get a matching glow behind them.
                _logoGlow = Glow(_logo, new Vector2(0, 60), new Vector2(1500, 700), new Color(0.75f, 0.45f, 0.95f, 0.30f));
                const float height = 520;
                var logoImage = Ui.Placed("LogoArt", _logo, new Vector2(0, 60), new Vector2(GameLogo.WidthFor(height), height)).gameObject.AddComponent<RawImage>();
                logoImage.texture = logoArt;
                logoImage.raycastTarget = false;
                subtitleY = -230;
            }
            else
            {
                _logoGlow = Glow(_logo, Vector2.zero, new Vector2(1500, 520), new Color(1f, 0.75f, 0.3f, 0.35f));
                var title = Ui.Text(Ui.Placed("Title", _logo, new Vector2(0, 30), new Vector2(1600, 170)), GameInfo.Title, 140, Color.white);
                title.fontStyle = FontStyle.Bold;
                title.horizontalOverflow = HorizontalWrapMode.Overflow;
                var outline = title.gameObject.AddComponent<Outline>();
                outline.effectColor = Ui.Hex("#C8901E");
                outline.effectDistance = new Vector2(4, -4);
                title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -10);
            }
            Ui.Text(Ui.Placed("Subtitle", _logo, new Vector2(0, subtitleY), new Vector2(1200, 50)), GameInfo.Subtitle.ToUpperInvariant(), 30,
                new Color(0.85f, 0.88f, 0.95f)).fontStyle = FontStyle.Italic;

            _pressGlow = Glow(_root, new Vector2(0, -250), new Vector2(900, 200), new Color(0.4f, 0.65f, 1f, 0.3f));
            _press = Ui.Text(Ui.Placed("Press", _root, new Vector2(0, -250), new Vector2(1200, 70)), "CLICK OR PRESS ANY KEY", 40, Color.white);
            _press.fontStyle = FontStyle.Bold;
            _press.gameObject.AddComponent<Outline>().effectColor = new Color(0.2f, 0.4f, 0.9f, 0.8f);

            // The attract duel plays behind: the notice gets a strip of its own so it doesn't run over the hand.
            Ui.Image(Ui.Box(_root, 0, 1028, 1920, 52), new Color(0, 0, 0, 0.75f)).raycastTarget = false;
            Ui.Text(Ui.Box(_root, 30, 1040, 1860, 30), $"{GameInfo.Version}   ·   {GameInfo.Notice}", 14, new Color(1, 1, 1, 0.6f));
        }

        private static RawImage Glow(Transform parent, Vector2 at, Vector2 size, Color color)
        {
            var glow = Ui.Placed("Glow", parent, at, size).gameObject.AddComponent<RawImage>();
            glow.texture = BoardFx.Textures.Glow;
            glow.color = color;
            glow.raycastTarget = false;
            return glow;
        }

        private void Update()
        {
            if (!IsOpen) return;
            var t = Time.unscaledTime;
            // The cards drift up and wrap around.
            foreach (var card in _cards)
            {
                var p = card.anchoredPosition;
                p.y += Time.unscaledDeltaTime * 22f;
                if (p.y > 1150) p.y -= 6 * 380;
                card.anchoredPosition = p;
            }
            var pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
            _press.color = new Color(1, 1, 1, Mathf.Lerp(0.35f, 1f, pulse));
            _pressGlow.color = new Color(0.4f, 0.65f, 1f, Mathf.Lerp(0.08f, 0.38f, pulse));
            _logoGlow.color = new Color(1f, 0.75f, 0.3f, 0.25f + 0.1f * Mathf.Sin(t * 1.3f));
            _logo.localScale = Vector3.one * (1 + 0.012f * Mathf.Sin(t * 1.3f));

            if (!InAttract && t - _shown > AttractAfter) SetAttract(true);
            // Attract mode: the background fades so the duel behind shows through (under a scrim, so it reads as a
            // backdrop to the logo, prompt and footer); the logo moves up out of the way.
            _attractBlend = Mathf.MoveTowards(_attractBlend, InAttract ? 1 : 0, Time.unscaledDeltaTime * 0.8f);
            _driftGroup.alpha = 1 - _attractBlend;
            _shade.color = new Color(0.03f, 0.04f, 0.06f, Mathf.Lerp(1f, 0.55f, _attractBlend));
            _logo.anchoredPosition = new Vector2(0, Mathf.Lerp(110, 330, _attractBlend));
            _logo.localScale *= Mathf.Lerp(1f, 0.6f, _attractBlend);

            if (AnyButton()) Continue?.Invoke();
        }

        /// <summary>For automated checks: go into attract mode now.</summary>
        public void DebugAttract() => SetAttract(true);

        /// <summary>For automated checks: as if a button was pressed.</summary>
        public void DebugPress() => Continue?.Invoke();

        private void SetAttract(bool on)
        {
            if (InAttract == on) return;
            InAttract = on;
            Attract?.Invoke(on);
        }

        /// <summary>Any key, mouse button, or controller button this frame.</summary>
        public static bool AnyButton()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
                foreach (var control in pad.allControls)
                    if (control is UnityEngine.InputSystem.Controls.ButtonControl b && !b.synthetic && b.wasPressedThisFrame && !(control.parent is UnityEngine.InputSystem.Controls.StickControl))
                        return true;
            return false;
#else
            return Input.anyKeyDown;
#endif
        }
    }

    /// <summary>
    /// The main menu: Campaign at the top (with where you are in it), then free play, the deck editor, replays, settings,
    /// About (licenses, credits, source code) and quit. Mouse, arrow keys + Enter, or a controller (the d-pad jumps between options).
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        public enum Choice { Campaign, FreeDuel, TagDuel, DeckEditor, Replays, Settings, Quit, BackToDuel, Concede, About, ImportCards }

        private sealed class Item
        {
            public Choice Home, Choice;
            public string Label = "";
            public Image Background = null!;
            public Text Title = null!, Subtitle = null!;
            public RectTransform Box = null!;
            public bool Locked;
        }

        /// <summary>
        /// For checks: each option's box, title box and subtitle box in the 1920x1080 layout from its top-left, with its texts,
        /// and where the notice line at the bottom starts.
        /// </summary>
        public (List<(Choice choice, Rect box, Rect title, Rect subtitle, Text titleText, Text subtitleText)> items, float noticeTop) DebugGeometry()
        {
            var corners = new Vector3[4];
            Rect Of(RectTransform rt)
            {
                rt.GetWorldCorners(corners);
                var a = _root.InverseTransformPoint(corners[0]);
                var b = _root.InverseTransformPoint(corners[2]);
                var size = _root.rect.size;
                return Rect.MinMaxRect(a.x + size.x / 2, size.y / 2 - b.y, b.x + size.x / 2, size.y / 2 - a.y);
            }
            var list = _items.Select(i => (i.Choice, Of(i.Box), Of(i.Title.rectTransform), Of(i.Subtitle.rectTransform), i.Title, i.Subtitle)).ToList();
            return (list, Of(_notice).yMin);
        }
        private RectTransform _notice = null!;

        private RectTransform _root = null!, _showcase = null!;
        private readonly List<Item> _items = new List<Item>();
        private int _focus, _openedFrame = -10;
        private Text _stats = null!;
        private Func<string>[] _subtitles = Array.Empty<Func<string>>();

        public event Action<Choice>? Chosen;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>Lines under each option, filled in when the menu opens (campaign progress and so on).</summary>
        public Func<string> CampaignLine = () => "", FreeDuelLine = () => "", StatsLine = () => "";
        /// <summary>
        /// Over a duel that has to be finished first (a campaign duel): a line for the way back, or null for the usual menu.
        /// Then the top two options are Back to the duel and Concede (a loss), Settings and Quit stay, and the others are shut.
        /// </summary>
        public Func<string?> MidDuel = () => null;
        /// <summary>Whether that duel can be conceded (not a watched one).</summary>
        public Func<bool> CanConcede = () => true;
        /// <summary>Whether quitting now loses a duel (the save has it live: the next launch settles it, CampaignState.LiveDuel).</summary>
        public Func<bool> QuitLoses = () => false;

        /// <summary>For checks: each option as shown (its title, and whether it's shut).</summary>
        public List<(Choice choice, string title, bool locked)> DebugItems => _items.Select(i => (i.Choice, i.Title.text, i.Locked)).ToList();

        /// <summary>For checks: the option the keys are on.</summary>
        public Choice DebugFocused => _items[_focus].Choice;

        /// <summary>For checks: picks an option as a click would (a shut one does nothing).</summary>
        public void DebugChoose(Choice choice)
        {
            var at = _items.FindIndex(i => i.Choice == choice);
            if (at >= 0) Choose(at);
        }

        /// <summary>The card shown turning on the right, and the rarity to show it at.</summary>
        public Func<(uint code, Rarity rarity)?> Showcase = () => null;

        public void Init()
        {
            _subtitles = new Func<string>[]
            {
                () => CampaignLine(),
                () => FreeDuelLine(),
                () => "Team up with a duelist: two against two on a shared field, Tag Force style.",
                () => "Build with every card. Your campaign decks are here too, as recipes to copy.",
                () => "Watch and share recorded duels.",
                () => "Foils, animation speed, window mode, card pictures.",
                () => "Add your own card pictures: a zip or image files.",
                () => "Licenses, credits and the source code.",
                () => "",
            };
            Build();
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _openedFrame = Time.frameCount;
            var midDuel = MidDuel();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                item.Choice = item.Home;
                item.Locked = false;
                var title = item.Label;
                var subtitle = _subtitles[i]();
                if (midDuel != null)
                {
                    switch (item.Home)
                    {
                        case Choice.Campaign: item.Choice = Choice.BackToDuel; title = "Back to the duel"; subtitle = midDuel; break;
                        case Choice.FreeDuel:
                            item.Choice = Choice.Concede;
                            title = "Concede";
                            item.Locked = !CanConcede();
                            subtitle = item.Locked ? "You're watching this duel: it plays to the end." : "Give up this duel. It counts as a loss.";
                            break;
                        case Choice.TagDuel: case Choice.DeckEditor: case Choice.Replays: case Choice.ImportCards:
                            item.Locked = true;
                            subtitle = "Finish or concede your duel first.";
                            break;
                        case Choice.Quit when QuitLoses(): subtitle = "Close the game. Your duel counts as a loss."; break;
                    }
                }
                item.Title.text = title.ToUpperInvariant();
                item.Title.color = item.Locked ? new Color(1, 1, 1, 0.35f) : Color.white;
                item.Subtitle.text = subtitle;
            }
            _stats.text = StatsLine();
            ShowCard();
            Focus(0);
        }

        public void Close() => _root.gameObject.SetActive(false);

        private void Build()
        {
            _root = Ui.Overlay(transform, "Menu", 35);
            Ui.Image(_root, Ui.Hex("#0B0E15"));
            var glow = Ui.Placed("Glow", _root, new Vector2(520, 40), new Vector2(1300, 1300)).gameObject.AddComponent<RawImage>();
            glow.texture = BoardFx.Textures.Glow;
            glow.color = new Color(0.3f, 0.4f, 0.9f, 0.14f);
            glow.raycastTarget = false;

            if (GameLogo.Texture is Texture2D logoArt)
            {
                // The logo art in the header's place, left-aligned with the menu below it.
                const float height = 170;
                var logoImage = Ui.Box(_root, 90, 30, GameLogo.WidthFor(height), height).gameObject.AddComponent<RawImage>();
                logoImage.texture = logoArt;
                logoImage.raycastTarget = false;
            }
            else
            {
                var title = Ui.Text(Ui.Box(_root, 110, 60, 1000, 110), GameInfo.Title, 76, Color.white, TextAnchor.MiddleLeft);
                title.fontStyle = FontStyle.Bold;
                var outline = title.gameObject.AddComponent<Outline>();
                outline.effectColor = Ui.Hex("#C8901E");
                outline.effectDistance = new Vector2(3, -3);
            }

            var labels = new[] { ("Campaign", Choice.Campaign), ("Free Duel", Choice.FreeDuel), ("Tag Duel", Choice.TagDuel), ("Deck Editor", Choice.DeckEditor),
                ("Replays", Choice.Replays), ("Settings", Choice.Settings), ("Import Cards", Choice.ImportCards), ("About", Choice.About), ("Quit", Choice.Quit) };
            float y = 210;
            for (int i = 0; i < labels.Length; i++)
            {
                var (label, choice) = labels[i];
                bool big = i == 0;
                // Nine options: the small ones 80 apart (68 tall), so the last ends at 1006, clear of the notice at 1040.
                var h = big ? 150f : 80f;
                var rt = Ui.Box(_root, 110, y, 760, h - 12);
                var bg = Ui.Image(rt, Ui.Hex("#1A2030"));
                var text = Ui.Text(Ui.Box(rt, 36, big ? 14 : 4, 700, big ? 70 : 40), label.ToUpperInvariant(), big ? 46 : 30, Color.white, TextAnchor.MiddleLeft);
                text.fontStyle = FontStyle.Bold;
                var sub = Ui.Text(Ui.Box(rt, 38, big ? 86 : 44, 700, big ? 44 : 22), "", big ? 19 : 15, new Color(0.72f, 0.76f, 0.86f), TextAnchor.UpperLeft);
                sub.supportRichText = true;
                int index = i;
                Ui.Clickable(rt, () => Choose(index), hover: () => Focus(index));
                _items.Add(new Item { Home = choice, Choice = choice, Label = label, Background = bg, Title = text, Subtitle = sub, Box = rt });
                y += h + (big ? 18 : 0);
            }

            _showcase = Ui.Placed("Showcase", _root, new Vector2(470, 40), new Vector2(380, 554));
            _stats = Ui.Text(Ui.Placed("Stats", _root, new Vector2(470, -300), new Vector2(700, 90)), "", 17, new Color(0.8f, 0.83f, 0.9f));
            _stats.supportRichText = true;
            _notice = Ui.Box(_root, 30, 1040, 1860, 30);
            Ui.Text(_notice, $"{GameInfo.Version}   ·   {GameInfo.Notice}", 14, new Color(1, 1, 1, 0.6f));
        }

        private void ShowCard()
        {
            for (int i = _showcase.childCount - 1; i >= 0; i--) Destroy(_showcase.GetChild(i).gameObject);
            if (Showcase() is not { } pick) return;
            var art = CardView.Art?.Get(pick.code);
            if (art == null) return;
            var image = Ui.Stretched("Card", _showcase, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
            CardFoil.Apply(image, FindInfo(pick.code), pick.rarity, 1f);
            CardFoil.Tilting(image);
        }

        /// <summary>Card data for the showcase (set by the controller).</summary>
        public Func<uint, CardInfo?> FindInfo = _ => null;

        private void Update()
        {
            if (!IsOpen) return;
            // A slow turn, like a card on a stand.
            _showcase.localRotation = Quaternion.Euler(0, 18 * Mathf.Sin(Time.unscaledTime * 0.6f), 2 * Mathf.Sin(Time.unscaledTime * 0.4f));
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            // Not on the frame it opened: the Esc or Enter that closed the screen over it (About, Settings) isn't read twice.
            if (k == null || Time.frameCount == _openedFrame) return;
            if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) Focus((_focus + 1) % _items.Count);
            if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) Focus((_focus - 1 + _items.Count) % _items.Count);
            if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Choose(_focus);
            // Over a duel, Esc goes back to it.
            else if (k.escapeKey.wasPressedThisFrame && _items.Count > 0 && _items[0].Choice == Choice.BackToDuel) Choose(0);
#endif
        }

        private void Focus(int index)
        {
            _focus = index;
            for (int i = 0; i < _items.Count; i++)
                _items[i].Background.color = i == index ? Ui.Hex(_items[i].Locked ? "#242A38" : "#2F3F66") : Ui.Hex("#1A2030");
        }

        private void Choose(int index)
        {
            Focus(index);
            if (_items[index].Locked) return; // shut while a campaign duel is under way
            Chosen?.Invoke(_items[index].Choice);
        }
    }

    /// <summary>Settings: foils, default animation speed, window mode, where card pictures come from and the preferred art style.</summary>
    public sealed class SettingsScreen : MonoBehaviour
    {
        private RectTransform _root = null!, _rows = null!;
        public event Action? Closed;
        /// <summary>Speed choices (events per second) and the one in use; set by the controller.</summary>
        public float[] Speeds = Array.Empty<float>();
        public Func<int> GetSpeed = () => 0;
        public Action<int> SetSpeed = _ => { };

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        private ScrollRect _scroll = null!;

        /// <summary>For checks: scrolls the rows (1 = top, 0 = bottom).</summary>
        public void DebugScroll(float position) { Canvas.ForceUpdateCanvases(); _scroll.verticalNormalizedPosition = position; }

        /// <summary>For checks: scrolls a row into view and returns its button's (or value's) text, or null if there's no such row.</summary>
        public string? DebugShowRow(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach (Transform row in _rows)
            {
                var texts = row.GetComponentsInChildren<Text>();
                if (texts.Length < 2 || texts[0].text != name) continue;
                float room = _rows.rect.height - ((RectTransform)_scroll.viewport).rect.height;
                float top = -((RectTransform)row).anchoredPosition.y - 200;
                _scroll.verticalNormalizedPosition = room <= 0 ? 1 : Mathf.Clamp01(1 - top / room);
                return texts[texts.Length - 1].text;
            }
            return null;
        }

        public void Init()
        {
            _root = Ui.Overlay(transform, "Settings", 36);
            Ui.Image(_root, Ui.Hex("#0B0E15"));
            Ui.Text(Ui.Box(_root, 110, 60, 1000, 100), "SETTINGS", 60, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            // More settings than fit: the rows scroll.
            var viewport = Ui.Box(_root, 110, 180, 1700, 760);
            Ui.Image(viewport, new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _rows = Ui.Rect("Rows", viewport);
            _rows.anchorMin = new Vector2(0, 1);
            _rows.anchorMax = new Vector2(1, 1);
            _rows.pivot = new Vector2(0.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll = scroll;
            scroll.viewport = viewport;
            scroll.content = _rows;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            // A fade at the list's bottom edge: more rows below (the list ends with room to scroll the last one clear of it).
            var fade = Ui.Box(_root, 110, 180 + 760 - 48, 1700, 48).gameObject.AddComponent<RawImage>();
            fade.texture = FadeTexture(Ui.Hex("#0B0E15"));
            fade.raycastTarget = false;
            Ui.BoxButton(_root, 110, 960, 240, 60, "Back", Back, 22);
            Ui.Text(Ui.Box(_root, 30, 1040, 1860, 30), $"{GameInfo.Version}   ·   {GameInfo.Notice}", 14, new Color(1, 1, 1, 0.6f));
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Close() => _root.gameObject.SetActive(false);

        /// <summary>The Back button: closes the screen and returns to wherever it was opened from.</summary>
        public void Back()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>The next volume step: 0, 20, 40, 60, 80, 100%, then back to 0.</summary>
        private static float Next(float volume) => Mathf.Round(volume * 5) >= 5 ? 0 : (Mathf.Round(volume * 5) + 1) / 5f;

        private void Refresh()
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);
            float y = 0;
            void Heading(string text)
            {
                Ui.Text(Ui.Box(_rows, 6, y + 10, 1000, 34), text, 18, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                y += 48;
            }
            string OnOff(bool on) => on ? "On" : "Off";
            void Row(string name, string value, string help, Action? change)
            {
                var rt = Ui.Box(_rows, 0, y, 1700, 94);
                Ui.Image(rt, Ui.Hex("#1A2030"));
                Ui.Text(Ui.Box(rt, 30, 8, 500, 42), name, 26, Color.white, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(rt, 30, 50, 1100, 40), help, 15, new Color(0.72f, 0.76f, 0.86f), TextAnchor.UpperLeft);
                if (change != null) Ui.BoxButton(rt, 1260, 19, 400, 56, value, () => { change(); Refresh(); }, 20);
                else Ui.Text(Ui.Box(rt, 1160, 0, 500, 94), value, 18, Color.white, TextAnchor.MiddleRight);
                y += 104;
            }
            Heading("GAME");
            var foils = CardFoil.ModeLabel.Replace("Foils: ", "");
            Row("Foils", foils.Length > 0 ? char.ToUpperInvariant(foils[0]) + foils.Substring(1) : foils,
                "On: each card at your best copy (campaign) or its rarest classic printing. Showcase: rarest printing of all. Off: plain.",
                CardFoil.CycleMode);
            Row("Foil intensity", FxSettings.FoilIntensityLabel,
                "How strongly foil cards shine: Subtle, Standard or Vivid. Their names, rainbow and the flash where the light catches them all follow it.",
                () => FxSettings.FoilIntensity = (FxSettings.FoilIntensity + 1) % 3);
            Row("Card tilt", OnOff(FxSettings.CardTilt) + (FxSettings.ReducedMotion ? " (reduced motion)" : ""),
                "In the card detail and the reading view, the card tilts a little toward the pointer and its foil catches the light as it turns.",
                () => FxSettings.CardTilt = !FxSettings.CardTilt);
            Row("Card grades", CardWear.Mode == CardWear.Display.Full ? "Full" : "Label only",
                "Full: your copies show their condition (soft corners at Near Mint, scratches when Played, creases when Damaged). "
                + "Label only: cards look pristine; the condition shows in words.",
                CardWear.CycleMode);
            Row("Prized moments", OnOff(FxSettings.PrizedMoments),
                "A Gem Mint Secret Rare or better of yours sparkles and chimes (in the music's key) when it lands face-up, once a duel.",
                () => FxSettings.PrizedMoments = !FxSettings.PrizedMoments);
            var speed = Speeds.Length > 0 ? Speeds[GetSpeed()] : 0;
            Row("Duel speed", $"{speed:0.#} events / s", "How fast duels play (the +/- buttons change it during a duel). Animations are full length up to 4.",
                () => SetSpeed((GetSpeed() + 1) % Speeds.Length));
            Row("Chain prompts", DuelController.ResponseSetting.ToString(),
                "When a duel stops to ask \"Chain?\". Auto: when your opponent plays something, or one of your effects suits the moment. "
                + "On: every chance. Off: only your own cards' effects right after your plays. Hold Shift (or LT) to be asked once. Also on the board.",
                () => { DuelController.ResponseSetting = DuelController.ResponseSetting switch { Ygo.Engine.Interactive.ResponseMode.Auto => Ygo.Engine.Interactive.ResponseMode.On, Ygo.Engine.Interactive.ResponseMode.On => Ygo.Engine.Interactive.ResponseMode.Off, _ => Ygo.Engine.Interactive.ResponseMode.Auto }; });
            Heading("DUEL PRESENTATION");
            Row("Repeated animations", FxSettings.ShorterRepeats ? "Shorter" : "Full every time",
                "Summon scenes and spotlights play in full the first time in a duel and quicker after; an ace's cut-in plays once.",
                () => FxSettings.ShorterRepeats = !FxSettings.ShorterRepeats);
            Row("Reduced motion", OnOff(FxSettings.ReducedMotion),
                "Turns off screen shake, flashes, summon scenes, cut-ins and drifting particles in one go. Everything that happens still shows.",
                () => FxSettings.ReducedMotion = !FxSettings.ReducedMotion);
            Row("Summon scenes", OnOff(FxSettings.SummonScenesOn) + (FxSettings.ReducedMotion ? " (reduced motion)" : ""),
                "The Fusion vortex, Synchro rings, Xyz galaxy, Link arrows and Ritual circle before an Extra Deck or Ritual monster lands.",
                () => FxSettings.SummonScenesOn = !FxSettings.SummonScenesOn);
            Row("Ace cut-ins", OnOff(FxSettings.CutInsOn) + (FxSettings.ReducedMotion ? " (reduced motion)" : ""),
                "The full-width banner with the card's art when a Level 8+ or 3000+ ATK monster arrives.",
                () => FxSettings.CutInsOn = !FxSettings.CutInsOn);
            Row("Screen shake", OnOff(FxSettings.ShakeOn) + (FxSettings.ReducedMotion ? " (reduced motion)" : ""),
                "The board shakes on big hits and big summons.", () => FxSettings.ShakeOn = !FxSettings.ShakeOn);
            Row("Flashes", OnOff(FxSettings.FlashesOn) + (FxSettings.ReducedMotion ? " (reduced motion)" : ""),
                "Full-screen color flashes when you take damage and on the final blow.", () => FxSettings.FlashesOn = !FxSettings.FlashesOn);
            Row("Slow down for combos", OnOff(FxSettings.SlowForCombos),
                "While a headliner's combo plays, the duel runs at Normal speed even when you've set it faster, so you can follow it. Off keeps your speed.",
                () => FxSettings.SlowForCombos = !FxSettings.SlowForCombos);
            Row("Card text size", FxSettings.TextSizeLabel,
                "Larger text for card effects, the duel log, dialogs and the deck editor's reading view.",
                () => FxSettings.TextSize = (FxSettings.TextSize + 1) % 3);
            Row("Card term help", OnOff(FxSettings.CardTermHelp),
                "Card-text terms (Quick Effect, once per turn, negate the activation...) in blue in the card detail; point at one to see what it means.",
                () => FxSettings.CardTermHelp = !FxSettings.CardTermHelp);
            Row("Playable card tabs", OnOff(FxSettings.PlayableTabs),
                "A gold ▶ tab above every card you can play right now, besides the gold border, so they stand out without relying on color.",
                () => FxSettings.PlayableTabs = !FxSettings.PlayableTabs);
            Heading("SOUND AND SCREEN");
            Row("Sound effects", $"{Mathf.RoundToInt(Sounds.EffectsVolume * 100)}%", "Summons, attacks, damage, pack openings and buttons. Click to change (off, 20% ... 100%).",
                () => { Sounds.EffectsVolume = Next(Sounds.EffectsVolume); Sounds.Play("summon"); });
            Row("Music", $"{Mathf.RoundToInt(Sounds.MusicVolume * 100)}%", "A calm theme in the menus, a driving one in duels, and a theme of their own for bosses. It dips under negations and big hits.",
                () => Sounds.MusicVolume = Next(Sounds.MusicVolume));
            Row("Reactive music", OnOff(Sounds.ReactiveMusic),
                "Duel music follows the duel: the boss's tune with their ace, more drive at low LP, a heartbeat when an attack could end it. Off: steady. Reduced motion slows its changes.",
                () => Sounds.ReactiveMusic = !Sounds.ReactiveMusic);
            Row("Interface sounds", $"{Mathf.RoundToInt(Sounds.UiVolume * 100)}%", "Button clicks and hovers, apart from the duel's sounds. Click to change (off, 20% ... 100%).",
                () => { Sounds.UiVolume = Next(Sounds.UiVolume); Sounds.Play("click"); });
            Row("Window", Screen.fullScreen ? "Full screen" : "Windowed", "Switch between full screen and a window. The window can be dragged to any size; its size is kept for next time.",
                WindowSize.Toggle);
            var art = CardView.Art;
            Row("Card pictures", $"{art?.Installed.Count ?? 0} installed",
                art == null ? "No picture folders." : $"From: {string.Join("  +  ", art.Folders)}. Add <card id>.jpg files to the first.", null);
            if (art != null)
            {
                var styles = art.Styles;
                Row("Prefer style", art.Preferred.Length == 0 ? "(none)" : CardArt.Label(art.Preferred),
                    styles.Count == 0
                        ? $"Alternate pictures (extended art, manga...) go in {art.StylesRoot}/<style>/<card id>.jpg; each folder is a style."
                        : $"{string.Join(", ", styles.Select(CardArt.Label))}, from {art.StylesRoot}. For cards with no Artwork choice of their own.",
                    styles.Count == 0 ? null : art.CyclePreferred);
            }
            Heading("CONTROLLER");
            Row("Controller", "Plug and play",
                "The left stick moves the cursor and the D-pad jumps it between buttons. Click a button below, then press the controller button you want for it.", null);
            foreach (PadAction action in Enum.GetValues(typeof(PadAction)))
            {
                var a = action;
                bool waiting = ControllerBindings.Capturing == a;
                Row(ControllerBindings.Name(a), waiting ? "Press a button..." : ControllerBindings.Label(a), ControllerBindings.Help(a),
                    () => { if (ControllerBindings.Capturing == null) ControllerBindings.BeginCapture(a, Refresh); });
            }
            Row("Reset buttons", "Reset", "Back to A select, B back, X right-click, Y extra, LB/RB pages, Start pause, LT ask, RT check field and View card terms.",
                ControllerBindings.Reset);
            _rows.sizeDelta = new Vector2(0, y + 48);
        }

        /// <summary>A vertical ramp: <paramref name="color"/> at the bottom, clear at the top.</summary>
        private static Texture2D FadeTexture(Color color)
        {
            const int h = 16;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < h; i++) tex.SetPixel(0, i, new Color(color.r, color.g, color.b, 1 - i / (float)(h - 1)));
            tex.Apply();
            return tex;
        }
    }
}
