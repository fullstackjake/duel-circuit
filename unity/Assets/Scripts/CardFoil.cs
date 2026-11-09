using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Draws a card picture with its rarity's foil (holo art, gold / silver / rainbow names, secret-rare sparkle...).
    /// Until there's a collection, each card shows its rarest real printing. The shine follows the pointer
    /// (or the controller cursor) while it's near the card and drifts slowly otherwise.
    /// </summary>
    public sealed class CardFoil : MonoBehaviour
    {
        private const string PrefKey = "foils";

        private static Shader? _shader;
        private static bool _shaderMissing;

        /// <summary>Every card's printings (from StreamingAssets/printings.bin); null when it isn't installed.</summary>
        public static PrintingIndex? Printings { get; set; }

        public enum Display
        {
            /// <summary>Plain cards.</summary>
            Off,
            /// <summary>Each card's rarest classic printing (up to Secret Rare).</summary>
            On,
            /// <summary>Each card's rarest printing of all (Starlight, Quarter Century...).</summary>
            Showcase,
        }

        /// <summary>For automated screenshots: a mode used for this run only, leaving the player's setting alone.</summary>
        public static Display? Override { get; set; }

        /// <summary>How foils are shown (a setting, remembered between runs).</summary>
        public static Display Mode
        {
            get => Override ?? (Display)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)Display.On), 0, 2);
            set
            {
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static string ModeLabel => Mode switch
        {
            Display.Off => "Foils: off",
            Display.On => "Foils: on",
            _ => "Foils: showcase",
        };

        /// <summary>Off, on, showcase, off...</summary>
        public static void CycleMode()
        {
            Override = null;
            Mode = (Display)(((int)Mode + 1) % 3);
        }

        /// <summary>
        /// For automated screenshots: one light every card uses instead of the pointer and the drift (x and y across the
        /// card, 0..1 from its bottom left, and z how much it's hovered, 0..1), with no idle glint, so two runs give the
        /// same pictures. Null in play.
        /// </summary>
        public static Vector3? FixedLight { get; set; }

        /// <summary>
        /// For automated screenshots: one tilt (degrees about the screen's x and y axes) every tilting picture takes at once,
        /// instead of following the pointer with its spring, so two runs give the same pictures. Null in play.
        /// </summary>
        public static Vector2? FixedTilt { get; set; }

        /// <summary>Where the idle light rests when it isn't drifting (the middle of <see cref="Drift"/>'s small circle).</summary>
        public static readonly Vector3 IdleLight = new Vector3(0.3f, 0.78f, 0);

        /// <summary>Your best copy of a card (by base card id), when you have a campaign collection; null if you don't own it.</summary>
        public static System.Func<uint, Rarity?>? OwnedRarity { get; set; }

        /// <summary>Your best copy's look (rarity, grade, wear seed) by base card id, for collection views; null if you don't own it.</summary>
        public static System.Func<uint, CopyLook?>? OwnedLook { get; set; }

        /// <summary>The duel on screen's copies (cards carry their deck slot; see <see cref="CopyLooks"/>); null outside a duel.</summary>
        public static CopyLooks? DuelLooks { get; set; }

        /// <summary>
        /// The rarity a card is drawn at: the best copy you own; otherwise (or in showcase mode) from its real printings.
        /// </summary>
        public static Rarity RarityOf(CardInfo info)
        {
            if (Mode == Display.On && OwnedRarity?.Invoke(info.Alias != 0 ? info.Alias : info.Code) is Rarity owned) return owned;
            return Printings == null ? Rarity.Common
                : Mode == Display.Showcase ? Printings.Rarest(info.Code, info.Alias)
                : Printings.RarestUpTo(info.Code, info.Alias, Rarity.SecretRare);
        }

        /// <summary>Rarity names colored like their foil, for detail panels.</summary>
        public static string ColoredName(Rarity r) => $"<color={RarityColor(r)}>{Rarities.Name(r)}</color>";

        private static string RarityColor(Rarity r) => r switch
        {
            Rarity.Common or Rarity.ShortPrint => "#B8BCC6",
            Rarity.Rare or Rarity.RareParallelRare or Rarity.PlatinumRare or Rarity.PlatinumSecretRare => "#E6EAF2",
            Rarity.SuperRare or Rarity.SuperParallelRare => "#7FD3FF",
            Rarity.NormalParallelRare or Rarity.StarfoilRare or Rarity.MosaicRare or Rarity.ShatterfoilRare => "#9FE6D8",
            Rarity.UltraRare or Rarity.UltraParallelRare or Rarity.PharaohsRare or Rarity.GoldRare or Rarity.PremiumGoldRare
                or Rarity.GoldSecretRare or Rarity.GhostGoldRare or Rarity.QuarterCenturySecretRare => "#FFD45A",
            Rarity.UltimateRare => "#E8B860",
            Rarity.GhostRare => "#D8E4FF",
            _ => "#FF8FE0", // secret and starlight kinds
        };

        /// <summary>
        /// A card's printings, rarest first, grouped by set code: "BETB-EN007  Secret Rare, Starlight Rare".
        /// Shows up to <paramref name="maxSets"/> sets, then how many more.
        /// </summary>
        public static string PrintingsText(CardInfo info, int maxSets)
        {
            var list = Printings?.Of(info.Code, info.Alias);
            if (list == null || list.Length == 0) return Printings == null ? "" : "<color=#8A8F9A>No TCG printings</color>";
            var sets = new System.Collections.Generic.List<string>();
            var rarities = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Rarity>>();
            foreach (var p in list)
            {
                if (!rarities.TryGetValue(p.SetCode, out var r))
                {
                    rarities[p.SetCode] = r = new System.Collections.Generic.List<Rarity>();
                    sets.Add(p.SetCode);
                }
                if (!r.Contains(p.Rarity)) r.Add(p.Rarity);
            }
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < sets.Count && i < maxSets; i++)
                lines.Add($"{sets[i]}  {string.Join(", ", rarities[sets[i]].ConvertAll(ColoredName))}");
            if (sets.Count > maxSets) lines.Add($"<color=#8A8F9A>+{sets.Count - maxSets} more printing{(sets.Count - maxSets == 1 ? "" : "s")}</color>");
            return string.Join("\n", lines);
        }

        private RawImage _image = null!;
        private Material? _material;
        private Vector2 _light = new Vector2(0.5f, 0.5f);
        private float _hover;
        private float _phase;
        private float _nextGlint, _glintStart = -10f;
        /// <summary>The card has foil layers: its light drifts and glints. A plain card only takes the pointer, for its gloss.</summary>
        private bool _foiled;
        /// <summary>The light last given to the material (a plain card stops updating once its hover has faded).</summary>
        private Vector4 _sent = new Vector4(float.NaN, 0, 0, 0);
        /// <summary>The picture is a worn copy (wear drawn).</summary>
        private bool _worn;
        /// <summary>The board-size boost last given to the material (see <see cref="UpdateSmall"/>).</summary>
        private float _small = -1;

        // ---- Tilt ----------------------------------------------------------------------------------------

        /// <summary>The most a picture tilts toward the pointer (degrees).</summary>
        public const float MaxTilt = 8f;
        // A light card held in the hand: a slight lag, a little overshoot, settled in about a quarter of a second.
        private const float TiltStiffness = 160f, TiltDamping = 19f, TiltStep = 1f / 240;

        /// <summary>
        /// Whether this picture tilts toward the pointer (set by <see cref="Tilting"/> for the picture-only views, and
        /// kept across redraws). Board, hand and grid cards never tilt: their stats strips and badges wouldn't tilt with
        /// them.
        /// </summary>
        private bool _tilts;
        private Vector2 _tilt, _tiltSpeed; // degrees about x and y, and how fast they're changing
        private float _tiltClock;           // time not yet stepped (under one step)
        private Vector2 _tiltSent = new Vector2(float.NaN, 0);

        /// <summary>
        /// Lets a picture-only view's card picture (the card detail, the reading view, a cut-in) tilt toward the pointer, up
        /// to <see cref="MaxTilt"/>°, when the Card tilt setting is on (and Reduced motion off). Call after <see cref="Apply"/>.
        /// The picture tilts in the shader; its rect, raycasts and layout stay flat.
        /// </summary>
        public static void Tilting(RawImage image)
        {
            var effect = image.GetComponent<CardFoil>();
            if (effect == null || effect._material == null) return;
            effect._tilts = true;
            if (image.GetComponent<TiltMesh>() == null) image.gameObject.AddComponent<TiltMesh>();
            effect._channel = false;
            effect.EnsureChannel();
            effect.UpdateTilt(0);
        }

        /// <summary>Whether the picture's canvas passes uv1 (the tilt's offsets) to the shader yet.</summary>
        private bool _channel;

        /// <summary>
        /// Turns on uv1 for the picture's canvas, and its root canvas (an inactive picture has no canvas yet, so this is
        /// tried again each frame until it has one).
        /// </summary>
        private void EnsureChannel()
        {
            if (_channel || _image == null) return;
            var canvas = _image.canvas;
            if (canvas == null) return;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            canvas.rootCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            _channel = true;
        }

        /// <summary>Gives each vertex of the picture its offset from the picture's centre (in uv1), for the shader's tilt.</summary>
        [RequireComponent(typeof(RawImage))]
        public sealed class TiltMesh : BaseMeshEffect
        {
            public override void ModifyMesh(VertexHelper vh)
            {
                int n = vh.currentVertCount;
                if (n == 0) return;
                var vertex = new UIVertex();
                Vector3 centre = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    vh.PopulateUIVertex(ref vertex, i);
                    centre += vertex.position;
                }
                centre /= n;
                for (int i = 0; i < n; i++)
                {
                    vh.PopulateUIVertex(ref vertex, i);
                    vertex.uv1 = new Vector4(vertex.position.x - centre.x, vertex.position.y - centre.y, 0, 0);
                    vh.SetUIVertex(vertex, i);
                }
            }
        }

        /// <summary>
        /// Moves the tilt toward the pointer (a damped spring stepped in fixed 1/240 s steps of real time, so it moves the
        /// same at any frame rate) and gives it and the view it makes to the material. Flat when the setting is off or under Reduced
        /// motion.
        /// </summary>
        private void UpdateTilt(float dt)
        {
            if (_material == null) return;
            Vector2 target = Vector2.zero;
            if (!_tilts || !FxSettings.CardTiltActive)
            {
                _tilt = _tiltSpeed = Vector2.zero;
            }
            else if (FixedTilt is Vector2 fixedTilt)
            {
                _tilt = fixedTilt;
                _tiltSpeed = Vector2.zero;
            }
            else
            {
                // The side under the pointer dips away from you, like a card pressed with a fingertip.
                if (PointerUv((RectTransform)transform, out var uv) && uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1)
                    target = new Vector2(-(uv.y - 0.5f) * 2 * MaxTilt, (uv.x - 0.5f) * 2 * MaxTilt);
                // Whole steps of real time only (the remainder waits for the next frame), so 60 and 30 fps step the same
                // states at the same moments; a long hitch is cut to a fifth of a second.
                _tiltClock = Mathf.Min(_tiltClock + dt, 0.2f);
                for (; _tiltClock >= TiltStep; _tiltClock -= TiltStep)
                {
                    _tiltSpeed += (TiltStiffness * (target - _tilt) - TiltDamping * _tiltSpeed) * TiltStep;
                    _tilt += _tiltSpeed * TiltStep;
                }
                if (target == Vector2.zero && _tilt.sqrMagnitude < 1e-4f && _tiltSpeed.sqrMagnitude < 1e-3f) _tilt = _tiltSpeed = Vector2.zero;
                _tilt.x = Mathf.Clamp(_tilt.x, -MaxTilt * 1.15f, MaxTilt * 1.15f);
                _tilt.y = Mathf.Clamp(_tilt.y, -MaxTilt * 1.15f, MaxTilt * 1.15f);
            }
            if (_tilt == _tiltSent) return;
            _tiltSent = _tilt;
            float ax = _tilt.x * Mathf.Deg2Rad, ay = _tilt.y * Mathf.Deg2Rad;
            _material.SetVector("_Tilt", new Vector4(ax, ay, 0, 0));
            // The viewer's direction in the card's frame (the card turned about x, then y; the eye straight in front).
            _material.SetVector("_TiltView", _tilt == Vector2.zero ? new Vector4(0, 0, 1, 0)
                : new Vector4(-Mathf.Sin(ay) * Mathf.Cos(ax), Mathf.Sin(ax), Mathf.Cos(ay) * Mathf.Cos(ax), 0));
        }

        /// <summary>
        /// Gives a card picture its rounded corners, gloss and foil (no foil for common cards, foils off, or no printings
        /// data). <paramref name="strength"/> is subtler for small board cards, full for the big detail picture.
        /// </summary>
        public static void Apply(RawImage image, CardInfo? info, float strength = 1f)
        {
            Apply(image, info, info == null ? Rarity.Common : RarityOf(info), strength);
        }

        /// <summary>
        /// Gives a card picture a particular rarity's foil (e.g. the copy you just pulled from a pack), and the wear of
        /// <paramref name="look"/>'s grade when it's a copy of yours (see <see cref="CardWear"/>). Wear shows even with
        /// foils off: it's the copy's condition, not its finish. Every card gets the material, plain ones too, for its
        /// rounded corners and the gloss that catches the pointer; only a card with no data is left square.
        /// </summary>
        public static void Apply(RawImage image, CardInfo? info, Rarity rarity, float strength, CopyLook look = default, uint cover = 0)
        {
            var finish = info == null || Mode == Display.Off ? new Finish() : Rarities.FinishOf(rarity);
            bool wear = info != null && CardWear.Shows(look, !finish.IsPlain);
            var existing = image.GetComponent<CardFoil>();
            if (info == null || LoadShader() == null)
            {
                if (existing != null) existing.Clear();
                return;
            }
            var effect = existing != null ? existing : image.gameObject.AddComponent<CardFoil>();
            effect.Set(image, info!, finish, strength, wear ? look : default, cover);
        }

        // ---- The prized moment -------------------------------------------------------------------------

        public enum PrizeKind { None, Glint, Full }

        /// <summary>
        /// Which copies have a moment when they arrive face-up: the full one for a Gem Mint Secret Rare up to a Collector's
        /// Rare, or a Mint or better Ghost, Quarter Century or Starlight; a glint for a Gem Mint Ultra up to a Platinum.
        /// </summary>
        public static PrizeKind PrizeOf(CopyLook look)
        {
            if (!look.IsCopy) return PrizeKind.None;
            // By the grade anyone can see: a Raw copy reads Mint at best, so only a slab shows Gem Mint (no moment gives a hidden 10 away).
            var r = look.Rarity;
            int g = look.Shown;
            if (g >= 10 && r >= Rarity.SecretRare && r <= Rarity.CollectorsRare) return PrizeKind.Full;
            if (g >= 9 && r >= Rarity.GhostGoldRare && r <= Rarity.StarlightRare) return PrizeKind.Full;
            if (g >= 10 && r >= Rarity.UltraRare && r <= Rarity.PlatinumRare) return PrizeKind.Glint;
            return PrizeKind.None;
        }

        /// <summary>A moment running on a card (by its cover), kept across redraws so a redrawn card resumes it.</summary>
        public sealed class PrizeRun
        {
            public float Start, Duration;
            public PrizeKind Kind;
            public bool Calm;
            public Color Color;
        }

        public static readonly System.Collections.Generic.Dictionary<uint, PrizeRun> Prizes = new System.Collections.Generic.Dictionary<uint, PrizeRun>();

        /// <summary>The color a rarity's moment glows in (the sweep's tint and its stars).</summary>
        public static Color PrizeColor(Rarity r) => Ui.Hex(r switch
        {
            Rarity.GoldSecretRare or Rarity.GhostGoldRare or Rarity.QuarterCenturySecretRare => "#FFD45A",
            Rarity.PlatinumSecretRare => "#E6EAF2",
            Rarity.UltimateRare => "#E8B860",
            Rarity.GhostRare => "#D8E4FF",
            Rarity.UltraRare or Rarity.UltraParallelRare or Rarity.PharaohsRare or Rarity.GoldRare or Rarity.PremiumGoldRare or Rarity.PlatinumRare => "#FFE9A8",
            _ => "#FF8FE0",
        });

        private uint _cover;
        private bool _prizeOn;

        private static Shader? LoadShader()
        {
            if (_shader == null && !_shaderMissing)
            {
                _shader = Resources.Load<Shader>("CardFoil");
                _shaderMissing = _shader == null || !_shader.isSupported;
                if (_shaderMissing) Debug.LogWarning("Card foil shader is missing or unsupported; cards are drawn plain.");
            }
            if (!_shaderMissing) PushIntensity();
            return _shaderMissing ? null : _shader;
        }

        /// <summary>
        /// Gives every card the Foil intensity setting (a global shader value, so cards already drawn change at once).
        /// Screenshot runs with a fixed light use Standard, so the player's setting can't change them.
        /// </summary>
        public static void PushIntensity() =>
            Shader.SetGlobalFloat("_FoilIntensity", FixedLight != null && FxSettings.FoilIntensityOverride == null ? 1 : FxSettings.FoilIntensityScale);

        private void Set(RawImage image, CardInfo info, Finish finish, float strength, CopyLook look, uint cover)
        {
            _cover = cover;
            _image = image;
            if (_material == null)
            {
                _material = new Material(_shader!) { name = "Card foil" };
                _phase = Random.value * 100f;
                _nextGlint = Time.unscaledTime + Random.Range(1f, 9f);
            }
            // A picture that wasn't card-shaped (a manga panel) is letterboxed: there's no name bar, stars, border or text
            // box where the card's layout would have them, so only the art and whole-card foils apply, over all of it.
            bool boxed = ArtLoader.IsLetterboxed(image.texture);
            _material.SetFloat("_ArtFoil", (float)finish.Art);
            _material.SetFloat("_NameFoil", boxed ? 0 : (float)finish.Name);
            _material.SetFloat("_CardFoil", (float)finish.Card);
            _material.SetFloat("_BorderFoil", boxed ? 0 : (float)finish.Border);
            _material.SetFloat("_Stars", finish.Stars && !boxed ? 1 : 0);
            _material.SetFloat("_Outline", finish.Outline && !boxed ? 1 : 0);
            // A plain card with wear: no foil layers and no surface glare, just the wear.
            _material.SetFloat("_Strength", finish.IsPlain ? 0 : strength);
            _material.SetFloat("_Ink", LightLetters(info.Type) ? 1 : 0);
            // Card layout in the picture (UV, origin bottom-left). Pendulum cards have a wider, shorter art window, and their
            // text runs from the monster's text box up through the Pendulum effect box (which overlaps the art's bottom).
            bool pendulum = (info.Type & 0x1000000) != 0;
            _material.SetVector("_ArtRect", boxed ? new Vector4(0, 0, 1, 1)
                : pendulum ? new Vector4(0.07f, 0.42f, 0.93f, 0.82f) : new Vector4(0.12f, 0.285f, 0.885f, 0.82f));
            _material.SetVector("_TextRect", boxed ? Vector4.zero
                : pendulum ? new Vector4(0.04f, 0.05f, 0.96f, 0.42f) : new Vector4(0.06f, 0.05f, 0.94f, 0.262f));
            _small = -1;
            UpdateSmall();
            _worn = look.IsCopy;
            if (look.IsCopy)
            {
                CardWear.Apply(_material, look, FxSettings.ReducedMotion, !finish.IsPlain);
                _material.EnableKeyword("_WEAR");
            }
            else _material.DisableKeyword("_WEAR");
            _image.material = _material;
            // Every card takes the light (a plain one only while it's pointed at, for its gloss).
            _foiled = !finish.IsPlain;
            _sent.x = float.NaN;
            Send(new Vector4(_light.x, _light.y, _hover, 0));
            _prizeOn = false;
            _material.DisableKeyword("_PRIZE");
            enabled = true;
            UpdatePrize();
        }

        private void Clear()
        {
            _tilts = false;
            if (_material != null)
            {
                // The material is reused if the picture gets a finish again: back to flat.
                _material.SetVector("_Tilt", Vector4.zero);
                _material.SetVector("_TiltView", new Vector4(0, 0, 1, 0));
            }
            _tilt = _tiltSpeed = Vector2.zero;
            _tiltSent = new Vector2(float.NaN, 0);
            if (_image != null) _image.material = null;
            enabled = false;
        }

        /// <summary>Spell, Trap, Xyz, and Link cards print their names in white; the rest in black.</summary>
        private static bool LightLetters(uint type) =>
            (type & (CardType.Spell | CardType.Trap | CardType.Xyz | CardType.Link)) != 0;

        /// <summary>Drives a running moment: its time into the shader, then off (and forgotten) once it's done.</summary>
        private void UpdatePrize()
        {
            if (_material == null || _cover == 0) return;
            if (!Prizes.TryGetValue(_cover, out var run))
            {
                if (_prizeOn) { _material.DisableKeyword("_PRIZE"); _prizeOn = false; }
                return;
            }
            float t = (Time.unscaledTime - run.Start) / run.Duration;
            if (t > 1)
            {
                Prizes.Remove(_cover);
                _material.DisableKeyword("_PRIZE");
                _material.SetFloat("_PrizeTime", -1);
                _prizeOn = false;
                return;
            }
            if (!_prizeOn)
            {
                _material.EnableKeyword("_PRIZE");
                var color = run.Color;
                color.a = run.Calm && run.Kind == PrizeKind.Glint ? 0.5f : 1;
                _material.SetColor("_PrizeColor", color);
                _material.SetFloat("_PrizeKind", run.Calm ? 2 : run.Kind == PrizeKind.Glint ? 1 : 0);
                _prizeOn = true;
            }
            _material.SetFloat("_PrizeTime", t); // below 0 until it starts: the shader shows nothing yet
        }

        private void Update()
        {
            if (_material == null) return;
            UpdatePrize();
            // A card that grows or shrinks (a summon, the spotlight) changes its board-size boost (foil and wear both use it).
            if (_foiled || _worn) UpdateSmall();
            if (_tilts)
            {
                EnsureChannel();
                UpdateTilt(Time.unscaledDeltaTime);
            }
            if (FixedLight is Vector3 fixedLight)
            {
                // Straight there, no easing: the picture can't depend on how long the frames took.
                _light = fixedLight;
                _hover = fixedLight.z;
                Send(new Vector4(fixedLight.x, fixedLight.y, fixedLight.z, 0));
                return;
            }
            Vector2 target = _light;
            float hovering = 0, glint = 0;
            bool near = PointerUv((RectTransform)transform, out var uv);
            if (near)
            {
                hovering = 1;
                target = uv;
            }
            // Away from the pointer a foil card's light drifts (and glints); a plain card's just stays where it was, and
            // once its gloss has faded there's nothing left to send.
            if (!near)
            {
                if (_foiled) target = Drift(out glint);
                else if (_hover < 0.002f && _sent.z == 0) return;
            }
            float t = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
            _light = Vector2.Lerp(_light, target, t);
            _hover = Mathf.Lerp(_hover, hovering, t);
            if (!near && !_foiled && _hover < 0.002f) _hover = 0;
            Send(new Vector4(_light.x, _light.y, _hover, glint));
        }

        /// <summary>The light as the card has it now (uv x, y from its bottom left, hover z), for a slab's glass to share.</summary>
        public Vector3 CurrentLight => new Vector3(_light.x, _light.y, _hover);

        /// <summary>The card has foil layers, so its light drifts and glints when nobody points at it.</summary>
        public bool Foiled => _foiled;

        /// <summary>
        /// Where the pointer (or the controller cursor) is over a card-shaped rect, in its uv (0..1 from the bottom left),
        /// and whether it's on or near it (within half a card around it): the light tracks the pointer there.
        /// </summary>
        public static bool PointerUv(RectTransform rt, out Vector2 uv)
        {
            uv = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, PointerPosition(), null, out var local)) return false;
            var rect = rt.rect;
            uv = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            return uv.x > -0.5f && uv.x < 1.5f && uv.y > -0.5f && uv.y < 1.5f;
        }

        /// <summary>
        /// The board-size boost (owner decision 5): 1 for a card 110 canvas units tall or less (the board, hand and deck
        /// grid), fading to 0 by 150, so a small card's foil shines a little more and its rarity reads across the table.
        /// Measured in the canvas's own units, so it doesn't depend on the screen's resolution.
        /// </summary>
        private void UpdateSmall()
        {
            if (_material == null) return;
            var rt = (RectTransform)transform;
            var canvas = _image != null ? _image.canvas : null;
            float scale = canvas != null && canvas.rootCanvas.transform.lossyScale.y > 0
                ? rt.lossyScale.y / canvas.rootCanvas.transform.lossyScale.y : 1;
            float small = Mathf.Clamp01((150 - rt.rect.height * Mathf.Abs(scale)) / 40);
            if (Mathf.Abs(small - _small) < 0.01f) return;
            _small = small;
            _material.SetFloat("_Small", small);
        }

        /// <summary>The light (uv xy, hover z, glint w) into the material, when it changed.</summary>
        private void Send(Vector4 light)
        {
            if (light == _sent) return;
            _sent = light;
            _material!.SetVector("_Light", light);
        }

        /// <summary>Sweeps a glint across the card now (e.g. as it lands in the spotlight); none under Reduced motion.</summary>
        public void Glint()
        {
            if (FxSettings.ReducedMotion) return;
            if (Time.unscaledTime - _glintStart > 1.3f) _nextGlint = Time.unscaledTime;
        }

        /// <summary>
        /// Where the light sits when nobody's pointing at the card: like a card lying still under a lamp, it barely
        /// moves, and every several seconds a glint sweeps across it as if it were tilted (<paramref name="glint"/>:
        /// how far into the sweep, 0 at its ends and 1 in the middle, for the gloss). Under Reduced motion it stays put
        /// and never glints.
        /// </summary>
        private Vector2 Drift(out float glint)
        {
            glint = 0;
            if (FxSettings.ReducedMotion) return IdleLight;
            var now = Time.unscaledTime;
            if (now >= _nextGlint)
            {
                _glintStart = now;
                _nextGlint = now + Random.Range(7f, 14f);
            }
            const float sweep = 1.3f;
            var g = (now - _glintStart) / sweep;
            if (g >= 0 && g <= 1)
            {
                glint = Mathf.Sin(Mathf.PI * g);
                var s = Mathf.SmoothStep(0, 1, g);
                return new Vector2(Mathf.Lerp(-0.4f, 1.4f, s), Mathf.Lerp(1.2f, 0.1f, s));
            }
            var t = now * 0.25f + _phase;
            return new Vector2(IdleLight.x + 0.04f * Mathf.Sin(t), IdleLight.y + 0.03f * Mathf.Cos(t * 0.8f));
        }

        private static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var controller = ControllerSupport.Instance;
            if (controller != null && controller.IsActive) return controller.CursorPosition;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null) return mouse.position.ReadValue();
#endif
            return Input.mousePosition;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
