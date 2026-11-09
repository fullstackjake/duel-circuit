using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-foilshowcase out.png`: installed card pictures in their foils side by side under a fixed light, screenshotted,
    /// then quits. Two runs give the same pictures, so foil work is judged by before/after shots (tools/foil_shots.sh
    /// takes the standard set). With no options it shows one card per finish, each at its rarest printing. Options:
    /// `-foilcards id,id,...` picks the cards; `-foilrarities r,r,...` (Rarity names such as UltraRare) forces a rarity
    /// per card, paired with `-foilcards` of the same length or, alone, each on a card with that printing (any monster
    /// if none has it); `-foilall` shows every finish (Common, then Rare up to Starlight) the same way;
    /// `-foillight x,y,hover` places the light (x and y across the card from its bottom left, default
    /// <see cref="CardFoil.IdleLight"/>), and `-foillight drift` leaves it live as in play for three shots
    /// (out-1.png...); `-foilsweep N` takes N shots (out-1.png...) as a hovered light crosses each card from top left to
    /// bottom right; `-foilstrength s` sets the big cards' strength (default 1, the detail picture's); `-foilboard` adds
    /// each card again beside it at the board's size and strength; `-foilslabs` shows a slab panel instead (Full,
    /// Reveal and Compact, for a few graded copies), `-foilslabs labels` every label at every size (and logs the label fit
    /// check), `-foilslabs context` slabs and grid rims on the backgrounds they're seen on (see <see cref="SlabShowcase"/>).
    /// `-foiltilt x,y` makes the big cards tilt as in the reading view (at about its size), held at x and y degrees;
    /// `-foiltilt sweep` takes five shots (out-1.png...) at fixed tilts: flat, turned left and right, tipped back, and
    /// corner-wise.
    /// </summary>
    public sealed class FoilShowcase : MonoBehaviour
    {
        /// <summary>The foil strength of a card on the field (see <see cref="CardView"/>; off-field views use 0.75).</summary>
        public const float BoardStrength = 0.6f;

        /// <summary>The `-foil...` options (see the class comment).</summary>
        public sealed class Options
        {
            public string? Cards, Rarities;
            public bool All, Board, Slabs;
            /// <summary>The slab sheet: "panel", "labels" or "context".</summary>
            public string SlabMode = "panel";
            public int Sweep;
            public float Strength = 1;
            /// <summary>The fixed light, or null for the live one (`-foillight drift`).</summary>
            public Vector3? Light = CardFoil.IdleLight;
            /// <summary>`-foiltilt`: the big cards tilt (as in the reading view), held at these angles (x, y degrees), or swept.</summary>
            public Vector2? Tilt;
            public bool TiltSweep;

            /// <summary>Reads the options from the command line (<paramref name="arg"/>: a flag's value; <paramref name="has"/>: whether a flag is there).</summary>
            public static Options From(Func<string, string?> arg, Func<string, bool> has) => new Options
            {
                Cards = arg("-foilcards"),
                Rarities = arg("-foilrarities"),
                All = has("-foilall"),
                Board = has("-foilboard"),
                Slabs = has("-foilslabs"),
                SlabMode = arg("-foilslabs") is string mode && !mode.StartsWith("-") ? mode : "panel",
                Sweep = int.TryParse(arg("-foilsweep"), out var sweep) ? Math.Max(0, sweep) : 0,
                Strength = float.TryParse(arg("-foilstrength"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1,
                Light = ParseLight(arg("-foillight")),
                Tilt = ParseTilt(arg("-foiltilt")),
                TiltSweep = arg("-foiltilt")?.Trim().Equals("sweep", StringComparison.OrdinalIgnoreCase) == true,
            };
        }

        /// <summary>`-foillight`'s value: "x,y,hover" (hover optional), "drift" for the live light (null), or the idle spot when it's missing.</summary>
        public static Vector3? ParseLight(string? text)
        {
            if (text == null) return CardFoil.IdleLight;
            if (text.Trim().Equals("drift", StringComparison.OrdinalIgnoreCase)) return null;
            var parts = text.Split(',');
            float Part(int i, float fallback) =>
                i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
            return new Vector3(Part(0, CardFoil.IdleLight.x), Part(1, CardFoil.IdleLight.y), Mathf.Clamp01(Part(2, 0)));
        }

        /// <summary>`-foiltilt`'s value: "x,y" in degrees about the screen's x and y axes, or "sweep" (starting flat).</summary>
        public static Vector2? ParseTilt(string? text)
        {
            if (text == null) return null;
            if (text.Trim().Equals("sweep", StringComparison.OrdinalIgnoreCase)) return Vector2.zero;
            var parts = text.Split(',');
            float Part(int i) => i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            return new Vector2(Part(0), Part(1));
        }

        /// <summary>The tilts `-foiltilt sweep` shoots (x, y degrees): flat, then the card turned each way and corner-wise.</summary>
        private static readonly Vector2[] TiltAngles = { new Vector2(0, 0), new Vector2(0, -8), new Vector2(0, 8), new Vector2(8, 0), new Vector2(-6, 6) };

        public static void Run(GameObject host, ICardDataProvider cards, string outPath, Options options)
        {
            CardFoil.Override = CardFoil.Display.Showcase; // every style, including the premium ones
            CardFoil.FixedLight = options.Light;
            CardFoil.FixedTilt = options.Tilt;
            var showcase = host.AddComponent<FoilShowcase>();
            showcase.StartCoroutine(showcase.Show(cards, outPath, options));
        }

        private Text _footer = null!;

        private IEnumerator Show(ICardDataProvider cards, string outPath, Options options)
        {
            var canvasGo = new GameObject("FoilShowcase", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio
            var root = Ui.Stretched("Root", canvasGo.transform, Vector2.zero, Vector2.one);
            Ui.Image(root, Ui.Hex("#10131A"));
            if (options.Slabs) SlabShowcase.Lay(root, cards, options.SlabMode);
            else LayCards(root, Picks(cards, options), options);
            _footer = Ui.Text(Ui.Placed("Footer", root, new Vector2(0, -522), new Vector2(1800, 24)), "", 14, new Color(1, 1, 1, 0.55f));

            yield return new WaitForSecondsRealtime(1.5f);
            if (options.Slabs) SlabShowcase.ReportFit();
            if (options.TiltSweep)
            {
                for (int shot = 1; shot <= TiltAngles.Length; shot++)
                {
                    CardFoil.FixedTilt = TiltAngles[shot - 1];
                    yield return null;
                    yield return null;
                    Save(Numbered(outPath, shot), options);
                    yield return new WaitForSecondsRealtime(0.25f);
                }
            }
            else if (options.Sweep > 0)
            {
                for (int shot = 1; shot <= options.Sweep; shot++)
                {
                    float t = options.Sweep == 1 ? 0.5f : (shot - 1f) / (options.Sweep - 1);
                    CardFoil.FixedLight = new Vector3(Mathf.Lerp(0.05f, 0.95f, t), Mathf.Lerp(0.95f, 0.05f, t), 1);
                    // The cards take the new light in their next Update; save the frame after that.
                    yield return null;
                    yield return null;
                    Save(Numbered(outPath, shot), options);
                    yield return new WaitForSecondsRealtime(0.25f);
                }
            }
            else if (CardFoil.FixedLight == null)
            {
                // The live light, as in play: a few shots as it drifts.
                for (int shot = 1; shot <= 3; shot++)
                {
                    if (shot > 1) yield return new WaitForSecondsRealtime(2.2f);
                    Save(Numbered(outPath, shot), options);
                }
            }
            else Save(outPath, options);
            yield return new WaitForSecondsRealtime(1);
            Application.Quit();
        }

        /// <summary>Notes the light and strengths on the sheet, then saves it at the end of the frame (after the cards take the light).</summary>
        private void Save(string path, Options options)
        {
            var light = CardFoil.FixedLight is Vector3 l ? FormattableString.Invariant($"light {l.x:0.00}, {l.y:0.00}, hover {l.z:0.##}") : "light: live (drift)";
            var board = options.Board && !options.Slabs
                ? FormattableString.Invariant($"  ·  right of each: board size {CardView.FieldSize.x:0}x{CardView.FieldSize.y:0} at {BoardStrength}") : "";
            var strength = options.Slabs ? "slabs at 1" : FormattableString.Invariant($"strength {options.Strength}");
            var tilt = options.Tilt == null ? ""
                : !FxSettings.CardTiltActive ? "  ·  tilt off (setting or reduced motion)"
                : CardFoil.FixedTilt is Vector2 t ? FormattableString.Invariant($"  ·  tilt {t.x:0}°, {t.y:0}°") : "";
            _footer.text = $"{light}  ·  {strength}{board}{tilt}";
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"Saved foil showcase to {path}");
        }

        private static string Numbered(string path, int shot) =>
            Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}-{shot}{Path.GetExtension(path)}");

        /// <summary>
        /// The cards in a grid, biggest first: 250x364 for up to ten, else 170x248 (smaller only when board copies need
        /// the room), each with its rarity and name under it and, with `-foilboard`, a board-size copy to its right.
        /// </summary>
        private static void LayCards(RectTransform root, List<(CardInfo info, Rarity rarity)> picks, Options options)
        {
            int n = picks.Count;
            bool many = n > 10;
            // A tilt sheet's cards at about the deck editor's reading-view size (360x525), shrunk only to fit.
            var preferred = many ? new Vector2(170, 248) : options.Tilt != null ? new Vector2(330, 481) : new Vector2(250, 364);
            int maxPerRow = many ? 9 : 5;
            float gapX = many ? 35 : 80, gapY = many ? 82 : 106;
            var boardSize = CardView.FieldSize;
            float extra = options.Board ? boardSize.x + 12 : 0;
            Vector2 size = preferred;
            int perRow = maxPerRow, rows = 1;
            for (float scale = 1; scale >= 0.4f; scale -= 0.02f)
            {
                size = new Vector2(Mathf.Round(preferred.x * scale), Mathf.Round(preferred.y * scale));
                perRow = Mathf.Clamp(Mathf.FloorToInt(1880 / (size.x + extra + gapX)), 1, maxPerRow);
                rows = (n + perRow - 1) / perRow;
                if (rows * (size.y + gapY) <= 1010) break;
            }
            float cellW = size.x + extra + gapX, cellH = size.y + gapY;
            for (int i = 0; i < n; i++)
            {
                var (info, rarity) = picks[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Math.Min(perRow, n - row * perRow);
                var cell = new Vector2((col - (inRow - 1) / 2f) * cellW, 15 + ((rows - 1) / 2f - row) * cellH);
                var center = cell - new Vector2(extra / 2, 0);
                var big = Card(root, info.Name, center, size, info, rarity, options.Strength);
                if (options.Tilt != null) CardFoil.Tilting(big);
                if (options.Board)
                    Card(root, info.Name + " (board)", center + new Vector2(size.x / 2 + 12 + boardSize.x / 2, (boardSize.y - size.y) / 2),
                        boardSize, info, rarity, BoardStrength);
                var label = Ui.Text(Ui.Placed("Label", root, center + new Vector2(0, -size.y / 2 - 26), new Vector2(Math.Min(many ? 200 : 300, cellW - 5), 44)),
                    $"{CardFoil.ColoredName(rarity)}\n<size=12>{info.Name}</size>", many ? 14 : 17, Color.white);
                label.supportRichText = true;
            }
        }

        private static RawImage Card(RectTransform root, string name, Vector2 center, Vector2 size, CardInfo info, Rarity rarity, float strength)
        {
            var raw = Ui.Placed(name, root, center, size).gameObject.AddComponent<RawImage>();
            raw.texture = CardView.Art?.Get(info.Code);
            CardFoil.Apply(raw, info, rarity, strength);
            return raw;
        }

        /// <summary>The cards and rarities to show, by the options (see the class comment).</summary>
        private static List<(CardInfo info, Rarity rarity)> Picks(ICardDataProvider cards, Options options)
        {
            var forced = new List<Rarity>();
            foreach (var name in (options.Rarities ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (Enum.TryParse<Rarity>(name.Trim(), true, out var r)) forced.Add(r);
                else Debug.LogWarning($"Foil showcase: no rarity called \"{name.Trim()}\" (use Rarity names, e.g. UltraRare)");

            if (options.Cards != null)
            {
                var infos = options.Cards.Split(',')
                    .Select(c => uint.TryParse(c.Trim(), out var code) ? cards.Get(code) : null)
                    .Where(c => c != null).Cast<CardInfo>().ToList();
                bool paired = forced.Count == infos.Count;
                if (forced.Count > 0 && !paired)
                    Debug.LogWarning($"Foil showcase: {infos.Count} cards but {forced.Count} rarities; each card shows its rarest printing");
                return infos.Select((info, i) => (info, paired ? forced[i] : CardFoil.RarityOf(info))).ToList();
            }

            var finder = new CardFinder(cards);
            var wanted = options.All
                ? new[] { Rarity.Common }.Concat(((Rarity[])Enum.GetValues(typeof(Rarity))).Where(r => r >= Rarity.Rare)).ToList()
                : forced;
            if (wanted.Count == 0) return finder.OnePerRarest();
            var used = new HashSet<uint>();
            var picks = new List<(CardInfo, Rarity)>();
            foreach (var rarity in wanted)
            {
                var info = finder.For(rarity, used);
                if (info == null) { Debug.LogWarning($"Foil showcase: no installed card for {rarity}"); continue; }
                var printed = CardFoil.Printings?.Of(info.Code, info.Alias).Any(p => p.Rarity == rarity) == true;
                Debug.Log($"Foil showcase: {rarity} on {info.Name} ({info.Code}){(printed ? "" : ", which was never printed at it")}");
                used.Add(info.Code);
                picks.Add((info, rarity));
            }
            return picks;
        }

        /// <summary>Installed monster pictures by their printings (first by card id, so every run picks the same cards).</summary>
        private sealed class CardFinder
        {
            private const int Keep = 12;
            private readonly Dictionary<Rarity, List<CardInfo>> _rarest = new Dictionary<Rarity, List<CardInfo>>();
            private readonly Dictionary<Rarity, List<CardInfo>> _having = new Dictionary<Rarity, List<CardInfo>>();
            private readonly List<CardInfo> _any = new List<CardInfo>();

            public CardFinder(ICardDataProvider cards)
            {
                var printings = CardFoil.Printings;
                if (printings == null || CardView.Art == null) return;
                var installed = new HashSet<uint>(CardView.Art.Installed);
                foreach (var pair in printings.All.OrderBy(p => p.Key))
                {
                    if (pair.Value.Length == 0) continue;
                    var info = cards.Get(pair.Key);
                    if (info == null || (info.Type & CardType.Monster) == 0) continue;
                    if (!installed.Contains(pair.Key) && (info.Alias == 0 || !installed.Contains(info.Alias))) continue;
                    Note(_rarest, pair.Value[0].Rarity, info);
                    foreach (var p in pair.Value) Note(_having, p.Rarity, info);
                    if (_any.Count < Keep * 4) _any.Add(info);
                }
            }

            private static void Note(Dictionary<Rarity, List<CardInfo>> lists, Rarity rarity, CardInfo info)
            {
                if (!lists.TryGetValue(rarity, out var list)) lists[rarity] = list = new List<CardInfo>();
                if (list.Count < Keep && !list.Contains(info)) list.Add(info);
            }

            /// <summary>For each rarity some card has as its rarest printing, the first such card.</summary>
            public List<(CardInfo, Rarity)> OnePerRarest() =>
                _rarest.OrderBy(p => p.Key).Select(p => (p.Value[0], p.Key)).Where(p => CardView.Art?.Get(p.Item1.Code) != null).ToList();

            /// <summary>
            /// A card not in <paramref name="used"/> for a rarity: one whose rarest printing it is, else one printed at it,
            /// else any installed monster.
            /// </summary>
            public CardInfo? For(Rarity rarity, HashSet<uint> used)
            {
                var candidates = (_rarest.TryGetValue(rarity, out var rarest) ? rarest : Enumerable.Empty<CardInfo>())
                    .Concat(_having.TryGetValue(rarity, out var having) ? having : Enumerable.Empty<CardInfo>())
                    .Concat(_any);
                return candidates.FirstOrDefault(c => !used.Contains(c.Code) && CardView.Art?.Get(c.Code) != null);
            }
        }
    }
}
