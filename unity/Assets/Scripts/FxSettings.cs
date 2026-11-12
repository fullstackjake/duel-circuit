using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// How much spectacle a duel shows (saved settings). Big moments play in full the first time in a duel and shorter
    /// after; each kind of effect can be switched off; Reduced motion turns off shake, flashes, summon scenes, cut-ins
    /// and drifting particles at once. Card text can be made larger, and the ▶ tabs on playable cards turned on. Only the presentation changes: the duel itself
    /// plays the same.
    /// </summary>
    public static class FxSettings
    {
        private static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(key, fallback ? 1 : 0) == 1;
        private static void Set(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); PlayerPrefs.Save(); }

        /// <summary>Repeats of a summon scene, cut-in or spotlight in the same duel play shorter (or not at all, for cut-ins).</summary>
        public static bool ShorterRepeats { get => Get("fxShortRepeats", true); set => Set("fxShortRepeats", value); }
        public static bool ReducedMotion { get => ReducedMotionOverride ?? Get("fxReducedMotion", false); set => Set("fxReducedMotion", value); }

        /// <summary>Launch options for checks (-reducedmotion, -textsize n): override the saved settings for one run.</summary>
        public static bool? ReducedMotionOverride;
        public static int? TextSizeOverride;
        public static bool SummonScenesOn { get => Get("fxScenes", true); set => Set("fxScenes", value); }
        public static bool CutInsOn { get => Get("fxCutIns", true); set => Set("fxCutIns", value); }
        public static bool ShakeOn { get => Get("fxShake", true); set => Set("fxShake", value); }
        /// <summary>A prized copy of yours (a Gem Mint Secret Rare and up) sparkles and chimes when it arrives face-up.</summary>
        public static bool PrizedMoments { get => Get("fxPrized", true); set => Set("fxPrized", value); }
        public static bool FlashesOn { get => Get("fxFlashes", true); set => Set("fxFlashes", value); }
        /// <summary>
        /// While a headliner's combo route plays, the duel runs at Normal speed (4 events a second) even when it's set faster,
        /// so the combo can be followed (docs/dev/HEADLINERS-2026-11-01.md, "The show"). Off keeps your speed.
        /// </summary>
        public static bool SlowForCombos { get => SlowForCombosOverride ?? Get("fxSlowCombos", true); set => Set("fxSlowCombos", value); }
        /// <summary>Checks only: overrides the saved setting for one run.</summary>
        public static bool? SlowForCombosOverride;

        // What actually plays: Reduced motion overrides the switches.
        public static bool SummonScenes => SummonScenesOn && !ReducedMotion;
        public static bool CutIns => CutInsOn && !ReducedMotion;
        public static bool Shake => ShakeOn && !ReducedMotion;
        public static bool Flashes => FlashesOn && !ReducedMotion;
        public static bool Particles => !ReducedMotion;

        /// <summary>
        /// The gold ▶ tab above every card you can play right now, a second signal beside the gold border (not only color).
        /// Off by default (owner, 2026-11-12): the gold border alone marks playable cards until the player turns the tab on.
        /// </summary>
        public static bool PlayableTabs { get => PlayableTabsOverride ?? Get("playableTabs", false); set => Set("playableTabs", value); }
        /// <summary>Launch option for checks (-noplaytabs).</summary>
        public static bool? PlayableTabsOverride;

        /// <summary>
        /// Card term help: card-text terms ("Quick Effect", "once per turn", "negate the activation") in blue and bold in the
        /// card detail, explained when you point at them, and a "?" with how card text is punctuated.
        /// </summary>
        public static bool CardTermHelp { get => Get("cardTermHelp", true); set => Set("cardTermHelp", value); }

        /// <summary>
        /// How strongly foils shine: 0 Subtle, 1 Standard, 2 Vivid (the name, the art's rainbow and the flash all scale;
        /// the gloss and wear don't). Remembered between runs.
        /// </summary>
        public static int FoilIntensity
        {
            get => FoilIntensityOverride ?? Mathf.Clamp(PlayerPrefs.GetInt("foilIntensity", 1), 0, 2);
            set
            {
                PlayerPrefs.SetInt("foilIntensity", Mathf.Clamp(value, 0, 2));
                PlayerPrefs.Save();
                CardFoil.PushIntensity();
            }
        }
        /// <summary>Checks only: overrides the saved setting for one run.</summary>
        public static int? FoilIntensityOverride;
        public static float FoilIntensityScale => FoilIntensity switch { 0 => 0.65f, 2 => 1.4f, _ => 1f };
        public static string FoilIntensityLabel => FoilIntensity switch { 0 => "Subtle", 2 => "Vivid", _ => "Standard" };

        /// <summary>
        /// Card tilt: in the card detail panels (the duel's and the deck editor's), the reading view, an ace's cut-in and
        /// the menu's showcase card, the picture dips a little under the pointer and its foil moves as a real card's does.
        /// On by default; Reduced motion keeps cards flat.
        /// </summary>
        public static bool CardTilt { get => CardTiltOverride ?? Get("cardTilt", true); set => Set("cardTilt", value); }
        /// <summary>Launch option for checks (-cardtilt on|off).</summary>
        public static bool? CardTiltOverride;
        public static bool CardTiltActive => CardTilt && !ReducedMotion;

        /// <summary>0 normal, 1 large, 2 larger.</summary>
        public static int TextSize { get => TextSizeOverride ?? Mathf.Clamp(PlayerPrefs.GetInt("textSize", 0), 0, 2); set { PlayerPrefs.SetInt("textSize", value); PlayerPrefs.Save(); } }
        public static float TextScale => TextSize switch { 1 => 1.2f, 2 => 1.4f, _ => 1f };
        public static string TextSizeLabel => TextSize switch { 1 => "Large", 2 => "Larger", _ => "Normal" };

        /// <summary>A font size scaled by the text size setting.</summary>
        public static int Scaled(int size) => Mathf.RoundToInt(size * TextScale);
    }
}
