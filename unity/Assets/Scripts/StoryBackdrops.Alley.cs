using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Back Alley's backdrops (<see cref="UiTheme.BackAlley"/>: wet asphalt, brick from <see cref="AlleyMaterials.Brick"/>,
    /// warm bulbs, neon only on signs): the alley, the Basement Game's stairwell, Moth's van and No Limits' bus depot. Laid
    /// out as the main street's are (<see cref="Done"/>).
    /// </summary>
    public static partial class StoryBackdrops
    {
        private static readonly Color Bulb = H("#FFB960"), BulbCore = H("#FFE2A8"), Pink = H("#FF4FA3");

        /// <summary>The alley's brick over rows <paramref name="y0"/>-<paramref name="y1"/> at a brightness, darker going up into the night.</summary>
        private static void Brick(BackdropCanvas c, float y0, float y1, float brightness)
        {
            c.Tile(_brick!, _brickW, _brickH, 0, y0, Width, y1 - y0, 128, 64, new Color(brightness, brightness * 0.97f, brightness * 0.97f, 1));
            c.Shade(y0, y0 + (y1 - y0) * 0.55f, 0.4f, 1);
        }

        /// <summary>The wet ground: asphalt, and the lights above smeared down it.</summary>
        private static void WetGround(BackdropCanvas c, float y, params (float x, Color color)[] lights)
        {
            c.Band(y, Height, UiTheme.BackAlley.Panel, UiTheme.BackAlley.Deep);
            c.Grain(0, y, Width, Height - y, 0.2f, 3, 23);
            // Each light smeared down the wet ground, fading fast; a few puddles catching the sky.
            foreach (var (x, color) in lights) c.Rect(x - 9, y + 2, 18, 90, color, Dim(color, 0), 0.07f);
            foreach (var (px, py, w) in new[] { (180f, y + 40, 120f), (520f, y + 70, 160f), (800f, y + 30, 90f) })
                c.Poly(H("#2A2C34"), 0.35f, new Vector2(px, py), new Vector2(px + w, py + 4), new Vector2(px + w * 0.8f, py + 14), new Vector2(px + w * 0.1f, py + 12));
        }

        /// <summary>A string of bulbs sagging from one point to another.</summary>
        private static void StringLights(BackdropCanvas c, Vector2 a, Vector2 b, float sag, int bulbs)
        {
            Vector2 At(float t) => Vector2.Lerp(a, b, t) + new Vector2(0, sag * 4 * t * (1 - t));
            var wire = H("#050404");
            for (int i = 0; i < 24; i++) { var p = At(i / 24f); var q = At((i + 1) / 24f); c.Line(p.x, p.y, q.x, q.y, 1.2f, wire); }
            for (int i = 1; i < bulbs; i++)
            {
                var p = At(i / (float)bulbs) + new Vector2(0, 4);
                c.Glow(p.x, p.y, 24, Bulb, 0.32f);
                c.Disc(p.x, p.y, 2.2f, BulbCore);
            }
        }

        /// <summary>The Back Alley: brick, string lights between the fire escapes, a neon sign, crates by the bins, wet ground.</summary>
        private static BackdropCanvas AlleyWall()
        {
            var c = new BackdropCanvas(Width, Height, UiTheme.BackAlley.Base);
            Brick(c, 0, 330, 0.55f);
            // The fire escape on the right: two landings, the stair between, its rails.
            var iron = H("#040303");
            foreach (float y in new[] { 70f, 190f })
            {
                c.Rect(690, y, 270, 5, iron);
                c.Line(690, y - 30, 960, y - 30, 2, iron);
                for (float x = 694; x < 960; x += 15) c.Line(x, y - 30, x, y, 1.2f, iron);
            }
            c.Line(712, 190, 900, 74, 4, iron);
            for (float t = 0.08f; t < 1; t += 0.11f) c.Line(Mathf.Lerp(712, 900, t) - 9, Mathf.Lerp(190, 74, t), Mathf.Lerp(712, 900, t) + 9, Mathf.Lerp(190, 74, t), 2, iron);
            // A neon sign over a side door, and the door.
            c.Rect(70, 52, 120, 42, H("#0A0807"), 0.85f);
            Dots(c, 84, 62, "SPARES", 3.4f, Pink, 0.55f);
            c.Glow(130, 72, 110, Pink, 0.09f);
            // Two strings of lights across.
            StringLights(c, new Vector2(0, 36), new Vector2(700, 60), 46, 14);
            StringLights(c, new Vector2(260, 18), new Vector2(960, 120), 34, 13);
            // The bins on the right, the crates in the middle, two strips of tape on the brick.
            c.Rect(730, 236, 190, 96, H("#1E2420"), H("#141815"));
            c.Rect(722, 228, 206, 10, H("#262C27"));
            c.Rect(730, 252, 190, 2, Color.black, 0.5f);
            var crate = H("#3A2A1C");
            foreach (var (x, y, w, h) in new[] { (390f, 254f, 104f, 78f), (500f, 270f, 92f, 62f), (420f, 200f, 70f, 54f) })
            {
                c.Rect(x, y, w, h, crate, Dim(crate, 0.7f));
                for (float s = y + h / 3; s < y + h - 4; s += h / 3) c.Rect(x, s, w, 2, Color.black, 0.45f);
                c.Rect(x, y, w, 2, H("#6A5238"), 0.5f);
                c.Rect(x, y, 3, h, Color.black, 0.35f);
            }
            // Two strips of tape across the crates' corner, as the alley's map has them.
            c.Turned(470, 206, 64, 8, 8, Color.Lerp(Pink, H("#E8D9A8"), 0.35f), 0.4f);
            c.Turned(476, 216, 64, 8, -5, Color.Lerp(Pink, H("#E8D9A8"), 0.7f), 0.4f);
            WetGround(c, 332, (130, Pink), (350, Bulb), (560, Bulb), (840, Bulb));
            return Done(c, 300);
        }

        /// <summary>The Basement Game: concrete, the stairs down from the street, pipes, one caged bulb over a card table.</summary>
        private static BackdropCanvas Basement()
        {
            var c = new BackdropCanvas(Width, Height, H("#0A0908"));
            c.Band(0, 320, H("#1E1B18"), H("#161411"));
            c.Grain(0, 0, Width, 320, 0.18f, 5, 29);
            c.Grain(0, 0, Width, 320, 0.08f, 60, 31);
            for (float y = 70; y < 320; y += 64) c.Rect(0, y, Width, 1.5f, Color.black, 0.25f);
            // The stairs coming down on the left, their rail, the street's light at the top.
            var step = H("#2A2622");
            for (int i = 0; i < 9; i++) c.Rect(i * 30, 70 + i * 26, 30, 340, step, Dim(step, 0.6f));
            c.Line(0, 40, 270, 274, 3, H("#0A0908"));
            c.Glow(30, 30, 180, H("#9FB0C8"), 0.14f);
            // Pipes along the ceiling, one coming down.
            var pipe = H("#2E2A26");
            c.Rect(0, 12, Width, 8, pipe, Dim(pipe, 0.6f));
            c.Rect(300, 26, Width - 300, 5, pipe, Dim(pipe, 0.6f));
            c.Rect(862, 26, 8, 300, pipe, Dim(pipe, 0.7f));
            // A dryer's vent through the ceiling, lint-grey.
            c.Rect(640, 0, 60, 12, H("#3A3632"));
            // The card table, felt under the bulb, its chairs.
            c.Poly(H("#1F3A2A"), 1, new Vector2(500, 238), new Vector2(740, 238), new Vector2(790, 276), new Vector2(450, 276));
            c.Rect(450, 276, 340, 6, H("#141210"));
            foreach (var (x, y, d) in new[] { (540f, 254f, -6f), (566f, 252f, 4f), (680f, 256f, 10f), (620f, 248f, 0f) }) c.Turned(x, y, 16, 9, d, H("#B3A999"), 0.6f);
            foreach (float x in new[] { 470f, 770f }) c.Line(x, 282, x - (x < 600 ? 10 : -10), 336, 3, H("#0C0A09"));
            c.Rect(420, 210, 34, 70, H("#120F0D"));
            c.Rect(786, 214, 34, 66, H("#120F0D"));
            // The caged bulb.
            c.Line(620, 31, 620, 70, 1.5f, H("#050404"));
            c.Glow(620, 84, 260, Bulb, 0.2f);
            c.Glow(620, 84, 30, BulbCore, 0.5f);
            c.Disc(620, 84, 8, BulbCore);
            c.Ring(620, 84, 12, 1.2f, H("#2A2622"));
            c.Line(608, 80, 632, 88, 1, H("#2A2622"));
            c.Line(608, 88, 632, 80, 1, H("#2A2622"));
            return Done(c, 320);
        }

        /// <summary>Moth's van: its side by the brick, the hatch propped open, the rail of toploaders, the lit glass shelf, AS-IS in cyan.</summary>
        private static BackdropCanvas Van()
        {
            var c = new BackdropCanvas(Width, Height, UiTheme.BackAlley.Base);
            Brick(c, 0, 330, 0.32f);
            // The van's side, cream gone grey in the dark.
            var paint = H("#4A453D");
            c.Rect(262, 70, 698, 270, paint, Dim(paint, 0.72f));
            c.Rect(282, 50, 678, 22, paint);
            c.Disc(282, 70, 20, paint);
            c.Rect(262, 298, 698, 3, Color.black, 0.4f);
            c.Rect(262, 150, 120, 2, Color.black, 0.3f);
            // Inside the hatch: warm light, the rail of toploaders on bulldog clips, the one HELD at its end, the glass shelf.
            c.Rect(400, 100, 400, 152, H("#6A5A40"), H("#3A3024"));
            c.Glow(600, 170, 230, BulbCore, 0.12f);
            c.Line(410, 118, 790, 118, 2, H("#8A8478"));
            for (int i = 0; i < 14; i++)
            {
                float x = 420 + i * 26;
                c.Rect(x + 5, 114, 8, 7, H("#1A1A1A"));
                bool held = i == 13;
                c.Rect(x, 121, 18, 26, held ? H("#5A5650") : H("#C9CED2"), 0.6f);
                if (!held) c.Rect(x + 3, 124, 12, 18, Color.HSVToRGB(i * 0.11f % 1, 0.45f, 0.55f), 0.8f);
                else c.Rect(x + 4, 148, 10, 8, H("#B3A999"), 0.8f);
            }
            var glass = H("#BFEFEA");
            for (int i = 0; i < 5; i++)
            {
                float x = 470 + i * 60;
                c.Rect(x, 176, 22, 30, H("#DDE3E6"), 0.35f);
                c.Rect(x + 3, 180, 16, 20, Color.HSVToRGB(0.08f + i * 0.17f, 0.4f, 0.6f), 0.8f);
            }
            c.Rect(440, 206, 320, 3, glass, 0.65f);
            for (float x = 460; x < 760; x += 50) c.Glow(x, 207, 46, UiTheme.NeonCyan, 0.08f);
            // The hatch lid propped up on a broom handle.
            c.Poly(Dim(paint, 1.15f), 1, new Vector2(394, 98), new Vector2(806, 98), new Vector2(846, 40), new Vector2(354, 40));
            c.Rect(354, 38, 492, 3, Color.black, 0.35f);
            c.Line(796, 250, 836, 44, 3, H("#6A4A2A"));
            c.Rect(392, 250, 416, 8, H("#2E2A24"));
            // The sign on the van's side.
            c.Rect(836, 142, 96, 40, H("#0A0807"), 0.85f);
            Dots(c, 845, 152, "AS-IS", 3.6f, UiTheme.NeonCyan, 0.5f);
            c.Glow(884, 162, 90, UiTheme.NeonCyan, 0.07f);
            WetGround(c, 340, (600, BulbCore), (884, UiTheme.NeonCyan));
            return Done(c, 300);
        }

        /// <summary>
        /// No Limits' bus depot (NO-LIMITS-STORY's brief): the long shed in perspective, its strip lights going back, the route
        /// board on the left wall, the boarded pits, the long table down the middle and one warm bulb over the end chair.
        /// </summary>
        private static BackdropCanvas Depot()
        {
            var c = new BackdropCanvas(Width, Height, H("#070807"));
            var vp = new Vector2(600, 190);
            float fw = 92, fh = 44;
            var farTL = new Vector2(vp.x - fw, vp.y - fh); var farTR = new Vector2(vp.x + fw, vp.y - fh);
            var farBL = new Vector2(vp.x - fw, vp.y + fh); var farBR = new Vector2(vp.x + fw, vp.y + fh);
            c.Poly(H("#0D0F0E"), 1, new Vector2(0, 0), new Vector2(Width, 0), farTR, farTL);
            c.Poly(H("#151915"), 1, new Vector2(0, 0), farTL, farBL, new Vector2(0, Height));
            c.Poly(H("#121512"), 1, new Vector2(Width, 0), new Vector2(Width, Height), farBR, farTR);
            c.Poly(H("#0F100E"), 1, new Vector2(0, Height), farBL, farBR, new Vector2(Width, Height));
            c.Rect(farTL.x, farTL.y, fw * 2, fh * 2, H("#1A1C18"), H("#141613"));
            c.Grain(0, 0, Width, Height, 0.1f, 8, 37);
            // The roof's trusses, going back.
            for (int k = 0; k < 7; k++)
            {
                float t = 1 - Mathf.Pow(1 - k / 7f, 1.6f);
                var l = Vector2.Lerp(new Vector2(0, 0), farTL, t); var r = Vector2.Lerp(new Vector2(Width, 0), farTR, t);
                c.Line(l.x, l.y, r.x, r.y, Mathf.Lerp(4, 1, t), H("#060706"));
            }
            // The boarded-over pits: planks down the floor.
            for (int k = -4; k <= 4; k++)
            {
                var near = new Vector2(vp.x + k * 120, Height); var far = new Vector2(vp.x + k * 16, farBL.y);
                c.Line(near.x, near.y, far.x, far.y, 1.5f, Color.black, 0.5f);
            }
            // The strip lights, two rows of tubes, smaller as they go; one dead.
            foreach (float side in new[] { -1f, 1f })
                for (int k = 0; k < 7; k++)
                {
                    float t = 1 - Mathf.Pow(1 - k / 7f, 1.6f);
                    var near = new Vector2(vp.x + side * 300, 26); var far = new Vector2(vp.x + side * 40, farTL.y + 6);
                    var p = Vector2.Lerp(near, far, t);
                    float len = Mathf.Lerp(74, 12, t), w = Mathf.Lerp(4, 1.2f, t);
                    bool dead = side > 0 && k == 2;
                    c.Line(p.x - len / 2, p.y, p.x + len / 2, p.y, w, dead ? H("#2A2E2C") : H("#DDEBEA"), dead ? 1 : 0.85f);
                    if (!dead) c.Glow(p.x, p.y + 2, Mathf.Lerp(80, 20, t), H("#BFD8D4"), 0.12f);
                }
            // The long table down the middle, three chairs down each side.
            c.Poly(H("#1C1611"), 1, new Vector2(586, 214), new Vector2(614, 214), new Vector2(800, 340), new Vector2(400, 340));
            c.Poly(H("#3A2A1A"), 0.5f, new Vector2(588, 214), new Vector2(612, 214), new Vector2(660, 250), new Vector2(540, 250));
            for (int k = 0; k < 3; k++)
            {
                float t = 0.25f + k * 0.25f, y = Mathf.Lerp(340, 214, t), off = Mathf.Lerp(200, 14, t), s = Mathf.Lerp(1, 0.25f, t);
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = 600 + side * (off + 14 * s);
                    c.Rect(x - 9 * s, y - 40 * s, 18 * s, 26 * s, H("#0B0C0A"));
                    c.Rect(x - 12 * s, y - 16 * s, 24 * s, 5 * s, H("#10110E"));
                    c.Line(x - 10 * s, y - 11 * s, x - 10 * s, y + 6 * s, 2 * s, H("#0B0C0A"));
                    c.Line(x + 10 * s, y - 11 * s, x + 10 * s, y + 6 * s, 2 * s, H("#0B0C0A"));
                }
            }
            // The end chair, its back to the far wall, under the one warm bulb.
            c.Rect(590, 196, 20, 26, H("#120F0C"));
            c.Rect(586, 218, 28, 5, H("#18130F"));
            c.Line(600, farTL.y, 600, 160, 1, H("#050504"));
            c.Glow(600, 166, 120, H("#FFB45A"), 0.32f);
            c.Glow(600, 166, 14, BulbCore, 0.7f);
            c.Beam(600, 168, 6, 230, 40, H("#FFB45A"), 0.18f);
            // The route board on the left wall: amber dots, the number nine at the top.
            c.Rect(96, 38, 236, 104, H("#2A2A26"));
            c.Rect(102, 44, 224, 92, H("#060606"));
            var amber = H("#FFB040");
            Dots(c, 112, 54, "9  DEPOT", 2.6f, amber, 0.35f);
            Dots(c, 112, 76, "12 MARKET", 2.6f, amber, 0.35f);
            Dots(c, 112, 98, "4  CIRCUIT", 2.6f, Dim(amber, 0.7f), 0.25f);
            Dots(c, 112, 120, "-  ------", 2.6f, Dim(amber, 0.5f), 0.15f);
            return Done(c, 330, 0.92f);
        }
    }
}
