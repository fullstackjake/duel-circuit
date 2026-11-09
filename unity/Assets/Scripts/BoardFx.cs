using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.Protocol;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Duel animation, played over the board after each redraw: cards fly between places, activated cards are
    /// spotlighted in the middle of the mat, summons burst, attackers lunge, and damage numbers pop.
    /// Everything scales with <see cref="Tempo"/> (tied to the speed control) and is skipped at 0.
    /// </summary>
    public sealed class BoardFx
    {
        private readonly BoardView _board;
        private readonly RectTransform _layer;
        private readonly RectTransform _cards;
        private readonly ICardDataProvider _data;
        private readonly List<Tween> _tweens = new List<Tween>();
        private float _busyUntil;

        /// <summary>1 = normal pace, 0.5 = twice as fast, 0 = no animation.</summary>
        public float Tempo { get; set; } = 1f;

        /// <summary>True while an animation the duel should wait for is still playing.</summary>
        public bool Busy => Time.unscaledTime < _busyUntil || HoldWhile?.Invoke() == true;

        /// <summary>Holds the duel as an animation does while it says so (a press held on a card: BoardView).</summary>
        public Func<bool>? HoldWhile { get; set; }

        /// <summary>Raised when an effect starts ("spotlight", "summon", "damage"...), for automated screenshots.</summary>
        public event Action<string>? Started;

        public BoardFx(BoardView board, RectTransform layer, RectTransform cards, ICardDataProvider data)
        {
            _board = board;
            _layer = layer;
            _cards = cards;
            _data = data;
        }

        private sealed class Tween
        {
            public float Start, Duration;
            public Action<float> Step = _ => { };
            public Action? Done;
            public bool Begun;
        }

        /// <summary>Runs <paramref name="step"/> with 0..1 over <paramref name="duration"/> seconds, after <paramref name="delay"/>.</summary>
        private void Animate(float delay, float duration, Action<float> step, Action? done = null)
        {
            var tween = new Tween { Start = Time.unscaledTime + delay, Duration = Mathf.Max(0.01f, duration), Step = step, Done = done };
            _tweens.Add(tween);
            if (delay <= 0) Run(tween, Time.unscaledTime);
        }

        /// <summary>Plays a sound effect <paramref name="delay"/> seconds from now, in step with the animation.</summary>
        private void Sound(float delay, string name, float volume = 1f)
        {
            if (delay <= 0) { Sounds.Play(name, volume); return; }
            Animate(delay, 0.001f, _ => { }, () => Sounds.Play(name, volume));
        }

        /// <summary>Does something <paramref name="delay"/> seconds from now, in step with the animations.</summary>
        private void AtTime(float delay, Action act)
        {
            if (delay <= 0) { act(); return; }
            Animate(delay, 0.001f, _ => { }, act);
        }

        /// <summary>The duel waits until this many seconds from now.</summary>
        private void Hold(float seconds) => _busyUntil = Mathf.Max(_busyUntil, Time.unscaledTime + seconds);

        public void Tick()
        {
            var now = Time.unscaledTime;
            for (int i = _tweens.Count - 1; i >= 0; i--)
                if (i < _tweens.Count && Run(_tweens[i], now)) _tweens.Remove(_tweens[i]);
            // No chain going, the board not busy, and a chain-link number up for longer than a chain's end takes: its
            // chain's end missed it (skipped at 60/s, or its clean-up cut short), so it goes now.
            if (_board.ChainLinksShown > 0) _noChainSince = now;
            if (now - _noChainSince > 0.5f && now >= _busyUntil && _badgeBorn.Count > 0 && OldestBadgeAge > 3f)
            {
                foreach (var (badge, born) in _badgeBorn.ToList())
                    if (badge != null && now - born > 3f && !_clearing.Contains(badge))
                    {
                        Debug.Log("Cleared a chain-link badge left after its chain");
                        UnityEngine.Object.Destroy(badge.gameObject);
                        _badgeBorn.Remove(badge);
                        _chainBadges.Remove(badge);
                    }
            }
        }

        private float _noChainSince;

        /// <summary>For checks: why a left-over badge is (or isn't) being cleared.</summary>
        public string BadgeDebug => $"chain shown {_board.ChainLinksShown}, no chain for {Time.unscaledTime - _noChainSince:0.0}s, busy {Busy}, tweens {_tweens.Count} [{string.Join(", ", _tweens.Select(t => $"{t.Start - Time.unscaledTime:0.0}s/{t.Duration:0.0}"))}], layer {_layer.gameObject.activeInHierarchy}";

        /// <returns>true when finished.</returns>
        private static bool Run(Tween tween, float now)
        {
            if (now < tween.Start) return false;
            var t = Mathf.Clamp01((now - tween.Start) / tween.Duration);
            try
            {
                tween.Step(t);
                if (t >= 1) tween.Done?.Invoke();
            }
            catch (Exception ex) when (ex is MissingReferenceException || ex is NullReferenceException)
            {
                if (DebugBadges) Debug.Log($"tween dropped: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}\n{ex.StackTrace?.Split('\n').FirstOrDefault()}");
                return true; // the board was redrawn underneath it
            }
            return t >= 1;
        }

        // Big moments already shown this duel (shorter or skipped when repeated; see FxSettings.ShorterRepeats).
        private readonly HashSet<string> _seen = new HashSet<string>();

        /// <summary>True the first time a moment happens in this duel (or always, when repeats play in full).</summary>
        private bool First(string key, bool count = true)
        {
            bool first = _seen.Add(key) || !FxSettings.ShorterRepeats;
            if (count) { if (first) FullMoments++; else ShortMoments++; }
            return first;
        }

        /// <summary>For checks: big moments played in full and shortened (this run).</summary>
        public int FullMoments { get; private set; }
        public int ShortMoments { get; private set; }

        public void Clear()
        {
            _seen.Clear();
            _openingDue = false;
            _tweens.Clear();
            _busyUntil = 0;
            _badgePlace.Clear();
            for (int i = _layer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_layer.GetChild(i).gameObject);
            _cards.anchoredPosition = Vector2.zero;
        }

        /// <summary>Animates what just happened (the events since the last redraw), in order.</summary>
        public void Play(IReadOnlyList<DuelEvent> events)
        {
            if (Tempo <= 0)
            {
                // Fast forward: finish anything in flight so cards end up in place.
                foreach (var tween in _tweens.ToArray()) { tween.Start = -100; Run(tween, Time.unscaledTime); }
                _tweens.Clear();
                _cards.anchoredPosition = Vector2.zero;
                _board.HideAttackArrow();
                // A chain that ends while fast-forwarding still takes its link numbers with it (no fade).
                if (events.Any(e => e.Kind == DuelEventKind.ChainEnd)) RemoveChainMarks();
                // No moments while fast-forwarding (the cards still count as seen face-up).
                _openingDue = false; // a duel fast-forwarded from its start has no opening riffle to catch up on
                if (_board.Arrivals.Count > 0) PrizesFastForwarded++;
                _board.Arrivals.Clear();
                CardFoil.Prizes.Clear();
                return;
            }
            _landed.Clear();
            _shuffledThisBatch.Clear();
            float at = 0;
            for (int index = 0; index < events.Count; index++)
            {
                var e = events[index];
                // The opening hands: both Decks are riffled together first (the core shuffles them without a message).
                // Only the hands dealt before turn 1: a duel with no opening hand (a lesson, a check setup) has none.
                if (e.Kind == DuelEventKind.NewTurn) _openingDue = false;
                if (e.Kind == DuelEventKind.Draw && _openingDue) { _openingDue = false; at += OpeningShuffle(at); }
                switch (e.Kind)
                {
                    case DuelEventKind.Move:
                    {
                        var left = e.From;
                        AtTime(at, () => { _board.MonsterLeft(left); DropBadgesAt(left); });
                        at += MoveCard(e, at, MaterialTarget(events, index));
                        break;
                    }
                    case DuelEventKind.Draw: at += Draw(e, at); break;
                    case DuelEventKind.NormalSummon:
                    case DuelEventKind.SpecialSummon:
                    case DuelEventKind.FlipSummon: at += Summon(e, at); break;
                    case DuelEventKind.Activate: at += Spotlight(e, at); break;
                    case DuelEventKind.Attack: at += Attack(e, at); break;
                    case DuelEventKind.Damage:
                    case DuelEventKind.Cost:
                    case DuelEventKind.Recover: at += LifePoints(e, at); break;
                    case DuelEventKind.Negate: at += Negated(at, effectOnly: e.Reason == 1); break;
                    case DuelEventKind.Phase: AtTime(at, _board.HideAttackArrow); at += PhaseBanner(e, at); break;
                    case DuelEventKind.NewTurn: AtTime(at, _board.HideAttackArrow); at += TurnBanner(e, at); break;
                    case DuelEventKind.AttackOver: AtTime(at, _board.HideAttackArrow); break;
                    case DuelEventKind.ChainEnd: ClearChain(at); break;
                    case DuelEventKind.ShuffleDeck:
                        // Repeats in one batch (a search, then another) riffle once.
                        if (_shuffledThisBatch.Add(e.Player)) at += Shuffle(e.Player, at, full: First("shuffle", count: false));
                        break;
                }
            }
            Prize(at);
            Hold(at);
            if (at <= 0) return;
            Batches++;
            if (at > LongestBatch) LongestBatch = at;
            // A batch this long holds the duel a while: say what was in it, to find out why.
            if (at > LongBatch)
                Debug.Log($"Long animation: {at:0.0}s for {events.Count} events ({string.Join(", ", events.GroupBy(e => e.Kind).Select(g => $"{g.Count()} {g.Key}"))}), tempo {Tempo:0.00}");
        }

        /// <summary>For checks: how many animated batches have played, and the longest one's length (seconds).</summary>
        public int Batches { get; private set; }
        public float LongestBatch { get; private set; }

        /// <summary>A batch longer than this is logged with what was in it.</summary>
        public const float LongBatch = 8f;

        // ---- Cards moving ----------------------------------------------------------------------------

        private static bool IsPile(uint location) => (location & (Location.Deck | Location.Extra | Location.Grave | Location.Removed)) != 0;

        // The core's reasons for a move (REASON_ in ygopro-core).
        private const uint ReasonDestroy = 0x1, ReasonMaterial = 0x8, ReasonFusion = 0x40000, ReasonSynchro = 0x80000,
            ReasonRitual = 0x100000, ReasonXyz = 0x200000, ReasonLink = 0x10000000;

        /// <summary>
        /// For a Fusion, Synchro, Xyz, Link or Ritual material: the point the materials gather at, in the middle of the
        /// field on their owner's side (the summon itself comes as a later event).
        /// </summary>
        private Vector2? MaterialTarget(IReadOnlyList<DuelEvent> events, int index)
        {
            var e = events[index];
            if ((e.Reason & ReasonMaterial) == 0 || (e.Reason & (ReasonFusion | ReasonSynchro | ReasonXyz | ReasonLink | ReasonRitual)) == 0) return null;
            return BoardView.PlayerSide(e.From.Controller) * 0.35f;
        }

        private static Color MaterialColor(uint reason) =>
            (reason & ReasonFusion) != 0 ? Ui.Hex("#C58CFF") : (reason & ReasonSynchro) != 0 ? Color.white : (reason & ReasonXyz) != 0 ? Ui.Hex("#FFE27A")
            : (reason & ReasonLink) != 0 ? Ui.Hex("#5AB4FF") : (reason & ReasonRitual) != 0 ? Ui.Hex("#8FE3FF") : Ui.Hex("#FFC94A");

        /// <summary>A point along a curved path: straight from a to b, bowed sideways by <paramref name="bow"/>.</summary>
        private static Vector2 Arc(Vector2 a, Vector2 b, float k, float bow)
        {
            var d = b - a;
            var side = new Vector2(-d.y, d.x).normalized;
            return Vector2.LerpUnclamped(a, b, k) + side * bow * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
        }

        private float MoveCard(DuelEvent e, float delay, Vector2? converge)
        {
            var from = e.From;
            var to = e.To;
            if (from.Location == 0 || to.Location == 0) return 0;
            bool xyzMaterial = (to.Location & Location.Overlay) != 0 && converge != null;
            if (((from.Location | to.Location) & Location.Overlay) != 0 && !xyzMaterial) return 0;
            if (from.Location == to.Location && from.Controller == to.Controller && from.Sequence == to.Sequence) return 0;
            var src = _board.PreviousPosition(from.Controller, from.Location, (int)from.Sequence);
            bool leavesField = (from.Location & (Location.MZone | Location.SZone)) != 0;
            bool faceUp = (to.Position & 0xA) == 0 && !(_board.IsHidden(to.Controller) && (to.Location & (Location.Deck | Location.Hand)) != 0);

            // Summoning materials: they fly together into the monster they make, glowing in its summon's color.
            if (converge is Vector2 meet)
            {
                Started?.Invoke("material");
                var color = MaterialColor(e.Reason);
                var flight = 0.5f * Tempo;
                var ghost = Ghost(e.Code, from.Controller, src, faceUp: true);
                var aura = Sprite("Aura", Textures.Glow, src, new Vector2(170, 170), color);
                Sound(delay, "move", 0.7f);
                Animate(delay, flight, t =>
                {
                    var k = EaseInCubic(t);
                    var p = Arc(src, meet, k, 90);
                    ghost.anchoredPosition = p;
                    ghost.localRotation = Quaternion.Euler(0, 0, 360 * k);
                    ghost.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, k);
                    aura.rectTransform.anchoredPosition = p;
                    aura.color = WithAlpha(color, 0.9f * Mathf.Sin(t * Mathf.PI));
                }, () => { UnityEngine.Object.Destroy(ghost.gameObject); UnityEngine.Object.Destroy(aura.gameObject); });
                return flight * 0.45f;
            }

            var dest = _board.ViewAt(to.Controller, to.Location, (int)to.Sequence);
            if (dest == null) return 0;
            var duration = 0.36f * Tempo;
            Started?.Invoke("move");

            // Destroyed on the field: the card shatters, then lands in its pile.
            if (leavesField && (e.Reason & ReasonDestroy) != 0 && (to.Location & (Location.Grave | Location.Removed)) != 0)
            {
                Sound(delay, "destroy", 0.9f);
                Shatter(e.Code, from.Controller, src, delay);
                PilePulse(dest.anchoredPosition, delay + 0.35f * Tempo, Ui.Hex("#B8C0D0"));
                return 0.4f * Tempo;
            }

            Sound(delay, (to.Location & (Location.Grave | Location.Removed)) != 0 && leavesField ? "destroy"
                : (to.Location & (Location.MZone | Location.SZone)) != 0 ? "place" : "move", 0.8f);

            if (!IsPile(to.Location))
            {
                // The card is already drawn at its new place: it travels there from where it was, on a slight curve.
                var end = dest.anchoredPosition;
                dest.anchoredPosition = src;
                dest.SetAsLastSibling();
                Animate(delay, duration, t =>
                {
                    var k = EaseOutCubic(t);
                    dest.anchoredPosition = Arc(src, end, k, 40);
                    dest.localScale = Vector3.one * (1 + 0.14f * Mathf.Sin(k * Mathf.PI));
                });
                return duration * 0.85f;
            }

            var target = dest.anchoredPosition;
            // Banished: the card dissolves in pale light where it was, and the banished pile glows.
            if ((to.Location & Location.Removed) != 0)
            {
                Started?.Invoke("banish");
                var ghost = Ghost(e.Code, from.Controller, src, faceUp);
                var group = ghost.gameObject.AddComponent<CanvasGroup>();
                var light = Sprite("Banish", Textures.Glow, src, new Vector2(220, 260), Ui.Hex("#BFE4FF"));
                var fade = 0.55f * Tempo;
                Animate(delay, fade, t =>
                {
                    ghost.localScale = new Vector3(Mathf.Lerp(1f, 0.2f, EaseInCubic(t)), Mathf.Lerp(1f, 1.4f, t), 1);
                    ghost.localRotation = Quaternion.Euler(0, 0, 25 * t);
                    group.alpha = 1 - EaseInCubic(t);
                    light.color = WithAlpha(Ui.Hex("#BFE4FF"), Mathf.Sin(t * Mathf.PI));
                    light.rectTransform.localScale = Vector3.one * (0.8f + 0.6f * t);
                }, () => { UnityEngine.Object.Destroy(ghost.gameObject); UnityEngine.Object.Destroy(light.gameObject); });
                PilePulse(target, delay + fade * 0.7f, Ui.Hex("#BFE4FF"));
                return fade * 0.8f;
            }

            // Into the GY, the Deck or the Extra Deck: a copy of the card arcs onto the pile, turning as it goes.
            var flier = Ghost(e.Code, to.Controller, src, faceUp);
            float spin = (to.Location & Location.Deck) != 0 ? 0 : 180;
            Animate(delay, duration, t =>
            {
                var k = EaseOutCubic(t);
                flier.anchoredPosition = Arc(src, target, k, 70);
                flier.localRotation = Quaternion.Euler(0, 0, spin * k);
                flier.localScale = Vector3.one * Mathf.Lerp(1.05f, 0.9f, k) * (1 + 0.15f * Mathf.Sin(k * Mathf.PI));
            }, () => UnityEngine.Object.Destroy(flier.gameObject));
            PilePulse(target, delay + duration * 0.8f, (to.Location & Location.Grave) != 0 ? Ui.Hex("#B8C0D0") : Ui.Hex("#FFC94A"));
            return duration * 0.85f;
        }

        /// <summary>A soft glow on a pile as a card lands on it.</summary>
        private void PilePulse(Vector2 at, float delay, Color color)
        {
            var glow = Sprite("PilePulse", Textures.Glow, at, new Vector2(150, 190), color);
            Animate(delay, 0.35f * Tempo, t =>
            {
                glow.color = WithAlpha(color, 0.7f * (1 - t));
                glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, t);
            }, () => UnityEngine.Object.Destroy(glow.gameObject));
        }

        /// <summary>A destroyed card breaks into pieces that fly apart, spin, fall and fade.</summary>
        private void Shatter(uint code, int owner, Vector2 at, float delay)
        {
            Started?.Invoke("shatter");
            var info = _data.Get(code);
            var art = info != null ? CardView.Art?.Get(code) : null;
            var color = info != null ? CardView.FrameColor(info.Type) : CardView.BackFill; // an unknown card breaks as its back
            var size = CardView.FieldSize;
            const int cols = 3, rows = 4;
            var rng = new System.Random((int)code + (int)(at.x * 7));
            var flash = Sprite("ShatterFlash", Textures.Glow, at, new Vector2(200, 240), Color.white);
            Animate(delay, 0.25f * Tempo, t => flash.color = WithAlpha(Color.white, 0.9f * (1 - t)), () => UnityEngine.Object.Destroy(flash.gameObject));
            for (int cx = 0; cx < cols; cx++)
                for (int cy = 0; cy < rows; cy++)
                {
                    var piece = new Vector2(size.x / cols, size.y / rows);
                    var offset = new Vector2((cx + 0.5f) * piece.x - size.x / 2, (cy + 0.5f) * piece.y - size.y / 2);
                    var rt = Ui.Placed("Shard", _layer, at + offset, piece * 0.96f);
                    var image = rt.gameObject.AddComponent<RawImage>();
                    image.raycastTarget = false;
                    if (art != null) { image.texture = art; image.uvRect = new Rect((float)cx / cols, (float)cy / rows, 1f / cols, 1f / rows); }
                    else image.color = color;
                    var velocity = (offset.normalized + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.6f)) * (220 + 160 * (float)rng.NextDouble());
                    float turn = ((float)rng.NextDouble() - 0.5f) * 720;
                    var startColor = image.color;
                    Animate(delay, 0.7f * Tempo, t =>
                    {
                        rt.anchoredPosition = at + offset + velocity * t + new Vector2(0, -420 * t * t);
                        rt.localRotation = Quaternion.Euler(0, 0, turn * t);
                        rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, t);
                        image.color = WithAlpha(startColor, 1 - EaseInCubic(t));
                    }, () => UnityEngine.Object.Destroy(rt.gameObject));
                }
        }

        private float Draw(DuelEvent e, float delay)
        {
            int hand = _board.HandCount(e.Player);
            var deck = _board.PreviousPosition(e.Player, Location.Deck, -1);
            var stagger = 0.08f * Tempo;
            var duration = 0.38f * Tempo;
            bool reveal = !_board.IsHidden(e.Player);
            for (int i = 0; i < e.Amount; i++)
            {
                var view = _board.ViewAt(e.Player, Location.Hand, hand - e.Amount + i);
                if (view == null) continue;
                var end = view.anchoredPosition;
                view.anchoredPosition = deck;
                view.localScale = Vector3.one * 0.8f;
                // Your own draws leave the Deck face-down and flip over on the way to your hand.
                var back = reveal ? Ghost(0, e.Player, deck, faceUp: false) : null;
                if (back != null) view.localScale = new Vector3(0, 0.8f, 1);
                Animate(delay + i * stagger, duration, t =>
                {
                    var k = EaseOutCubic(t);
                    var p = Arc(deck, end, k, e.Player == 0 ? -60 : 60);
                    var s = Mathf.Lerp(0.8f, 1f, k) * (1 + 0.1f * Mathf.Sin(k * Mathf.PI));
                    if (back != null)
                    {
                        // First half: the back, narrowing; second half: the face, widening.
                        float flip = Mathf.Clamp01((t - 0.25f) / 0.5f);
                        back.anchoredPosition = p;
                        back.localScale = new Vector3(s * Mathf.Max(0, 1 - flip * 2), s, 1);
                        view.anchoredPosition = p;
                        view.localScale = new Vector3(s * Mathf.Max(0, flip * 2 - 1), s, 1);
                    }
                    else
                    {
                        view.anchoredPosition = p;
                        view.localScale = Vector3.one * s;
                    }
                }, () =>
                {
                    if (back != null) UnityEngine.Object.Destroy(back.gameObject);
                    if (view != null) view.localScale = Vector3.one;
                });
            }
            if (e.Amount > 0) Started?.Invoke("draw");
            for (int i = 0; i < Math.Min(e.Amount, 6); i++) Sound(delay + i * stagger, "draw", 0.7f);
            return e.Amount > 0 ? duration + stagger * (e.Amount - 1) : 0;
        }

        // ---- Deck shuffles -----------------------------------------------------------------------------

        private readonly HashSet<int> _shuffledThisBatch = new HashSet<int>();
        private bool _openingDue;

        /// <summary>For checks: Deck riffles played (opening ones included), and the opening ones alone (this run).</summary>
        public int Shuffles { get; private set; }
        public int OpeningShuffles { get; private set; }

        /// <summary>The next opening draw riffles both Decks first (a new duel or a replay from its start; Clear cancels it).</summary>
        public void ArmOpeningShuffle() => _openingDue = true;

        /// <summary>For checks: riffle cards still in the effects layer.</summary>
        public int ShuffleGhosts
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _layer.childCount; i++) if (_layer.GetChild(i).name == ShuffleGhostName) n++;
                return n;
            }
        }

        /// <summary>For checks: where the riffle cards are (rounded), and where the Decks were.</summary>
        public string ShuffleSpots
        {
            get
            {
                var spots = new List<string>();
                for (int i = 0; i < _layer.childCount; i++)
                    if (_layer.GetChild(i).name == ShuffleGhostName && _layer.GetChild(i) is RectTransform rt)
                        spots.Add($"{rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}{(rt.gameObject.activeInHierarchy ? "" : " off")}");
                return $"{string.Join(" ", spots)}; decks {_board.PreviousPosition(0, Location.Deck, -1)} {_board.PreviousPosition(1, Location.Deck, -1)}";
            }
        }

        private const string ShuffleGhostName = "ShuffleCard";

        private float OpeningShuffle(float delay)
        {
            _seen.Add("shuffle"); // the opening counts as the duel's first: later ones are the shorter kind
            float hold = 0;
            for (int p = 0; p < 2; p++)
            {
                float h = Shuffle(p, delay, full: true);
                if (h > 0) OpeningShuffles++;
                hold = Mathf.Max(hold, h);
            }
            return hold;
        }

        /// <summary>
        /// A cut and riffle on a player's Deck: a few face-down cards split into two halves, interleave back with small
        /// hops, and settle with a squash. Returns how long the duel waits (about 60%: what follows overlaps the settle).
        /// </summary>
        private float Shuffle(int player, float delay, bool full)
        {
            if (_board.DeckCount(player) < 2) return 0;
            var duration = (full ? 0.45f : 0.28f) * Tempo;
            var deck = _board.PreviousPosition(player, Location.Deck, -1);
            const int count = 4;
            var cards = new RectTransform[count];
            var baseRotation = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                cards[i] = Ghost(0, player, deck + new Vector2(0, i * 1.5f), faceUp: false);
                cards[i].name = ShuffleGhostName;
                baseRotation[i] = cards[i].localRotation;
            }
            // The interleave: left, right, left, right (cards 0 and 1 are the left half, 2 and 3 the right).
            int[] order = { 0, 2, 1, 3 };
            var turn = new int[count];
            for (int j = 0; j < count; j++) turn[order[j]] = j;
            const float stagger = 0.14f;
            var raised = new bool[count];
            Started?.Invoke("shuffle");
            Shuffles++;
            Sound(delay, "shuffle", full ? 0.5f : 0.4f);
            Animate(delay, duration, t =>
            {
                for (int i = 0; i < count; i++)
                {
                    float side = i < 2 ? -1 : 1;
                    float spread, hop = 0, sx = 1, sy = 1;
                    if (t < 0.4f) spread = EaseOutCubic(t / 0.4f);
                    else if (t < 0.8f)
                    {
                        // Each card hops back onto the pile in turn.
                        float v = (t - 0.4f) / 0.4f;
                        float local = Mathf.Clamp01((v - turn[i] * stagger) / (1 - (count - 1) * stagger));
                        // Onto the top of the riffle once, as its hop starts (not every frame: the first draws stay above it).
                        if (local > 0 && !raised[i]) { raised[i] = true; RaiseAbove(cards[i], cards); }
                        spread = 1 - EaseOutCubic(local);
                        hop = 10 * Mathf.Sin(local * Mathf.PI);
                    }
                    else
                    {
                        spread = 0;
                        float w = Mathf.Sin((t - 0.8f) / 0.2f * Mathf.PI);
                        sx = 1 + 0.07f * w;
                        sy = 1 - 0.07f * w;
                    }
                    float lift = i * 1.5f * (1 - spread);
                    // Drawn upside down for the top player: their hops go "up" toward their side too.
                    var up = player == 0 ? 1 : -1;
                    cards[i].anchoredPosition = deck + new Vector2(side * 22 * spread, (lift + hop) * up);
                    cards[i].localRotation = baseRotation[i] * Quaternion.Euler(0, 0, -side * 6 * spread);
                    cards[i].localScale = new Vector3(sx, sy, 1);
                }
            }, () =>
            {
                foreach (var card in cards) if (card != null) UnityEngine.Object.Destroy(card.gameObject);
            });
            return duration * 0.6f;
        }

        /// <summary>Puts a riffle card just above the highest of its own riffle's cards, leaving anything newer above it.</summary>
        private static void RaiseAbove(RectTransform card, RectTransform[] riffle)
        {
            int top = -1;
            foreach (var other in riffle) if (other != null && other != card) top = Mathf.Max(top, other.GetSiblingIndex());
            if (top > card.GetSiblingIndex()) card.SetSiblingIndex(top);
        }

        /// <summary>A card drawn in the effects layer (for flights onto piles and the spotlight).</summary>
        private RectTransform Ghost(uint code, int owner, Vector2 at, bool faceUp, Vector2? size = null, uint cover = 0)
        {
            var info = code != 0 ? _data.Get(code) : null;
            var card = new FieldCard
            {
                Code = code,
                Cover = cover, // your copy's look (and a prized moment) follows it into the spotlight
                Position = faceUp && info != null ? 0x1u : 0x8u,
                Type = info?.Type ?? 0,
                Level = info?.Level ?? 0,
                Attack = info?.Attack ?? 0,
                Defense = info?.Defense ?? 0,
            };
            var view = CardView.Create(_layer, at, size ?? CardView.FieldSize, card, info, owner, faceUp && info != null, null, offField: true);
            foreach (var graphic in view.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            return (RectTransform)view.transform;
        }

        // ---- Summons ---------------------------------------------------------------------------------

        private float Summon(DuelEvent e, float delay)
        {
            var view = _board.ViewAt(e.To.Controller, e.To.Location, (int)e.To.Sequence);
            if (view == null) return 0;
            var info = _data.Get(e.Code);
            var type = info?.Type ?? 0;
            bool big = (info?.Level ?? 0) >= 7 || (type & CardType.ExtraDeckTypes) != 0;
            var color = SummonColor(type, e.Kind);
            var duration = (big ? 0.75f : 0.55f) * Tempo;
            var at = view.anchoredPosition;
            Started?.Invoke("summon");
            if (e.Kind == DuelEventKind.SpecialSummon && info != null) delay += SummonScene(info, view, at, delay);
            if (info != null && ((info.Level >= 8 && (type & CardType.Link) == 0) || info.Attack >= 3000)) delay += CutIn(info, delay);
            Sound(delay, big ? "summon_big" : e.Kind == DuelEventKind.SpecialSummon ? "special" : "summon");
            _landed[(e.To.Controller, e.To.Location, (int)e.To.Sequence)] = (delay, big ? 0.35f : 0.25f, null);

            var glow = Sprite("Glow", Textures.Glow, at, new Vector2(180, 180), color);
            var ring = Sprite("Ring", Textures.Ring, at, new Vector2(160, 160), color);
            RawImage? rays = big ? Sprite("Rays", Textures.Rays, at, new Vector2(320, 320), color) : null;
            view.SetAsLastSibling();
            Animate(delay, duration, t =>
            {
                var k = EaseOutCubic(t);
                glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, big ? 2.4f : 1.8f, k);
                glow.color = WithAlpha(color, 0.9f * (1 - t));
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, big ? 3.2f : 2.4f, k);
                ring.color = WithAlpha(color, 1 - t);
                if (rays != null)
                {
                    rays.rectTransform.localRotation = Quaternion.Euler(0, 0, 40 * t);
                    rays.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.5f, k);
                    rays.color = WithAlpha(color, Mathf.Sin(t * Mathf.PI) * 0.8f);
                }
                if (view != null) view.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, EaseOutBack(t));
            }, () =>
            {
                UnityEngine.Object.Destroy(glow.gameObject);
                UnityEngine.Object.Destroy(ring.gameObject);
                if (rays != null) UnityEngine.Object.Destroy(rays.gameObject);
            });
            if (big) Shake(delay + 0.05f * Tempo, 0.3f * Tempo, 7);
            return duration * 0.8f;
        }

        /// <summary>
        /// The summon's own scene before the monster lands: a Fusion vortex, Synchro rings and stars, an Xyz galaxy,
        /// Link arrows lighting up, a Ritual circle; with its name ("SYNCHRO SUMMON"). Hidden until it lands.
        /// </summary>
        private float SummonScene(CardInfo info, RectTransform view, Vector2 at, float delay)
        {
            var type = info.Type;
            string? name = (type & CardType.Fusion) != 0 ? "FUSION SUMMON" : (type & CardType.Synchro) != 0 ? "SYNCHRO SUMMON"
                : (type & CardType.Xyz) != 0 ? "XYZ SUMMON" : (type & CardType.Link) != 0 ? "LINK SUMMON" : (type & CardType.Ritual) != 0 ? "RITUAL SUMMON" : null;
            if (name == null || !FxSettings.SummonScenes) return 0;
            // The first of each kind in a duel plays in full; repeats are a quick version without the title.
            bool first = First("scene:" + name);
            var duration = (first ? 1.15f : 0.5f) * Tempo;
            Started?.Invoke("scene-" + name.Split(' ')[0].ToLowerInvariant());
            var color = SummonColor(type, DuelEventKind.SpecialSummon);
            var made = new List<Graphic>();
            RawImage Add(string n, Texture2D tex, Vector2 pos, Vector2 size, Color c) { var g = Sprite(n, tex, pos, size, c); made.Add(g); return g; }
            // The monster appears at the end of the scene.
            var scale = view.localScale;
            view.localScale = Vector3.zero;
            Animate(delay + duration * 0.92f, 0.01f, _ => { }, () => { if (view != null) view.localScale = scale; });
            Sound(delay, "special");

            if ((type & CardType.Fusion) != 0)
            {
                var swirl = Add("Vortex", Textures.Spiral, at, new Vector2(460, 460), color);
                var core = Add("Core", Textures.Glow, at, new Vector2(240, 240), Ui.Hex("#3A1460"));
                Animate(delay, duration, t =>
                {
                    swirl.rectTransform.localRotation = Quaternion.Euler(0, 0, -720 * EaseInCubic(t));
                    swirl.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 0.3f, EaseInCubic(t));
                    swirl.color = WithAlpha(color, Mathf.Sin(t * Mathf.PI) * 0.95f);
                    core.color = WithAlpha(Ui.Hex("#3A1460"), Mathf.Sin(t * Mathf.PI) * 0.8f);
                });
            }
            else if ((type & CardType.Synchro) != 0)
            {
                // Rings sweep up through the spot one after another, with the Level's stars in a column; then a pillar.
                int stars = (int)Mathf.Clamp(info.Level & 0xff, 1, 12);
                for (int i = 0; i < 3; i++)
                {
                    var ring = Add("SynchroRing", Textures.Ring, at, new Vector2(300, 300), Ui.Hex("#9CFFB0"));
                    float start = i * 0.18f;
                    Animate(delay + start * duration, duration * 0.5f, t =>
                    {
                        ring.rectTransform.anchoredPosition = at + new Vector2(0, Mathf.Lerp(-200, 220, t));
                        ring.rectTransform.localScale = new Vector3(1, 0.32f, 1) * Mathf.Lerp(0.8f, 1.15f, t);
                        ring.color = WithAlpha(Ui.Hex("#9CFFB0"), Mathf.Sin(t * Mathf.PI));
                    });
                }
                for (int i = 0; i < stars; i++)
                {
                    var star = Add("Star", Icons.For("star"), at + new Vector2(0, (i - (stars - 1) / 2f) * 30), new Vector2(30, 30), Ui.Hex("#FFE27A"));
                    float start = 0.15f + 0.4f * i / Mathf.Max(1, stars);
                    Animate(delay + start * duration, duration * (0.75f - start * 0.5f), t =>
                    {
                        star.color = WithAlpha(Ui.Hex("#FFE27A"), t < 0.2f ? t * 5 : 1 - EaseInCubic((t - 0.2f) / 0.8f));
                        star.rectTransform.localScale = Vector3.one * (1 + 0.4f * Mathf.Sin(t * Mathf.PI));
                    });
                }
                var pillar = Add("Pillar", Textures.Glow, at, new Vector2(170, 900), Color.white);
                Animate(delay + 0.6f * duration, 0.4f * duration, t =>
                {
                    pillar.color = WithAlpha(Color.white, Mathf.Sin(t * Mathf.PI));
                    pillar.rectTransform.localScale = new Vector3(Mathf.Lerp(0.3f, 1.2f, t), 1, 1);
                });
            }
            else if ((type & CardType.Xyz) != 0)
            {
                // A dark galaxy opens with gold lights orbiting into it.
                var dark = Add("Galaxy", Textures.Glow, at, new Vector2(420, 420), new Color(0.02f, 0.02f, 0.06f));
                var arms = Add("Arms", Textures.Spiral, at, new Vector2(440, 440), Ui.Hex("#FFE27A"));
                var orbs = new List<RawImage>();
                for (int i = 0; i < 3; i++) orbs.Add(Add("Orb", Textures.Glow, at, new Vector2(60, 60), Ui.Hex("#FFE27A")));
                Animate(delay, duration, t =>
                {
                    float open = Mathf.Sin(t * Mathf.PI);
                    dark.color = new Color(0.02f, 0.02f, 0.06f, 0.95f * open);
                    dark.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.1f, EaseOutCubic(Mathf.Clamp01(t * 2)));
                    arms.rectTransform.localRotation = Quaternion.Euler(0, 0, 200 * t);
                    arms.rectTransform.localScale = dark.rectTransform.localScale;
                    arms.color = WithAlpha(Ui.Hex("#FFE27A"), 0.6f * open);
                    for (int i = 0; i < orbs.Count; i++)
                    {
                        float a = t * 8 + i * 2.094f, r = Mathf.Lerp(190, 0, EaseInCubic(t));
                        orbs[i].rectTransform.anchoredPosition = at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.6f) * r;
                        orbs[i].color = WithAlpha(Ui.Hex("#FFE27A"), open);
                    }
                });
            }
            else if ((type & CardType.Link) != 0)
            {
                // Its Link Arrows light up around the spot, one by one.
                var markers = new (uint bit, Vector2 dir)[]
                {
                    (0x40, new Vector2(-1, 1)), (0x80, new Vector2(0, 1)), (0x100, new Vector2(1, 1)), (0x20, new Vector2(1, 0)),
                    (0x4, new Vector2(1, -1)), (0x2, new Vector2(0, -1)), (0x1, new Vector2(-1, -1)), (0x8, new Vector2(-1, 0)),
                };
                var lit = markers.Where(m => (info.LinkMarker & m.bit) != 0).ToList();
                var frame = Add("LinkFrame", Textures.Ring, at, new Vector2(320, 320), Ui.Hex("#5AB4FF"));
                Animate(delay, duration, t => frame.color = WithAlpha(Ui.Hex("#5AB4FF"), 0.7f * Mathf.Sin(t * Mathf.PI)));
                for (int i = 0; i < lit.Count; i++)
                {
                    var dir = lit[i].dir;
                    var arrow = Add("LinkArrow", Textures.Arrow, at + Vector2.Scale(dir, new Vector2(110, 140)), new Vector2(64, 64), Ui.Hex("#FF4B4B"));
                    arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90);
                    float start = 0.1f + 0.5f * i / Mathf.Max(1, lit.Count);
                    Animate(delay + start * duration, duration * (0.9f - start), t =>
                    {
                        arrow.color = WithAlpha(Ui.Hex("#FF4B4B"), t < 0.15f ? t / 0.15f : 1 - EaseInCubic((t - 0.15f) / 0.85f));
                        arrow.rectTransform.localScale = Vector3.one * (1 + 0.5f * Mathf.Sin(Mathf.Clamp01(t * 3) * Mathf.PI));
                    });
                }
            }
            else
            {
                // Ritual: a magic circle turning on the ground, then a beam.
                var outer = Add("Circle", Textures.Ring, at, new Vector2(420, 420), color);
                var spokes = Add("Spokes", Textures.Rays, at, new Vector2(380, 380), color);
                var beam = Add("Beam", Textures.Glow, at, new Vector2(150, 900), Color.white);
                Animate(delay, duration, t =>
                {
                    float a = Mathf.Sin(t * Mathf.PI);
                    outer.rectTransform.localScale = new Vector3(1, 0.45f, 1) * Mathf.Lerp(0.5f, 1.1f, EaseOutCubic(t));
                    outer.color = WithAlpha(color, a);
                    spokes.rectTransform.localRotation = Quaternion.Euler(0, 0, 120 * t);
                    spokes.rectTransform.localScale = new Vector3(1, 0.45f, 1) * Mathf.Lerp(0.5f, 1.1f, EaseOutCubic(t));
                    spokes.color = WithAlpha(color, 0.8f * a);
                    beam.color = WithAlpha(Color.white, t > 0.6f ? Mathf.Sin((t - 0.6f) / 0.4f * Mathf.PI) : 0);
                });
            }
            if (first) Label(name, color, at, delay + 0.1f * duration, duration * 0.9f);
            Animate(delay, duration, _ => { }, () => { foreach (var g in made) if (g != null) UnityEngine.Object.Destroy(g.gameObject); });
            return duration * 0.85f;
        }

        /// <summary>A summon's name in big italic letters across the spot, fading.</summary>
        private void Label(string text, Color color, Vector2 at, float delay, float duration)
        {
            var y = Mathf.Clamp(at.y + (at.y < 0 ? 170 : -170), -380, 380);
            var rt = Ui.Placed("SummonName", _layer, new Vector2(0, y), new Vector2(1200, 90));
            var label = Ui.Text(rt, text, 58, WithAlpha(color, 0));
            label.fontStyle = FontStyle.BoldAndItalic;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            Animate(delay, duration, t =>
            {
                rt.anchoredPosition = new Vector2(Mathf.Lerp(-60, 60, t), y);
                label.color = WithAlpha(Color.Lerp(color, Color.white, 0.4f), t < 0.15f ? t / 0.15f : t > 0.75f ? (1 - t) / 0.25f : 1);
            }, () => UnityEngine.Object.Destroy(rt.gameObject));
        }

        private static Color SummonColor(uint type, DuelEventKind kind)
        {
            if ((type & CardType.Synchro) != 0) return Color.white;
            if ((type & CardType.Xyz) != 0) return Ui.Hex("#FFE27A");
            if ((type & CardType.Fusion) != 0) return Ui.Hex("#C58CFF");
            if ((type & CardType.Link) != 0) return Ui.Hex("#5AB4FF");
            if ((type & CardType.Ritual) != 0) return Ui.Hex("#8FE3FF");
            return kind == DuelEventKind.SpecialSummon ? Ui.Hex("#9FD8FF") : Ui.Hex("#FFC94A");
        }

        // ---- Activation spotlight --------------------------------------------------------------------

        // ---- Effect text and chain links -------------------------------------------------------------

        private RectTransform? _effectBanner;
        private readonly List<RectTransform> _chainBadges = new List<RectTransform>();

        /// <summary>
        /// The effect being activated, in words, at the top of the field (Master Duel style): the card's name and the
        /// effect line, staying until the chain is over.
        /// </summary>
        private void EffectBanner(DuelEvent e, CardInfo info, float delay)
        {
            var effect = Ygo.Engine.Interactive.DuelText.Describe(e.Description, _data);
            if (effect.Length == 0) effect = FirstSentence(info.Text);
            Animate(delay, 0.001f, _ => { }, () =>
            {
                if (_effectBanner != null) UnityEngine.Object.Destroy(_effectBanner.gameObject);
                var rt = Ui.Placed("EffectText", _layer, new Vector2(445, 445), new Vector2(380, 96));
                _effectBanner = rt;
                Ui.Image(rt, new Color(0.04f, 0.06f, 0.1f, 0.88f));
                var edge = Ui.Rect("Edge", rt);
                edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f);
                edge.sizeDelta = new Vector2(5, 0);
                Ui.Image(edge, e.Player == _board.HumanSeat ? Ui.Hex("#4FA3FF") : Ui.Hex("#FF5A5A"));
                var text = Ui.Text(Ui.Placed("Text", rt, new Vector2(6, 0), new Vector2(352, 90)),
                    $"<b>{(e.ChainLink > 0 ? $"Chain Link {e.ChainLink} · " : "")}{info.Name}</b>\n{effect}", 14, new Color(1f, 0.93f, 0.7f), TextAnchor.MiddleLeft);
                text.supportRichText = true;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 10;
                text.resizeTextMaxSize = 14;
            });
        }

        private static string FirstSentence(string text)
        {
            var line = text.Split('\n')[0];
            int dot = line.IndexOf(". ", StringComparison.Ordinal);
            line = dot > 0 ? line.Substring(0, dot + 1) : line;
            return line.Length > 150 ? line.Substring(0, 147) + "..." : line;
        }

        /// <summary>For checks: chain-link badges on the board (there should be none once a chain is over).</summary>
        public int ChainBadgesShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _layer.childCount; i++) if (_layer.GetChild(i).name == "ChainLink") n++;
                return n;
            }
        }

        /// <summary>A numbered badge on the activated card's place, kept until the chain resolves.</summary>
        private void ChainBadge(DuelEvent e, float delay)
        {
            if (e.ChainLink <= 0) return;
            var at = _board.PreviousPosition(e.From.Controller, e.From.Location, (int)e.From.Sequence) + new Vector2(26, 36);
            Animate(delay + 0.2f * Tempo, 0.25f * Tempo, t => { }, null);
            var badge = Ui.Placed("ChainLink", _layer, at, new Vector2(40, 40));
            var disc = Sprite("Disc", Textures.Glow, Vector2.zero, new Vector2(64, 64), Ui.Hex("#FFC940"));
            disc.rectTransform.SetParent(badge, false);
            var num = Ui.Text(Ui.Stretched("N", badge, Vector2.zero, Vector2.one), e.ChainLink.ToString(), 26, Color.white);
            num.fontStyle = FontStyle.Bold;
            num.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);
            badge.localScale = Vector3.zero;
            Animate(delay + 0.25f * Tempo, 0.25f * Tempo, t => badge.localScale = Vector3.one * EaseOutBack(t));
            _chainBadges.Add(badge);
            _badgeBorn[badge] = Time.unscaledTime;
            foreach (var gone in _badgePlace.Keys.Where(b => b == null).ToList()) _badgePlace.Remove(gone);
            _badgePlace[badge] = (e.From.Controller, e.From.Location & 0x7f, e.From.Sequence);
            if (DebugBadges) Debug.Log($"badge+ link {e.ChainLink} {e.Code} at {at} id {badge.GetInstanceID()} t={Time.unscaledTime:0.00} delay {delay:0.00}");
        }

        /// <summary>Where each chain-link badge stands (the activated card's zone).</summary>
        private readonly Dictionary<RectTransform, (int controller, uint location, uint sequence)> _badgePlace =
            new Dictionary<RectTransform, (int, uint, uint)>();

        /// <summary>
        /// A card left a zone: a chain-link number standing on that Monster or Spell &amp; Trap Zone goes with it, so no bare
        /// number is left on an empty zone (Tsukuyomi returning itself to the hand while its own link resolves).
        /// </summary>
        private void DropBadgesAt(Ygo.Engine.Protocol.CardRef from)
        {
            uint location = from.Location & 0x7f;
            if (location != Location.MZone && location != Location.SZone) return;
            foreach (var (badge, place) in _badgePlace.ToList())
            {
                if (place.controller != from.Controller || place.location != location || place.sequence != from.Sequence) continue;
                _badgePlace.Remove(badge);
                _badgeBorn.Remove(badge);
                _clearing.Remove(badge);
                _chainBadges.Remove(badge);
                if (badge != null) UnityEngine.Object.Destroy(badge.gameObject);
            }
        }

        /// <summary>For checks: log each chain badge's life.</summary>
        public static bool DebugBadges;

        private readonly Dictionary<RectTransform, float> _badgeBorn = new Dictionary<RectTransform, float>();

        /// <summary>For checks: how long the oldest chain badge no chain's end has claimed has been up (0 with none).</summary>
        public float OldestBadgeAge
        {
            get
            {
                float oldest = 0;
                foreach (var (badge, born) in _badgeBorn.ToList())
                {
                    if (badge == null) { _badgeBorn.Remove(badge!); _clearing.Remove(badge!); continue; }
                    if (_clearing.Contains(badge)) continue; // its chain's end is taking it away
                    oldest = Mathf.Max(oldest, Time.unscaledTime - born);
                }
                return oldest;
            }
        }

        /// <summary>The chain's link numbers and effect banner, gone at once.</summary>
        private void RemoveChainMarks()
        {
            foreach (var b in _chainBadges) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            _chainBadges.Clear();
            if (_effectBanner != null) UnityEngine.Object.Destroy(_effectBanner.gameObject);
            _effectBanner = null;
        }

        /// <summary>Badges a chain's end has taken in hand (they fade out on their own; the safety net leaves them).</summary>
        private readonly HashSet<RectTransform> _clearing = new HashSet<RectTransform>();

        private void ClearChain(float delay)
        {
            var badges = new List<RectTransform>(_chainBadges);
            foreach (var b in badges) _clearing.Add(b);
            if (DebugBadges) Debug.Log($"clear {badges.Count} [{string.Join(",", badges.Select(b => b != null ? b.GetInstanceID() : 0))}] t={Time.unscaledTime:0.00} delay {delay:0.00}");
            var banner = _effectBanner;
            _chainBadges.Clear();
            _effectBanner = null;
            Animate(delay + 0.3f, 0.3f, t =>
            {
                foreach (var b in badges) if (b != null) b.localScale = Vector3.one * (1 - t);
                if (banner != null) banner.localScale = new Vector3(1, 1 - t, 1);
            }, () =>
            {
                if (DebugBadges) Debug.Log($"clear done [{string.Join(",", badges.Select(b => b != null ? b.GetInstanceID() : 0))}] t={Time.unscaledTime:0.00}");
                foreach (var b in badges) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
                if (banner != null) UnityEngine.Object.Destroy(banner.gameObject);
            });
        }

        // ---- Big moments: text, flashes, cut-ins, the final blow ------------------------------------

        /// <summary>Huge italic text that punches in and fades ("DIRECT ATTACK").</summary>
        private void BigText(string text, Color color, float delay, float duration, float y)
        {
            var rt = Ui.Placed("BigText", _layer, new Vector2(0, y), new Vector2(1400, 140));
            var t = Ui.Text(rt, text, 84, new Color(color.r, color.g, color.b, 0));
            t.fontStyle = FontStyle.BoldAndItalic;
            t.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4, -4);
            Animate(delay, duration, k =>
            {
                rt.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, EaseOutCubic(Mathf.Clamp01(k * 4)));
                t.color = WithAlpha(color, k < 0.7f ? Mathf.Clamp01(k * 6) : (1 - k) / 0.3f);
            }, () => UnityEngine.Object.Destroy(rt.gameObject));
        }

        /// <summary>A full-field color flash that fades out (taking damage, the final blow).</summary>
        private void Flash(float delay, Color color, float duration)
        {
            if (!FxSettings.Flashes) return;
            var img = Ui.Image(Ui.Stretched("Flash", _layer, Vector2.zero, Vector2.one), WithAlpha(color, 0));
            img.raycastTarget = false;
            Animate(delay, Mathf.Max(0.05f, duration), k => img.color = WithAlpha(color, 0.45f * (1 - k)), () => UnityEngine.Object.Destroy(img.gameObject));
        }

        /// <summary>An ace hits the field: the screen darkens, the card's art sweeps across a band with its name.</summary>
        private float CutIn(CardInfo info, float delay)
        {
            // An ace's cut-in plays once per duel (every time when repeats are full).
            if (!FxSettings.CutIns || !First("cutin:" + info.Code)) return 0;
            var art = CardView.Art?.Get(info.Code);
            var duration = 1.1f * Tempo;
            if (duration <= 0) return 0;
            Started?.Invoke("cutin");
            Sound(delay, "summon_big");
            var dim = Ui.Image(Ui.Stretched("CutDim", _layer, Vector2.zero, Vector2.one), new Color(0, 0, 0, 0));
            dim.raycastTarget = false;
            var band = Ui.Placed("CutBand", _layer, Vector2.zero, new Vector2(1500, 300));
            var bandImg = Ui.Image(band, new Color(0.05f, 0.07f, 0.12f, 0));
            RawImage? picture = null;
            if (art != null)
            {
                picture = Ui.Placed("Art", band, new Vector2(-260, 0), new Vector2(260, 380)).gameObject.AddComponent<RawImage>();
                picture.texture = art;
                CardFoil.Apply(picture, info, 1f);
                CardFoil.Tilting(picture);
            }
            var name = Ui.Text(Ui.Placed("Name", band, new Vector2(240, 20), new Vector2(760, 90)), info.Name.ToUpperInvariant(), 46, new Color(1, 1, 1, 0));
            name.fontStyle = FontStyle.BoldAndItalic;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 20;
            name.resizeTextMaxSize = 46;
            name.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            var stats = Ui.Text(Ui.Placed("Stats", band, new Vector2(240, -46), new Vector2(760, 40)), $"ATK {CardDetail.Stat(info.Attack)}", 26, new Color(1, 0.8f, 0.3f, 0));
            Animate(delay, duration, k =>
            {
                float a = k < 0.15f ? k / 0.15f : k > 0.85f ? (1 - k) / 0.15f : 1;
                dim.color = new Color(0, 0, 0, 0.6f * a);
                bandImg.color = new Color(0.05f, 0.07f, 0.12f, 0.92f * a);
                band.localScale = new Vector3(1, Mathf.Lerp(0.2f, 1f, EaseOutCubic(Mathf.Clamp01(k / 0.15f))), 1);
                if (picture != null)
                {
                    picture.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-900, -260, EaseOutCubic(Mathf.Clamp01(k / 0.25f))) + 40 * k, 0);
                    picture.color = new Color(1, 1, 1, a);
                }
                name.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(900, 240, EaseOutCubic(Mathf.Clamp01((k - 0.08f) / 0.25f))), 20);
                name.color = new Color(1, 1, 1, a);
                stats.color = new Color(1, 0.8f, 0.3f, a);
            }, () =>
            {
                UnityEngine.Object.Destroy(dim.gameObject);
                UnityEngine.Object.Destroy(band.gameObject);
            });
            return duration;
        }

        /// <summary>The winning hit: "FINAL BLOW!" down both sides, a white flash, a heavy shake, then FINISHED.</summary>
        private float FinalBlow(float delay)
        {
            var duration = Mathf.Max(1.4f * Tempo, 0.6f);
            Started?.Invoke("finalblow");
            Sound(delay, "damage_big");
            for (int side = -1; side <= 1; side += 2)
            {
                var strip = Ui.Placed("FinalBlow", _layer, new Vector2(side * 640, 0), new Vector2(70, 1100));
                var bar = Ui.Image(strip, new Color(1f, 0.85f, 0.1f, 0));
                var label = Ui.Text(Ui.Placed("T", strip, Vector2.zero, new Vector2(1100, 70)), "FINAL BLOW!  FINAL BLOW!  FINAL BLOW!", 40, new Color(0.8f, 0.1f, 0.1f, 0));
                label.rectTransform.localRotation = Quaternion.Euler(0, 0, side * 90);
                label.fontStyle = FontStyle.BoldAndItalic;
                int s = side;
                Animate(delay, duration, k =>
                {
                    float a = k < 0.1f ? k / 0.1f : k > 0.8f ? (1 - k) / 0.2f : 1;
                    bar.color = new Color(1f, 0.85f, 0.1f, 0.9f * a);
                    label.color = new Color(0.8f, 0.1f, 0.1f, a);
                    label.rectTransform.anchoredPosition = new Vector2(0, s * 300 * k);
                }, () => UnityEngine.Object.Destroy(strip.gameObject));
            }
            Flash(delay + 0.2f * duration, Color.white, 0.4f * duration);
            Shake(delay + 0.2f * duration, 0.5f * duration, 22);
            BigText("FINISHED", Color.white, delay + 0.6f * duration, 0.8f * duration, 0);
            return duration;
        }

        // ---- Phase and turn banners ----------------------------------------------------------------------

        /// <summary>A banner that sweeps in from the left, holds, and sweeps out ("BATTLE PHASE").</summary>
        private float Sweep(string text, Color color, float delay, float y = 60)
        {
            var duration = 0.9f * Tempo;
            var rt = Ui.Placed("Banner", _layer, new Vector2(0, y), new Vector2(1400, 86));
            var bar = Ui.Image(rt, WithAlpha(color, 0));
            var label = Ui.Text(Ui.Stretched("T", rt, Vector2.zero, Vector2.one), text, 52, new Color(1, 1, 1, 0));
            label.fontStyle = FontStyle.BoldAndItalic;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            Animate(delay, duration, t =>
            {
                float x = t < 0.2f ? Mathf.Lerp(-1600, 0, EaseOutCubic(t / 0.2f)) : t < 0.8f ? Mathf.Lerp(0, 30, (t - 0.2f) / 0.6f)
                        : Mathf.Lerp(30, 1600, EaseInCubic((t - 0.8f) / 0.2f));
                rt.anchoredPosition = new Vector2(x, y);
                // Fades in as it slides in: at full strength from the first frame, its edge showed as a box at the
                // field's left edge (caught by screenshots taken as a turn starts).
                float a = t < 0.2f ? EaseOutCubic(t / 0.2f) : t < 0.8f ? 1 : 1 - (t - 0.8f) / 0.2f;
                bar.color = WithAlpha(color, 0.85f * a);
                label.color = new Color(1, 1, 1, a);
            }, () => UnityEngine.Object.Destroy(rt.gameObject));
            return duration * 0.85f;
        }

        private float PhaseBanner(DuelEvent e, float delay)
        {
            string? name = e.Amount switch
            {
                Ygo.Engine.Phase.Standby => "STANDBY PHASE", Ygo.Engine.Phase.BattleStart => "BATTLE PHASE",
                Ygo.Engine.Phase.Main2 => "MAIN PHASE 2", Ygo.Engine.Phase.End => "END PHASE", _ => null,
            };
            if (name == null) return 0;
            Started?.Invoke("phase");
            return Sweep(name, e.Amount == Ygo.Engine.Phase.BattleStart ? Ui.Hex("#B8322F") : Ui.Hex("#8A2B2B"), delay);
        }

        private float TurnBanner(DuelEvent e, float delay)
        {
            Started?.Invoke("turn");
            var mine = e.Player == _board.HumanSeat;
            return Sweep(_board.HumanSeat < 0 ? $"{_board.PlayerName(e.Player).ToUpperInvariant()}'S TURN" : mine ? "YOUR TURN" : "OPPONENT'S TURN",
                mine || _board.HumanSeat < 0 && e.Player == 0 ? Ui.Hex("#2F5FB8") : Ui.Hex("#2A3A66"), delay);
        }

        private float Spotlight(DuelEvent e, float delay)
        {
            var info = _data.Get(e.Code);
            if (info == null) return 0;
            var src = _board.PreviousPosition(e.From.Controller, e.From.Location, (int)e.From.Sequence);
            var duration = (First("spot:" + e.Code) ? 1.05f : 0.6f) * Tempo;
            Started?.Invoke("spotlight");
            Sound(delay, "activate");
            // The effect's words live in its callout (BoardView.ShowCallout) and the chain panel (BoardView.ShowChain).
            ChainBadge(e, delay);
            // And its text at the top right, stacked with the rest of the chain.
            AtTime(delay, () => _board.ShowCallout(e.Code, e.Player, e.ChainLink, e.Description));

            var dim = Ui.Image(Ui.Stretched("Dim", _layer, Vector2.zero, Vector2.one), new Color(0, 0, 0, 0));
            dim.raycastTarget = false;
            var tint = (info.Type & CardType.Spell) != 0 ? Ui.Hex("#3FE0A6") : (info.Type & CardType.Trap) != 0 ? Ui.Hex("#FF6FB8") : Ui.Hex("#FFB347");
            var center = new Vector2(0, 0);
            var glow = Sprite("Glow", Textures.Glow, center, new Vector2(520, 620), tint);
            var live = _board.LiveCard(new CardRef(e.Code, e.From.Controller, e.From.Location, e.From.Sequence, 0));
            var card = Ghost(e.Code, e.Player, src, faceUp: true, size: new Vector2(232, 338), cover: live?.Cover ?? 0);
            var group = card.gameObject.AddComponent<CanvasGroup>();
            var foil = card.GetComponentInChildren<CardFoil>();
            var caption = Ui.Text(Ui.Placed("Caption", _layer, center + new Vector2(0, -200), new Vector2(700, 40)), info.Name, 26, Color.white);
            caption.fontStyle = FontStyle.Bold;
            caption.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);

            float inEnd = 0.22f, outStart = 0.82f;
            _landed[(e.From.Controller, e.From.Location, (int)e.From.Sequence)] = (delay + inEnd * duration, 0.3f, card);
            Animate(delay, duration, t =>
            {
                float travel = EaseOutCubic(Mathf.Clamp01(t / inEnd));
                float leave = Mathf.Clamp01((t - outStart) / (1 - outStart));
                card.anchoredPosition = Vector2.LerpUnclamped(src, center, travel);
                card.localScale = Vector3.one * (Mathf.Lerp(0.35f, 1f, travel) + 0.08f * leave);
                group.alpha = 1 - leave;
                dim.color = new Color(0, 0, 0, 0.45f * Mathf.Min(travel, 1 - leave));
                glow.color = WithAlpha(tint, 0.75f * Mathf.Min(travel, 1 - leave));
                glow.rectTransform.localScale = Vector3.one * (0.9f + 0.1f * Mathf.Sin(t * 9));
                caption.color = WithAlpha(Color.white, Mathf.Min(travel, 1 - leave));
                if (foil != null && t > inEnd && t < inEnd + 0.05f) foil.Glint();
            }, () =>
            {
                UnityEngine.Object.Destroy(card.gameObject);
                UnityEngine.Object.Destroy(dim.gameObject);
                UnityEngine.Object.Destroy(glow.gameObject);
                UnityEngine.Object.Destroy(caption.gameObject);
            });
            return duration;
        }

        private float Negated(float delay, bool effectOnly)
        {
            var duration = 0.7f * Tempo;
            Started?.Invoke("negate");
            Sound(delay, effectOnly ? "negate_effect" : "negate");
            var text = Ui.Text(Ui.Placed("Negated", _layer, new Vector2(0, 0), new Vector2(760, 90)), effectOnly ? "EFFECT NEGATED" : "NEGATED", 64,
                effectOnly ? Ui.Hex("#C78CFF") : Ui.Hex("#FF4D4D"));
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            var rt = text.rectTransform;
            Animate(delay, duration, t =>
            {
                text.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, EaseOutCubic(Mathf.Clamp01(t * 4)));
                text.color = WithAlpha(effectOnly ? Ui.Hex("#C78CFF") : Ui.Hex("#FF4D4D"), t < 0.75f ? 1 : (1 - t) * 4);
            }, () => UnityEngine.Object.Destroy(rt.gameObject));
            return duration * 0.8f;
        }

        // ---- Battle ----------------------------------------------------------------------------------

        private float Attack(DuelEvent e, float delay)
        {
            var attacker = _board.ViewAt(e.From.Controller, Location.MZone, (int)e.From.Sequence);
            if (attacker == null) return 0;
            var start = attacker.anchoredPosition;
            bool direct = e.To.Location == 0;
            var target = direct
                ? BoardView.PlayerSide(1 - e.From.Controller)
                : _board.PreviousPosition(e.To.Controller, e.To.Location, (int)e.To.Sequence);
            // Heavier hitters hit harder: a bigger flash and shake.
            int atk = _board.LiveCard(e.From)?.Attack ?? 1500;
            float power = Mathf.Clamp01(atk / 3000f);
            var duration = 0.6f * Tempo;
            // The arrow first (where it's going, and the ATK comparison), then the strike. It stays until the attack is over.
            var from = e.From;
            var to = e.To;
            AtTime(delay, () => _board.ShowAttackArrow(from, to));
            float lead = 0.35f * Tempo;
            delay += lead;
            Started?.Invoke("attack");
            Sound(delay, "attack");
            if (direct) BigText("DIRECT ATTACK", Color.white, delay + duration * 0.3f, 0.7f * Tempo, 0);
            attacker.SetAsLastSibling();
            var baseRotation = attacker.localRotation;
            var dir = (target - start).normalized;
            float tilt = Mathf.Clamp(-dir.x * 25, -25, 25);
            var streak = Sprite("Streak", Textures.Glow, start, new Vector2(70, 10), Color.white);
            bool hit = false;
            Animate(delay, duration, t =>
            {
                // Wind up (pull back and lean), strike most of the way to the target, a beat on impact, then return.
                float k = t < 0.25f ? -0.12f * EaseOutCubic(t / 0.25f)
                        : t < 0.5f ? Mathf.Lerp(-0.12f, 0.8f, EaseInCubic((t - 0.25f) / 0.25f))
                        : t < 0.58f ? 0.8f
                        : Mathf.Lerp(0.8f, 0f, EaseOutCubic((t - 0.58f) / 0.42f));
                attacker.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
                attacker.localScale = Vector3.one * (1 + 0.2f * Mathf.Max(0, k));
                attacker.localRotation = baseRotation * Quaternion.Euler(0, 0, tilt * (t < 0.5f ? Mathf.Clamp01(t / 0.25f) : 1 - Mathf.Clamp01((t - 0.58f) / 0.42f)));
                // A streak of light behind the strike.
                bool striking = t >= 0.25f && t < 0.58f;
                var mid = Vector2.Lerp(start, attacker.anchoredPosition, 0.5f);
                streak.rectTransform.anchoredPosition = mid;
                streak.rectTransform.sizeDelta = new Vector2(Vector2.Distance(start, attacker.anchoredPosition) + 60, 26);
                streak.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                streak.color = WithAlpha(Color.white, striking ? 0.7f : Mathf.Max(0, streak.color.a - 0.08f));
                if (!hit && t >= 0.5f)
                {
                    hit = true;
                    Impact(Vector2.Lerp(start, target, 0.9f), power);
                }
            }, () =>
            {
                UnityEngine.Object.Destroy(streak.gameObject);
                if (attacker != null) attacker.localRotation = baseRotation;
            });
            return duration + lead;
        }

        private void Impact(Vector2 at, float power)
        {
            Sounds.Play("impact");
            var flash = Sprite("Impact", Textures.Glow, at, new Vector2(200, 200), Color.white);
            var burst = Sprite("Burst", Textures.Rays, at, new Vector2(300, 300), Ui.Hex("#FFE27A"));
            float size = Mathf.Lerp(1.4f, 2.4f, power);
            Animate(0, 0.3f * Tempo, t =>
            {
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, size, t);
                flash.color = WithAlpha(Color.white, (1 - t) * (FxSettings.Flashes ? 1 : 0.35f));
                burst.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, size, EaseOutCubic(t));
                burst.rectTransform.localRotation = Quaternion.Euler(0, 0, 30 * t);
                burst.color = WithAlpha(Ui.Hex("#FFE27A"), 1 - t);
            }, () => { UnityEngine.Object.Destroy(flash.gameObject); UnityEngine.Object.Destroy(burst.gameObject); });
            // Sparks fly out of the hit.
            var rng = new System.Random((int)(at.x * 13 + at.y));
            for (int i = 0; i < (FxSettings.Particles ? 10 : 0); i++)
            {
                var spark = Sprite("Spark", Textures.Glow, at, new Vector2(26, 26), Ui.Hex("#FFD27A"));
                var v = new Vector2((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1).normalized * (200 + 260 * (float)rng.NextDouble());
                Animate(0, 0.4f * Tempo, t =>
                {
                    spark.rectTransform.anchoredPosition = at + v * EaseOutCubic(t);
                    spark.color = WithAlpha(Ui.Hex("#FFD27A"), 1 - t);
                }, () => UnityEngine.Object.Destroy(spark.gameObject));
            }
            Shake(0, 0.28f * Tempo, Mathf.Lerp(5, 14, power));
        }

        // ---- Life points -----------------------------------------------------------------------------

        private float LifePoints(DuelEvent e, float delay)
        {
            if (e.Amount <= 0) return 0;
            var color = e.Kind == DuelEventKind.Recover ? Ui.Hex("#7CE38B") : e.Kind == DuelEventKind.Cost ? Ui.Hex("#FFB347") : Ui.Hex("#FF4D4D");
            var label = e.Kind == DuelEventKind.Recover ? $"+{e.Amount}" : $"-{e.Amount}";
            var size = e.Kind == DuelEventKind.Damage ? Mathf.RoundToInt(Mathf.Lerp(54, 92, Mathf.Clamp01(e.Amount / 4000f))) : 46;
            var start = BoardView.PlayerSide(e.Player) + new Vector2(0, e.Player == 0 ? 40 : -40);
            var duration = 1.0f * Tempo;
            Started?.Invoke("damage");
            Sound(delay, e.Kind == DuelEventKind.Recover ? "recover" : e.Kind == DuelEventKind.Cost ? "cost" : e.Amount >= 2000 ? "damage_big" : "damage");

            var holder = Ui.Placed("LpChange", _layer, start, new Vector2(500, 120));
            var text = Ui.Text(holder, label, size, color);
            text.fontStyle = FontStyle.Bold;
            text.gameObject.AddComponent<Outline>().effectDistance = new Vector2(3, -3);
            text.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -6);
            Animate(delay, duration, t =>
            {
                holder.anchoredPosition = start + new Vector2(0, 60 * EaseOutCubic(t));
                holder.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, EaseOutBack(Mathf.Clamp01(t * 5)));
                text.color = WithAlpha(color, t < 0.6f ? 1 : (1 - t) / 0.4f);
            }, () => UnityEngine.Object.Destroy(holder.gameObject));
            if (e.Kind == DuelEventKind.Damage && e.Amount >= 1000) Shake(delay, 0.35f * Tempo, Mathf.Min(18, e.Amount / 250f));
            if (e.Kind == DuelEventKind.Damage && (e.Player == _board.HumanSeat || e.Amount >= 2000)) Flash(delay, new Color(0.9f, 0.1f, 0.1f), 0.5f * Tempo);
            if (e.Kind == DuelEventKind.Damage && _board.LifePoints(e.Player) <= 0) return duration * 0.55f + FinalBlow(delay + duration * 0.3f);
            return duration * 0.55f;
        }

        /// <summary>A short label that rises from a point and fades ("-800 ATK").</summary>
        public void Floater(Vector2 at, string text, Color color)
        {
            if (Tempo <= 0) return;
            Started?.Invoke("floater");
            var rt = Ui.Placed("Floater", _layer, at, new Vector2(200, 34));
            var label = Ui.Text(rt, text, 22, color);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectDistance = new Vector2(2, -2);
            Animate(0, 1.3f * Tempo, t =>
            {
                rt.anchoredPosition = at + new Vector2(0, 30 + 40 * EaseOutCubic(t));
                label.color = WithAlpha(color, t < 0.7f ? 1 : (1 - t) / 0.3f);
            }, () => UnityEngine.Object.Destroy(rt.gameObject));
        }

        // ---- Helpers ---------------------------------------------------------------------------------

        private void Shake(float delay, float duration, float strength)
        {
            if (!FxSettings.Shake) return;
            Animate(delay, duration, t =>
            {
                var falloff = 1 - t;
                _cards.anchoredPosition = new Vector2(
                    Mathf.PerlinNoise(t * 40, 0.3f) - 0.5f,
                    Mathf.PerlinNoise(0.7f, t * 40) - 0.5f) * 2 * strength * falloff;
            }, () => _cards.anchoredPosition = Vector2.zero);
        }

        // ---- The prized moment ------------------------------------------------------------------------

        /// <summary>
        /// Where cards landed in this batch (by place): when, how long after their own sound the chime waits, and the
        /// spotlight's big card when it was activated.
        /// </summary>
        private readonly Dictionary<(int, uint, int), (float at, float chimeAfter, RectTransform? spotlight)> _landed =
            new Dictionary<(int, uint, int), (float, float, RectTransform?)>();

        /// <summary>For checks: prized moments played this run, and arrivals dropped (fast-forward, the setting off).</summary>
        public int PrizedPlayed { get; private set; }
        public int PrizesFastForwarded { get; private set; }
        public int PrizesOff { get; private set; }

        /// <summary>
        /// At most one prized copy per batch gets its moment, a full one before a glint: a sweep of light across the
        /// card, stars in its rarity's color (seeded by the copy, so it always sparkles the same way), and its chime in
        /// the music's key. It runs alongside the summon and never holds the duel. Reduced motion: a still luster, no
        /// stars; the chime stays.
        /// </summary>
        private void Prize(float batchEnd)
        {
            var arrivals = _board.Arrivals;
            if (arrivals.Count == 0) return;
            if (!FxSettings.PrizedMoments) { PrizesOff++; arrivals.Clear(); return; }
            var pick = arrivals.OrderByDescending(a => CardFoil.PrizeOf(a.look)).First();
            arrivals.Clear();
            _board.Prized.Add(pick.cover);
            PrizedPlayed++;
            var kind = CardFoil.PrizeOf(pick.look);
            bool calm = FxSettings.ReducedMotion;
            var (delay, chimeAfter, spotlight) = _landed.TryGetValue((pick.controller, pick.location, pick.sequence), out var landed)
                ? landed : (0f, 0.25f, null);
            var color = CardFoil.PrizeColor(pick.look.Rarity);
            CardFoil.Prizes[pick.cover] = new CardFoil.PrizeRun
            {
                Start = Time.unscaledTime + delay, Duration = calm ? 0.6f : kind == CardFoil.PrizeKind.Full ? 1.1f : 0.6f,
                Kind = kind, Calm = calm, Color = color,
            };
            Started?.Invoke("prize");

            var rarity = pick.look.Rarity;
            if (kind == CardFoil.PrizeKind.Glint) Sound(delay + chimeAfter, "prize_glint", 0.6f);
            else
            {
                var chime = rarity is Rarity.GhostRare or Rarity.GhostGoldRare ? "prize_ghost"
                    : rarity is Rarity.QuarterCenturySecretRare or Rarity.StarlightRare ? "prize_starlight" : "prize_secret";
                Sound(delay + chimeAfter, chime, 0.85f);
                if (pick.look.Grade >= 10)
                    Sound(delay + chimeAfter + (chime == "prize_ghost" ? 0.31f : chime == "prize_starlight" ? 0.34f : 0.23f), "prize_ping", 0.85f);
            }
            if (kind != CardFoil.PrizeKind.Full || calm) return;

            // The stars: from the card's outline, drifting out, one after another.
            var view = spotlight ?? _board.ViewAt(pick.controller, pick.location, pick.sequence);
            if (view == null) return;
            float scale = spotlight != null ? 2.2f : 1;
            var center = view.anchoredPosition;
            var size = view.sizeDelta * scale;
            bool top = rarity is Rarity.GhostRare or Rarity.QuarterCenturySecretRare or Rarity.StarlightRare;
            int n = top ? (pick.look.Grade >= 10 ? 12 : 8) : 10;
            for (int i = 0; i < n; i++)
            {
                float R(uint salt) => Hash.Mix(pick.look.Seed, 10u + (uint)i, salt) / (float)uint.MaxValue;
                // A point on the card's outline (inset 10%), and the way out from the middle.
                float edge = R(1) * 4;
                var local = edge < 1 ? new Vector2(Mathf.Lerp(-0.4f, 0.4f, edge), 0.4f)
                    : edge < 2 ? new Vector2(0.4f, Mathf.Lerp(0.4f, -0.4f, edge - 1))
                    : edge < 3 ? new Vector2(Mathf.Lerp(0.4f, -0.4f, edge - 2), -0.4f)
                    : new Vector2(-0.4f, Mathf.Lerp(-0.4f, 0.4f, edge - 3));
                var from = center + Vector2.Scale(local, size);
                var outward = local.normalized * Mathf.Lerp(10, 22, R(2)) * scale;
                float peak = view.sizeDelta.y * scale * Mathf.Lerp(0.18f, 0.30f, R(3));
                float spin = Mathf.Lerp(-30, 30, R(4));
                var tint = rarity == Rarity.StarlightRare ? Color.Lerp(Color.HSVToRGB((float)i / n, 0.8f, 1), Color.white, 0.3f)
                    : (rarity is Rarity.GoldSecretRare or Rarity.GhostGoldRare or Rarity.QuarterCenturySecretRare) && i % 3 == 2 ? Color.white : color;
                var star = Sprite("PrizeStar", CardView.Sparkle, from, new Vector2(peak, peak), tint);
                Animate(delay + (float)i / n * 0.45f, 0.55f, t =>
                {
                    star.rectTransform.anchoredPosition = from + outward * EaseOutCubic(t);
                    float grow = t < 0.25f ? EaseOutBack(t / 0.25f) : t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1;
                    star.rectTransform.localScale = Vector3.one * Mathf.Max(0, grow);
                    star.rectTransform.localRotation = Quaternion.Euler(0, 0, spin * t);
                    star.color = WithAlpha(tint, Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.7f));
                }, () => UnityEngine.Object.Destroy(star.gameObject));
            }
        }

        private RawImage Sprite(string name, Texture2D texture, Vector2 at, Vector2 size, Color color)
        {
            var rt = Ui.Placed(name, _layer, at, size);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = WithAlpha(color, 0);
            image.raycastTarget = false;
            return image;
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
        private static float EaseOutCubic(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);
        private static float EaseInCubic(float t) => Mathf.Pow(Mathf.Clamp01(t), 3);
        private static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c = 1.70158f;
            return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2);
        }

        /// <summary>Soft shapes for effects, drawn once in code (the game ships no image assets).</summary>
        public static class Textures
        {
            private static Texture2D? _glow, _ring, _rays, _spiral, _arrow;
            /// <summary>Three soft spiral arms (Fusion vortex, Xyz galaxy).</summary>
            public static Texture2D Spiral => _spiral ??= Make((x, y) =>
            {
                var r = Mathf.Sqrt(x * x + y * y);
                var a = Mathf.Atan2(y, x);
                var arms = Mathf.Pow(Mathf.Max(0, Mathf.Cos(a * 3 + r * 9)), 4);
                return arms * Mathf.Clamp01(1 - r) * Mathf.Clamp01(r * 3) * 1.2f;
            });
            /// <summary>A triangle pointing up (Link Arrows).</summary>
            public static Texture2D Arrow => _arrow ??= Make((x, y) =>
                Mathf.Clamp01((Mathf.Min(0.55f - Mathf.Abs(x) - (y + 0.1f) * 0.9f, y + 0.5f)) * 30));
            public static Texture2D Glow => _glow ??= Make((x, y) => Mathf.Pow(Mathf.Clamp01(1 - Mathf.Sqrt(x * x + y * y)), 2.2f));
            public static Texture2D Ring => _ring ??= Make((x, y) =>
            {
                var r = Mathf.Sqrt(x * x + y * y);
                return Mathf.Clamp01(1 - Mathf.Abs(r - 0.8f) / 0.08f) * (r < 1 ? 1 : 0);
            });
            public static Texture2D Rays => _rays ??= Make((x, y) =>
            {
                var r = Mathf.Sqrt(x * x + y * y);
                var a = Mathf.Atan2(y, x);
                var spokes = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 6)), 18);
                return spokes * Mathf.Clamp01(1 - r) * Mathf.Clamp01(r * 4);
            });

            private static Texture2D Make(Func<float, float, float> alpha)
            {
                const int size = 128;
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
}
