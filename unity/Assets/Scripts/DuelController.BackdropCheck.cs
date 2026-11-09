using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The theme check's story backdrops (<see cref="StoryBackdrops"/>): a scene from every place, shot on its first spoken
    /// line ("theme-story-&lt;place&gt;.png"), each backdrop its own and dim where the box, the name plate and the portraits
    /// stand (the worker thread having painted them before the first story); the Every Era epilogue's two scenes
    /// crossfading from the back room to a cold corkboard (its backdrop forgotten first, so it's painted on the click) in
    /// about the fade's time, no jump (shot before, during and after); and the same two under Reduced motion, swapped at once. Played on a throwaway profile (nothing is marked
    /// seen in the save).
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The brightest a backdrop may be (mean luma) overall, behind the portraits, and in the strip over the box and plate.</summary>
        private const float BackdropMeanMax = 0.2f, BackdropFigureMax = 0.14f, BackdropPlateMax = 0.2f;

        private IEnumerator StoryBackdropShots(Func<string, IEnumerator> shot, Action<string, bool> report)
        {
            string PlaceOf(StoryScene s) => StoryPlaces.Of(s, _campaign.AlleyData, _campaign.Library);
            var reader = new Profile();
            var reducedBefore = FxSettings.ReducedMotionOverride;
            FxSettings.ReducedMotionOverride = false;
            // The worker paints every place at the story screen's Init; by now (the theme shots took a while) all are done.
            for (float until = Time.realtimeSinceStartup + 10; StoryBackdrops.PaintMs.Count < StoryPlaces.Known.Count && Time.realtimeSinceStartup < until;) yield return null;
            var warmed = StoryPlaces.Known.Where(k => StoryBackdrops.PaintMs.ContainsKey(k)).Select(k => $"{k} {StoryBackdrops.PaintMs[k]}").ToList();
            report($"the worker thread painted the backdrops before any story played ({warmed.Count} of {StoryPlaces.Known.Count}, ms each: {string.Join(", ", warmed)})",
                warmed.Count == StoryPlaces.Known.Count);

            // A scene from every place, on its first spoken line.
            var shown = new List<string>();
            var textures = new HashSet<Texture>();
            var readings = new List<string>();
            bool dim = true, themed = true;
            foreach (var place in StoryPlaces.Known)
            {
                var scene = _story.Scenes.FirstOrDefault(s => PlaceOf(s) == place);
                if (scene == null) { shown.Add(place + " (no scene)"); continue; }
                _storyScreen.Play(new[] { scene }, reader, null, () => { });
                yield return null;
                for (int i = 0; i < 12 && _storyScreen.IsOpen && _storyScreen.DebugSpeaker.Length == 0; i++)
                {
                    _storyScreen.DebugAdvance();
                    yield return null;
                }
                _storyScreen.DebugAdvance(); // the line typed in whole
                yield return new WaitForSecondsRealtime(0.3f);
                yield return shot($"story-{place}");
                if (_storyScreen.IsOpen && _storyScreen.DebugPlace == place && _storyScreen.DebugBackdrop is Texture t && !_storyScreen.DebugFading)
                {
                    shown.Add(place);
                    textures.Add(t);
                }
                else shown.Add($"{place} (showed {_storyScreen.DebugPlace})");
                themed &= (_storyScreen.DebugBoxColor == UiTheme.BackAlley.PanelDeep) == StoryPlaces.IsAlley(place);
                float all = StoryBackdrops.Brightness(place, 0, 0, 1920, 1080) ?? 1;
                float figure = StoryBackdrops.Brightness(place, 140, 250, 460, 390) ?? 1;
                float plate = StoryBackdrops.Brightness(place, 160, 540, 1600, 100) ?? 1;
                readings.Add($"{place} {all:F2}/{figure:F2}/{plate:F2}");
                dim &= all <= BackdropMeanMax && figure <= BackdropFigureMax && plate <= BackdropPlateMax;
                _storyScreen.DebugFinish();
                yield return null;
            }
            report($"story backdrops: a scene in each of the {StoryPlaces.Known.Count} places shows that place's own backdrop ({string.Join(", ", shown)}; {textures.Count} distinct), "
                   + "the box in its theme (the alley's dark iron in the alley's places)", shown.All(StoryPlaces.IsKnown) && textures.Count == StoryPlaces.Known.Count && themed);
            report($"story backdrops stay dim behind the box, the name plate and the portraits (mean / portraits' corner / over the box, at most "
                   + $"{BackdropMeanMax} / {BackdropFigureMax} / {BackdropPlateMax}: {string.Join(", ", readings)})", dim);

            // The Every Era epilogue: two scenes in two rooms, a crossfade between them.
            var epilogue = _story.Scenes.Where(s => s.Trigger == "cupclear:" + _campaign.Library.Final.Name).ToList();
            IEnumerator ToNextPlace(string from)
            {
                for (int i = 0; i < 60 && _storyScreen.IsOpen && _storyScreen.DebugPlace == from; i++)
                {
                    _storyScreen.DebugAdvance();
                    yield return null;
                }
            }
            int fadesBefore = _storyScreen.Crossfades;
            // The corkboard cold: painted on the click that reaches it, a long frame the fade mustn't jump over.
            StoryBackdrops.DebugForget(StoryPlaces.Corkboard);
            _storyScreen.Play(epilogue, reader, null, () => { });
            yield return new WaitForSecondsRealtime(0.3f);
            var firstPlace = _storyScreen.DebugPlace;
            var firstTexture = _storyScreen.DebugBackdrop;
            yield return shot("story-epilogue-1");
            yield return ToNextPlace(firstPlace);
            float fadeStart = Time.realtimeSinceStartup;
            var secondPlace = _storyScreen.DebugPlace;
            // One frame in: the long frame is clamped, so the corkboard has barely begun to show.
            float firstStep = _storyScreen.DebugFadeAlpha;
            bool fading = _storyScreen.DebugFading && _storyScreen.DebugSettledBackdrop == firstTexture;
            yield return new WaitForSecondsRealtime(StoryScreen.FadeSeconds * 0.45f);
            yield return shot("story-epilogue-crossfade");
            bool midway = _storyScreen.DebugFading;
            while (_storyScreen.DebugFading && Time.realtimeSinceStartup < fadeStart + 5) yield return null;
            float took = Time.realtimeSinceStartup - fadeStart;
            long coldMs = StoryBackdrops.PaintMs.TryGetValue(StoryPlaces.Corkboard, out var ms) ? ms : -1;
            yield return new WaitForSecondsRealtime(0.2f);
            _storyScreen.DebugAdvance();
            yield return null;
            yield return shot("story-epilogue-2");
            bool settled = !_storyScreen.DebugFading && _storyScreen.DebugSettledBackdrop == _storyScreen.DebugBackdrop && _storyScreen.DebugBackdrop != firstTexture;
            int fades = _storyScreen.Crossfades - fadesBefore;
            _storyScreen.DebugFinish();
            yield return null;
            report($"the Every Era epilogue's {epilogue.Count} scenes play in two places ({string.Join(" then ", epilogue.Select(PlaceOf))}): the backdrop crossfades "
                   + $"from {firstPlace} to {secondPlace} ({fades} crossfade; fading over the old one {fading}, midway {midway}, settled {settled})",
                epilogue.Count == 2 && firstPlace == StoryPlaces.BackRoom && secondPlace == StoryPlaces.Corkboard && fades == 1 && fading && midway && settled);
            report($"a cold place's crossfade doesn't jump: the corkboard painted on the click ({coldMs} ms), {firstStep:F3} shown after that frame, "
                   + $"the fade done in {took:F2} s (its time {StoryScreen.FadeSeconds} s; at most 1.5 s with the shots)",
                coldMs >= 0 && firstStep <= 0.1f && took >= StoryScreen.FadeSeconds * 0.9f && took <= 1.5f);

            // Reduced motion: the same two, the backdrop swapped at once.
            FxSettings.ReducedMotionOverride = true;
            fadesBefore = _storyScreen.Crossfades;
            _storyScreen.Play(epilogue, reader, null, () => { });
            yield return null;
            var before = _storyScreen.DebugBackdrop;
            yield return ToNextPlace(_storyScreen.DebugPlace);
            bool swapped = !_storyScreen.DebugFading && _storyScreen.DebugSettledBackdrop == _storyScreen.DebugBackdrop && _storyScreen.DebugBackdrop != before
                && _storyScreen.DebugPlace == StoryPlaces.Corkboard && _storyScreen.Crossfades == fadesBefore;
            _storyScreen.DebugFinish();
            yield return null;
            report($"under Reduced motion the epilogue's backdrop swaps at once, no crossfade ({_storyScreen.Crossfades - fadesBefore})", swapped);
            FxSettings.ReducedMotionOverride = reducedBefore;
        }
    }
}
