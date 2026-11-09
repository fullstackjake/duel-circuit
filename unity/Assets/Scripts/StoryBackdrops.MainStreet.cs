using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The main street's backdrops (<see cref="UiTheme.MainStreet"/>: navy walls, wood, lamp gold), on a 960 × 540 canvas. The
    /// dialogue box covers rows 320-490 and the portraits stand at the left (x 70-300, rows 125-320): the detail sits up and to
    /// the right, and <see cref="Done"/> shades the portraits' corner and the floor.
    /// </summary>
    public static partial class StoryBackdrops
    {
        private static readonly Color Lamp = H("#FFC940"), LampSoft = H("#FFD9A0"), Night = H("#6F86B0");

        /// <summary>The finish: the portraits' corner in shade, the floor darker toward the bottom, the vignette and blur.</summary>
        private static BackdropCanvas Done(BackdropCanvas c, float floorY = 300, float brightness = 0.9f)
        {
            c.Shadow(185, 235, 200, 190, 0.5f);
            c.Shade(floorY, Height, 1, 0.45f);
            c.Finish(0.55f, brightness);
            return c;
        }

        /// <summary>The shop: binders shelved along the back wall, the counter with the glass case and the register, two lamps.</summary>
        private static BackdropCanvas ShopFloor()
        {
            var c = new BackdropCanvas(Width, Height, H("#0D1017"));
            c.Band(0, 300, H("#1B2231"), H("#151B27"));
            c.Grain(0, 0, Width, 300, 0.07f, 40, 3);
            c.Band(300, Height, H("#121722"), H("#0B0E14"));
            // The back wall's bookcase: three shelves of binders.
            var wood = H("#4A3420");
            c.Rect(318, 24, 642, 280, H("#1E1712"), H("#16110D"));
            for (int r = 0; r < 3; r++)
            {
                float baseY = 104 + r * 82;
                // The top shelf holds booster boxes; binders below.
                if (r == 0) PackBoxes(c, 330, 950, baseY, 56, 5);
                else Binders(c, 330, 950, baseY, 66, 11 + r, 0.42f, 0.36f);
                Plank(c, 318, 960, baseY, wood);
            }
            c.Rect(312, 20, 10, 290, wood, H("#2E2014"));
            c.Rect(632, 24, 8, 286, wood, H("#2E2014"));
            // The left wall: a framed poster (a pack's art, faded) under a sconce.
            c.Rect(40, 70, 150, 190, H("#2A3448"));
            c.Rect(48, 78, 134, 174, H("#3A2A3A"), H("#1E2433"));
            c.Disc(115, 150, 34, H("#5A3A2A"), 0.6f);
            c.Glow(115, 52, 120, LampSoft, 0.14f);
            // The counter: its top, its front, the glass case (lit cards inside) and the register.
            c.Rect(360, 266, 600, 10, H("#4A3420"), H("#3A2A1A"));
            c.Rect(360, 266, 600, 1.5f, LampSoft, 0.35f);
            c.Rect(360, 276, 600, 270, H("#1E1610"), H("#100C08"));
            for (float x = 420; x < 960; x += 120) c.Rect(x, 284, 2, 260, Color.black, 0.35f);
            c.Rect(380, 214, 190, 52, H("#1C2836"), H("#26384A"), 0.95f);
            for (int i = 0; i < 6; i++)
                c.Turned(400 + i * 30, 244, 16, 22, (i % 3 - 1) * 4, Color.HSVToRGB(i * 0.15f, 0.45f, 0.55f), 0.8f);
            c.Glow(475, 238, 110, H("#BFE3FF"), 0.08f);
            c.Rect(380, 214, 190, 2, H("#9FD8FF"), 0.35f);
            c.Rect(806, 222, 70, 44, H("#1A1E26"), H("#11141A"));
            c.Rect(816, 206, 50, 18, H("#141820"));
            c.Rect(820, 210, 42, 10, H("#7BD88F"), 0.6f);
            c.Glow(841, 215, 34, H("#7BD88F"), 0.18f);
            // The shop's sign over the counter.
            c.Line(520, 0, 520, 150, 1, H("#05070B"));
            c.Line(680, 0, 680, 150, 1, H("#05070B"));
            c.Rect(500, 150, 200, 34, H("#1A1F2B"));
            c.Rect(500, 150, 200, 2, Lamp, 0.3f);
            Dots(c, 552, 158, "CARDS", 4, Lamp, 0.3f);
            // Two lamps over the counter.
            Pendant(c, 470, 46, H("#2A3448"), Lamp, 0.42f, 230);
            Pendant(c, 720, 40, H("#2A3448"), Lamp, 0.42f, 236);
            return Done(c);
        }

        /// <summary>A shelf of booster boxes standing on a plank: each a box of art in a bright color, its lid's lit edge.</summary>
        private static void PackBoxes(BackdropCanvas c, float x0, float x1, float baseY, float tall, int seed)
        {
            var rng = new System.Random(seed);
            for (float x = x0; x < x1 - 40; x += 46)
            {
                float h = tall * (0.85f + (float)rng.NextDouble() * 0.15f);
                var art = Color.HSVToRGB((float)rng.NextDouble(), 0.55f, 0.5f);
                c.Rect(x, baseY - h, 42, h, H("#1A1F2B"), H("#12161F"));
                c.Rect(x + 4, baseY - h + 8, 34, h - 16, art, Dim(art, 0.6f));
                c.Disc(x + 21, baseY - h / 2, 8, Dim(art, 1.5f), 0.5f);
                c.Rect(x, baseY - h, 42, 2, Color.white, 0.18f);
            }
        }

        /// <summary>The shop after closing: dark shelves, the front door with its bell, the street lamp through the glass.</summary>
        private static BackdropCanvas ShopNight()
        {
            var c = new BackdropCanvas(Width, Height, H("#07090E"));
            c.Band(0, 330, H("#0E131D"), H("#0B0F17"));
            c.Grain(0, 0, Width, 330, 0.06f, 50, 7);
            c.Band(330, Height, H("#0A0D13"), H("#06080C"));
            // The shelves, unlit.
            c.Rect(300, 40, 200, 280, H("#0F0C0A"));
            for (int r = 0; r < 3; r++) { Binders(c, 306, 494, 120 + r * 90, 70, 31 + r, 0.3f, 0.12f); Plank(c, 300, 500, 120 + r * 90, H("#1E160E")); }
            // The door: the night through its glass, a lamp post, the CLOSED sign, the handle, the bell over it.
            var frame = H("#1E1712");
            c.Rect(545, 45, 170, 172, H("#132240"), H("#0B1426"));
            c.Glow(686, 78, 90, LampSoft, 0.45f);
            c.Glow(686, 78, 18, LampSoft, 0.6f);
            c.Line(686, 86, 686, 217, 3, H("#05080F"));
            c.Rect(545, 196, 170, 21, H("#0A1020"), 0.8f);
            c.Rect(586, 118, 62, 26, H("#5A2420"), 0.9f);
            Dots(c, 590, 124, "CLOSED", 2.4f, H("#E8C9A0"), 0.2f);
            c.Line(592, 104, 600, 118, 1, H("#3A2E26"));
            c.Line(642, 104, 634, 118, 1, H("#3A2E26"));
            c.Rect(520, 20, 25, 312, frame);
            c.Rect(715, 20, 25, 312, frame);
            c.Rect(520, 20, 220, 25, frame);
            c.Rect(520, 217, 220, 115, H("#1A1410"), H("#120E0B"));
            c.Rect(540, 237, 180, 75, H("#151110"), 0.8f);
            c.Disc(706, 248, 5, H("#8A6A1E"));
            c.Line(630, 0, 630, 14, 1.5f, H("#3A3020"));
            c.Disc(630, 19, 6, H("#8A6A1E"));
            c.Glow(630, 19, 20, Lamp, 0.12f);
            // The lamp's light in through the glass, on the floor.
            c.Poly(Night, 0.07f, true, new Vector2(545, 332), new Vector2(715, 332), new Vector2(940, Height), new Vector2(430, Height));
            // The counter at the right, the register's display still lit.
            c.Rect(790, 250, 170, 12, H("#2A1E14"));
            c.Rect(790, 262, 170, 280, H("#140F0B"), H("#0B0907"));
            c.Rect(860, 214, 64, 36, H("#12151B"));
            c.Rect(866, 220, 52, 10, H("#7BD88F"), 0.45f);
            c.Glow(892, 225, 40, H("#7BD88F"), 0.16f);
            return Done(c, 330, 0.85f);
        }

        /// <summary>
        /// Out front: the window, and the corkboard: bracket sheets, pushpins, the old card signed in gold at the top.
        /// <paramref name="swapped"/> (corkboard-top, after the Every Era final): the plain card moved up top, the gold-signed one
        /// (with its gold strokes) in the lower slot.
        /// </summary>
        private static BackdropCanvas Corkboard(bool swapped = false)
        {
            var c = new BackdropCanvas(Width, Height, H("#0D1017"));
            c.Band(0, 300, H("#18202D"), H("#131925"));
            c.Grain(0, 0, Width, 300, 0.07f, 40, 5);
            c.Band(300, Height, H("#11161F"), H("#0A0D13"));
            // The window, evening outside.
            c.Rect(20, 30, 270, 250, H("#2A2018"));
            c.Rect(30, 40, 250, 230, H("#22344E"), H("#16233A"));
            c.Glow(200, 60, 160, H("#8FA8D0"), 0.12f);
            c.Rect(152, 40, 6, 230, H("#2A2018"));
            c.Rect(30, 152, 250, 6, H("#2A2018"));
            c.Poly(Night, 0.06f, true, new Vector2(30, 282), new Vector2(280, 282), new Vector2(420, Height), new Vector2(0, Height));
            // The board: a wood frame, cork.
            c.Rect(340, 30, 560, 262, H("#4A3420"), H("#3A2817"));
            c.Rect(352, 42, 536, 238, H("#6E5232"), H("#5C4329"));
            c.Grain(352, 42, 536, 238, 0.22f, 2.5f, 2);
            c.Grain(352, 42, 536, 238, 0.1f, 30, 9);
            var paper = H("#D6CFBE");
            // A bracket sheet: eight names into one.
            Sheet(c, 430, 150, 120, 150, -2, paper, true);
            // Lists and flyers.
            Sheet(c, 560, 200, 96, 120, 4, paper, false);
            Sheet(c, 690, 150, 110, 140, -5, H("#E3D9A8"), false);
            Sheet(c, 820, 205, 90, 110, 3, paper, false);
            Sheet(c, 810, 95, 80, 70, -3, H("#C9D6E3"), false);
            // The old card near the top, signed in gold; the newer card under it (swapped: the plain card up top, the signed one below).
            var (signedX, signedY, signedDeg) = swapped ? (612f, 248f, 5f) : (600f, 82f, -3f);
            var (plainX, plainY, plainDeg) = swapped ? (600f, 82f, -3f) : (612f, 248f, 5f);
            c.Turned(signedX, signedY, 38, 54, signedDeg, H("#2A2018"));
            c.Turned(signedX, signedY, 32, 48, signedDeg, H("#3A4A66"));
            c.Line(signedX - 14, signedY + 14, signedX + 14, signedY + 6, 1.5f, H("#E6C27A"));
            c.Line(signedX - 10, signedY + 18, signedX + 4, signedY + 16, 1, H("#E6C27A"));
            c.Turned(plainX, plainY, 30, 42, plainDeg, H("#4A2E2A"));
            // A fresh sheet's name circled in red marker.
            c.Ring(450, 104, 14, 1.6f, H("#C8402F"), 0.8f);
            foreach (var (x, y, col) in new[] { (430f, 78f, "#C8402F"), (560f, 143f, "#3D6FB5"), (690f, 84f, "#E3B21A"), (820f, 152f, "#C8402F"), (600f, 58f, "#E3B21A"), (810f, 62f, "#3D6FB5") })
            {
                c.Disc(x, y, 4, H(col));
                c.Disc(x - 1, y - 1, 1.4f, Color.white, 0.6f);
            }
            c.Glow(620, 20, 300, LampSoft, 0.1f);
            return Done(c);
        }

        /// <summary>A pinned sheet, turned slightly: a bracket drawn on it, or lines of writing.</summary>
        private static void Sheet(BackdropCanvas c, float cx, float cy, float w, float h, float degrees, Color paper, bool bracket)
        {
            c.Turned(cx + 3, cy + 4, w, h, degrees, Color.black, 0.3f);
            c.Turned(cx, cy, w, h, degrees, paper, 0.92f);
            float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            Vector2 P(float u, float v) => new Vector2(cx + u * cos - v * sin, cy + u * sin + v * cos);
            var ink = H("#2A2A2E");
            void Ink(float u0, float v0, float u1, float v1) { var a = P(u0, v0); var b = P(u1, v1); c.Line(a.x, a.y, b.x, b.y, 1, ink, 0.7f); }
            if (bracket)
            {
                // Eight names in the first round, then four, two, one.
                float top = -h / 2 + 14, step = (h - 28) / 7;
                for (int i = 0; i < 8; i++) Ink(-w / 2 + 8, top + i * step, -w / 2 + 34, top + i * step);
                for (int round = 0, n = 8, span = 1; n > 1; round++, n /= 2, span *= 2)
                    for (int i = 0; i < n; i += 2)
                    {
                        float u = -w / 2 + 34 + round * 22, a = top + (i * span + (span - 1) / 2f) * step, b = top + ((i + 1) * span + (span - 1) / 2f) * step;
                        Ink(u, a, u, b);
                        Ink(u, (a + b) / 2, u + 22, (a + b) / 2);
                    }
            }
            else
                for (float v = -h / 2 + 14; v < h / 2 - 10; v += 11) Ink(-w / 2 + 10, v, w / 2 - 10 - (v * 7 % 20 + 20) % 20, v);
        }

        /// <summary>The back room's working side: one bare bulb, boxes, the stockroom door (chalk on it), the shelf with an empty slot.</summary>
        private static BackdropCanvas BackRoom()
        {
            var c = new BackdropCanvas(Width, Height, H("#0A0C10"));
            c.Band(0, 320, H("#161A22"), H("#11141B"));
            c.Grain(0, 0, Width, 320, 0.09f, 30, 13);
            c.Band(320, Height, H("#0F1117"), H("#08090D"));
            // The stockroom door, a chalked bracket on it.
            c.Rect(712, 62, 176, 270, H("#3A3028"));
            c.Rect(724, 74, 152, 258, H("#2C2520"), H("#1E1916"));
            c.Disc(860, 206, 5, H("#8A7A5A"));
            var chalk = H("#D8D4CC");
            for (int i = 0; i < 8; i++) c.Line(744, 100 + i * 16, 772, 100 + i * 16, 1.4f, chalk, 0.4f);
            for (int i = 0; i < 4; i++) { float y = 100 + i * 32; c.Line(772, y, 772, y + 16, 1.4f, chalk, 0.4f); c.Line(772, y + 8, 792, y + 8, 1.4f, chalk, 0.4f); }
            for (int i = 0; i < 2; i++) { float y = 108 + i * 64; c.Line(792, y, 792, y + 32, 1.4f, chalk, 0.4f); c.Line(792, y + 16, 812, y + 16, 1.4f, chalk, 0.4f); }
            c.Line(812, 124, 812, 188, 1.4f, chalk, 0.4f);
            c.Line(812, 156, 834, 156, 1.4f, chalk, 0.4f);
            Dots(c, 748, 240, "FRIDAYS", 2.2f, chalk, 0.05f);
            // The shelf, its binders, and the one slot still empty.
            Binders(c, 520, 690, 168, 62, 47, 0.42f, 0.34f, gapAt: 612);
            Plank(c, 512, 698, 168, H("#4A3420"));
            // Boxes stacked under the bulb.
            var box = H("#5A4128");
            c.Rect(330, 222, 120, 110, box, H("#3E2C1B"));
            c.Rect(450, 246, 100, 86, Dim(box, 0.9f), H("#382817"));
            c.Rect(350, 160, 96, 62, Dim(box, 1.05f), H("#46321F"));
            foreach (var (x, y, w) in new[] { (330f, 262f, 120f), (450f, 280f, 100f), (350f, 186f, 96f) }) c.Rect(x, y, w, 7, H("#B9A27A"), 0.4f);
            c.Rect(330, 222, 120, 2, Color.white, 0.12f);
            // The bulb.
            c.Line(470, 0, 470, 44, 1.5f, H("#05060A"));
            c.Glow(470, 54, 300, H("#FFB960"), 0.2f);
            c.Glow(470, 54, 40, H("#FFE7B0"), 0.55f);
            c.Disc(470, 54, 7, H("#FFE7B0"));
            return Done(c, 320);
        }

        /// <summary>The Format Library: a bookcase of year binders with brass plates, a high window's morning light, Hollis's lamp.</summary>
        private static BackdropCanvas LibraryShelves()
        {
            var c = new BackdropCanvas(Width, Height, H("#0E0B08"));
            c.Band(0, 320, H("#1C160F"), H("#17120C"));
            c.Grain(0, 0, Width, 320, 0.08f, 30, 17);
            c.Band(320, Height, H("#120E0A"), H("#0A0806"));
            // The high window.
            c.Rect(36, 18, 216, 108, H("#3A2817"));
            c.Rect(44, 26, 200, 92, H("#55667C"), H("#3A4758"));
            c.Rect(141, 26, 6, 92, H("#3A2817"));
            c.Rect(44, 69, 200, 5, H("#3A2817"));
            // The bookcase: three shelves of binders by year, brass plates, the plaque on top.
            var wood = H("#5A3F22");
            c.Rect(286, 8, 670, 314, H("#3A2817"), H("#2E2011"));
            c.Rect(298, 30, 646, 290, H("#1E150D"), H("#170F09"));
            for (int r = 0; r < 3; r++)
            {
                float baseY = 112 + r * 90;
                Binders(c, 306, 940, baseY, 74, 3 + r * 5, 0.45f, 0.42f);
                Plank(c, 298, 944, baseY, wood);
            }
            c.Rect(560, 12, 130, 14, H("#8A6A1E"));
            c.Rect(563, 14, 124, 10, H("#B8862B"), 0.8f);
            // Morning light across the shelves.
            c.Poly(H("#FFE6B8"), 0.075f, true, new Vector2(60, 118), new Vector2(244, 118), new Vector2(760, Height), new Vector2(380, Height));
            c.Poly(H("#FFE6B8"), 0.05f, true, new Vector2(44, 26), new Vector2(244, 26), new Vector2(560, 300), new Vector2(330, 300));
            // Hollis's desk lamp, low on the left.
            c.Rect(150, 268, 160, 8, H("#3A2817"));
            c.Poly(H("#2F5A35"), 1, new Vector2(226, 240), new Vector2(262, 240), new Vector2(270, 254), new Vector2(218, 254));
            c.Glow(244, 258, 70, LampSoft, 0.25f);
            return Done(c, 320);
        }

        /// <summary>The Every Era final's stage: the long table under one lamp, six binders down it, the chair at the far end.</summary>
        private static BackdropCanvas EveryEraStage()
        {
            var c = new BackdropCanvas(Width, Height, H("#07080C"));
            c.Band(0, 190, H("#0F121A"), H("#0B0D13"));
            c.Band(190, Height, H("#0C0E14"), H("#07080B"));
            // The shelves behind, in the dark.
            for (int r = 0; r < 2; r++)
            {
                Binders(c, 90, 430, 110 + r * 70, 52, 61 + r, 0.3f, 0.13f);
                Binders(c, 690, 950, 110 + r * 70, 52, 71 + r, 0.3f, 0.13f);
            }
            // The chair at the far end, its back to the wall.
            c.Rect(546, 118, 28, 46, H("#1A1410"));
            c.Rect(540, 160, 40, 8, H("#1E1712"));
            c.Rect(556, 132, 8, 4, H("#B8862F"), 0.5f);  // a glint of gold on the chair (the Duchess's sash)
            // The long table, in perspective.
            c.Poly(H("#3A2A1A"), 1, new Vector2(500, 168), new Vector2(620, 168), new Vector2(910, 334), new Vector2(210, 334));
            c.Poly(H("#22180F"), 1, new Vector2(210, 334), new Vector2(910, 334), new Vector2(910, 352), new Vector2(210, 352));
            // Six binders down it, the most worn nearest.
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f, y = Mathf.Lerp(304, 182, t), s = Mathf.Lerp(1, 0.32f, t);
                var cover = Color.HSVToRGB(Mathf.Repeat((i * 2 + 1) * 0.083f, 1), 0.45f, 0.5f);
                c.Rect(560 - 46 * s, y - 7 * s, 92 * s, 14 * s, cover, Dim(cover, 0.7f));
                c.Rect(560 - 46 * s, y - 7 * s, 92 * s, 2.5f * s, Dim(cover, 1.5f), 0.7f);
                c.Rect(560 - 46 * s, y + 7 * s, 92 * s, 4 * s, Color.black, 0.35f);
            }
            // The one lamp over it all.
            Pendant(c, 560, 34, H("#1A1F2B"), H("#FFD27A"), 0.4f, 300);
            c.Glow(560, 250, 260, H("#FFD27A"), 0.07f);
            return Done(c, 340);
        }

        /// <summary>A tournament hall: banners on the far wall, rows of hanging lights and of tables going back.</summary>
        private static BackdropCanvas Hall()
        {
            var c = new BackdropCanvas(Width, Height, H("#0A0D14"));
            c.Band(0, 130, H("#0A0D14"), H("#0E121B"));
            c.Band(130, 214, H("#161C29"), H("#131824"));
            c.Band(214, Height, H("#11151E"), H("#0A0C12"));
            // Banners.
            foreach (var (x, col) in new[] { (110f, "#8A6A1E"), (250f, "#7A2E26"), (690f, "#2E4A7A"), (830f, "#8A6A1E") })
                c.Poly(Dim(H(col), 0.75f), 1, new Vector2(x, 128), new Vector2(x + 44, 128), new Vector2(x + 44, 206), new Vector2(x + 22, 218), new Vector2(x, 206));
            c.Rect(330, 140, 300, 40, H("#0E121B"));
            c.Rect(330, 140, 300, 2, Lamp, 0.25f);
            Dots(c, 362, 150, "CHAMPIONSHIP", 4, H("#E6C27A"), 0.25f);
            // Rows of tables going back to the far wall, chairs at each.
            var vp = new Vector2(480, 205);
            for (int r = 0; r < 6; r++)
            {
                float t = Mathf.Pow(r / 6f, 0.8f), y = Mathf.Lerp(330, 222, t), half = Mathf.Lerp(470, 140, t), thick = Mathf.Lerp(20, 5, t);
                for (int k = -2; k <= 2; k++)
                {
                    float cx = vp.x + k * half * 0.42f, w = half * 0.34f;
                    c.Rect(cx - w / 2, y, w, thick * 0.4f, H("#3A4A66"), H("#2A3448"));
                    c.Rect(cx - w / 2, y + thick * 0.4f, w, thick * 0.6f, H("#141924"), H("#0E121A"));
                    c.Rect(cx - w / 2, y, w, 1.2f, H("#7FB2FF"), 0.2f);
                    for (int s = 0; s < 3; s++) c.Rect(cx - w / 2 + w * (0.2f + s * 0.3f) - thick * 0.4f, y - thick * 1.1f, thick * 0.8f, thick * 1.1f, H("#0E121A"));
                }
            }
            // The lights, row on row, smaller as they go.
            for (int r = 0; r < 6; r++)
            {
                float t = r / 6f, y = Mathf.Lerp(22, 118, Mathf.Pow(t, 0.75f)), spread = Mathf.Lerp(230, 70, t), size = Mathf.Lerp(1, 0.35f, t);
                for (int k = -2; k <= 2; k++)
                {
                    float x = vp.x + k * spread;
                    c.Rect(x - 16 * size, y, 32 * size, 3 * size, H("#E8EEF8"), 0.7f);
                    c.Glow(x, y + 2, 46 * size, H("#CFE0FF"), 0.16f);
                }
            }
            return Done(c, 300);
        }
    }
}
