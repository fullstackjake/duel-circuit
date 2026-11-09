// Every card picture: its rounded corners and gloss, and the foil finishes, built from layers (see
// Ygo.Engine.Data.Finish and CardFoil.cs): art foil, name foil, whole-card foil, border foil, foiled attribute / level
// stars, and a gold outline.
// Foil is lit, not painted on: it shows through the ink (strongest under light ink, hidden by dark ink), goes a little
// dark away from the light and flashes where the light catches it, and its colours gather near the light's reflection
// while the rest of the art keeps its own. Its rainbow bands slide about 2.75x as fast as the light moves, the way a
// tilted holo card's colours run. The light is the pointer while it's near the card.
// Patterns are at the real cards' scale (a Secret Rare's ~250 lines per card height): where the screen can show them they
// show; where it can't, they become what the eye sees from further away, an even sheen with broad bands and glitter fixed
// to the card (flakes that light up as the light crosses them, never on their own), not a flat tint or a screen door.
Shader "YgoCampaign/CardFoil"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ArtFoil ("Art foil", Float) = 0
        _NameFoil ("Name foil", Float) = 0
        _CardFoil ("Whole-card foil", Float) = 0
        _BorderFoil ("Border foil", Float) = 0
        _Stars ("Foiled attribute and stars", Float) = 0
        _Outline ("Gold outline in the art", Float) = 0
        _Strength ("Strength", Range(0, 1)) = 1
        _Small ("Board-size boost: 0 big, 1 at 110 canvas units tall and below", Float) = 0
        _Light ("Light (uv xy, hover z, glint w)", Vector) = (0.3, 0.8, 0, 0)
        // The tilt (CardFoil.cs, picture-only views): angles about the screen's x and y axes (radians), and the direction
        // to the viewer in the card's own frame (0, 0, 1 when it lies flat).
        _Tilt ("Tilt (about x, about y, radians)", Vector) = (0, 0, 0, 0)
        _TiltView ("View direction in the card's frame", Vector) = (0, 0, 1, 0)
        _ArtRect ("Art window (xMin yMin xMax yMax)", Vector) = (0.12, 0.285, 0.885, 0.82)
        _NameRect ("Name bar", Vector) = (0.06, 0.885, 0.83, 0.955)
        _AttrRect ("Attribute icon", Vector) = (0.835, 0.885, 0.945, 0.96)
        _StarsRect ("Level / rank stars", Vector) = (0.1, 0.828, 0.9, 0.878)
        _TextRect ("Text box (and a Pendulum's effect box): kept readable under every layer", Vector) = (0.06, 0.05, 0.94, 0.262)
        _Corner ("Corner radius (card heights)", Float) = 0.028
        _Ink ("Name letters are light (1) or dark (0)", Float) = 0
        // Wear (a copy's grade, keyword _WEAR; see CardWear.cs).
        _WearTex ("Wear atlas: R fibre, G scratch rank, B grime, A stains", 2D) = "black" {}
        _WearSeed ("Wear: atlas offset xy, quarter turns z, reduced motion w", Vector) = (0,0,0,0)
        _WearCorners ("Soft corners BL BR TL TR (0..1)", Vector) = (0,0,0,0)
        _WearEdge ("Corner radius, edge whitening, edge width (card heights), chips", Vector) = (0,0,0,0)
        _WearSurface ("Scratches (count/24), scratch opacity, grime, stains", Vector) = (0,0,0,0)
        _WearAge ("Fade, desaturation, crease opacity, keep edges a pixel wide", Vector) = (0,0,0,0)
        _WearCrease ("Crease point xy (card heights), angle (rad), half length", Vector) = (0,0,0,0)
        _WearCenter ("Print off-centre xy (uv), print line x (uv), print line strength", Vector) = (0,0,0,0)
        _WearGloss ("Gloss dulled 0..1, dents (count), dent depth, 0", Vector) = (0,0,0,0)
        // The prized moment (keyword _PRIZE, on only while it runs).
        _PrizeTime ("Prize time 0..1 (set from C#)", Float) = -1
        _PrizeColor ("Prize color (alpha: strength)", Color) = (1,0.56,0.88,1)
        _PrizeKind ("0 full sweep, 1 glint, 2 static luster", Float) = 0
        // Not a property: _FoilIntensity is global (the Foil intensity setting, see CardFoil.PushIntensity), so changing
        // it reaches every card at once.

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            // multi_compile, not shader_feature: the materials are made at runtime, so a feature would be stripped.
            #pragma multi_compile_local _ _WEAR
            #pragma multi_compile_local _ _PRIZE

            // uv1: the vertex's offset from the picture's centre (CardFoil.TiltMesh), for the tilt.
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex;
            float4 _MainTex_ST, _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            float _ArtFoil, _NameFoil, _CardFoil, _BorderFoil, _Stars, _Outline, _Strength, _Small, _Ink, _Corner;
            float _FoilIntensity;
            float4 _Tilt, _TiltView;
            float4 _Light, _ArtRect, _NameRect, _AttrRect, _StarsRect, _TextRect;
            sampler2D _WearTex;
            float4 _WearSeed, _WearCorners, _WearEdge, _WearSurface, _WearAge, _WearCrease, _WearCenter, _WearGloss;
            float _PrizeTime, _PrizeKind;
            fixed4 _PrizeColor;

            #define PI 3.14159265
            #define TAU 6.2831853
            #define ASPECT 0.686 // card width / height
            #define SILVER float3(0.86, 0.88, 0.94)
            #define GOLD float3(1.0, 0.74, 0.2)
            #define PALE_GOLD float3(1.0, 0.93, 0.68)
            #define CHAMPAGNE float3(0.91, 0.79, 0.54) // #E8C98A
            #define GOLD_SILVER float3(0.93, 0.83, 0.62) // Ultimate's desaturated gold
            #define STEEL float3(0.78, 0.79, 0.82)       // Ultimate's raised frame: silver-grey
            #define PAPER float3(0.93, 0.90, 0.82)
            #define WHITE_CORE float3(0.97, 0.96, 0.93) // the white paper core a chip or worn corner shows
            #define LIGHT_HEIGHT 0.7 // how far above the card the light is (card heights): sets how wide its reflection spreads
            #define BAND_SPEED 2.75  // rainbow bands slide this many times as far as the light moves
            #define TILT_BANDS 7     // and this far (card heights) per unit the view swings as the card tilts
            #define TILT_EYE 3.5     // how far the eye is from a tilted card (half its height): sets the perspective

            // The light for this pixel, worked out once at the top of frag: where it points on the card (card units), the
            // direction to it, the half vector between it and the viewer straight above, how hovered the card is, how
            // much of the gloss's glare shows (only while hovered or a glint crosses it), the card's height in pixels and
            // its board-size boost.
            // sTurn: how far a tilt has slid the rainbow bands (on top of the light's own slide); sV: the direction to the
            // viewer. (Kept as a separate term so a flat card computes exactly what it did before tilting existed.)
            static float2 sLP, sTurn;
            static float3 sV;
            static float3 sL, sH;
            static float sHover, sGlare, sPx, sSmall;

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                if (any(_Tilt.xy != 0))
                {
                    // Perspective tilt about the picture's centre: the corner turned in 3D, then w set so the GPU divides it
                    // back toward the centre (nearer corners larger), which also keeps the texture perspective-correct.
                    // The mesh and its raycast target stay flat.
                    float h = max(abs(v.uv1.y), 1e-4);
                    float2 q = v.uv1 / h;
                    float2 sx, sy;
                    sincos(_Tilt.x, sx.x, sx.y);
                    sincos(_Tilt.y, sy.x, sy.y);
                    float x = q.x * sy.y;
                    float zy = -q.x * sy.x;
                    float y = q.y * sx.y - zy * sx.x;
                    float z = q.y * sx.x + zy * sx.y;
                    float4 centre = UnityObjectToClipPos(float4(v.vertex.xy - v.uv1, v.vertex.zw));
                    float4 turned = UnityObjectToClipPos(float4(v.vertex.xy - v.uv1 + float2(x, y) * h, v.vertex.zw));
                    float w = 1 - z / TILT_EYE;
                    o.pos = float4(centre.xy * w + (turned.xy - centre.xy), turned.z * w, w * centre.w);
                }
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float3 Hue(float h) { return saturate(abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1); }
            float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float Sat(float3 c) { float hi = max(c.r, max(c.g, c.b)); return (hi - min(c.r, min(c.g, c.b))) / max(hi, 1e-3); }

            // Hashes without sin (Dave Hoskins' "hash without sine"): stable on every GPU and still random for the many
            // cells of fine patterns, where a sin hash shows its own patterns.
            float Hash(float2 p)
            {
                float3 q = frac(p.xyx * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float2 Hash2(float2 p)
            {
                float3 q = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // Smooth value noise 0..1 (one value per unit cell, blended).
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            // 1 inside the rectangle, fading over a hairline at its edges.
            float InRect(float2 uv, float4 r)
            {
                float2 lo = smoothstep(r.xy - 0.004, r.xy + 0.004, uv);
                float2 hi = 1 - smoothstep(r.zw - 0.004, r.zw + 0.004, uv);
                return lo.x * lo.y * hi.x * hi.y;
            }

            // A line pattern 0..1 for phase x (radians). Lines thinner than ~2 pixels fade to their average (0.5),
            // which is what the eye sees from far away, instead of aliasing into moiré.
            float Lines(float x)
            {
                float fade = saturate((PI - length(float2(ddx(x), ddy(x)))) / (PI * 0.5));
                return lerp(0.5, 0.5 + 0.5 * sin(x), fade);
            }

            // How much cell-based detail survives at this size: cells under ~4 pixels fade out.
            float CellFade(float2 cells) { return saturate((0.3 - max(fwidth(cells.x), fwidth(cells.y))) / 0.12); }

            // The rainbow you see tilting a foil card: bands across it (scale: hues per card height) that slide about
            // 2.75x as far as the light moves.
            float3 Rainbow(float2 p, float scale)
            {
                return Hue(dot(p - BAND_SPEED * sLP - sTurn, float2(0.6, 0.8)) * scale);
            }

            // ---- Light ---------------------------------------------------------------------------------

            // How squarely a surface tilted by `slope` (its height's change across the card) faces the light's reflection:
            // 1 where it mirrors the light straight back, falling off around it.
            float Facing(float2 slope) { return saturate(dot(normalize(float3(-slope, 1)), sH)); }

            // The extra light (or shade) an embossed slope catches, compared with the flat card.
            float Emboss(float2 slope) { return dot(normalize(float3(-slope, 1)), sL) - sL.z; }

            // How much foil shows through the ink: none under dark ink, most under light ink.
            float Through(float3 c) { return smoothstep(0.04, 0.55, Luma(c)); }

            // The foil layer, the same for every pattern: ink × (base + lobe × reflection), plus a little screen-like
            // brightening where the lobe peaks.
            // amount: how much foil the pattern puts here (0..1); tint: the colour it reflects (a rainbow, silver, gold);
            // slope: the surface's tilt (0 for a flat grating, a facet's own tilt, an emboss); k: strength.
            // Dark ink hides the foil, light ink lets it through, and light unsaturated ink shows the foil's own colours
            // (saturated ink tints it, like coloured glass over a mirror). Away from the light the foiled ink goes a little
            // darker. The foil's own colours gather near the light's reflection; elsewhere the art keeps its own colour (a
            // little more of the colour spreads at rest, under the room's broad light, than under a close hovered one).
            // At rest the room's light keeps every pattern showing at about a quarter of its hovered strength (its
            // signature never goes quiet). Light ink can only brighten so far: past that the foil tints toward its own
            // colour (the metal, the grid's rainbow) instead of washing out to white.
            float3 Foil(float3 c, float amount, float3 tint, float2 slope, float k)
            {
                float through = Through(c);
                float own = through * (1 - 0.6 * Sat(c));
                float facing = Facing(slope);
                float wide = pow(facing, 5);
                float near = pow(facing, 36);
                float flash = pow(facing, 110);
                float rest = 1 - sHover;
                float spread = lerp(0.15, 0.5, rest) + 0.25 * sSmall;
                float colour = saturate(own * 1.3) * saturate(spread + (1 - spread) * near);
                float f = saturate(amount) * k * through;
                float3 reflection = lerp(float3(1, 1, 1), tint * 1.3, colour);
                float lit = lerp(0.25, 0.45, rest) * wide + lerp(0.75, 0.3, rest) * near + (0.35 + 0.65 * sHover) * flash
                    + 0.22 * rest;
                float lc = Luma(c);
                float3 gain = 1 - 0.22 * f + f * lit * reflection;
                float cap = lerp(1.75, 1.1, smoothstep(0.4, 0.9, lc));
                float over = saturate((Luma(gain) - cap) / 0.6);
                float3 tinted = tint / max(max(tint.r, max(tint.g, tint.b)), 0.2);
                c *= min(gain, cap) * lerp(float3(1, 1, 1), lerp(float3(0.82, 0.82, 0.82), tinted, 0.8), over * f);
                float3 highlight = saturate(tint * flash * own * saturate(amount) * k * (0.15 + 0.25 * sHover) * (1 - 0.7 * smoothstep(0.55, 0.95, lc)));
                return 1 - (1 - c) * (1 - highlight);
            }

            // A broad rainbow band across the card at 45° (Secret Rares' tell), sliding with the light and the tilt:
            // smooth, no lines. 0..1 at its middle, with its colour.
            float3 DiagonalBand(float2 p, out float band)
            {
                float2 m = p - BAND_SPEED * sLP - sTurn;
                float along = (m.x + m.y) * 0.7071;
                float d = frac(along * 0.75 + 0.35) - 0.5;
                band = exp(-d * d / 0.018);
                return Hue(along * 1.6 + d * 2.2);
            }

            // One glitter flake per cell of q, each with its own tilt (spread: how far they're tilted), lit only while its
            // tilt mirrors the light into the eye (sharp: how exactly). A round flake at least a pixel across.
            float Flakes(float2 q, float spread, float sharp, float pxPerCell)
            {
                float2 id = floor(q);
                float3 n = normalize(float3((Hash2(id) - 0.5) * spread, 1));
                // A few flakes always catch some of the room's light, so the glitter still shows at rest.
                float lit = max(smoothstep(sharp, 1, dot(n, sH)), (1 - sHover) * 0.45 * step(0.82, Hash(id + 9.7)));
                float2 at = frac(q) - 0.5 - (Hash2(id + 17.3) - 0.5) * 0.5;
                float radius = max(0.16 + 0.2 * Hash(id + 5.1), 0.75 / pxPerCell);
                return lit * saturate(1.5 - length(at) / radius);
            }

            // Glitter fixed to the card: `cells` flakes per card height where they're a couple of pixels wide, else a
            // coarser layer (about a third as many) so small cards still sparkle. Only one layer is worked out (the size
            // test is the same over the whole card), which keeps small cards cheap.
            float Sparkle(float2 q, float cells, float spread, float sharp)
            {
                float px = sPx / cells;
                float fine = saturate((px - 1.2) / 1.5);
                if (fine > 0.99) return Flakes(q * cells, spread, sharp, px);
                float coarse = Flakes(q * cells * 0.35 + 31.7, spread, sharp, px * 2.86) * 0.85;
                return fine < 0.01 ? coarse : lerp(coarse, Flakes(q * cells, spread, sharp, px), fine);
            }

            // Glitter over the ink (through it like the foil), tinted, as a screen-like brightening.
            float3 Glitter(float3 c, float s, float3 tint, float k)
            {
                float3 g = saturate(s * k * Through(c) * lerp(float3(1, 1, 1), tint, 0.6));
                return 1 - (1 - c) * (1 - g);
            }

            // How bright flat metal (a foil name, a gold border, the gold outline) is here, 0..1: the room's reflection in
            // it (broad bands, a finer set across them and a lighter top, all sliding with the light, so it never reads as
            // flat paint), a sheen around the light, and the mirror flash.
            float MetalShine(float2 p, float streaks)
            {
                float2 m = p - BAND_SPEED * sLP - sTurn;
                float room = 0.5 + 0.5 * sin(m.x * streaks + m.y * streaks * 1.5);
                float fine = 0.5 + 0.5 * sin((m.x * 0.6 - m.y) * streaks * 3.1);
                float facing = sH.z;
                float wide = pow(facing, 8), flash = pow(facing, 64);
                return saturate(0.05 + 0.35 * room + 0.15 * fine + 0.15 * saturate(p.y) + lerp(0.2, 0.4, sHover) * wide
                    + flash * (0.4 + 0.6 * sHover));
            }

            // A metal's colour at a brightness: darker below 1, and above 1 it doesn't just turn a brighter yellow but goes
            // pale toward the highlight, the way polished gold and silver do.
            float3 Metal(float3 base, float3 pale, float level)
            {
                return base * min(level, 1) + (pale - base) * saturate((level - 1) / 0.4) * 0.85;
            }

            // Edge-finding steps of at least one texel of a 268x391 picture (the biggest classic size), so an HD picture's
            // relief and outline follow the same features at the same strength rather than its finer grain.
            #define ART_STEP max(_MainTex_TexelSize.xy, float2(1.0 / 268, 1.0 / 391))

            // The picture's brightness slope at uv, read from a blurred mip (`lod`) so JPEG grain doesn't emboss. Only
            // called inside the regions that use it, after their branch, so it uses explicit LODs (no derivatives).
            float2 InkSlope(float2 uv, float lod)
            {
                float2 t = max(ART_STEP, exp2(lod) * _MainTex_TexelSize.xy) * 1.2;
                float dx = Luma(tex2Dlod(_MainTex, float4(uv + float2(t.x, 0), 0, lod)).rgb) - Luma(tex2Dlod(_MainTex, float4(uv - float2(t.x, 0), 0, lod)).rgb);
                float dy = Luma(tex2Dlod(_MainTex, float4(uv + float2(0, t.y), 0, lod)).rgb) - Luma(tex2Dlod(_MainTex, float4(uv - float2(0, t.y), 0, lod)).rgb);
                return float2(dx, dy) * 2.5;
            }

            // ---- Patterns -------------------------------------------------------------------------------

            // Super / Ultra Rare: an even rainbow sheen with a fine linear texture, no glitter.
            float3 Holo(float3 c, float2 p, float k)
            {
                float lines = Lines((p.x * 0.5 + p.y) * TAU * 70);
                return Foil(c, 0.5 + 0.5 * lines, Rainbow(p, 2.0), 0, k);
            }

            // Secret Rare: two gratings on the diagonals, ~250 lines per card height, each throwing its own colour, so
            // two sets of bands cross in an X. Unresolved, it's an even sheen in those crossing colours with glitter in
            // diagonal rows.
            float3 SecretFoil(float3 c, float2 p, float k, float silver)
            {
                float a = Lines((p.x + p.y) * TAU * 177);
                float b = Lines((p.x - p.y) * TAU * 177);
                float2 m = p - BAND_SPEED * sLP - sTurn;
                float3 ta = Hue((m.x + m.y) * 0.7071 * 3.0);
                float3 tb = Hue((m.x - m.y) * 0.7071 * 3.0 + 0.33);
                float3 tint = lerp(max(ta * (0.5 + a), tb * (0.5 + b)) * 0.8, SILVER, silver);
                float band;
                float3 bandHue = DiagonalBand(p, band);
                c = Foil(c, 0.55 + 0.45 * max(a, b) + 0.6 * band, lerp(tint, bandHue, band * 0.7), 0, k);
                // The band itself, a soft screen of colour through the ink, even at rest.
                float3 sheen = saturate(lerp(bandHue, SILVER, silver * 0.6) * band * k * Through(c) * (0.16 + 0.12 * sHover));
                c = 1 - (1 - c) * (1 - sheen);
                float s = Sparkle(float2(p.x + p.y, p.x - p.y) * 0.7071, 120, 0.7, 0.992);
                return Glitter(c, s, lerp(max(ta, tb), SILVER, silver), k);
            }

            // Prismatic (and Starlight): a fine grid of horizontal and vertical lines (~170 per card height) whose
            // colours run in rings around the light, with square-set glitter.
            float3 PrismaticFoil(float3 c, float2 p, float k, float glitter, float cells)
            {
                float a = Lines(p.x * TAU * 170);
                float b = Lines(p.y * TAU * 170);
                float2 middle = float2(ASPECT, 1) * 0.5;
                float3 tint = Hue(length(p - (middle + BAND_SPEED * (sLP - middle)) - sTurn) * 2.5 + a * 0.1);
                c = Foil(c, 0.55 + 0.45 * max(a, b), tint, 0, k);
                float s = Sparkle(p, cells, 0.75, 0.99);
                return Glitter(c, s * glitter, tint, k);
            }

            // Collector's Rare: the whorls of a fingerprint, concentric ridges (~18 per card height) round two cores,
            // wobbled by a smooth noise, whose rings run outward as the light moves.
            float FingerprintRidges(float2 p)
            {
                float2 w = p + 0.025 * float2(Noise(p * 5), Noise(p * 5 + 7.1));
                float r = min(length(w - float2(0.22, 0.6)), length(w - float2(0.47, 0.38)) * 1.08);
                float phase = dot(BAND_SPEED * sLP + sTurn, float2(0.6, 0.8)) * 2.5;
                return Lines((r * 18 - phase) * TAU);
            }
            float3 Fingerprint(float3 c, float2 p, float ridges, float k)
            {
                float r = ridges * ridges;
                float through = Through(c);
                // The grooves between the ridges are a little darker, and the crests catch some of the room's light even
                // away from the lamp, so the texture shows at rest as well as in the light.
                c *= 1 - 0.14 * (1 - ridges) * saturate(k) * through;
                c = Foil(c, 0.2 + 0.8 * r, lerp(SILVER, Rainbow(p, 2.6), 0.3), 0, k * 1.3);
                float3 crest = SILVER * saturate(r * k * through * (0.14 + 0.2 * pow(sH.z, 8)));
                return 1 - (1 - c) * (1 - crest);
            }

            // Pharaoh's Rare: holo foil with hieroglyphs in it, each glyph tilted its own way so it catches the light as
            // the light crosses it.
            float3 Hieroglyphs(float3 c, float2 p, float k)
            {
                float2 cells = p * 12;
                float2 id = floor(cells);
                // The holo's lines, their phase jittered per glyph cell, so they don't run as one regular grating.
                float lines = Lines(((p.x * 0.5 + p.y) * 70 + Hash(id + 4.2)) * TAU);
                c = Foil(c, 0.5 + 0.5 * lines, Rainbow(p, 2.0), 0, k);
                float2 f = frac(cells) - 0.5;
                float r = Hash(id);
                float glyph = r < 0.25 ? step(abs(f.x), 0.11) * step(abs(f.y), 0.36)                // reed
                            : r < 0.5 ? step(abs(length(f * float2(1, 1.6)) - 0.28), 0.09)            // eye
                            : r < 0.75 ? step(abs(f.y + 0.2), 0.09) * step(abs(f.x), 0.34)            // ground line
                                       : step(abs(length(f) - 0.22), 0.09);                           // sun disk
                glyph *= CellFade(cells);
                // Each glyph is tilted its own way: dim at rest, bright where the light's reflection crosses it.
                float3 n = normalize(float3((Hash2(id + 9.1) - 0.5) * 0.5, 1));
                float caught = smoothstep(0.9, 1, dot(n, sH));
                c = Foil(c, glyph * (0.3 + 0.7 * caught), Rainbow(p, 3.0), 0, k * 1.5);
                float3 shine = saturate(PALE_GOLD * glyph * caught * k * Through(c) * 0.45);
                return 1 - (1 - c) * (1 - shine);
            }

            // Platinum Rare: brushed metal over the whole card. The ink greys toward silver, horizontal brush marks run
            // through it, and the highlight is stretched across the brushing (a tall streak) that follows the light. It
            // brightens the art, never bleaches it.
            float3 Platinum(float3 c, float2 p, float k)
            {
                float3 metal = lerp(c, Luma(c).xxx * SILVER * 1.05 + 0.03, saturate((0.5 + 0.2 * sSmall) * k));
                float brush = Noise(float2(p.x * 3, p.y * 240));
                float3 h = normalize(float3(sH.x, sH.y * 0.3, sH.z));
                float streak = pow(h.z, 60) * (0.6 + 0.4 * brush);
                float wide = pow(sH.z, 6);
                float level = 0.9 + k * (lerp(0.15, 0.25, sHover) * wide + lerp(0.25, 0.4, sHover) * streak + 0.06 * (brush - 0.5) + 0.12 * sSmall);
                metal *= level;
                float3 highlight = SILVER * saturate(streak * k * (0.08 + 0.12 * sHover));
                metal = 1 - (1 - metal) * (1 - highlight);
                // Fine silver glitter in the metal (more of it at board size, where it's the platinum's tell).
                return Glitter(metal, Sparkle(p, 110, 0.6, 0.99) * (0.7 + 0.9 * sSmall), SILVER, k);
            }

            // Starfoil Rare: stars all over the card, each tilted its own way, that only show when they catch the light.
            float3 Starfoil(float3 c, float2 p, float k)
            {
                float star = 0, catches = 0;
                for (int layer = 0; layer < 2; layer++)
                {
                    float scale = layer == 0 ? 11 : 23;
                    float2 cells = p * scale + layer * 7.7;
                    float2 id = floor(cells);
                    float2 f = frac(cells) - 0.5 + (Hash2(id) - 0.5) * 0.25;
                    float a = atan2(f.y, f.x);
                    float s = (1 - smoothstep(0.0, 0.03, length(f) - 0.14 * (0.6 + 0.4 * cos(5 * a)))) * step(0.35 * layer, Hash(id + 2.2));
                    float3 n = normalize(float3((Hash2(id + 4.4) - 0.5) * 0.9, 1));
                    float lit = smoothstep(0.93, 1, dot(n, sH));
                    star = max(star, s);
                    catches = max(catches, s * lit);
                }
                star *= CellFade(p * 23);
                catches *= CellFade(p * 23);
                // Silver stars; their colour shows only in the light's reflection.
                float3 tint = lerp(SILVER, Rainbow(p, 2.5), pow(sH.z, 30));
                return Foil(c, 0.12 * star + catches, tint, 0, k * 1.5);
            }

            // Mosaic Rare: fine squares (~48 per card height), each tilted its own way, with the art's own colour in them.
            float3 Mosaic(float3 c, float2 p, float k, float art)
            {
                k *= lerp(0.45, 1, art); // strongest on the art, quiet on the frame
                float2 cells = p * 48;
                float2 id = floor(cells);
                float2 f = frac(cells);
                float fade = CellFade(cells);
                float grout = lerp(1, step(0.07, f.x) * step(f.x, 0.93) * step(0.07, f.y) * step(f.y, 0.93), fade);
                float2 tilt = (Hash2(id) - 0.5) * 0.7;
                float3 tint = lerp(float3(1, 1, 1), Hue(Hash(id + 3.3) * 0.5 + BAND_SPEED * dot(sLP, float2(0.6, 0.8)) * 0.6 + dot(sTurn, float2(0.6, 0.8)) * 0.6), 0.2);
                return Foil(c, 0.35 + 0.65 * grout, tint, tilt, k);
            }

            // Shatterfoil Rare: shattered glass, irregular shards (~7 across a card height) each reflecting at its own
            // angle, and the cracks between them catching the light.
            float3 Shatter(float3 c, float2 p, float k)
            {
                float2 g = p * 7;
                float2 i = floor(g), f = frac(g);
                float d1 = 8, d2 = 8;
                float2 shard = 0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 o = float2(x, y);
                        float2 at = o + 0.08 + 0.84 * Hash2(i + o) - f;
                        float d = dot(at, at);
                        if (d < d1) { d2 = d1; d1 = d; shard = i + o; }
                        else if (d < d2) d2 = d;
                    }
                float edge = sqrt(d2) - sqrt(d1);
                float crack = 1 - smoothstep(0.015, 0.015 + 14 / max(sPx, 1), edge);
                float2 tilt = (Hash2(shard + 1.7) - 0.5) * 1.1;
                float3 tint = Hue(Hash(shard) + BAND_SPEED * dot(sLP, float2(0.6, 1.0)) * 0.6 + dot(sTurn, float2(0.6, 1.0)) * 0.6);
                c = Foil(c, 0.45 + 0.4 * Hash(shard + 8.8), tint, tilt, k);
                return Foil(c, crack * 0.8, SILVER, 0, k);
            }

            // Parallel / Duel Terminal: a fine dotted film (~70 dots per card height) with faint bands, over the art and
            // frame (kept off the name and the text box by the caller).
            float3 Parallel(float3 c, float2 p, float k)
            {
                float bands = 0.5 + 0.5 * sin((p.y - BAND_SPEED * sLP.y - sTurn.y) * TAU * 6);
                float2 cells = p * 70;
                float2 jitter = (Hash2(floor(cells)) - 0.5) * 0.3; // each dot a little off the grid
                float dots = (1 - smoothstep(0.16, 0.28, length(frac(cells) - 0.5 - jitter))) * CellFade(cells);
                // On a board-size card the dots are too fine to see: the film shows as stronger rainbow bands instead.
                return Foil(c, 0.25 + lerp(0.15, 0.5, sSmall) * bands + 0.5 * dots, Rainbow(p, 1.6), 0, k * (1 + 0.8 * sSmall));
            }

            // ---- Name, stars, border, outline -----------------------------------------------------------

            // Pixels that are the name's letters (dark letters on a light bar, or light on dark).
            float Letters(float3 c)
            {
                float l = Luma(c);
                return _Ink > 0.5 ? smoothstep(0.55, 0.8, l) : 1 - smoothstep(0.2, 0.42, l);
            }

            // A foil name's metal. It replaces the ink outright, from about 0.75 to 1.4 of the metal's colour as the light
            // moves over it (brighter at board size). 1 silver, 2 gold, 3 secret (silver with the diagonal rainbow in
            // it), 4 embossed (Ultimate's gold-silver, its relief added by the caller), 5 speckled (silver with specks of
            // every colour), 6 champagne (pale gold with fine bright specks).
            float3 NameMetal(int mode, float2 p, float k)
            {
                float shine = saturate(MetalShine(p, 14) * saturate(k * 1.25));
                float level = lerp(0.75, 1.15, sSmall) + 0.65 * shine;
                if (mode == 1) return Metal(SILVER, float3(1, 1, 1), level);
                if (mode == 2) return Metal(GOLD, PALE_GOLD, level);
                if (mode == 3)
                {
                    float hatch = max(Lines((p.x + p.y) * TAU * 38), Lines((p.x - p.y) * TAU * 38));
                    return Metal(lerp(SILVER, Rainbow(p, 4.0) * 0.8 + 0.3, 0.35 + 0.3 * hatch), float3(1, 1, 1), level);
                }
                if (mode == 4) return Metal(GOLD_SILVER, float3(1, 0.97, 0.88), level);
                float2 id = floor(p * 160);
                float caught = 0.5 + 0.5 * smoothstep(0.9, 1, dot(normalize(float3(Hash2(id + 3.1) - 0.5, 1)), sH));
                if (mode == 5)
                {
                    float speck = step(0.45, Hash(id)) * caught;
                    return Metal(lerp(SILVER * 0.9, Hue(Hash(id + 1.9)), speck), float3(1, 1, 1), level);
                }
                float speck = step(0.6, Hash(id)) * caught;
                return Metal(CHAMPAGNE, float3(1, 0.93, 0.78), level + speck * 0.35);
            }

            float BorderMask(float2 uv)
            {
                // The card's outer edge, plus the frame around the art window.
                float outer = 1 - InRect(uv, float4(0.045, 0.032, 0.955, 0.968));
                float4 a = _ArtRect;
                float frame = InRect(uv, a + float4(-0.014, -0.012, 0.014, 0.012)) * (1 - InRect(uv, a));
                return saturate(outer + frame);
            }

            // The thin frame round the text box (a Gold Rare's is gold too).
            float TextBoxEdge(float2 uv)
            {
                float4 t = _TextRect;
                return InRect(uv, t + float4(-0.005, -0.004, 0.005, 0.004)) * (1 - InRect(uv, t + float4(0.003, 0.003, -0.003, -0.003)));
            }

            // How far a card point (card units) is outside the card's rounded outline (negative inside).
            float CardEdge(float2 p, float radius)
            {
                float2 extent = float2(ASPECT, 1) * 0.5;
                float2 q = abs(p - extent) - (extent - radius);
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
            }

            // Distance to a stroke from a to b, minus its half width.
            float Stroke(float2 q, float2 a, float2 b, float r)
            {
                float2 pa = q - a, ba = b - a;
                return length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba))) - r;
            }

            // A plain "25" (our own numeral, drawn in straight strokes), 1 on the strokes: q in units of the digits'
            // height, centred on the pair; aa a pixel in those units.
            float TwentyFive(float2 q, float aa)
            {
                float w = 0.21, h = 0.42, r = 0.055;
                float2 t = q + float2(0.3, 0); // the 2
                float d = Stroke(t, float2(-w, h), float2(w, h), r);
                d = min(d, Stroke(t, float2(w, h), float2(w, 0), r));
                d = min(d, Stroke(t, float2(w, 0), float2(-w, 0), r));
                d = min(d, Stroke(t, float2(-w, 0), float2(-w, -h), r));
                d = min(d, Stroke(t, float2(-w, -h), float2(w, -h), r));
                float2 f = q - float2(0.3, 0); // the 5
                d = min(d, Stroke(f, float2(w, h), float2(-w, h), r));
                d = min(d, Stroke(f, float2(-w, h), float2(-w, 0), r));
                d = min(d, Stroke(f, float2(-w, 0), float2(w, 0), r));
                d = min(d, Stroke(f, float2(w, 0), float2(w, -h), r));
                d = min(d, Stroke(f, float2(w, -h), float2(-w, -h), r));
                return 1 - smoothstep(-aa, aa, d);
            }

            // ---- Wear ----------------------------------------------------------------------------------

            // Where a card point (card heights) falls in the wear atlas for this copy: its seeded offset and quarter turn.
            float2 WearUV(float2 p)
            {
                int turns = (int)round(_WearSeed.z);
                float2 q = turns == 1 ? float2(-p.y, p.x) : turns == 2 ? -p : turns == 3 ? float2(p.y, -p.x) : p;
                return q + _WearSeed.xy;
            }

            // The print as it sits on an off-centre copy: shifted a little, the border band mirrored into the margin it
            // leaves (so one border is wider and the other narrower), and the name, stars and text box carried with it.
            float2 Centered(float2 uv)
            {
                float2 s = uv - _WearCenter.xy;
                return 1 - abs(1 - abs(s));
            }

            // Wear may change the art and border freely; elsewhere (name, stars, text box) brightness stays within ±6%
            // of what it was before this layer (two layers: ±12% in all).
            float3 Protect(float3 before, float3 worn, float free)
            {
                float l0 = Luma(before), l = Luma(worn);
                float3 capped = worn * clamp(l, 0.94 * l0, 1.06 * l0) / max(l, 1e-4);
                return lerp(capped, worn, free);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // uv: where this pixel is in the print (the layout, foil patterns and picture); pc: where it is on the card
                // itself (its outline, the light, the wear). They're the same unless the copy's print is off-centre.
                float2 uv = i.uv;
                #ifdef _WEAR
                uv = Centered(i.uv);
                fixed4 tex = tex2Dgrad(_MainTex, uv, ddx(i.uv), ddy(i.uv)) * i.color; // the unmirrored gradient: no seam at the fold
                #else
                fixed4 tex = tex2D(_MainTex, uv) * i.color;
                #endif
                float3 c = tex.rgb;
                float2 p = uv * float2(ASPECT, 1);
                float2 pc = i.uv * float2(ASPECT, 1);
                sLP = _Light.xy * float2(ASPECT, 1);
                sHover = _Light.z;
                sGlare = saturate(max(_Light.z, _Light.w));
                sL = normalize(float3(sLP - pc, LIGHT_HEIGHT));
                // The view: straight down at a flat card; a tilted card sees it from the side, so its reflections and
                // rainbow bands move as a real card's do when it's turned under a lamp.
                sV = _TiltView.xyz;
                sH = normalize(sL + sV);
                sTurn = sV.xy * TILT_BANDS;
                sSmall = saturate(_Small);
                // Small cards shine a little more, so their rarity reads across the table (owner decision 5).
                float k = _Strength * _FoilIntensity * (0.8 + 0.2 * sHover) * (1 + 0.3 * sSmall);
                float art = InRect(uv, _ArtRect);
                float text = InRect(uv, _TextRect);
                int artFoil = (int)round(_ArtFoil);
                int nameFoil = (int)round(_NameFoil);
                int cardFoil = (int)round(_CardFoil);
                int borderFoil = (int)round(_BorderFoil);
                // Derivatives up here, outside the branches below: a screen pixel in uv, the card's height in pixels, and
                // the mip the GPU would pick.
                float2 onePixel = float2(fwidth(uv.x), fwidth(uv.y));
                sPx = 1 / max(onePixel.y, 1e-5);
                float2 texels = i.uv * _MainTex_TexelSize.zw;
                float mip = max(0.5 * log2(max(dot(ddx(texels), ddx(texels)), dot(ddy(texels), ddy(texels)))), 0);
                // The mip a 268x391 picture's texel lands on: relief and outlines read from it, or blurrier on small cards.
                float classicMip = max(log2(max(_MainTex_TexelSize.w / 391, 1)), mip);
                float edgeDistance = CardEdge(pc, _Corner);
                float edgeAA = max(fwidth(edgeDistance), 1e-5);
                // How much of the gloss wear has taken here (0 on an unworn card): the specular goes broad and weak.
                float dull = 0;

                #ifdef _WEAR
                // Owner decision 6: the finer wear (nicks, scratches, dents, print lines) shows only at detail size; on the
                // board a worn card reads by its dulled gloss, its age and its corners.
                float fine = saturate(1 - 1.4 * sSmall);
                float2 wa = WearUV(pc);
                float4 wt = tex2D(_WearTex, wa);
                float tpp = max(max(fwidth(wa.x), fwidth(wa.y)) * 512, 1e-3); // atlas texels per screen pixel
                float amp = saturate((8 / tpp - 1.5) / 1.5);                 // fibre detail fades on small cards
                // Where wear may change brightness freely: the art and border, and at board size the whole frame too (only
                // the name, stars and text box stay within ±12%), so a played card reads duller across the table.
                float kept = saturate(InRect(uv, _NameRect) + InRect(uv, _AttrRect) + InRect(uv, _StarsRect) + text);
                float free = saturate(art + BorderMask(uv) + sSmall * (1 - kept));
                float fuzz = (wt.r - 0.5) * amp;
                float2 corner[4] = { float2(0, 0), float2(ASPECT, 0), float2(0, 1), float2(ASPECT, 1) };
                float strength[4] = { _WearCorners.x, _WearCorners.y, _WearCorners.z, _WearCorners.w };
                // Worn corners change shape: the outline is cut back and rounded off, frayed by the paper's fibre, and the
                // paper core shows white along what's left. (The cut goes into the outline the alpha is taken from.)
                float r = _WearEdge.x, cut = 0, nearCorner = 0;
                for (int n = 0; n < 4; n++)
                {
                    float d = length(pc - corner[n]);
                    cut = max(cut, strength[n] * (1 - smoothstep(0.15 * r, 1.7 * r, d)));
                    nearCorner = max(nearCorner, 1 - smoothstep(0.04, 0.3, d));
                }
                edgeDistance += cut * r * 0.34 * (1 + 0.8 * fuzz);
                float inside = -edgeDistance; // how far in from the card's (worn) outline
                {
                    float3 before = c;
                    // Ageing of the ink first (fade, desaturation, grime, stains), so the foil sits on the aged card.
                    c = lerp(c, Luma(c).xxx, _WearAge.y * lerp(0.5, 1, free));
                    c = lerp(c, PAPER, _WearAge.x);
                    float edgeNear = 1 - smoothstep(0, 0.12, inside);
                    float grime = saturate((wt.b - (1 - _WearSurface.z)) * 2) * (0.6 + 0.2 * edgeNear);
                    c *= 1 - 0.35 * grime;
                    float stain = saturate((wt.a - (1 - _WearSurface.w)) * 3);
                    c *= lerp(float3(1, 1, 1), float3(0.86, 0.78, 0.62), stain);
                    // Gloss dulling, in hazy patches (the grime map's broad shapes): a milky veil that lifts the darks and
                    // softens the lights, the clearest sign of a played card, on the board too. The foil loses its shine
                    // with it.
                    dull = _WearGloss.x * saturate(0.6 + 1.6 * (wt.b - 0.5)) * (1 + 0.3 * sSmall);
                    float3 veiled = lerp(Luma(c).xxx, c, 0.7) * 0.78 + float3(0.14, 0.14, 0.15);
                    c = lerp(c, veiled, saturate(dull));
                    k *= 1 - 0.7 * saturate(dull);
                    // At board size, from Excellent (6) down, the whole card goes flatter and greyer, evenly, so the grade
                    // reads at a glance (owner decision 6). It pulls whites down too, so white frames show it.
                    float flatten = saturate(sSmall * 1.6) * saturate((_WearGloss.x - 0.08) * 3);
                    float3 flat = lerp(Luma(c).xxx, c, 0.4) * 0.68 + float3(0.15, 0.14, 0.12);
                    c = lerp(c, flat, flatten * 0.85);
                    c = Protect(before, c, free);
                }
                #endif

                // The card as printed (aged, if worn) under the foil layers: the text box keeps most of it.
                float3 printed = c;

                // The fingerprint's ridges, worked out once for the Collector's art and border.
                float ridges = 0;
                if (artFoil == 6 || borderFoil == 2) ridges = FingerprintRidges(p);

                // Whole card first, so the art and name layers sit on top. (Each pattern is linear enough in its strength
                // that the art's stronger foil is one call at a blended strength.) The text box has a white underprint on
                // real cards: whole-card foil keeps a little of it there (none for Starlight, Parallel and Quarter
                // Century), and Parallel's film keeps off the name too.
                if (cardFoil > 0)
                {
                    float keep = 0.25;
                    if (cardFoil == 1) { c = PrismaticFoil(c, p, k * lerp(0.75, 1, art), 2.6, 140); keep = 0; }
                    else if (cardFoil == 2) c = Platinum(c, p, k);
                    else if (cardFoil == 3) { c = SecretFoil(c, p, k * lerp(0.75, 1, art), 0); keep = 0.15; }
                    else if (cardFoil == 4) c = Starfoil(c, p, k);
                    else if (cardFoil == 5) c = Mosaic(c, p, k, art);
                    else if (cardFoil == 6) c = Shatter(c, p, k);
                    else if (cardFoil == 7) { c = Parallel(c, p, k); keep = 0; c = lerp(c, printed, InRect(uv, _NameRect)); }
                    else if (cardFoil == 8) { c = SecretFoil(c, p, k * lerp(0.75, 1, art), 0.65); keep = 0.15; }
                    else if (cardFoil == 9) { c = SecretFoil(c, p, k * lerp(0.8, 1, art), 0); keep = 0; }
                    c = lerp(c, lerp(printed, c, keep), text);
                    if (cardFoil == 9 && _TextRect.w - _TextRect.y > 0.02) // not on a letterboxed picture (no text box)
                    {
                        // Quarter Century: a translucent "25" in the text box's empty lower right (above the ATK / DEF line,
                        // clear of the rules), catching the light.
                        float4 t = _TextRect;
                        float height = (t.w - t.y) * 0.32;
                        float2 q = (p - float2((t.z - 0.085) * ASPECT, t.y + 0.072)) / height;
                        float mark = TwentyFive(q, max(onePixel.y / height, 1e-4) * 0.8);
                        float near = pow(sH.z, 30);
                        c = lerp(c, c * float3(0.86, 0.87, 0.92), mark) + SILVER * mark * 0.1 * near;
                    }
                }

                if (artFoil > 0)
                {
                    float3 a = c;
                    if (artFoil == 1) a = Holo(c, p, k);
                    else if (artFoil == 2) a = SecretFoil(c, p, k, 0);
                    else if (artFoil == 3) a = PrismaticFoil(c, p, k, 1, 90);
                    else if (artFoil == 4) // Ghost: muted to silver, with a hint of depth and a faint rainbow
                    {
                        float2 slope = 0;
                        float3 deep = c;
                        if (art > 0.001)
                        {
                            slope = InkSlope(uv, classicMip + 0.5);
                            // A second, deeper image a little behind, shifted by the light: the hologram's depth.
                            deep = tex2Dlod(_MainTex, float4(uv + (_Light.xy - 0.5) * 0.012 - sV.xy * 0.09, 0, classicMip)).rgb * i.color.rgb;
                        }
                        float l = max(Luma(c), Luma(deep) * 0.85);
                        float3 ghost = SILVER * (0.2 + 0.85 * l) + Emboss(slope) * 0.8;
                        a = Foil(lerp(c, saturate(ghost), saturate(0.85 * k)), 0.6, lerp(SILVER, Rainbow(p, 1.6), 0.45), slope * 0.6, k);
                    }
                    else if (artFoil == 5) // Ultimate: strongly embossed, in desaturated gold-silver, its centre highlighted
                    {
                        // Low-saturation silver (about 40% of the art's colour), a soft relief from a well-blurred mip, its
                        // shadows kept shallow so the edges don't get dark halos.
                        float2 slope = 0;
                        if (art > 0.001) slope = InkSlope(uv, classicMip + 2) * 0.9;
                        float2 center = (_ArtRect.xy + _ArtRect.zw) * 0.5;
                        float glow = exp(-dot(uv - center, uv - center) * 18);
                        float l = Luma(c);
                        float3 muted = lerp(l.xxx, c, 0.4) * lerp(float3(1, 1, 1), GOLD_SILVER / 0.85, 0.35);
                        float3 metal = lerp(c, 0.08 + 0.95 * muted, saturate(k));
                        float relief = Emboss(slope);
                        float3 embossed = metal + max(relief, -0.06) * 0.9 + glow * 0.08;
                        a = Foil(saturate(embossed), 0.5, lerp(SILVER, GOLD_SILVER, 0.4), slope * 0.7, k);
                    }
                    else if (artFoil == 6) a = Fingerprint(c, p, ridges, k);
                    else if (artFoil == 7) a = Hieroglyphs(c, p, k);
                    c = lerp(c, a, art);
                }

                if (_Outline > 0.5) // Premium Gold: gold highlight around the figure in the middle of the art
                {
                    float2 center = (_ArtRect.xy + _ArtRect.zw) * 0.5;
                    float middle = exp(-dot((uv - center) * float2(1.6, 1.3), (uv - center) * float2(1.6, 1.3)) * 14) * art;
                    if (middle > 0.002)
                    {
                        float2 t = ART_STEP;
                        float2 grad = float2(
                            Luma(tex2Dlod(_MainTex, float4(uv + float2(t.x, 0), 0, classicMip)).rgb) - Luma(tex2Dlod(_MainTex, float4(uv - float2(t.x, 0), 0, classicMip)).rgb),
                            Luma(tex2Dlod(_MainTex, float4(uv + float2(0, t.y), 0, classicMip)).rgb) - Luma(tex2Dlod(_MainTex, float4(uv - float2(0, t.y), 0, classicMip)).rgb));
                        float edge = length(grad);
                        // A raised gold line: its side toward the light lit, the far side in shade.
                        float lit = Emboss(grad * 4);
                        float3 gold = Metal(GOLD, PALE_GOLD, 0.5 + 0.9 * MetalShine(p, 12) + 1.2 * lit);
                        c = lerp(c, gold, smoothstep(0.12, 0.3, edge) * middle * saturate(k * 1.1));
                    }
                }

                if (borderFoil > 0)
                {
                    float m = BorderMask(uv);
                    if (borderFoil == 1)
                    {
                        // Gold: the border, the art frame and the text box's frame, as polished gold reflecting the room.
                        m = saturate(m + TextBoxEdge(uv));
                        float bands = 0.5 + 0.5 * sin(((p.x - BAND_SPEED * sLP.x - sTurn.x) * 0.8 + (p.y - BAND_SPEED * sLP.y - sTurn.y)) * 26);
                        float3 gold = Metal(GOLD, PALE_GOLD, 0.35 + 0.8 * MetalShine(p, 11) + 0.35 * bands + 0.15 * sSmall);
                        c = lerp(c, gold, m * saturate(0.65 + 0.5 * k));
                    }
                    else if (borderFoil == 2) c = lerp(c, Fingerprint(c, p, ridges, k * 1.2), m);
                    else if (m > 0.001) // embossed (Ultimate): the frame raised and turned silver-grey, never darker than printed
                    {
                        float2 slope = InkSlope(uv, classicMip + 2) * 0.8;
                        float3 frame = (0.3 + 0.75 * Luma(c)) * STEEL * (0.9 + 0.3 * MetalShine(p, 9) * saturate(k));
                        frame += max(Emboss(slope), -0.05) * 0.7;
                        frame *= max(Luma(c) / max(Luma(frame), 1e-3), 1); // never below the printed border's brightness
                        c = lerp(c, saturate(frame), m * saturate(k * 1.2));
                    }
                }

                // Platinum and Platinum Secret: the border is bright silver (brighter at board size, where it's the tell).
                if (cardFoil == 2 || cardFoil == 8)
                {
                    float m = BorderMask(uv);
                    float3 silver = Metal(SILVER, float3(1, 1, 1), 0.75 + 0.6 * MetalShine(p, 10) + 0.25 * sSmall);
                    c = lerp(c, silver, m * saturate(k) * lerp(0.45, 0.85, sSmall));
                }

                if (nameFoil > 0)
                {
                    float region = InRect(uv, _NameRect);
                    float3 metal = NameMetal(nameFoil, p, k);
                    if (region > 0.001)
                    {
                        float m = saturate(Letters(tex.rgb) * (1 + 0.8 * sSmall)) * region;
                        // A dark rim a pixel wide round the letters, so a metal name reads on an orange or tan frame.
                        float4 o = float4(onePixel, -onePixel);
                        float3 tint = i.color.rgb;
                        float around = max(
                            max(Letters(tex2Dlod(_MainTex, float4(uv + float2(o.x, 0), 0, mip)).rgb * tint), Letters(tex2Dlod(_MainTex, float4(uv + float2(o.z, 0), 0, mip)).rgb * tint)),
                            max(Letters(tex2Dlod(_MainTex, float4(uv + float2(0, o.y), 0, mip)).rgb * tint), Letters(tex2Dlod(_MainTex, float4(uv + float2(0, o.w), 0, mip)).rgb * tint)));
                        c = lerp(c, c * 0.2, saturate((around - m) * (1 + 0.5 * sSmall)) * region * 0.85);
                        if (nameFoil == 4) metal += Emboss(InkSlope(uv, mip)) * 1.2;
                        c = lerp(c, saturate(metal), m);
                    }
                }

                if (_Stars > 0.5)
                {
                    // Attribute icon and level / rank stars: foil the colourful pixels (not the frame around them); on an
                    // Ultimate, raised and gold-silver. (Foil and the relief take no derivatives, so they run only there.)
                    float region = saturate(InRect(uv, _AttrRect) + InRect(uv, _StarsRect));
                    if (region > 0.001)
                    {
                        float colorful = saturate((max(tex.r, max(tex.g, tex.b)) - min(tex.r, min(tex.g, tex.b))) * 2.5) * Luma(tex.rgb);
                        if (nameFoil == 4)
                        {
                            float2 slope = InkSlope(uv, mip) * 1.6;
                            float3 raised = lerp(c, Luma(c) * GOLD_SILVER * 1.2, 0.55) + Emboss(slope) * 1.3;
                            c = lerp(c, saturate(raised), region * saturate(k * 1.2));
                        }
                        else
                        {
                            float3 glint = nameFoil == 2 ? GOLD : Rainbow(p, 3.0) * 0.8 + 0.2;
                            c = Foil(c, colorful * region * 0.8, glint, 0, k);
                            // The foil's own specular on the stars and the attribute, so they read as foiled.
                            float3 spec = saturate(lerp(float3(1, 1, 1), glint, 0.4) * colorful * region * k
                                * (0.12 * (1 - sHover) + 0.5 * pow(sH.z, 24)));
                            c = 1 - (1 - c) * (1 - spec);
                        }
                    }
                }

                #ifdef _WEAR
                // Damage takes the foil with it: corners, nicks, chips, scratches, dents, print lines and creases go on top.
                {
                    float3 before = c;
                    float w = lerp(_WearEdge.z, max(_WearEdge.z, 0.75 * fwidth(pc.y)), _WearAge.w);
                    // The corners' white paper core along the cut-back outline.
                    // (At least a pixel and a half wide, so a worn corner still shows on a board-size card.)
                    float coreW = max(r * 0.6, 1.5 * fwidth(pc.y));
                    c = lerp(c, WHITE_CORE, saturate(cut * 1.3) * (1 - smoothstep(0, coreW, inside + fuzz * 0.3 * r)) * 0.95);
                    if (fine > 0.001)
                    {
                        // Edge nicks: small bright white chips of the paper core, crowded at the corners and thinning out
                        // along the edges (not a haze round the card).
                        if (_WearEdge.y > 0)
                        {
                            float along = abs(pc.x - ASPECT * 0.5) > abs(pc.y - 0.5) * ASPECT ? pc.y : pc.x;
                            float cell = floor(along * 90 + _WearSeed.x * 97);
                            float density = _WearEdge.y * lerp(0.05, 0.9, pow(nearCorner, 0.7));
                            float nick = step(1 - density, Hash(float2(cell, 3.1)));
                            float depth = w * (0.5 + 1.1 * Hash(float2(cell, 7.7)));
                            float whiten = (1 - smoothstep(depth * 0.6, depth, inside)) * nick * fine;
                            c = lerp(c, WHITE_CORE, whiten);
                        }
                        float chip = saturate((wt.r - (1 - 0.3 * _WearEdge.w)) * 8) * step(inside, 1.5 * max(w, 0.004)) * step(0.001, _WearEdge.w);
                        c = lerp(c, PAPER, chip * fine);
                        // Scratches: lines of light inside the light's reflection, faintly dark elsewhere (a fine haze where
                        // they're too thin to see). No flash under Reduced motion.
                        float fadeS = saturate((6 - tpp) / 3) * fine;
                        float scratch = saturate((wt.g - (1 - _WearSurface.x)) / max(fwidth(wt.g), 0.02)) * step(0.001, _WearSurface.x) * fadeS;
                        float lobe = pow(sH.z, 14) * (0.4 + 0.6 * sHover);
                        float flash = _WearSeed.w > 0.5 ? 0 : pow(sH.z, 50) * sHover;
                        c = c * (1 - 0.1 * scratch * _WearSurface.y) + scratch * _WearSurface.y * (1.1 * lobe + 0.8 * flash);
                        c += 0.03 * _WearSurface.y * _WearSurface.x * (fine - fadeS) * art;
                        // Dents: small hollows that leave the colours alone and only bend the reflection, so they show
                        // only inside the light's lobe.
                        int dents = (int)round(_WearGloss.y);
                        if (dents > 0)
                        {
                            float2 slope = 0;
                            for (int n = 0; n < 3; n++)
                            {
                                if (n >= dents) break;
                                float2 at = (0.12 + 0.76 * Hash2(_WearSeed.xy * 173 + n * 7.13)) * float2(ASPECT, 1);
                                float rad = 0.018 + 0.022 * Hash(_WearSeed.yx * 91 + n * 3.7);
                                float2 d = pc - at;
                                slope += d / rad * exp(-dot(d, d) / (rad * rad)) * 0.25 * _WearGloss.z;
                            }
                            float bend = (pow(Facing(slope), 40) - pow(sH.z, 40)) * (0.4 + 0.6 * sHover) * fine;
                            c = c * (1 - 0.3 * saturate(-bend)) + 0.25 * saturate(bend);
                        }
                        // A print line: a faint streak through the foil (some foil copies have one, even at Mint).
                        if (_WearCenter.w > 0 && (artFoil > 0 || cardFoil > 0))
                        {
                            // At least a pixel wide, so it reads at detail size; its light core and a darker hairline beside it.
                            float width = max(0.0012, onePixel.x * 0.9);
                            float dx = uv.x - _WearCenter.z;
                            float streak = 1 - smoothstep(width * 0.5, width * 0.5 + onePixel.x, abs(dx));
                            float shade = 1 - smoothstep(width * 0.5, width * 0.5 + onePixel.x, abs(dx - width * 1.2));
                            float where = (cardFoil > 0 ? 1 : art) * _WearCenter.w * fine;
                            c = lerp(c, c * 0.7 + 0.24, streak * where);
                            c *= 1 - 0.15 * shade * where;
                        }
                    }
                    // A crease: a fold, its two faces tilted toward the line, so one side catches the light and the other
                    // falls into shade (which side depends on where the light is), and the ridge flashes inside the lobe.
                    if (_WearAge.z > 0)
                    {
                        float2 dir = float2(cos(_WearCrease.z), sin(_WearCrease.z));
                        float2 rel = pc - _WearCrease.xy;
                        float along = clamp(dot(rel, dir), -_WearCrease.w, _WearCrease.w);
                        float side = dot(rel, float2(-dir.y, dir.x));
                        float d = length(rel - dir * along) * (side < 0 ? -1 : 1);
                        float wc = max(0.004, fwidth(pc.y));
                        float ridge = 1 - smoothstep(0, wc, abs(d));
                        float fold = exp(-pow(d / max(0.012, 2 * wc), 2)) * _WearAge.z;
                        float2 slope = float2(-dir.y, dir.x) * (d < 0 ? -1 : 1) * fold * 0.5;
                        c *= 1 + clamp(Emboss(slope) * 0.9, -0.3, 0.3);
                        float bend = (pow(Facing(slope), 30) - pow(sH.z, 30)) * (0.4 + 0.6 * sHover);
                        c = c * (1 - 0.25 * saturate(-bend)) + 0.2 * saturate(bend);
                        c = lerp(c, PAPER, _WearAge.z * ridge * saturate((wt.r - 0.35) * 3));
                    }
                    c = Protect(before, c, free);
                }
                #endif

                // The text box stays within ±12% of the picture's brightness, whatever the layers above did to it (its
                // frame, a hair inside its edge, is left to the border foil). Two passing lights go over it afterwards on
                // purpose: the prized moment's sweep and the gloss (halved on the text box), both brief.
                {
                    float core = InRect(uv, _TextRect + float4(0.006, 0.006, -0.006, -0.006));
                    float l0 = Luma(tex.rgb), l = Luma(c);
                    c = lerp(c, c * clamp(l, 0.88 * l0, 1.12 * l0) / max(l, 1e-4), core);
                }

                #ifdef _PRIZE
                // A prized copy arriving: a band of light sweeps corner to corner (the wear stays under it).
                {
                    float t = _PrizeTime;
                    float s = dot(p - float2(0, 1), normalize(float2(1, -1.3))) / 1.13; // 0 top left .. 1 bottom right
                    float env = 1 - smoothstep(0.75, 1, t);
                    float h, wB, I;
                    if (_PrizeKind < 0.5)      { h = lerp(-0.2, 1.2, smoothstep(0, 0.6, t)); wB = 0.07;  I = 0.55; }
                    else if (_PrizeKind < 1.5) { h = lerp(-0.2, 1.2, smoothstep(0, 0.5, t)); wB = 0.035; I = 0.35; }
                    else                       { h = 0.5; wB = 0.25; I = 0.12; env = sin(PI * saturate(t)); }
                    I *= _PrizeColor.a;
                    float band = exp(-pow((s - h) / wB, 2)) * env * step(0, t);
                    float core = exp(-pow((s - h) / (wB * 0.2), 2)) * env * step(0, t);
                    float lift = (0.35 + 0.65 * Luma(c)) * (0.55 + 0.45 * art);
                    float3 tint = lerp(float3(1, 1, 1), _PrizeColor.rgb, 0.5);
                    c += (tint * band * I + core * 0.5 * step(_PrizeKind, 1.5)) * lift;
                    if (_PrizeKind < 0.5)
                    {
                        float2 cells = p * 28;
                        c += Hue(dot(p, float2(0.6, 0.8)) * 3.0 - h * 1.8) * step(0.93, Hash(floor(cells))) * band * CellFade(cells) * 0.6;
                        c += _PrizeColor.rgb * 0.05 * (1 - smoothstep(0.6, 1, t)) * step(0, t) * art;
                    }
                }
                #endif

                // The gloss every card has: a faint sheen and the light's reflection, only while the card is hovered or a
                // glint crosses it (never parked anywhere). Half as strong on the text box, so it stays readable. A played
                // card's is broad and weak where its gloss has dulled.
                float gloss = sGlare * (lerp(0.24, 0.05, dull) * pow(sH.z, lerp(120, 25, dull)) + 0.07 * pow(sH.z, 14)) * (1 - 0.5 * text)
                    * (1 - 0.5 * smoothstep(0.6, 0.95, Luma(c))); // a light picture doesn't wash out to white
                c = 1 - (1 - saturate(c)) * (1 - gloss);

                tex.rgb = c;
                // Rounded corners, antialiased over a pixel.
                tex.a *= saturate(0.5 - edgeDistance / edgeAA);
                #ifdef UNITY_UI_CLIP_RECT
                tex.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(tex.a - 0.001);
                #endif
                return tex;
            }
            ENDCG
        }
    }
}
