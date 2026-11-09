using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A slab coming back from grading (the desk's reveal; the harness's -slabreveal): it fades in, a light sweeps across
    /// the case, the number stamps into the gauge (the label presses down, the ring flashes), then the label prints and the
    /// sticker flashes (and, for a Gem Mint, one glint runs round the case). The same slab throughout: only its label is
    /// reprinted (<see cref="SlabView.SetLabel"/>), so the glass's light runs on smoothly. Under Reduced Motion the stages
    /// change on the same timings, but nothing sweeps, presses, flashes, scales or glints. A skip jumps to the printed
    /// label with the light at rest.
    /// </summary>
    public static class SlabReveal
    {
        public enum Beat { Number, Printed }

        /// <summary>When the number stamps, when the label prints, and when it's all at rest (seconds from the start).</summary>
        public const float NumberAt = 0.7f, PrintedAt = 1.6f, RestAt = 2.5f;

        /// <summary>
        /// Plays the reveal on a sealed Full slab (from <see cref="SlabView.Create"/>). <paramref name="beat"/> hears each
        /// stage (for sounds); <paramref name="delta"/> gives each frame's time step (default: real time), so a harness can
        /// step it frame by frame.
        /// </summary>
        public static IEnumerator Play(RectTransform slab, CardCopy copy, CardInfo? info, string setCode, Func<bool> skip,
            Action<Beat>? beat = null, Func<float>? delta = null)
        {
            bool calm = FxSettings.ReducedMotion;
            var glass = slab.GetComponentInChildren<SlabGlass>();
            var group = slab.GetComponent<CanvasGroup>();
            if (group == null) group = slab.gameObject.AddComponent<CanvasGroup>();
            var label = (RectTransform)slab.Find("Label");
            var labelAt = label.anchoredPosition;
            bool numbered = false, printed = false;
            Text? number = null;
            for (float t = 0; ; t += delta?.Invoke() ?? Time.unscaledDeltaTime)
            {
                if (slab == null) yield break; // the screen closed under it
                if (skip()) t = RestAt;
                // Arrival: a fade (always) and a slight grow (not under Reduced Motion).
                float k = Mathf.Clamp01(t / 0.4f);
                group.alpha = k;
                slab.localScale = Vector3.one * (calm ? 1 : Mathf.Lerp(0.9f, 1f, 1 + 2.7f * Mathf.Pow(k - 1, 3) + 1.7f * Mathf.Pow(k - 1, 2)));
                // A light sweep across the case, from 0.25 s over 0.7 s.
                float u = (t - 0.25f) / 0.7f;
                if (glass != null)
                    glass.Override(!calm && u >= 0 && u <= 1
                        ? Vector3.Lerp(new Vector3(-0.3f, 1.25f, 1), new Vector3(1.3f, -0.25f, 1), Mathf.SmoothStep(0, 1, u))
                        : (Vector3?)null);
                // The number stamps into the gauge.
                if (!numbered && t >= NumberAt)
                {
                    numbered = true;
                    SlabView.SetLabel(slab, copy, info, setCode, LabelStage(printed: false));
                    number = slab.Find("Label/Number")?.GetComponent<Text>();
                    beat?.Invoke(Beat.Number);
                }
                float sinceNumber = t - NumberAt;
                if (numbered && !printed)
                {
                    if (number != null) number.rectTransform.localScale = Vector3.one * (calm ? 1 : Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(sinceNumber / 0.18f)));
                    label.anchoredPosition = labelAt + (calm || sinceNumber > 0.06f ? Vector2.zero : new Vector2(0, -1.5f));
                    SlabView.FlashGauge(slab, calm ? 0 : Mathf.Clamp01(1 - sinceNumber / 0.25f));
                }
                // Then the label prints, and the sticker flashes in.
                if (!printed && t >= PrintedAt)
                {
                    printed = true;
                    label.anchoredPosition = labelAt;
                    SlabView.SetLabel(slab, copy, info, setCode, LabelStage(printed: true));
                    beat?.Invoke(Beat.Printed);
                }
                float sincePrint = t - PrintedAt;
                if (printed && glass != null)
                {
                    glass.SetStickerFlash(calm ? 0 : Mathf.Clamp01(1 - sincePrint / 0.6f));
                    SlabView.ScaleSticker(slab, calm ? 1 : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01(sincePrint / 0.12f)));
                    float glint = sincePrint / 0.9f;
                    glass.SetGlint(!calm && copy.Slab >= 10 && glint >= 0 && glint <= 1 ? glint : -1);
                }
                if (t >= RestAt)
                {
                    // At rest: the printed label, the light given back.
                    group.alpha = 1;
                    slab.localScale = Vector3.one;
                    glass?.Override(null);
                    glass?.SetStickerFlash(0);
                    glass?.SetGlint(-1);
                    if (printed) SlabView.ScaleSticker(slab, 1);
                    yield break;
                }
                yield return null;
            }
        }

        private static SlabView.LabelStage LabelStage(bool printed) => printed ? SlabView.LabelStage.Printed : SlabView.LabelStage.Number;
    }
}
