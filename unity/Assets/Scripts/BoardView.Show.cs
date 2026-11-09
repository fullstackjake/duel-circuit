using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The headliners' show on the board (docs/dev/HEADLINERS-2026-11-01.md, "The show"): the "going for it" banner across the
    /// board (the headliner's name, the combo's, and Baz's call), the combo meter at the top of the callout column, over the
    /// chain ("step 6 of 11", the steps as pips, the running total), the payoff beat (a stamp over the board and the meter
    /// lit) and the fizzle ("fizzled at step 5", Baz and Mika's calls). Drawn in the current UI theme (neon in the alley).
    /// Reduced motion drops the banner's and stamps' movement, never the banner. Callouts and the chain stack under the
    /// meter (<see cref="TopBanners"/>).
    /// </summary>
    public sealed partial class BoardView
    {
        private RectTransform? _comboMeter, _comboBanner, _comboStamp;
        private Coroutine? _bannerRun, _stampRun, _meterHide;
        private const float MeterWidth = 380, MeterHeight = 96;

        /// <summary>For checks: what the banner, the meter and the last stamp said, and how many banners have shown.</summary>
        public string ComboBannerText { get; private set; } = "";
        public string ComboMeterText { get; private set; } = "";
        public string ComboStampText { get; private set; } = "";
        public int ComboBanners { get; private set; }
        /// <summary>For checks: whether the last banner moved in (false under Reduced motion), and whether it's on screen now.</summary>
        public bool ComboBannerAnimated { get; private set; }
        public bool ComboBannerShowing => _comboBanner != null;
        public bool ComboMeterShowing => _comboMeter != null;
        /// <summary>For checks: the duel's title in the top bar ("Exhibition · Moss vs ...").</summary>
        public string DuelTitleText => _campaignTitle.text;

        /// <summary>Clears the show (a new duel, a replay).</summary>
        public void ClearCombo()
        {
            foreach (var run in new[] { _bannerRun, _stampRun, _meterHide }) if (run != null) StopCoroutine(run);
            _bannerRun = _stampRun = _meterHide = null;
            foreach (var rt in new[] { _comboMeter, _comboBanner, _comboStamp }) if (rt != null) Destroy(rt.gameObject);
            _comboMeter = _comboBanner = _comboStamp = null;
            ComboBannerText = ComboMeterText = ComboStampText = "";
            ComboBanners = 0;
        }

        /// <summary>
        /// "MOSS IS GOING FOR IT: FROG FTK": a band across the board with the route's name in the sign color and Baz's
        /// call under it, for <paramref name="seconds"/> (it sweeps in and out, or just shows under Reduced motion).
        /// </summary>
        public void ShowComboBanner(string who, string route, string call, float seconds)
        {
            var T = UiTheme.Current;
            if (_bannerRun != null) StopCoroutine(_bannerRun);
            if (_comboBanner != null) Destroy(_comboBanner.gameObject);
            var band = Ui.Placed("ComboBanner", _fxLayer, new Vector2(0, 150), new Vector2(1500, call.Length > 0 ? 210 : 160));
            _comboBanner = band;
            var group = band.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Ui.Image(band, T.Deep.WithAlpha(0.94f)).raycastTarget = false;
            // Neon rules above and below (signs and rules only, UI-THEME rule 9): the sign color in the alley, gold on main street.
            float h = band.sizeDelta.y;
            Ui.Image(Ui.Placed("Top", band, new Vector2(0, h / 2 - 2), new Vector2(1500, 4)), T.Sign).raycastTarget = false;
            Ui.Image(Ui.Placed("Bottom", band, new Vector2(0, -h / 2 + 2), new Vector2(1500, 4)), T.IsAlley ? UiTheme.NeonCyan : T.Sign).raycastTarget = false;
            var title = Ui.Text(Ui.Placed("Who", band, new Vector2(0, h / 2 - 50), new Vector2(1400, 64)), $"{who.ToUpperInvariant()} IS GOING FOR IT", 44, T.Text);
            title.fontStyle = FontStyle.BoldAndItalic;
            title.raycastTarget = false;
            T.Title(title);
            title.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            var name = Ui.Text(Ui.Placed("Route", band, new Vector2(0, h / 2 - 108), new Vector2(1400, 54)), route.ToUpperInvariant(), 40, T.Sign);
            name.fontStyle = FontStyle.Bold;
            name.raycastTarget = false;
            T.Title(name);
            if (call.Length > 0)
            {
                var said = Ui.Text(Ui.Placed("Call", band, new Vector2(0, -h / 2 + 34), new Vector2(1400, 40)), call, 22, T.Quote);
                said.fontStyle = FontStyle.Italic;
                said.raycastTarget = false;
                said.resizeTextForBestFit = true;
                said.resizeTextMinSize = 14;
                said.resizeTextMaxSize = 22;
            }
            ComboBannerText = $"{who.ToUpperInvariant()} IS GOING FOR IT: {route.ToUpperInvariant()}";
            ComboBanners++;
            ComboBannerAnimated = !FxSettings.ReducedMotion;
            _bannerRun = StartCoroutine(Run(band, group, seconds, ComboBannerAnimated, () => { _comboBanner = null; _bannerRun = null; }));
        }

        /// <summary>
        /// A stamp over the board's middle: "PAYOFF!" (the payoff beat) or "FIZZLED" (the route stopped), its line under it, the
        /// calls under that. Held <paramref name="seconds"/>.
        /// </summary>
        public void ShowComboStamp(bool payoff, string line, IReadOnlyList<string> calls, float seconds)
        {
            var T = UiTheme.Current;
            if (_stampRun != null) StopCoroutine(_stampRun);
            if (_comboStamp != null) Destroy(_comboStamp.gameObject);
            float h = 150 + calls.Count * 34;
            var stamp = Ui.Placed("ComboStamp", _fxLayer, new Vector2(0, -40), new Vector2(1100, h));
            _comboStamp = stamp;
            var group = stamp.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var accent = payoff ? T.Gold : T.Bad;
            Ui.Image(stamp, T.Deep.WithAlpha(0.9f)).raycastTarget = false;
            var edge = stamp.gameObject.AddComponent<Outline>();
            edge.effectColor = accent;
            edge.effectDistance = new Vector2(4, -4);
            var word = Ui.Text(Ui.Placed("Word", stamp, new Vector2(0, h / 2 - 52), new Vector2(1060, 84)), payoff ? "PAYOFF!" : "FIZZLED", 68, accent);
            word.fontStyle = FontStyle.BoldAndItalic;
            word.raycastTarget = false;
            T.Title(word);
            var sub = Ui.Text(Ui.Placed("Line", stamp, new Vector2(0, h / 2 - 116), new Vector2(1060, 40)), line, 24, T.Text);
            sub.raycastTarget = false;
            sub.resizeTextForBestFit = true;
            sub.resizeTextMinSize = 14;
            sub.resizeTextMaxSize = 24;
            for (int i = 0; i < calls.Count; i++)
            {
                var said = Ui.Text(Ui.Placed($"Call{i}", stamp, new Vector2(0, h / 2 - 160 - i * 34), new Vector2(1060, 32)), calls[i], 20, T.Quote);
                said.fontStyle = FontStyle.Italic;
                said.raycastTarget = false;
                said.resizeTextForBestFit = true;
                said.resizeTextMinSize = 12;
                said.resizeTextMaxSize = 20;
            }
            ComboStampText = $"{(payoff ? "PAYOFF!" : "FIZZLED")} {line}";
            _stampRun = StartCoroutine(Run(stamp, group, seconds, !FxSettings.ReducedMotion, () => { _comboStamp = null; _stampRun = null; }, stampIn: true));
        }

        /// <summary>
        /// The combo meter, drawn for the show's state: the route and the headliner, "STEP 3 OF 5" (or "PAYOFF", "FINISHED",
        /// "FIZZLED AT STEP 4"), the steps as pips, and the running total. Ended routes stay a few seconds, then go.
        /// </summary>
        public void SetComboMeter(ComboShow show)
        {
            var T = UiTheme.Current;
            if (show.State == ComboShow.Phase.None) { HideComboMeter(); return; }
            if (_meterHide != null) { StopCoroutine(_meterHide); _meterHide = null; }
            if (_comboMeter != null) Destroy(_comboMeter.gameObject);
            var meter = Ui.Placed("ComboMeter", _field, MeterSpot(), new Vector2(MeterWidth, MeterHeight));
            _comboMeter = meter;
            bool fizzled = show.State == ComboShow.Phase.Fizzled;
            bool paid = show.State is ComboShow.Phase.PaidOff or ComboShow.Phase.Finished;
            var accent = fizzled ? T.Bad : paid ? T.Good : T.Highlight;
            Ui.Image(meter, T.Deep.WithAlpha(0.94f)).raycastTarget = false;
            Ui.Image(Ui.Placed("Edge", meter, new Vector2(-MeterWidth / 2 + 3, 0), new Vector2(6, MeterHeight)), accent).raycastTarget = false;
            var head = Ui.Text(Ui.Placed("Head", meter, new Vector2(8, MeterHeight / 2 - 14), new Vector2(MeterWidth - 32, 22)),
                $"{show.Route.ToUpperInvariant()}  <color={T.TextDimHex}>· {show.Handle}</color>", 14, T.Sign, TextAnchor.MiddleLeft);
            head.supportRichText = true;
            head.fontStyle = FontStyle.Bold;
            head.raycastTarget = false;
            string state = fizzled ? $"FIZZLED AT STEP {show.FizzledAt}"
                : show.State == ComboShow.Phase.Finished ? $"FINISHED · STEP {show.Step} OF {show.Steps}"
                : show.State == ComboShow.Phase.PaidOff ? $"PAYOFF · STEP {show.Step} OF {show.Steps}"
                : $"STEP {Mathf.Max(1, show.Step)} OF {show.Steps}";
            var big = Ui.Text(Ui.Placed("State", meter, new Vector2(8, 6), new Vector2(MeterWidth - 32, 34)), state, 24, fizzled ? T.Bad : T.Text, TextAnchor.MiddleLeft);
            big.fontStyle = FontStyle.Bold;
            big.raycastTarget = false;
            big.resizeTextForBestFit = true;
            big.resizeTextMinSize = 14;
            big.resizeTextMaxSize = 24;
            // The steps as pips: played, the one it stopped at (fizzled), the rest.
            int steps = Mathf.Clamp(show.Steps, 1, 16);
            float pipW = Mathf.Min(26, (MeterWidth - 190) / steps - 4);
            for (int i = 0; i < steps; i++)
            {
                bool done = i < show.Step;
                bool stopped = fizzled && i == show.FizzledAt - 1;
                var pip = Ui.Placed($"Pip{i}", meter, new Vector2(-MeterWidth / 2 + 22 + pipW / 2 + i * (pipW + 4), -MeterHeight / 2 + 18), new Vector2(pipW, 10));
                // A fizzle marks only where it stopped: the steps before it stay plain.
                Ui.Image(pip, stopped ? T.Bad : done ? (fizzled ? T.TextDim : accent) : T.Inset).raycastTarget = false;
            }
            var total = Ui.Text(Ui.Placed("Total", meter, new Vector2(MeterWidth / 2 - 92, -MeterHeight / 2 + 18), new Vector2(170, 24)), show.TotalText, 16, T.Gold, TextAnchor.MiddleRight);
            total.fontStyle = FontStyle.Bold;
            total.raycastTarget = false;
            ComboMeterText = $"{state.ToLowerInvariant()} · {show.TotalText}";
            if (show.State is ComboShow.Phase.Fizzled or ComboShow.Phase.Finished) _meterHide = StartCoroutine(HideMeterLater(6f));
        }

        public void HideComboMeter()
        {
            if (_meterHide != null) { StopCoroutine(_meterHide); _meterHide = null; }
            if (_comboMeter != null) Destroy(_comboMeter.gameObject);
            _comboMeter = null;
        }

        private IEnumerator HideMeterLater(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _meterHide = null;
            HideComboMeter();
        }

        /// <summary>Where the meter sits: the callout column's top, as the lesson banner does (the column stacks under it).</summary>
        private Vector2 MeterSpot() =>
            new Vector2(_field.rect.width / 2 - ColumnMargin - MeterWidth / 2, _field.rect.height / 2 - 8 - MeterHeight / 2);

        /// <summary>Keeps the meter at its spot when the layout changes (the log drawer).</summary>
        private void PlaceComboMeter() { if (_comboMeter != null) _comboMeter.anchoredPosition = MeterSpot(); }

        /// <summary>
        /// Shows <paramref name="rt"/> for <paramref name="seconds"/>: moving in and out when <paramref name="moving"/> (a sweep
        /// from the left for the banner, a scale-down "thump" for a stamp), else simply there and gone.
        /// </summary>
        private IEnumerator Run(RectTransform rt, CanvasGroup group, float seconds, bool moving, System.Action done, bool stampIn = false)
        {
            var home = rt.anchoredPosition;
            float start = Time.unscaledTime, inTime = moving ? 0.25f : 0, outTime = moving ? 0.3f : 0;
            while (rt != null)
            {
                float t = Time.unscaledTime - start;
                if (t >= seconds) break;
                if (moving)
                {
                    float kIn = Mathf.Clamp01(t / inTime), kOut = Mathf.Clamp01((t - (seconds - outTime)) / outTime);
                    float ease = 1 - Mathf.Pow(1 - kIn, 3);
                    group.alpha = Mathf.Min(kIn * 1.5f, 1 - kOut);
                    if (stampIn) rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, ease);
                    else rt.anchoredPosition = home + new Vector2(Mathf.Lerp(-1600, 0, ease) + 1600 * kOut * kOut, 0);
                }
                else group.alpha = 1;
                yield return null;
            }
            if (rt != null) Destroy(rt.gameObject);
            done();
        }
    }
}
