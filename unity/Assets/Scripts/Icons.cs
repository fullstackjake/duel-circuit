using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Action icons drawn in code (no image files): Summon, Special Summon, Set, Activate, Attack and the card positions.
    /// <see cref="Disc"/> puts one on a round button with its label underneath, Master Duel style.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>The icon for a verb or position label ("Summon", "Set", "Face-up Defense Position"...).</summary>
        public static Texture2D For(string verb)
        {
            var key = KeyOf(verb);
            if (Cache.TryGetValue(key, out var cached)) return cached;
            Func<float, float, float> shape = key switch
            {
                "summon" => (x, y) => Max(Arrow(x, y + 0.1f, 1), Platform(x, y)),
                "special" => (x, y) => Max(Chevron(x, y - 0.25f), Chevron(x, y + 0.15f), Platform(x, y)),
                "set" => (x, y) => Max(CardOutline(x, y + 0.05f, 0.62f, 0.42f), Ring(x, y + 0.05f, 0.2f, 0.06f)),
                "activate" => (x, y) => Max(Burst(x, y - 0.12f), Platform(x, y)),
                "attack" => Sword,
                "up-attack" => (x, y) => Box(x, y, 0.4f, 0.62f),
                "up-defense" => (x, y) => Box(x, y, 0.62f, 0.4f),
                "down-defense" => (x, y) => Max(CardOutline(x, y, 0.62f, 0.4f), Ring(x, y, 0.18f, 0.06f)),
                "down-attack" => (x, y) => Max(CardOutline(x, y, 0.4f, 0.62f), Ring(x, y, 0.18f, 0.06f)),
                "flip" => (x, y) => Max(CardOutline(x, y, 0.4f, 0.62f), Box(x - 0.2f, y, 0.2f, 0.62f) * 0.8f),
                "star" or "rank" => Star,
                "crown" => Crown,
                "hand" => (x, y) => Max(CardOutline(x - 0.3f, y, 0.3f, 0.45f), CardOutline(x, y + 0.08f, 0.3f, 0.45f), CardOutline(x + 0.3f, y, 0.3f, 0.45f)),
                "search" => (x, y) => Max(Ring(x + 0.15f, y - 0.15f, 0.38f, 0.09f), Edge(Mathf.Abs((x - y) * 0.7071f) - 0.09f) * (x + y < -0.55f && x + y > -1.5f ? 1 : 0)),
                "removal" => (x, y) => Max(Edge(Mathf.Abs((x - y) * 0.7071f) - 0.12f), Edge(Mathf.Abs((x + y) * 0.7071f) - 0.12f)) * (Mathf.Abs(x) < 0.7f && Mathf.Abs(y) < 0.7f ? 1 : 0),
                "negate" => (x, y) => Max(Ring(x, y, 0.62f, 0.1f), Edge(Mathf.Abs((x + y) * 0.7071f) - 0.1f) * (x * x + y * y < 0.4f ? 1 : 0)),
                "lock" => Lock,
                // Cancel: a thin X. View: an eye (an almond outline and a pupil).
                "cancel" => (x, y) => Max(Edge(Mathf.Abs((x - y) * 0.7071f) - 0.07f), Edge(Mathf.Abs((x + y) * 0.7071f) - 0.07f)) * (Mathf.Abs(x) < 0.5f && Mathf.Abs(y) < 0.5f ? 1 : 0),
                // A bold chevron (the attack arrow's links), pointing up.
                "chevron" => (x, y) => Edge(Mathf.Abs(y - (0.3f - Mathf.Abs(x) * 0.85f)) - 0.17f) * (Mathf.Abs(x) < 0.7f ? 1 : 0),
                // The phases: Draw (a card with an arrow down), Standby (an hourglass), Main (two cards), End (an exit).
                "draw" => (x, y) => Max(CardOutline(x - 0.18f, y + 0.12f, 0.36f, 0.5f), Arrow(-x + 0.42f, -(y + 0.1f), 0.6f) * (x > 0.1f ? 1 : 0)),
                "standby" => (x, y) => Max(Edge(Mathf.Abs(x) - 0.55f * Mathf.Abs(y) - 0.06f) * (Mathf.Abs(y) < 0.62f ? 1 : 0), Box(x, y - 0.66f, 0.5f, 0.06f), Box(x, y + 0.66f, 0.5f, 0.06f)),
                "main" => (x, y) => Max(CardOutline(x - 0.2f, y - 0.05f, 0.34f, 0.5f), CardOutline(x + 0.2f, y + 0.05f, 0.34f, 0.5f)),
                "end" => (x, y) => Max(CardOutline(x - 0.2f, y, 0.36f, 0.56f) * (x < 0.05f ? 1 : 0), Box(x + 0.2f, y, 0.38f, 0.08f), Edge(Mathf.Max(Mathf.Abs(y) - (0.62f - x) * 0.9f, 0.3f - x)) * (x < 0.68f ? 1 : 0)),
                "view" => (x, y) => Max(Ring(x, y * 1.9f, 0.62f, 0.09f), Edge(Mathf.Sqrt(x * x + y * y) - 0.2f)),
                // Details: an "i" in a ring.
                "details" => (x, y) => Max(Ring(x, y, 0.62f, 0.09f), Edge(Mathf.Sqrt(x * x + (y - 0.3f) * (y - 0.3f)) - 0.1f), Box(x, y + 0.14f, 0.08f, 0.24f)),
                // A gear: a ring with eight teeth.
                "gear" => (x, y) => Max(Ring(x, y, 0.42f, 0.13f),
                    Mathf.Cos(Mathf.Atan2(y, x) * 8) > 0.35f ? Edge(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.62f) - 0.14f) : 0),
                // A play triangle pointing right: the "you can act on this" mark.
                "play" => (x, y) => Edge(Mathf.Max(-0.5f - x, Mathf.Abs(y) * 1.3f + x - 0.5f)),
                "shield" => Shield,
                // LP changes: a coin (a cost paid) and a heart (LP gained).
                "coin" => (x, y) => Max(Ring(x, y, 0.58f, 0.1f), Box(x, y, 0.08f, 0.3f), Box(x, y, 0.22f, 0.07f)),
                "heart" => (x, y) => Edge(Mathf.Min(Mathf.Min(Mathf.Sqrt((x + 0.28f) * (x + 0.28f) + (y + 0.2f) * (y + 0.2f)) - 0.33f,
                    Mathf.Sqrt((x - 0.28f) * (x - 0.28f) + (y + 0.2f) * (y + 0.2f)) - 0.33f), Mathf.Abs(x) + (y + 0.05f) * 0.95f - 0.62f + Mathf.Max(0, -y - 0.05f) * 2)),
                // Menu: three bars. History: a clock.
                "menu" => (x, y) => Max(Box(x, y - 0.42f, 0.62f, 0.08f), Box(x, y, 0.62f, 0.08f), Box(x, y + 0.42f, 0.62f, 0.08f)),
                "history" => (x, y) => Max(Ring(x, y, 0.62f, 0.09f), Box(x, y + 0.2f, 0.07f, 0.28f), Box(x + 0.17f, y, 0.24f, 0.07f)),
                "link" => (x, y) => Max(CardOutline(x, y, 0.62f, 0.62f), Edge(Mathf.Abs(x - y) * 0.7071f - 0.08f) * Box(x, y, 0.5f, 0.5f)),
                // Empty zones and piles (Link Evolution's marks). Field Zone: two peaks and a sun. Extra Monster Zone /
                // Extra Deck: a hexagon around a dot. GY: a grave cross. Banished: a card with an X through it.
                "zone-field" => (x, y) => Max(Edge(Mathf.Max(y - (0.36f - Mathf.Abs(x + 0.22f) * 1.2f), -0.42f - y)),
                    Edge(Mathf.Max(y - (0.1f - Mathf.Abs(x - 0.36f) * 1.2f), -0.42f - y)), Ring(x - 0.42f, y - 0.42f, 0.12f, 0.05f)),
                "zone-extra" => (x, y) => Max(Edge(Mathf.Abs(Mathf.Max(Mathf.Abs(x) * 0.866f + Mathf.Abs(y) * 0.5f, Mathf.Abs(y)) - 0.55f) - 0.06f),
                    Edge(Mathf.Sqrt(x * x + y * y) - 0.14f)),
                "zone-gy" => (x, y) => Max(Box(x, y - 0.08f, 0.09f, 0.62f), Box(x, y + 0.2f, 0.36f, 0.09f)),
                "zone-banish" => (x, y) => Max(CardOutline(x, y, 0.42f, 0.58f),
                    Max(Edge(Mathf.Abs((x - y * 0.72f) * 0.8f) - 0.06f), Edge(Mathf.Abs((x + y * 0.72f) * 0.8f) - 0.06f)) * Box(x, y, 0.3f, 0.45f)),
                "zone-deck" => (x, y) => Max(CardOutline(x - 0.1f, y - 0.08f, 0.36f, 0.5f), CardOutline(x + 0.1f, y + 0.08f, 0.36f, 0.5f)),
                _ => (x, y) => Ring(x, y, 0.3f, 0.12f),
            };
            return Cache[key] = Make(shape);
        }

        private static string KeyOf(string verb)
        {
            var v = verb.ToLowerInvariant();
            if (v is "star" or "rank" or "shield" or "link" or "crown" or "lock" or "hand" or "search" or "removal" or "negate" or "play" or "gear" or "menu" or "history" or "coin" or "heart"
                or "cancel" or "view" or "details" or "chevron" or "draw" or "standby" or "end") return v;
            if (v.StartsWith("zone-")) return v; // the empty-zone and pile marks
            if (v.StartsWith("main")) return "main";
            if (v == "battle") return "attack";
            if (v.Contains("face-up attack") || v == "to attack") return "up-attack";
            if (v.Contains("face-up defense") || v == "to defense") return "up-defense";
            if (v.Contains("face-down defense")) return "down-defense";
            if (v.Contains("face-down attack")) return "down-attack";
            if (v.Contains("flip")) return "flip";
            if (v.Contains("special")) return "special";
            if (v.Contains("summon")) return "summon";
            if (v == "set" || v.StartsWith("set ")) return "set";
            if (v.Contains("attack")) return "attack";
            if (v.Contains("activate") || v.StartsWith("effect")) return "activate";
            return "other";
        }

        /// <summary>
        /// A round icon button with its label underneath (e.g. "Summon" over a hand card). The disc glows gold for the
        /// main choice. Returns the disc.
        /// </summary>
        public static RectTransform Disc(Transform parent, Vector2 center, string label, Action onClick, float size = 92, bool primary = false)
        {
            var disc = Ui.Placed($"Disc {label}", parent, center, new Vector2(size, size));
            // What's drawn grows on hover (a child of its own); what's clicked doesn't: the hit area never moves or grows.
            var visual = Ui.Stretched("Visual", disc, Vector2.zero, Vector2.one);
            var glow = Ui.Placed("Glow", visual, Vector2.zero, new Vector2(size * 1.5f, size * 1.5f)).gameObject.AddComponent<RawImage>();
            glow.texture = BoardFx.Textures.Glow;
            glow.color = primary ? new Color(1f, 0.85f, 0.3f, 0.45f) : new Color(0.3f, 0.6f, 1f, 0.35f);
            glow.raycastTarget = false;
            var face = Ui.Placed("Face", visual, Vector2.zero, new Vector2(size * 0.86f, size * 0.86f)).gameObject.AddComponent<RawImage>();
            face.texture = Round;
            face.color = primary ? Ui.Hex("#2C5FB0") : Ui.Hex("#1A2E57");
            face.raycastTarget = false;
            var ring = Ui.Placed("Ring", visual, Vector2.zero, new Vector2(size * 1.08f, size * 1.08f)).gameObject.AddComponent<RawImage>();
            ring.texture = BoardFx.Textures.Ring;
            ring.color = primary ? Ui.Hex("#FFD24A") : Ui.Hex("#6FB6FF");
            ring.raycastTarget = false;
            var icon = Ui.Placed("Icon", visual, Vector2.zero, new Vector2(size * 0.62f, size * 0.62f)).gameObject.AddComponent<RawImage>();
            icon.texture = For(label);
            icon.raycastTarget = false;
            var text = Ui.Text(Ui.Placed("Label", disc, new Vector2(0, -size / 2 - 16), new Vector2(size + 90, 30)), label, 18, Color.white);
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;
            var shadow = text.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0, 0, 0, 0.9f);
            shadow.effectDistance = new Vector2(2, -2);
            // The hit area: the disc's face at rest, clear (the face above draws it).
            var fill = Ui.Placed("Fill", disc, Vector2.zero, new Vector2(size * 0.86f, size * 0.86f)).gameObject.AddComponent<RawImage>();
            fill.color = new Color(0, 0, 0, 0);
            fill.canvasRenderer.cullTransparentMesh = true;
            Ui.Clickable(fill.rectTransform, () => { Sounds.Play("click"); onClick(); },
                hover: () => visual.localScale = Vector3.one * 1.08f);
            fill.gameObject.AddComponent<HoverShrink>().Target = visual; // the hit area hears the pointer; the visual shrinks
            return disc;
        }

        /// <summary>
        /// Puts a hovered disc back to its size once the pointer leaves it, over about 100 ms (UiTheme.HoverOut, as every
        /// hover fades out; it used to ease back over half a second, even while still hovered).
        /// </summary>
        private sealed class HoverShrink : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            private bool _inside;
            /// <summary>What grows and shrinks (the disc's visual), not the hit area this sits on.</summary>
            public Transform? Target;

            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => _inside = true;
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => _inside = false;

            private void Update()
            {
                var t = Target != null ? Target : transform;
                float x = t.localScale.x;
                if (_inside || x <= 1.0001f) return;
                // From 1.08 back to 1 in HoverOut, at a steady rate.
                t.localScale = Vector3.one * Mathf.MoveTowards(x, 1, 0.08f * Time.unscaledDeltaTime / UiTheme.HoverOut);
            }
        }

        private static Texture2D? _round;
        /// <summary>A solid round disc.</summary>
        public static Texture2D Round => _round ??= Make((x, y) => Mathf.Clamp01((1 - Mathf.Sqrt(x * x + y * y)) * 40));

        // ------------------------------------------------------------------ Shapes (x, y in -1..1, y up; 1 = filled)

        private static float Max(params float[] v) { float m = 0; foreach (var f in v) m = Mathf.Max(m, f); return m; }
        private static float Edge(float d) => Mathf.Clamp01(0.5f - d * 48); // anti-aliased inside test for a signed distance

        private static float Box(float x, float y, float hw, float hh) => Edge(Mathf.Max(Mathf.Abs(x) - hw, Mathf.Abs(y) - hh));
        private static float CardOutline(float x, float y, float hw, float hh) =>
            Mathf.Clamp01(Box(x, y, hw, hh) - Box(x, y, hw - 0.1f, hh - 0.1f));
        private static float Ring(float x, float y, float r, float w) => Edge(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - r) - w);
        private static float Platform(float x, float y) => Box(x, y + 0.72f, 0.7f, 0.08f);

        private static float Arrow(float x, float y, float scale)
        {
            // A triangle head (apex at the top) on a stem.
            float head = Edge(Mathf.Max(0.12f - y, Mathf.Abs(x) - (0.78f - y) * 0.8f));
            float stem = Box(x, y + 0.12f, 0.15f, 0.26f);
            return Max(head, stem);
        }

        private static float Chevron(float x, float y)
        {
            float v = y - (0.35f - Mathf.Abs(x) * 0.9f);
            return Edge(Mathf.Abs(v) - 0.09f) * (Mathf.Abs(x) < 0.55f ? 1 : 0);
        }

        private static float Burst(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
            float spikes = 0.38f + 0.2f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4)), 3);
            return Edge(r - spikes);
        }

        private static float Star(float x, float y)
        {
            // A five-pointed star (Level, and dark for Rank): the radius runs from a tip to a valley and back.
            float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x) - Mathf.PI / 2;
            float t = Mathf.Repeat(a, 2 * Mathf.PI / 5) / (2 * Mathf.PI / 5);
            float radius = Mathf.Lerp(0.95f, 0.4f, 1 - Mathf.Abs(2 * t - 1));
            return Edge(r - radius);
        }

        private static float Crown(float x, float y)
        {
            // A band with three points, a jewel dot on each.
            float band = Box(x, y + 0.45f, 0.72f, 0.16f);
            float points = 0;
            foreach (var px in new[] { -0.6f, 0f, 0.6f })
                points = Mathf.Max(points, Edge(Mathf.Max(-0.32f - y, Mathf.Abs(x - px) - (0.5f - y) * 0.42f)) * (y < 0.55f ? 1 : 0));
            float jewels = Mathf.Max(Edge(Mathf.Sqrt((x + 0.6f) * (x + 0.6f) + (y - 0.62f) * (y - 0.62f)) - 0.1f),
                Edge(Mathf.Sqrt(x * x + (y - 0.62f) * (y - 0.62f)) - 0.1f), Edge(Mathf.Sqrt((x - 0.6f) * (x - 0.6f) + (y - 0.62f) * (y - 0.62f)) - 0.1f));
            return Max(band, points, jewels);
        }

        private static float Lock(float x, float y)
        {
            // A padlock: body and shackle.
            float body = Box(x, y + 0.25f, 0.5f, 0.4f);
            float r = Mathf.Sqrt(x * x + (y - 0.15f) * (y - 0.15f));
            float shackle = Edge(Mathf.Abs(r - 0.32f) - 0.08f) * (y > 0.15f ? 1 : 0) + Box(Mathf.Abs(x) - 0.32f, y - 0.05f, 0.08f, 0.12f);
            float hole = Edge(Mathf.Sqrt(x * x + (y + 0.2f) * (y + 0.2f)) - 0.09f);
            return Mathf.Clamp01(Max(body, Mathf.Clamp01(shackle)) - hole);
        }

        private static float Shield(float x, float y)
        {
            // A heater shield: straight top, sides curving to a point.
            float width = y > 0.2f ? 0.66f : 0.66f * Mathf.Sqrt(Mathf.Clamp01((y + 0.8f) / 1.0f));
            return Edge(Mathf.Max(Mathf.Abs(x) - width, Mathf.Max(y - 0.7f, -0.8f - y)));
        }

        private static float Sword(float x, float y)
        {
            // Pointing up and to the right: a long blade with a point, a cross-guard, a grip and a pommel.
            float u = (x + y) * 0.7071f, v = (y - x) * 0.7071f;
            float blade = Edge(Mathf.Max(Mathf.Abs(v) - 0.11f, Mathf.Abs(u - 0.18f) - 0.5f));
            float tip = Edge(Mathf.Max(Mathf.Abs(v) - (0.9f - u) * 0.55f, Mathf.Max(0.68f - u, u - 0.9f)));
            float guard = Box(u + 0.38f, v, 0.07f, 0.34f);
            float grip = Box(u + 0.6f, v, 0.16f, 0.07f);
            float pommel = Edge(Mathf.Sqrt((u + 0.82f) * (u + 0.82f) + v * v) - 0.11f);
            return Max(blade, tip, guard, grip, pommel);
        }

        private static Texture2D Make(Func<float, float, float> alpha)
        {
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var a = alpha((x + 0.5f) / size * 2 - 1, (y + 0.5f) / size * 2 - 1);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
