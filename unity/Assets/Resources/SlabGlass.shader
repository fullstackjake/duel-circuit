// A Gauge & Gilt slab's clear case, drawn over the card and label (see SlabView.cs, SlabGlass.cs and
// docs/dev/SLAB-LOOK-SPEC-2026-11-07.md section 4). Plastic is sold by its edges and reflections, not its tint: a dark outer
// rim, a bevel and its crest that turn with the light, a faint chromatic fringe, the walls' veil, the wells' walls and
// the card's contact shadow, the ridge between the wells, two glare bands and an inner reflection that travels further
// than they do (the depth), hairline scuffs that only show in the glare, and the hologram sticker on the label.
// One pass, premultiplied (Blend One OneMinusSrcAlpha), so it can lighten and darken at once: rgb is the light it adds
// (plus the sticker's print), alpha how much it darkens what's under it. No grab pass; the only texture is the sticker.
// Everything is measured in the case's layout pixels (origin at its centre, y up); hairlines are anti-aliased in screen
// pixels (fwidth), at least one wide, so the Full slab still reads at 0.62 scale.
Shader "YgoCampaign/SlabGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sticker masks (R dial, G monogram, B border, A hexagon)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _CaseSize ("Case size (px), corner radius, bevel width", Vector) = (360, 586, 12, 6)
        _CardRect ("Card (xMin yMin xMax yMax, px from the case centre)", Vector) = (-166, -205, 166, 279)
        _CardWell ("Card well", Vector) = (-170, -209, 170, 283)
        _LabelWell ("Label well", Vector) = (-170, -283, 170, -215)
        _LabelText ("Label text column (protected)", Vector) = (0, 0, 0, 0)
        _NameStrip ("Card name bar (protected)", Vector) = (0, 0, 0, 0)
        _StickerRect ("Sticker (zero: none)", Vector) = (0, 0, 0, 0)
        _Radii ("Card corner, card well, label well radius, crest inset", Vector) = (13.6, 17.6, 3, 1.5)
        _Bands ("Well wall band, contact fade, fringe on, bands B and inner on", Vector) = (2, 3, 1, 1)
        _Light ("Light (case uv xy from the bottom left, hover z)", Vector) = (0.3, 0.8, 0, 0)
        _Seed ("Scuff seed", Float) = 0
        _ScuffCount ("Scuffs", Float) = 7
        _StickerFlash ("Sticker flash 1..0", Float) = 0
        _Glint ("Perimeter glint phase 0..1 (-1 off)", Float) = -1
        _RidgeDark ("Ridge's dark line on", Float) = 1
        _Side ("The quad's margin round the case (px), the case side's offset xy", Vector) = (6, 3, -4, 0)

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
        Blend One OneMinusSrcAlpha
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _CaseSize, _CardRect, _CardWell, _LabelWell, _LabelText, _NameStrip, _StickerRect, _Radii, _Bands, _Light;
            float4 _Side;
            float _Seed, _ScuffCount, _StickerFlash, _Glint, _RidgeDark;

            #define TAU 6.2831853
            #define CREST float3(0.957, 0.973, 1.0)   // #F4F8FF
            #define GLARE float3(0.933, 0.957, 1.0)   // #EEF4FF
            #define GILT float3(0.788, 0.627, 0.290) // #C9A04A

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            // Dave Hoskins' hash without sine (as CardFoil.shader): stable on every GPU.
            float Hash(float n)
            {
                n = frac(n * 0.1031);
                n *= n + 33.33;
                n *= n + n;
                return frac(n);
            }

            // Signed distance to a rounded rect given as (xMin yMin xMax yMax), and the outward normal of its nearest edge.
            float Box(float2 p, float4 r, float radius, out float2 n)
            {
                float2 c = (r.xy + r.zw) * 0.5, h = (r.zw - r.xy) * 0.5;
                float2 d = p - c;
                float2 q = abs(d) - h + radius;
                float2 s = float2(d.x >= 0 ? 1 : -1, d.y >= 0 ? 1 : -1);
                n = (q.x > 0 && q.y > 0) ? normalize(q) * s : (q.x > q.y ? float2(s.x, 0) : float2(0, s.y));
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - radius;
            }

            float Inside(float2 p, float4 r) { return step(r.x, p.x) * step(p.x, r.z) * step(r.y, p.y) * step(p.y, r.w); }

            // A hairline of width w (layout px, at least one screen pixel) centred on d = 0.
            float Line(float d, float w, float aa)
            {
                float width = max(w, aa);
                return saturate((width * 0.5 + aa * 0.5 - abs(d)) / aa) * min(1, w / aa);
            }

            float3 Rainbow(float t, float sat)
            {
                float3 c = 0.5 + 0.5 * cos(TAU * (t + float3(0, 0.33, 0.67)));
                return lerp((float3)dot(c, float3(0.333, 0.333, 0.333)), c, sat);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 size = _CaseSize.xy;
                float2 p = (i.uv - 0.5) * (size + 2 * _Side.x); // the quad reaches past the case, for its side
                float2 uv = p / size + 0.5;                     // case uv
                float aa = max(max(fwidth(p.x), fwidth(p.y)), 1e-3); // layout px per screen px
                float2 L = _Light.xy - 0.5;
                float2 L2 = L / max(length(L), 1e-3);
                float h = _Light.z;

                float3 add = 0;
                float keep = 1; // the product of (1 - each darkening)

                // The case outline: outside it, nothing.
                float2 n;
                float sdCase = Box(p, float4(-size * 0.5, size * 0.5), _CaseSize.z, n);
                float coverage = saturate(0.5 - sdCase / aa);
                float lit = saturate(dot(n, L2)), unlit = saturate(dot(-n, L2));
                float bevel = _CaseSize.w;

                // 1. Outer rim.
                keep *= 1 - 0.55 * Line(sdCase + 0.5, 1, aa);
                // 2. Bevel face, fading inward.
                float face = saturate(1 + sdCase / bevel) * step(sdCase, 0);
                add += face * (0.05 + 0.20 * lit);
                keep *= 1 - 0.18 * unlit * face;
                // 3. Crest.
                add += CREST * Line(sdCase + _Radii.w, 1, aa) * (0.30 + 0.45 * lit + 0.15 * h);
                // 4. Chromatic fringe at the bevel's inner edge (lit side only).
                if (_Bands.z > 0.5)
                {
                    float k = (0.5 + 0.5 * h) * lit;
                    add += float3(0.10, 0.02, 0) * Line(sdCase + bevel - 0.5, 1, aa) * k;
                    add += float3(0, 0.03, 0.12) * Line(sdCase + bevel + 0.5, 1, aa) * k;
                }

                // The wells.
                float2 nw, nl, nc;
                float sdWell = Box(p, _CardWell, _Radii.y, nw);
                float sdLabel = Box(p, _LabelWell, _Radii.z, nl);
                float sdCard = Box(p, _CardRect, _Radii.x, nc);
                float inWells = saturate(0.5 - min(sdWell, sdLabel) / aa);

                // 5. Wall veil: the case's walls, outside both wells, a little brighter at the top.
                float grad = lerp(0.85, 1.15, uv.y);
                float wall = (1 - inWells) * step(sdCase, 0) * (1 - face);
                keep *= 1 - 0.16 * grad * wall;
                add += (float3(0.05, 0.06, 0.075) * grad + 0.04 * h) * wall;

                // 6. Well walls (refraction): a band just outside each well, lit where it faces the light.
                float band = _Bands.x;
                float wellBand = saturate(1 - sdWell / band) * step(0, sdWell);
                float labelBand = saturate(1 - sdLabel / band) * step(0, sdLabel);
                float faceW = saturate(dot(-nw, L2)), shadeW = saturate(dot(nw, L2));
                float faceL = saturate(dot(-nl, L2)), shadeL = saturate(dot(nl, L2));
                add += float3(0.85, 0.95, 1.0) * 0.06 * (wellBand * faceW + labelBand * faceL);
                keep *= 1 - 0.10 * wellBand * shadeW;
                keep *= 1 - 0.10 * labelBand * shadeL;
                if (_Bands.z > 0.5)
                {
                    float f = 0.5 * (0.5 + 0.5 * h);
                    add += float3(0.10, 0.02, 0) * Line(sdWell - 0.5, 1, aa) * faceW * f;
                    add += float3(0, 0.03, 0.12) * Line(sdWell - 1.5, 1, aa) * faceW * f;
                }

                // 7. Contact shadow just inside the card's edge, darkest on the edges away from the light.
                float contact = saturate(1 + sdCard / _Bands.y) * step(sdCard, 0);
                keep *= 1 - 0.30 * contact * (0.2 + 0.8 * saturate(dot(nc, L2)));

                // 8. The ridge between the wells: a light line over a dark one at its top.
                float ridgeTop = _CardWell.y, ridgeIn = step(_CardWell.x, p.x) * step(p.x, _CardWell.z);
                add += Line(p.y - (ridgeTop - 0.5), 1, aa) * ridgeIn * (0.25 + 0.20 * saturate(L2.y));
                keep *= 1 - 0.35 * _RidgeDark * Line(p.y - (ridgeTop - 1.5), 1, aa) * ridgeIn;

                // 9-11. Glare: two bands across the case, and the wells' inner reflection travelling further (depth).
                float2 nb = float2(0.819, -0.573); // normalize(1, -0.7)
                float q = dot(p, nb);
                float2 lp = L * size;
                float lpq = dot(lp, nb), w = size.x;
                float cA = 0.55 * lpq, wA = 0.11 * w;
                float glareA = smoothstep(wA, 0.4 * wA, abs(q - cA)) * (0.06 + 0.08 * h);
                float glare = glareA;
                if (_Bands.w > 0.5)
                {
                    float wB = 0.025 * w;
                    glare += 0.6 * smoothstep(wB, 0.4 * wB, abs(q - cA - 0.24 * w)) * (0.06 + 0.08 * h);
                    float cI = 0.75 * lpq + 0.03 * w;
                    glare += 0.45 * smoothstep(wA, 0.4 * wA, abs(q - cI)) * (0.06 + 0.08 * h) * inWells;
                }
                add += GLARE * glare;

                // 12. Scuffs: hairlines fixed to the case, seen only where the glare or a lit bevel crosses them.
                float scuffLight = glareA / max(0.06 + 0.08 * h, 1e-3) + 0.5 * face * lit;
                if (scuffLight > 0.01)
                {
                    float baseAngle = Hash(_Seed) * 3.14159;
                    float scuffs = 0;
                    for (int k = 0; k < 7; k++)
                    {
                        if (k >= _ScuffCount) break;
                        float s = _Seed + k * 7.31;
                        float2 mid = (float2(Hash(s + 1.1), Hash(s + 2.3)) - 0.5) * size * 0.9;
                        if (Inside(mid, _LabelText) > 0.5) continue;
                        float angle = baseAngle + (Hash(s + 3.7) - 0.5) * 0.87; // ±25°
                        float len = lerp(0.06, 0.22, Hash(s + 4.9)) * w * 0.5;
                        float2 dir = float2(cos(angle), sin(angle));
                        float2 d = p - mid;
                        float along = clamp(dot(d, dir), -len, len);
                        float dist = length(d - dir * along);
                        scuffs = max(scuffs, Line(dist, 0.8, aa) * (1 - smoothstep(0.7 * len, len, abs(dot(d, dir)))));
                    }
                    add += 0.35 * scuffs * saturate(scuffLight);
                }

                // 14. The perimeter glint (the reveal of a 10): a bright segment running clockwise round the crest.
                if (_Glint >= 0)
                {
                    float around = frac(0.25 - atan2(p.y / size.y, p.x / size.x) / TAU);
                    float gap = abs(frac(around - _Glint + 0.5) - 0.5);
                    add += 0.5 * CREST * Line(sdCase + _Radii.w, 1.5, aa) * smoothstep(0.09, 0.02, gap);
                }

                // Never hide the name or the grade: inside them, at most a little light and shade; over the rest of the card, a cap.
                float protect = max(Inside(p, _NameStrip), Inside(p, _LabelText));
                float onCard = Inside(p, _CardRect);
                add = protect > 0.5 ? min(add, 0.05) : onCard > 0.5 ? min(add, 0.14) : add;
                keep = protect > 0.5 ? max(keep, 0.90) : keep;

                // 13. The sticker, printed under the glass effects.
                float3 rgb = add;
                float alpha = 1 - keep;
                float2 sMin = _StickerRect.xy, sMax = _StickerRect.zw;
                float2 st = (p - sMin) / max(sMax - sMin, 1e-3);
                float2 dx = ddx(st), dy = ddy(st);
                if (sMax.x > sMin.x && st.x >= 0 && st.x <= 1 && st.y >= 0 && st.y <= 1)
                {
                    float4 m = tex2Dgrad(_MainTex, st, dx, dy);
                    float2 centre = ((sMin + sMax) * 0.5) / size + 0.5;
                    float dl = length(_Light.xy - centre);
                    float lobe = 0.15 + (0.5 + 0.5 * h) * pow(saturate(1 - dl / 0.6), 2);
                    float sat = lerp(0.45, 0.75, h);
                    lobe = lerp(lobe, 1, _StickerFlash);
                    sat = lerp(sat, 1, _StickerFlash);
                    float2 c = (st - 0.5) * float2(112, 128) / 64; // circumradius 1
                    float r = max(abs(c.x), 0.5 * abs(c.x) + 0.866 * abs(c.y)) / 0.866;
                    float3 col = (0.62 + 0.25 * lobe) * float3(0.92, 0.93, 0.96);
                    col = lerp(col, Rainbow(frac(2.2 * r + 1.6 * _Light.x + 1.1 * _Light.y), sat) * (0.25 + 0.75 * lobe), m.r);
                    // The monogram is inked, not lit: dark against the silver at rest (so "G&G" reads at 34x39), its
                    // colour flashing in as the light comes near.
                    float3 mono = Rainbow(frac(2.2 * st.x - 1.4 * st.y + 1.6 * _Light.x + 1.1 * _Light.y + 0.5), sat) * (0.22 + 0.78 * lobe * lobe);
                    col = lerp(col, mono, m.g);
                    col = lerp(col, GILT * (0.8 + 0.4 * lobe), m.b);
                    float a = m.a;
                    // The print under the glass: the glass darkens it like anything else, its light adds on top.
                    rgb = col * a * keep + add;
                    alpha = 1 - (1 - a) * keep;
                }

                fixed4 o = fixed4(rgb, alpha) * coverage;
                // The case's thickness: its side, offset down and right, shows only where the case doesn't cover it, with a
                // dark line at its outer edge.
                float2 nSide;
                float sdSide = Box(p - _Side.yz, float4(-size * 0.5, size * 0.5), _CaseSize.z, nSide);
                float side = saturate(0.5 - sdSide / aa) * (1 - coverage);
                float sideEdge = Line(sdSide + 0.5, 1, aa);
                float sideA = lerp(0.55, 1 - (1 - 0.55) * 0.5, sideEdge); // the 1 px rgba(0,0,0,0.5) outline over it
                o += fixed4(float3(0.42, 0.48, 0.55) * 0.55 * (1 - 0.5 * sideEdge), sideA) * side;
                o *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                o *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return o;
            }
            ENDCG
        }
    }
}
