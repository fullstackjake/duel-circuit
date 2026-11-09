using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Plays story scenes: a dialogue box over the scene's backdrop (its place: <see cref="StoryBackdrops"/>, crossfading
    /// when the next scene plays somewhere else; Reduced motion swaps it at once), the speaker's name, a card from their
    /// deck as a portrait, and the text typing in. Click, Space or Enter shows the whole line, then the next one; Skip
    /// ends the scenes. Each scene is marked seen when it starts. The box and name plate take the place's theme.
    /// </summary>
    public sealed class StoryScreen : MonoBehaviour
    {
        private const float CharsPerSecond = 70;
        /// <summary>How long one backdrop takes to crossfade into the next.</summary>
        public const float FadeSeconds = 0.6f;
        // #141A28, #2A3448, #2F5A35 (as literals: a MonoBehaviour's static fields stay off Unity's APIs).
        private static readonly Color BoxMain = new Color32(0x14, 0x1A, 0x28, 0xFF), PlateMain = new Color32(0x2A, 0x34, 0x48, 0xFF),
            PlatePartner = new Color32(0x2F, 0x5A, 0x35, 0xFF);

        private RectTransform _root = null!, _portrait = null!, _figure = null!;
        private Text _name = null!, _text = null!, _hint = null!;
        private Image _plate = null!, _box = null!, _skip = null!;
        /// <summary>The backdrop shown, and the next one fading in over it while a crossfade runs.</summary>
        private RawImage _back = null!, _front = null!;
        private string _place = "";
        private float _fade = -1;
        private Color _plateColor = PlateMain;
        private ICardDataProvider _cards = null!;
        private DeckLibrary _library = null!;
        private Dictionary<string, Personality> _cast = new Dictionary<string, Personality>();

        private readonly List<(StoryScene scene, StoryLine line)> _queue = new List<(StoryScene, StoryLine)>();
        private int _index;
        private string _full = "";
        private float _shown;
        private Profile? _profile;
        private Personality? _partner;
        private Action? _done;
        private StoryScene? _currentScene;
        /// <summary>The card unlocks of the result being told (for {card} and {set}); empty for none.</summary>
        private IReadOnlyList<Grant> _grants = Array.Empty<Grant>();
        /// <summary>The scenes' other placeholders (an alley result's {table}, {handle}, {card}, {grade}), for this play only.</summary>
        private IReadOnlyDictionary<string, string>? _fill;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>The deck a partner brings, by the name players know ({partnerdeck} in story lines).</summary>
        public Func<Ygo.Engine.AI.Personality, string>? PartnerDeckName;
        /// <summary>
        /// Where a scene plays (<see cref="StoryPlaces.Of"/> with the game's alley and Library data, so their tables' and cups'
        /// scenes find their places); unset, the data-free default.
        /// </summary>
        public Func<StoryScene, string>? PlaceOf;
        /// <summary>For automated checks: lines shown so far.</summary>
        public int LinesShown { get; private set; }

        public void Init(ICardDataProvider cards, DeckLibrary library, IEnumerable<Personality> roster)
        {
            _cards = cards;
            _library = library;
            _cast = roster.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            _root = Ui.Overlay(transform, "Story", 40);
            var dim = Ui.Image(Ui.Stretched("Dim", _root, Vector2.zero, Vector2.one), Ui.Hex("#0B0F17")); // opaque: a scene never shows an unrelated board behind it
            Ui.Clickable((RectTransform)dim.transform, Advance);
            // The scene's backdrop over the dim, and the next one's layer for the crossfade; clicks go through to the dim.
            _back = Backdrop("Backdrop");
            _front = Backdrop("Backdrop (next)");
            _front.gameObject.SetActive(false);
            // Paint every place's backdrop on a worker thread now, so no click waits on one (Update uploads them).
            StoryBackdrops.Warm();

            // A character's portrait stands on the dialogue box, at the left.
            _figure = Ui.Box(_root, 140, 250, 460, 390);
            var box = Ui.Box(_root, 160, 640, 1600, 340);
            _box = Ui.Image(box, BoxMain);
            Ui.Image(Ui.Box(box, 0, 0, 1600, 4), Ui.Hex("#C8901E"));
            Ui.Clickable(box, Advance);
            _portrait = Ui.Box(_root, 190, 560, 220, 321);
            // The name plate lines up with the text column under it.
            _plate = Ui.Image(Ui.Box(box, 280, -26, 560, 52), Ui.Hex("#2A3448"));
            _name = Ui.Text(Ui.Box(box, 300, -26, 540, 52), "", 24, Color.white, TextAnchor.MiddleLeft);
            _name.fontStyle = FontStyle.Bold;
            _text = Ui.Text(Ui.Box(box, 280, 50, 1270, 230), "", 27, new Color(0.93f, 0.94f, 0.97f), TextAnchor.UpperLeft);
            _text.supportRichText = true;
            _hint = Ui.Text(Ui.Box(box, 280, 290, 900, 34), "", 18, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft);
            _skip = (Image)Ui.BoxButton(box, 1360, 280, 200, 44, "Skip", Finish, 18).targetGraphic;
            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Plays <paramref name="scenes"/> in order, then calls <paramref name="done"/>. <paramref name="grants"/>: the card
        /// unlocks the scenes are about (a gift scene's {card}, a set's {set}); <paramref name="fill"/>: the result's other
        /// placeholders (<see cref="AlleyResult.Fill"/>), kept for these scenes only.
        /// </summary>
        public void Play(IReadOnlyList<StoryScene> scenes, Profile profile, Personality? partner, Action done, IReadOnlyList<Grant>? grants = null,
            IReadOnlyDictionary<string, string>? fill = null)
        {
            _grants = grants ?? Array.Empty<Grant>();
            _fill = fill?.ToDictionary(p => p.Key, p => p.Value);
            _queue.Clear();
            foreach (var scene in scenes)
                // A {partnerline} line is skipped for a partner without a "partner:" line of their own.
                foreach (var line in scene.Lines.Where(l => l.ShownIn(profile.EraLimited) && Story.Shows(l, partner))) _queue.Add((scene, line));
            _profile = profile;
            _partner = partner;
            _done = done;
            _index = -1;
            _currentScene = null;
            if (_queue.Count == 0) { done(); return; }
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Next();
        }

        private void Next()
        {
            _index++;
            if (_index >= _queue.Count) { Finish(); return; }
            var (scene, line) = _queue[_index];
            if (scene != _currentScene)
            {
                // The first scene's place shows at once; a later one crossfades in if it plays somewhere else.
                ShowPlace(PlaceOf?.Invoke(scene) ?? StoryPlaces.Of(scene), fade: _currentScene != null);
                _currentScene = scene;
                Story.MarkSeen(_profile!, scene, _partner?.Id ?? "");
            }
            LinesShown++;
            // "opponent" is the CPU you just played (No Limits' nl-restored: the seated pilot), named by the fill's "opponent" key
            // (DuelController's alley results); without one, the token stands as written.
            var who = line.Speaker == "partner" ? _partner
                : line.Speaker == OpponentSpeaker ? (_fill != null && _fill.TryGetValue(OpponentSpeaker, out var oid) && _cast.TryGetValue(oid, out var op) ? op : null)
                : line.Speaker.Length > 0 && _cast.TryGetValue(line.Speaker, out var p) ? p : null;
            bool narration = line.Speaker.Length == 0;
            var grant = CardUnlocks.For(scene.Trigger, _grants);
            _full = Story.Fill(line.Text, _partner, _profile!, _partner != null ? PartnerDeckName?.Invoke(_partner) : null, grant?.CardName, grant?.SetName, _fill);
            if (narration) _full = $"<i>{_full}</i>";
            _shown = 0;
            _plate.gameObject.SetActive(!narration);
            _name.text = narration ? "" : who?.DisplayName ?? (line.Speaker == OpponentSpeaker ? "Your opponent" : line.Speaker);
            _plate.color = line.Speaker == "partner" ? PlatePartner : _plateColor;
            ShowPortrait(narration ? null : who, hooded: line.Speaker == OpponentSpeaker);
            _hint.text = _index + 1 < _queue.Count ? "Click or press Space to continue" : "Click or press Space to finish";
            Sounds.Play("flip", 0.5f);
            Draw();
        }

        private RawImage Backdrop(string name)
        {
            var image = Ui.Stretched(name, _root, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Shows a place's backdrop: at once (the first scene, or under Reduced motion), else fading in over the one shown. The
        /// dialogue box and name plate take its theme (the Back Alley's dark iron in the alley's places).
        /// </summary>
        private void ShowPlace(string place, bool fade)
        {
            if (place == _place && _back.texture != null) return;
            var texture = StoryBackdrops.Get(place);
            var tint = StoryBackdrops.IsArt(place) ? StoryBackdrops.ArtTint : Color.white;
            bool alley = StoryPlaces.IsAlley(place);
            _box.color = alley ? UiTheme.BackAlley.PanelDeep : BoxMain;
            _plateColor = alley ? UiTheme.BackAlley.Button : PlateMain;
            _skip.color = (alley ? UiTheme.BackAlley : UiTheme.MainStreet).Button;
            _place = place;
            if (!fade || FxSettings.ReducedMotion || _back.texture == null)
            {
                _back.texture = texture;
                _back.color = tint;
                _front.gameObject.SetActive(false);
                _fade = -1;
                return;
            }
            // A fade still running settles first: its backdrop becomes the one shown.
            if (_fade >= 0) { _back.texture = _front.texture; _back.color = _front.color.WithAlpha(1); }
            _front.texture = texture;
            _front.color = tint.WithAlpha(0);
            _front.gameObject.SetActive(true);
            _fade = 0;
            Crossfades++;
        }

        /// <summary>For checks: the place shown (the one fading in during a crossfade), its backdrop, and whether a crossfade is running.</summary>
        public string DebugPlace => _place;
        public Texture? DebugBackdrop => _fade >= 0 ? _front.texture : _back.texture;
        public bool DebugFading => _fade >= 0;
        /// <summary>For checks: the incoming backdrop's opacity during a crossfade (0 to 1; 1 when none runs).</summary>
        public float DebugFadeAlpha => _fade >= 0 ? _front.color.a : 1;
        /// <summary>For checks: the backdrop fully shown now (the old one until a crossfade ends), and the box's color.</summary>
        public Texture? DebugSettledBackdrop => _back.texture;
        public Color DebugBoxColor => _box.color;
        /// <summary>For checks: crossfades started so far.</summary>
        public int Crossfades { get; private set; }

        /// <summary>The "opponent" speaker token (the CPU you just played), as the voice pools use it.</summary>
        public const string OpponentSpeaker = "opponent";

        /// <summary>The speaker's name as shown (for checks).</summary>
        public string DebugSpeaker => _name.text;

        /// <summary>
        /// A card from the speaker's deck stands in for a portrait (their best monster); <paramref name="hooded"/> (the "opponent"
        /// token, a No Limits pilot without art) stands the alley's hooded silhouette in instead.
        /// </summary>
        private void ShowPortrait(Personality? who, bool hooded = false)
        {
            for (int i = _portrait.childCount - 1; i >= 0; i--) Destroy(_portrait.GetChild(i).gameObject);
            for (int i = _figure.childCount - 1; i >= 0; i--) Destroy(_figure.GetChild(i).gameObject);
            if (who == null && !hooded) return;
            // Their portrait if they have one; otherwise a card from their deck (the opponent token: the hood).
            if (who != null && Portraits.Place(_figure, who.Id)) return;
            if (hooded)
            {
                var hood = Ui.Box(_figure, 70, 0, 320, 390);
                hood.gameObject.AddComponent<RectMask2D>();
                AlleyMaterials.Silhouette(hood, UiTheme.BackAlley.TextDim);
                return;
            }
            if (who == null) return;
            var deckName = who.Decks.FirstOrDefault(_library.Contains);
            if (deckName == null) return;
            var deck = _library.Load(deckName);
            var code = CampaignScenes.ShowcaseOf(deck, _cards).FirstOrDefault();
            var art = code == 0 ? null : CardView.Art?.Get(code);
            if (art == null) return;
            var frame = Ui.Stretched("Frame", _portrait, Vector2.zero, Vector2.one);
            Ui.Image(frame, Ui.Hex("#C8901E"));
            var image = Ui.Stretched("Art", frame, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.985f)).gameObject.AddComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
            if (_cards.Get(code) is CardInfo info) CardFoil.Apply(image, info, 1f);
            _portrait.localRotation = Quaternion.Euler(0, 0, -4);
        }

        private void Draw()
        {
            // Type the text in, keeping rich-text tags whole.
            int n = Mathf.Min(_full.Length, Mathf.FloorToInt(_shown));
            var visible = _full.Substring(0, n);
            if (visible.StartsWith("<i>") && !visible.EndsWith("</i>")) visible += "</i>";
            _text.text = visible.Replace("<i></i>", "");
        }

        private void Advance()
        {
            if (!IsOpen) return;
            if (_shown < _full.Length) { _shown = _full.Length; Draw(); return; }
            Sounds.Play("click", 0.5f);
            Next();
        }

        private void Finish()
        {
            // Skipping still counts the rest of the scenes as seen.
            for (int i = Math.Max(0, _index); i < _queue.Count; i++) Story.MarkSeen(_profile!, _queue[i].scene, _partner?.Id ?? "");
            // A crossfade cut short settles on the place it was going to.
            if (_fade >= 0)
            {
                _back.texture = _front.texture;
                _back.color = _front.color.WithAlpha(1);
                _front.gameObject.SetActive(false);
                _fade = -1;
            }
            _root.gameObject.SetActive(false);
            var done = _done;
            _done = null;
            done?.Invoke();
        }

        /// <summary>
        /// The longest step the fade and the typing take in one frame: a long frame (a backdrop painted on the spot, a hitch)
        /// slows them for that frame instead of jumping them.
        /// </summary>
        private const float MaxStep = 1f / 30;

        private void Update()
        {
            StoryBackdrops.Pump();
            if (!IsOpen) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            if (_fade >= 0)
            {
                _fade += dt / FadeSeconds;
                var c = _front.color;
                _front.color = c.WithAlpha(Mathf.SmoothStep(0, 1, Mathf.Clamp01(_fade)));
                if (_fade >= 1)
                {
                    _back.texture = _front.texture;
                    _back.color = c.WithAlpha(1);
                    _front.gameObject.SetActive(false);
                    _fade = -1;
                }
            }
            if (_shown < _full.Length)
            {
                _shown += dt * CharsPerSecond;
                Draw();
            }
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Advance();
#endif
        }

        /// <summary>For automated play: a click (finishes the line being typed, else the next line).</summary>
        public void DebugAdvance() => Advance();

        /// <summary>For automated checks: shows every remaining line at once and finishes.</summary>
        public void DebugFinish() => Finish();
        /// <summary>For automated checks: the line on screen.</summary>
        public string DebugText => _name.text + ": " + _full;
    }
}
