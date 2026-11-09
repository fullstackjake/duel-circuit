using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A filled or outlined polygon for UI frames (the duelist panels' angular HUD shape). Points are in the rect's
    /// local space, around its pivot; a fill is drawn as a fan from the first point, so the shape should be convex.
    /// </summary>
    public sealed class UiPolygon : MaskableGraphic
    {
        private readonly List<Vector2> _points = new List<Vector2>();
        /// <summary>0 fills the shape; above 0, only its edge, this many pixels wide.</summary>
        public float Thickness;

        public void SetPoints(IEnumerable<Vector2> points)
        {
            _points.Clear();
            _points.AddRange(points);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_points.Count < 3) return;
            var c = (Color32)color;
            if (Thickness <= 0)
            {
                foreach (var p in _points) vh.AddVert(p, c, Vector2.zero);
                for (int i = 1; i < _points.Count - 1; i++) vh.AddTriangle(0, i, i + 1);
                return;
            }
            // An outline: a quad along each edge, mitred at the corners (inward offset along the corner's bisector).
            int n = _points.Count;
            var inner = new Vector2[n];
            var center = Vector2.zero;
            foreach (var p in _points) center += p;
            center /= n;
            for (int i = 0; i < n; i++)
            {
                var prev = _points[(i + n - 1) % n];
                var next = _points[(i + 1) % n];
                var p = _points[i];
                var e1 = (p - prev).normalized;
                var e2 = (next - p).normalized;
                var n1 = new Vector2(-e1.y, e1.x);
                var n2 = new Vector2(-e2.y, e2.x);
                // Normals that point inward (toward the centroid).
                if (Vector2.Dot(n1, center - p) < 0) n1 = -n1;
                if (Vector2.Dot(n2, center - p) < 0) n2 = -n2;
                var bisector = (n1 + n2).normalized;
                float cos = Mathf.Max(0.35f, Vector2.Dot(bisector, n1));
                inner[i] = p + bisector * (Thickness / cos);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int v = vh.currentVertCount;
                vh.AddVert(_points[i], c, Vector2.zero);
                vh.AddVert(_points[j], c, Vector2.zero);
                vh.AddVert(inner[j], c, Vector2.zero);
                vh.AddVert(inner[i], c, Vector2.zero);
                vh.AddTriangle(v, v + 1, v + 2);
                vh.AddTriangle(v, v + 2, v + 3);
            }
        }

        /// <summary>A rectangle with cut corners: each corner's cut size, clockwise from the top left (0 = square).</summary>
        public static Vector2[] Chamfered(float width, float height, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            float l = -width / 2, r = width / 2, t = height / 2, b = -height / 2;
            var pts = new List<Vector2>();
            void Corner(Vector2 at, Vector2 a, Vector2 b2, float cut)
            {
                if (cut <= 0) pts.Add(at);
                else { pts.Add(at + a * cut); pts.Add(at + b2 * cut); }
            }
            Corner(new Vector2(l, t), Vector2.down, Vector2.right, topLeft);
            Corner(new Vector2(r, t), Vector2.left, Vector2.down, topRight);
            Corner(new Vector2(r, b), Vector2.up, Vector2.left, bottomRight);
            Corner(new Vector2(l, b), Vector2.right, Vector2.up, bottomLeft);
            return pts.ToArray();
        }
    }

    /// <summary>Rounded-rectangle sprites drawn in code, nine-sliced so one texture fits any size (the glass zones).</summary>
    public static class HudShapes
    {
        private static Sprite? _fill, _edge, _soft, _circle;

        /// <summary>A filled circle (round masks: the tag partner's portrait chip).</summary>
        public static Sprite Circle => _circle ??= Rounded(24, 0, sliced: false);

        /// <summary>A rounded rectangle, filled.</summary>
        public static Sprite RoundedFill => _fill ??= Rounded(8, 0);
        /// <summary>A rounded rectangle's edge, about 2 pixels wide.</summary>
        public static Sprite RoundedEdge => _edge ??= Rounded(8, 2.2f);
        /// <summary>A soft glow around a rounded rectangle (fades out over its border), for the zones' outer light.</summary>
        public static Sprite RoundedGlow => _soft ??= Glow(8, 10);

        private static Sprite Rounded(float radius, float edge, bool sliced = true)
        {
            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Distance(x + 0.5f, y + 0.5f, size, radius);
                // Inside (d < 0) is filled; an edge keeps only the band within `edge` of the outline.
                float a = Mathf.Clamp01(0.5f - d);
                if (edge > 0) a *= Mathf.Clamp01(d + edge + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            float border = sliced ? radius + 4 : 0;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite Glow(float radius, float spread)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float inset = spread;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance from a rounded rectangle inset by `spread`: light outside it fades over `spread` pixels.
                float d = Distance(x + 0.5f - inset, y + 0.5f - inset, size - 2 * inset, radius);
                float a = d <= 0 ? 0 : Mathf.Pow(Mathf.Clamp01(1 - d / spread), 2);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            float border = inset + radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>Signed distance from a point to a size x size rounded rectangle's outline (negative inside).</summary>
        private static float Distance(float x, float y, float size, float radius)
        {
            float half = size / 2;
            float qx = Mathf.Abs(x - half) - (half - radius), qy = Mathf.Abs(y - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }
    }
}
