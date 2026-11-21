using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Settings screen's sound block (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md, section A): Master volume, Sound effects,
    /// Music, Reactive music, Interface sounds and Mute in the background, drawn by one call so a later reorder moves one
    /// line. The volumes are sliders in 5% steps (drag, click, the &lt; &gt; buttons, Left / Right on the focused slider, the
    /// d-pad); they apply at once and never rebuild the rows (a rebuild would end a drag), and save on release, after each
    /// step, and when the screen closes. The On / Off rows rebuild the screen as the other rows do.
    /// </summary>
    internal static class SettingsSound
    {
        private const float Steps = 20; // 5% each

        private sealed class Volume
        {
            public string Group = "", Key = "", Preview = "";
            public Func<float> Get = () => 0;
            public Slider Slider = null!;
            public Text Label = null!;
        }

        private static readonly Dictionary<string, Volume> Shown = new Dictionary<string, Volume>();
        private static RectTransform? _rows;

        /// <summary>The sound rows from <paramref name="y"/> down (104 px apart); returns the y after the last row.</summary>
        public static float Build(RectTransform rows, float y, Action refresh)
        {
            _rows = rows;
            Shown.Clear();
            string OnOff(bool on) => on ? "On" : "Off";
            y = SliderRow(rows, y, "master", "masterVolume", "Master volume",
                "Everything the game plays. Drag, click, or use < > or Left / Right, in 5% steps.", () => Sounds.MasterVolume, "summon");
            y = SliderRow(rows, y, "effects", "sfxVolume", "Sound effects",
                "Summons, attacks, damage, packs and other game sounds. Buttons have their own row below.", () => Sounds.EffectsVolume, "summon");
            y = SliderRow(rows, y, "music", "musicVolume", "Music",
                "A calm theme in the menus, a driving one in duels, and a theme of their own for bosses. It dips under negations and big hits.",
                () => Sounds.MusicVolume, "");
            y = Toggle(rows, y, "Reactive music", OnOff(Sounds.ReactiveMusic),
                "Duel music follows the duel: the boss's tune with their ace, more drive at low LP, a heartbeat when an attack could end it. Off: steady. Reduced motion slows its changes.",
                () => { Sounds.ReactiveMusic = !Sounds.ReactiveMusic; refresh(); });
            y = SliderRow(rows, y, "interface", "uiVolume", "Interface sounds",
                "Button clicks, hovers and errors, on their own: Sound effects doesn't change them.", () => Sounds.UiVolume, "click");
            y = Toggle(rows, y, "Mute in the background", OnOff(Sounds.MuteInBackground),
                "On: the game goes quiet while another window is in front, and comes back when you return.",
                () => { Sounds.MuteInBackground = !Sounds.MuteInBackground; refresh(); });
            return y;
        }

        /// <summary>A row's box, name and help line, as the screen's other rows.</summary>
        private static RectTransform Box(RectTransform rows, float y, string name, string help)
        {
            var rt = Ui.Box(rows, 0, y, 1700, 94);
            Ui.Image(rt, Ui.Hex("#1A2030"));
            Typography.Strong(Ui.Text(Ui.Box(rt, 30, 8, 500, 42), name, TextRole.Heading, Color.white, TextAnchor.MiddleLeft));
            Ui.Text(Ui.Box(rt, 30, 50, 1100, 40), help, TextRole.Small, new Color(0.72f, 0.76f, 0.86f), TextAnchor.UpperLeft);
            return rt;
        }

        private static float Toggle(RectTransform rows, float y, string name, string value, string help, Action change)
        {
            var rt = Box(rows, y, name, help);
            Ui.BoxButton(rt, 1260, 19, 400, 56, value, change, TextRole.Label);
            return y + 104;
        }

        private static float SliderRow(RectTransform rows, float y, string group, string key, string name, string help, Func<float> get, string preview)
        {
            var rt = Box(rows, y, name, help);
            var v = new Volume { Group = group, Key = key, Preview = preview, Get = get };
            Ui.BoxButton(rt, 1200, 19, 48, 56, "<", () => Step(v, -1), TextRole.Label);

            // The slider: a 284 x 8 track, the gold fill, a 24 x 24 white handle; the whole 284 x 28 takes the pointer.
            var area = Ui.Box(rt, 1260, 33, 284, 28);
            Ui.Image(area, new Color(0, 0, 0, 0.001f));
            var track = Ui.Rect("Track", area);
            track.anchorMin = new Vector2(0, 0.5f);
            track.anchorMax = new Vector2(1, 0.5f);
            track.sizeDelta = new Vector2(0, 8);
            Ui.Image(track, Ui.Hex("#2A3245")).raycastTarget = false;
            var fillArea = Ui.Rect("Fill Area", area);
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(0, 8);
            var fill = Ui.Rect("Fill", fillArea);
            fill.sizeDelta = Vector2.zero;
            Ui.Image(fill, Ui.Hex("#FFC940")).raycastTarget = false;
            var handleArea = Ui.Stretched("Handle Slide Area", area, Vector2.zero, Vector2.one);
            handleArea.offsetMin = new Vector2(12, 0);
            handleArea.offsetMax = new Vector2(-12, 0);
            var handle = Ui.Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(24, -4); // 24 x 24 in the 28 px tall area
            var knob = Ui.Image(handle, Color.white);

            // Before the Slider: a press that jumps the value is already a drag (saved and heard on release, once).
            var drag = area.gameObject.AddComponent<SliderPointer>();
            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0;
            slider.maxValue = Steps;
            slider.wholeNumbers = true;
            // Left / Right change the value (Slider.OnMove does that only with no neighbour that way); Up / Down move between rows.
            slider.navigation = new Navigation { mode = Navigation.Mode.Vertical };
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.92f, 1f);
            colors.pressedColor = new Color(0.8f, 0.82f, 0.9f);
            colors.selectedColor = Ui.Hex("#FFC940"); // the keyboard's focus
            slider.colors = colors;
            slider.SetValueWithoutNotify(get() * Steps);
            v.Slider = slider;

            Ui.BoxButton(rt, 1556, 19, 48, 56, ">", () => Step(v, +1), TextRole.Label);
            v.Label = Ui.Text(Ui.Box(rt, 1612, 19, 80, 56), Percent(get()), TextRole.Label, Color.white, TextAnchor.MiddleRight);

            slider.onValueChanged.AddListener(value =>
            {
                Sounds.SetVolume(v.Key, value / Steps, save: false);
                v.Label.text = Percent(value / Steps);
                if (!drag.Down) Commit(v); // a key, d-pad or < > step: saved and heard at once
            });
            drag.Released = () => Commit(v);
            drag.Closed = () => PlayerPrefs.Save();
            Shown[group] = v;
            return y + 104;
        }

        private static string Percent(float volume) => volume <= 0.0001f ? "Off" : $"{Mathf.RoundToInt(volume * 100)}%";

        /// <summary>As the &lt; &gt; buttons: 5% down or up, stopping at 0% and 100%.</summary>
        private static void Step(Volume v, int by)
        {
            if (v.Slider == null) return;
            v.Slider.value = Mathf.Clamp(Mathf.Round(v.Slider.value) + by, 0, Steps);
        }

        /// <summary>Saved, and the preview played (Music has none: it's playing).</summary>
        private static void Commit(Volume v)
        {
            PlayerPrefs.Save();
            if (v.Preview.Length > 0) Sounds.Play(v.Preview);
        }

        /// <summary>For checks: a volume row's value as shown ("80%", "Off"); group: master, effects, music, interface. Null with no such row.</summary>
        public static string? DebugSliderText(string group) =>
            Shown.TryGetValue(group, out var v) && v.Label != null ? v.Label.text : null;

        /// <summary>For checks: steps a volume row as its &lt; &gt; buttons do (negative: down).</summary>
        public static void DebugStep(string group, int steps)
        {
            if (!Shown.TryGetValue(group, out var v)) return;
            for (int i = 0; i < Math.Abs(steps); i++) Step(v, Math.Sign(steps));
        }

        /// <summary>For checks: a volume row's slider (to focus it).</summary>
        public static Slider? DebugSlider(string group) => Shown.TryGetValue(group, out var v) ? v.Slider : null;

        /// <summary>For checks: a row's &lt; or &gt; button.</summary>
        public static Button? DebugStepButton(string group, bool up)
        {
            if (!Shown.TryGetValue(group, out var v) || v.Slider == null) return null;
            var row = v.Slider.transform.parent;
            foreach (var b in row.GetComponentsInChildren<Button>())
                if (b.GetComponentInChildren<Text>()?.text == (up ? ">" : "<")) return b;
            return null;
        }

        /// <summary>For checks: the first button of the Settings row with this name (an On / Off row's), or null.</summary>
        public static Button? DebugRowButton(string name)
        {
            if (_rows == null) return null;
            foreach (Transform row in _rows)
            {
                var texts = row.GetComponentsInChildren<Text>();
                if (texts.Length >= 2 && texts[0].text == name) return row.GetComponentInChildren<Button>();
            }
            return null;
        }

        /// <summary>For checks: the names of the Settings rows in order (each row's first text), the block's and the others'.</summary>
        public static List<string> DebugRowNames()
        {
            var names = new List<string>();
            if (_rows == null) return names;
            foreach (Transform row in _rows)
            {
                var texts = row.GetComponentsInChildren<Text>();
                if (texts.Length >= 2) names.Add(texts[0].text);
            }
            return names;
        }

        /// <summary>The slider's pointer: held (a drag: applied at once, saved on release) and the screen closing (saved).</summary>
        private sealed class SliderPointer : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public bool Down;
            public Action? Released, Closed;

            public void OnPointerDown(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) Down = true;
            }

            public void OnPointerUp(PointerEventData e)
            {
                if (!Down) return;
                Down = false;
                Released?.Invoke();
            }

            private void OnDisable()
            {
                Down = false;
                Closed?.Invoke();
            }
        }
    }
}
