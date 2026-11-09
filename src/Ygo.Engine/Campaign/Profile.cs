using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// A batch of story scenes queued for the next campaign screen (<see cref="Profile.StoryQueue"/>): a result's or a notice's
    /// events, the placeholders its scenes fill, its card unlocks (display only) and its Tag Cup partner (who speaks a
    /// "partner:" line).
    /// </summary>
    public sealed class StoryBatch
    {
        public List<string> Events = new List<string>();
        public Dictionary<string, string> Fill = new Dictionary<string, string>();
        /// <summary>The result's card unlocks, for the scenes' {card} and {set} only (<see cref="Shown"/>): replaying a batch never pays.</summary>
        public List<Grant> Grants = new List<Grant>();
        public string Partner = "";

        /// <summary>
        /// A grant as a queued scene shows it: what <see cref="CardUnlocks.For"/> and the results line read (kind, card, rarity,
        /// event, names, text), never its copy (<see cref="Grant.Copy"/> is null): the copy was paid at the duel, and nothing
        /// that replays the queue pays a grant (the story screen only reads its names).
        /// </summary>
        public static Grant Shown(Grant g) => new Grant
        {
            Kind = g.Kind, Character = g.Character, Code = g.Code, Rarity = g.Rarity, Text = g.Text, Event = g.Event,
            CardName = g.CardName, SetName = g.SetName, SetCode = g.SetCode, Owed = g.Owed,
        };
    }

    /// <summary>What happened to a pulled card: kept, kept in place of a less rare copy, or turned into DP.</summary>
    public readonly struct Added
    {
        /// <summary>The card is in your collection now (false: a 4th copy that wasn't rarer, sold for DP).</summary>
        public readonly bool Kept;
        /// <summary>The less rare copy it replaced (sold for DP), if any.</summary>
        public readonly Rarity? Replaced;
        public readonly int Dp;
        /// <summary>The copy kept (null when it was sold).</summary>
        public readonly CardCopy? Copy;

        public Added(bool kept, Rarity? replaced, int dp, CardCopy? copy = null)
        {
            Kept = kept;
            Replaced = replaced;
            Dp = dp;
            Copy = copy;
        }
    }

    /// <summary>Where a copy came from; it sets the copy's grade odds (see <see cref="Grades"/>).</summary>
    public enum CopySource
    {
        /// <summary>A booster's regular slots.</summary>
        Pack,
        /// <summary>A booster's foil slot (Super Rare and up).</summary>
        Foil,
        /// <summary>Bought outright or traded for: the shop, the Card Trader, recipe packs, structure decks.</summary>
        Shop,
        /// <summary>The starter deck.</summary>
        Starter,
        /// <summary>A level-up or story reward.</summary>
        Reward,
        /// <summary>The used market (the post-game's second-hand copies).</summary>
        Used,
        /// <summary>A copy from a save made before copies were recorded one by one.</summary>
        Old,
        /// <summary>A bracket's Prize Pack (see <see cref="PrizePacks"/>): graded at the foil odds, paid 0, so it never sells for DP.</summary>
        Prize,
        /// <summary>Won at ante in the Back Alley (see <see cref="Ante"/>): its stake's grade and seed, paid 0, so it never sells for DP.</summary>
        Ante,
    }

    /// <summary>
    /// One copy of a card you own: the rarity it was printed at, its true grade (1-10, set when you got it and never
    /// rerolled), a seed for how its wear looks, and an id that stays with it (decks and replays point at copies by id).
    /// </summary>
    public sealed class CardCopy
    {
        public uint Code;
        public Rarity Rarity;
        /// <summary>The true grade, 1 (Damaged) to 10 (Gem Mint). A Raw copy shows only its condition word.</summary>
        public int Grade;
        public uint Seed;
        public int Id;
        public CopySource Source;
        /// <summary>DP put into this copy (its share of a pack, its price, grading fees); the Sell desk pays at most half.</summary>
        public int Paid;
        /// <summary>The grade on its Gauge &amp; Gilt slab (0: Raw). It can differ from <see cref="Grade"/> by one.</summary>
        public int Slab;
        /// <summary>The lowest a resubmission can come back: the slab it was cracked out of (0: never graded).</summary>
        public int Floor;
        /// <summary>Times it has been graded (each attempt's result is fixed by the seed and this count).</summary>
        public int Submissions;
        /// <summary>At the Grading Desk: the duel count it's back at (0: not at the desk).</summary>
        public int ReadyAt;
        /// <summary>
        /// The text its printing carries (a texts.txt row's version id, 0 = current), the printing's set code ("MRD-069") and
        /// month ("2002-06", by set name) (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.1). Set when the copy is made
        /// (<see cref="Collection.Stamp(CardCopy, string, string)"/>) and saved; a copy from an older save, or one whose
        /// source names no real printing, is current text, printing unknown (0, "", ""). <see cref="Text"/> is stored, not
        /// derived again, so a later correction to texts.txt never changes a copy you own.
        /// </summary>
        public uint Text;
        public string Set = "", Printed = "";

        public bool IsSlab => Slab > 0;
        public bool AtDesk => ReadyAt > 0;

        /// <summary>The grade you can see: the slab's, or a Raw copy's word (Raw 9 and 10 both read Mint: the desk tells them apart).</summary>
        public int Shown => IsSlab ? Slab : Math.Min(Grade, 9);

        /// <summary>The look a replay keeps: rarity, grade and wear seed.</summary>
        public CopyLook Look => new CopyLook(Rarity, Grade, Seed, Slab);
    }

    /// <summary>
    /// Card grades: the odds a copy's true grade is drawn with, by where it came from, and the labels. The odds are the
    /// post-game plan's (POST-GAME-PLAN-2026-10-31, "Where grades come from"); shop-bought copies are always 8.
    /// </summary>
    public static class Grades
    {
        /// <summary>Per-mille odds of grades 10 down to 1.</summary>
        private static readonly int[] PackOdds = { 100, 350, 230, 220, 50, 40, 5, 5, 0, 0 };
        private static readonly int[] FoilOdds = { 60, 340, 240, 240, 50, 50, 10, 10, 0, 0 };
        private static readonly int[] UsedOdds = { 10, 60, 180, 170, 160, 160, 90, 80, 50, 40 };
        /// <summary>Copies from older saves: 7 to 10 only, so a collection looks as it did.</summary>
        private static readonly int[] OldOdds = { 100, 250, 400, 250, 0, 0, 0, 0, 0, 0 };
        /// <summary>The grade of a copy bought new (shop, trader, recipe packs, structure decks, starters, rewards): Raw Near Mint.</summary>
        public const int Fresh = 8;

        /// <summary>The true grade for a copy from this source with this seed: always the same for the same pair.</summary>
        public static int Roll(CopySource source, uint seed)
        {
            var odds = source switch
            {
                CopySource.Pack => PackOdds,
                CopySource.Foil or CopySource.Prize => FoilOdds,
                CopySource.Used or CopySource.Ante => UsedOdds,
                CopySource.Old => OldOdds,
                _ => null,
            };
            if (odds == null) return Fresh;
            int roll = (int)(Hash.Mix(seed, 0x6A09E667u) % 1000), sum = 0;
            for (int i = 0; i < odds.Length; i++)
                if (roll < (sum += odds[i])) return 10 - i;
            return Fresh;
        }

        /// <summary>
        /// The per-mille odds of a copy's true grade from this source, grades 10 down to 1 (a copy bought new: all at
        /// <see cref="Fresh"/>). For the shop's "Condition when pulled" line (see <see cref="ConditionOdds"/>).
        /// </summary>
        public static int[] OddsFor(CopySource source)
        {
            var odds = source switch
            {
                CopySource.Pack => PackOdds,
                CopySource.Foil or CopySource.Prize => FoilOdds,
                CopySource.Used or CopySource.Ante => UsedOdds,
                CopySource.Old => OldOdds,
                _ => null,
            };
            if (odds != null) return (int[])odds.Clone();
            var fresh = new int[10];
            fresh[10 - Fresh] = 1000;
            return fresh;
        }

        /// <summary>
        /// The odds by condition word as a Raw copy shows it (9 and 10 both read Mint), best first, words with no chance
        /// left out: "Mint 45%, Near Mint 45%, Excellent 9%, Played 1%" for a pack's regular slots.
        /// </summary>
        public static List<(string word, double share)> ConditionOdds(CopySource source)
        {
            var odds = OddsFor(source);
            var words = new List<(string word, double share)>();
            for (int i = 0; i < odds.Length; i++)
            {
                var word = Word(Math.Min(9, 10 - i));
                if (words.Count > 0 && words[words.Count - 1].word == word) words[words.Count - 1] = (word, words[words.Count - 1].share + odds[i] / 1000.0);
                else words.Add((word, odds[i] / 1000.0));
            }
            return words.Where(w => w.share > 0).ToList();
        }

        /// <summary>The word for a grade: Gem Mint, Mint, Near Mint, Excellent, Played, Damaged.</summary>
        public static string Word(int grade) => grade switch
        {
            >= 10 => "Gem Mint",
            9 => "Mint",
            >= 7 => "Near Mint",
            >= 5 => "Excellent",
            >= 3 => "Played",
            _ => "Damaged",
        };

        /// <summary>"Raw · Near Mint": what an ungraded copy shows (its number stays hidden until it's graded).</summary>
        public static string RawLabel(int grade) => $"Raw · {Word(grade)}";

        /// <summary>"9 Mint": a graded copy's label.</summary>
        public static string Label(int grade) => $"{grade} {Word(grade)}";

        /// <summary>The band's glyph, shown beside its word so a condition never reads by color alone.</summary>
        public static string Glyph(int grade) => grade switch
        {
            >= 10 => "✦",
            9 => "★",
            >= 7 => "◆",
            >= 5 => "●",
            >= 3 => "▲",
            _ => "✕",
        };

        /// <summary>The band's color (#RRGGBB), readable on the game's dark panels.</summary>
        public static string Color(int grade) => grade switch
        {
            >= 10 => "#9CF5D0",
            9 => "#6FE39A",
            >= 7 => "#B7E07A",
            >= 5 => "#F2B65E",
            >= 3 => "#F2925A",
            _ => "#F06A6A",
        };

        /// <summary>
        /// What the wear looks like. A Raw copy describes its band only (so the words never give away the hidden
        /// number); a graded copy, its exact grade.
        /// </summary>
        public static string Flavor(int grade, bool raw) => raw
            ? grade switch
            {
                >= 10 => "flawless",
                9 => "sharp corners",
                >= 7 => "a soft corner",
                >= 5 => "whitened edges",
                >= 3 => "scratched, faded",
                _ => "creased, chipped",
            }
            : grade switch
            {
                >= 10 => "flawless",
                9 => "sharp corners",
                8 => "one soft corner",
                7 => "two soft corners",
                6 => "whitened edges",
                5 => "edge wear, scratch",
                4 => "scratched, faded",
                3 => "grimy, faded",
                2 => "creased, chipped",
                _ => "creased, stained",
            };

        /// <summary>A Raw copy's condition line: "Raw · Near Mint: a soft corner" (Raw 9 and 10 both read Mint).</summary>
        public static string Condition(int grade) => $"{RawLabel(Math.Min(grade, 9))}: {Flavor(Math.Min(grade, 9), raw: true)}";

        /// <summary>A copy's condition as anyone can see it: "Raw · Mint: sharp corners", or for a slab "Grade 9 · Mint: sharp corners".</summary>
        public static string Describe(CardCopy copy) => copy.IsSlab
            ? $"Grade {copy.Slab} · {Word(copy.Slab)}: {Flavor(copy.Slab, raw: false)}"
            : Condition(copy.Grade);
    }

    /// <summary>A small integer hash, the same on every machine (copy seeds and grades must never change between runs).</summary>
    public static class Hash
    {
        public static uint Mix(uint a, uint b, uint c = 0)
        {
            ulong x = a * 0x9E3779B97F4A7C15UL ^ (b + 0x632BE59BD9B4E019UL) * 0xBF58476D1CE4E5B9UL ^ (c + 0x94D049BB133111EBUL);
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (uint)x;
        }
    }

    /// <summary>
    /// The cards you own, one record per copy (<see cref="CardCopy"/>: rarity, grade, seed, id), at most
    /// <see cref="MaxCopies"/> of a card (you can't use more in a deck). Alternate artworks count as their base card.
    /// A copy's seed comes from the save's salt, its id and the card, so its grade is fixed the moment you get it.
    /// Copies are ordered by value (<see cref="Pricing.V"/>): the best one plays first, the worst is the one a 4th copy
    /// replaces. Copies at the Grading Desk stay yours but can't be played or sold until they're back.
    /// </summary>
    public sealed class Collection
    {
        public const int MaxCopies = 3;

        /// <summary>
        /// What a 4th copy paid before copies had prices (5 Common up to 300 Starlight/Ghost). Copies from older saves count
        /// twice this as what you paid, so the Sell desk pays them exactly that.
        /// </summary>
        public static int SellValue(Rarity r) => r switch
        {
            Rarity.Common or Rarity.ShortPrint => 5,
            Rarity.Rare or Rarity.NormalParallelRare => 10,
            Rarity.SuperRare or Rarity.RareParallelRare or Rarity.StarfoilRare or Rarity.MosaicRare or Rarity.ShatterfoilRare => 20,
            Rarity.SecretRare or Rarity.UltraParallelRare or Rarity.PharaohsRare or Rarity.PremiumGoldRare => 60,
            Rarity.StarlightRare or Rarity.GhostRare or Rarity.GhostGoldRare => 300,
            _ => 40, // Ultra, Ultimate, Gold, Platinum and the rest
        };

        /// <summary>Each card's copies, best first (see <see cref="Compare"/>).</summary>
        private readonly Dictionary<uint, List<CardCopy>> _cards = new Dictionary<uint, List<CardCopy>>();

        /// <summary>This save's salt for copy seeds (0 for a scratch collection), and the next copy's id.</summary>
        public uint Salt;
        public int NextId = 1;

        /// <summary>
        /// The text timeline a copy's text is read from when it's stamped (<see cref="Stamp(CardCopy, string, string)"/>), and
        /// what <see cref="Assign"/>, <see cref="Take"/> and <see cref="CollectionRules"/> tell texts apart by. Set it where
        /// <see cref="CommonestOf"/> is set, before any copy is made: with <see cref="CardTexts.Empty"/> every stamp reads current.
        /// </summary>
        public CardTexts Texts = CardTexts.Empty;

        /// <summary>
        /// The printings packs, boxes, structure decks and the starter stamp their copies from (the other sources pass their
        /// own). Set it with <see cref="Texts"/>; null: those copies are made unstamped (current text, printing unknown).
        /// </summary>
        public PrintingIndex? Printings;

        /// <summary>
        /// Keep the lone version (spec 4.3, knob KeepLoneText): a 4th copy never displaces your only copy of its text, and a
        /// new copy of a text you don't own yet stays. False: the lowest-valued Raw copy goes, whatever its text.
        /// </summary>
        public static bool KeepLoneText = true;

        private Func<uint, Rarity>? _commonest;

        /// <summary>
        /// A card's commonest printing (base code), which sets its value floor; set once printings are loaded. Without it a
        /// copy is valued as if its own rarity were the commonest.
        /// </summary>
        public Func<uint, Rarity>? CommonestOf
        {
            get => _commonest;
            set
            {
                _commonest = value;
                foreach (var list in _cards.Values) list.Sort(Compare);
            }
        }

        public IEnumerable<uint> Codes => _cards.Keys;
        public int Total => _cards.Values.Sum(c => c.Count);
        public int Distinct => _cards.Count;

        /// <summary>A copy's value: the one price every screen shows (<see cref="Pricing.V"/>).</summary>
        public int Value(CardCopy copy) => Pricing.V(copy, _commonest?.Invoke(copy.Code) ?? copy.Rarity);

        /// <summary>The copy's Raw Near Mint value (its rarity against the card's commonest printing): the grading fee's basis.</summary>
        public int Basis(CardCopy copy) => Pricing.Basis(copy.Rarity, _commonest?.Invoke(copy.Code) ?? copy.Rarity);

        /// <summary>What the Sell desk (and a replaced 4th copy) pays: a quarter of its value, at most half of what you paid.</summary>
        public int SellPrice(CardCopy copy) => Math.Min(Value(copy) / 4, copy.Paid / 2);

        /// <summary>Best first: the higher value, then a slab before a Raw copy, then the older, so the order never changes on its own.</summary>
        public int Compare(CardCopy a, CardCopy b)
        {
            int va = Value(a), vb = Value(b);
            if (va != vb) return vb.CompareTo(va);
            if (a.IsSlab != b.IsSlab) return a.IsSlab ? -1 : 1;
            return a.Id.CompareTo(b.Id);
        }

        /// <summary>
        /// Adds new copies (bought, traded, rewarded...), each graded for <paramref name="source"/> and stamped with the
        /// printing <paramref name="setCode"/> dated <paramref name="printedYm"/> ("" for none: unstamped).
        /// </summary>
        public void Add(uint baseCode, Rarity rarity, int copies = 1, CopySource source = CopySource.Shop, int paid = 0, string setCode = "", string printedYm = "")
        {
            for (int i = 0; i < copies; i++)
            {
                var copy = NewCopy(baseCode, rarity, source);
                copy.Paid = paid;
                Stamp(copy, setCode, printedYm);
                Insert(copy);
            }
        }

        /// <summary>
        /// Stamps a copy with its printing (spec 4.1): the set code, the month it was printed ("yyyy-mm"; a full date is cut
        /// to its month) and the text that printing carries (<see cref="CardTexts.PrintedText"/> by <see cref="Texts"/>; 0 for
        /// a card with no timeline row). An empty set code or date leaves it unstamped: current text, printing unknown.
        /// </summary>
        public void Stamp(CardCopy copy, string setCode, string printedYm)
        {
            setCode ??= "";
            printedYm ??= "";
            // No timeline loaded (a missing texts.txt, a caller that never set Texts): unstamped, so no copy is frozen as current.
            bool known = setCode.Length > 0 && printedYm.Length >= 7 && Texts.Count > 0;
            copy.Set = known ? setCode : "";
            copy.Printed = known ? printedYm.Substring(0, 7) : "";
            copy.Text = known ? Texts.PrintedText(copy.Code, copy.Printed) : 0;
        }

        /// <summary>Stamps a copy with a printing dated by <see cref="PrintingIndex.DateOf"/> (null: unstamped).</summary>
        public void Stamp(CardCopy copy, Printing? printing, PrintingIndex printings)
        {
            if (printing is Printing p) Stamp(copy, p.SetCode, printings.DateOf(p));
            else Stamp(copy, "", "");
        }

        /// <summary>
        /// A stored text as it plays (as <see cref="PlayTexts"/> reads a copy): a collectible row's canonical id (a dup's
        /// canonical entry), else 0, the current text (0 itself, a row since removed or marked `never` or `cpu`, or no texts).
        /// </summary>
        private uint Canonical(uint text) => text != 0 && Texts.Row(text) is TextRow row && row.Collectible ? row.Code : 0;

        /// <summary>
        /// The text a deck slot asks for (spec 4.3): 0 for the current text (a base card or alternate art, and a version entry
        /// no copy can carry: a `never` or `cpu` row, a Pre-Errata entry with no row, a ruling-only GOAT entry); else the
        /// collectible row a copy must carry: a Pre-Errata entry's own row (a dup's canonical), a GOAT entry's card's earliest.
        /// </summary>
        public static uint AskedText(uint slot, CardTexts texts, ICardDataProvider cards)
        {
            var info = cards.Get(slot);
            if (info == null) return 0;
            var baseCode = Banlist.BaseCode(slot, cards);
            if (baseCode == slot) return 0;
            switch (CardVersions.KindOf(info))
            {
                case CardVersions.TextKind.Goat: return texts.Earliest(baseCode)?.Code ?? 0;
                case CardVersions.TextKind.PreErrata: return texts.Row(slot) is TextRow row && row.Collectible ? row.Code : 0;
                default: return 0;
            }
        }

        /// <summary>A new copy with the next id, its seed from the salt, id and card, and its grade from the seed.</summary>
        public CardCopy NewCopy(uint baseCode, Rarity rarity, CopySource source)
        {
            int id = NextId++;
            uint seed = Hash.Mix(Salt, (uint)id, baseCode);
            return new CardCopy { Code = baseCode, Rarity = rarity, Id = id, Seed = seed, Source = source, Grade = Grades.Roll(source, seed) };
        }

        /// <summary>Puts a copy in as it is (loading a save, or copying another collection's).</summary>
        public void Insert(CardCopy copy)
        {
            if (!_cards.TryGetValue(copy.Code, out var list)) _cards[copy.Code] = list = new List<CardCopy>();
            list.Add(copy);
            list.Sort(Compare);
            if (copy.Id >= NextId) NextId = copy.Id + 1;
        }

        /// <summary>Takes a copy out (sold); false if it isn't here.</summary>
        public bool Remove(CardCopy copy)
        {
            if (!_cards.TryGetValue(copy.Code, out var list) || !list.Remove(copy)) return false;
            if (list.Count == 0) _cards.Remove(copy.Code);
            return true;
        }

        /// <summary>Orders a card's copies again after one changed (graded, cracked).</summary>
        public void Resort(uint baseCode)
        {
            if (_cards.TryGetValue(baseCode, out var list)) list.Sort(Compare);
        }

        /// <summary>
        /// Adds a pulled card, stamped with the printing <paramref name="setCode"/> dated <paramref name="printedYm"/> ("": unstamped).
        /// A 4th copy isn't kept: of your Raw copies not at the desk and the new one, the lowest value goes and sells
        /// (<see cref="SellPrice"/>), keeping the lone version (<see cref="Take"/>); if all three you own are slabs or at the
        /// desk, the new one sells. A booster's Super Rare and up are graded at the foil slot's odds. <paramref name="paid"/>
        /// is its share of what you paid (-1: as an old save's copy).
        /// </summary>
        public Added AddPulled(uint baseCode, Rarity rarity, CopySource source = CopySource.Pack, int paid = -1, string setCode = "", string printedYm = "")
        {
            if (source == CopySource.Pack && rarity >= Rarity.SuperRare) source = CopySource.Foil;
            var copy = NewCopy(baseCode, rarity, source);
            copy.Paid = paid >= 0 ? paid : 2 * SellValue(rarity);
            Stamp(copy, setCode, printedYm);
            return Take(copy);
        }

        /// <summary>
        /// The copy a 4th copy displaces, or the new copy itself when it isn't kept (spec 4.3). Of your Raw copies not at the
        /// desk and the new one, the lowest value goes; with <see cref="KeepLoneText"/>, only among those whose text the four
        /// copies hold at least twice (the new one counted), so whichever goes, every text you'd have held is still held: your
        /// only copy of a text never goes, a new copy of a text you don't own stays, and a better copy of a text you own swaps
        /// with the worse one. When no copy qualifies, the lowest value goes as before. Either way one of the four goes.
        /// </summary>
        private CardCopy Outgoing(CardCopy copy)
        {
            var mine = _cards[copy.Code];
            var raw = mine.Where(c => !c.IsSlab && !c.AtDesk).ToList();
            var order = Comparer<CardCopy>.Create(Compare);
            if (KeepLoneText)
            {
                var held = mine.Append(copy).Select(c => Canonical(c.Text)).ToList();
                var eligible = raw.Append(copy).Where(c => held.Count(t => t == Canonical(c.Text)) >= 2).ToList();
                if (eligible.Count > 0) return eligible.OrderBy(c => c, order).Last();
            }
            return raw.Append(copy).OrderBy(c => c, order).Last();
        }

        /// <summary>Whether a copy would stay if it joined (not a 4th copy that <see cref="Take"/> would sell on the spot).</summary>
        public bool WouldKeep(CardCopy copy) => Owned(copy.Code) < MaxCopies || Outgoing(copy) != copy;

        /// <summary>Adds a copy made elsewhere (bought used), with the 4th-copy rule (<see cref="Outgoing"/>); the result says what went.</summary>
        public Added Take(CardCopy copy)
        {
            if (Owned(copy.Code) < MaxCopies)
            {
                Insert(copy);
                return new Added(true, null, 0, copy);
            }
            var worst = Outgoing(copy);
            if (worst == copy) return new Added(false, null, SellPrice(copy), null);
            Remove(worst);
            Insert(copy);
            return new Added(true, worst.Rarity, SellPrice(worst), copy);
        }

        /// <summary>
        /// Copies you can play of a card: every one but those at the Grading Desk. Decks, the editor and legality count
        /// these; <see cref="Owned"/> counts them all.
        /// </summary>
        public int Count(uint baseCode) => _cards.TryGetValue(baseCode, out var c) ? c.Count(x => !x.AtDesk) : 0;

        /// <summary>Copies you can play of a card with one text (0: the current text; a dup counts as its canonical row).</summary>
        public int Count(uint baseCode, uint text)
        {
            if (!_cards.TryGetValue(baseCode, out var c)) return 0;
            var want = Canonical(text);
            return c.Count(x => !x.AtDesk && Canonical(x.Text) == want);
        }

        /// <summary>Every copy you own of a card, those at the desk included (the 3-copy cap counts these).</summary>
        public int Owned(uint baseCode) => _cards.TryGetValue(baseCode, out var c) ? c.Count : 0;

        public bool Owns(uint baseCode) => _cards.ContainsKey(baseCode);

        /// <summary>The rarest copy you own, or null.</summary>
        public Rarity? Best(uint baseCode) => _cards.TryGetValue(baseCode, out var c) && c.Count > 0 ? c.Max(x => x.Rarity) : (Rarity?)null;

        /// <summary>A card's copies, best first (those at the desk included).</summary>
        public IReadOnlyList<CardCopy> CopiesOf(uint baseCode) =>
            _cards.TryGetValue(baseCode, out var c) ? c : (IReadOnlyList<CardCopy>)Array.Empty<CardCopy>();

        /// <summary>Every copy you own.</summary>
        public IEnumerable<CardCopy> All => _cards.Values.SelectMany(c => c);

        /// <summary>Owned copies by rarity, rarest first.</summary>
        public IEnumerable<(Rarity rarity, int copies)> Copies(uint baseCode) =>
            CopiesOf(baseCode).GroupBy(c => c.Rarity).OrderByDescending(g => g.Key).Select(g => (g.Key, g.Count()));

        /// <summary>
        /// The copy each slot of a deck plays (main deck, then extra deck, in the deck's order), per text (spec 4.3): a slot
        /// asking for an original (<see cref="AskedText"/>) takes a copy of that text, best first; then the current slots take
        /// the rest, current-text copies first (so originals stay free), then originals; a slot still without one takes
        /// whatever is left (it plays its current text). Within each pass a card's slots go in deck order and copies best
        /// first, so the same deck shows the same copies every duel. Null where you don't own enough (a borrowed card in a
        /// loaner) or the copy is at the Grading Desk. Alternate artworks use their base card's copies.
        /// </summary>
        public List<CardCopy?> Assign(Deck deck, ICardDataProvider cards)
        {
            var codes = deck.Main.Concat(deck.Extra).ToList();
            var bases = codes.Select(c => Banlist.BaseCode(c, cards)).ToList();
            var asked = codes.Select(c => AskedText(c, Texts, cards)).ToList();
            // A GOAT slot plays with any collectible original of its card (PlayTexts), not only the earliest it asks for.
            var goat = codes.Select((c, i) => asked[i] != 0 && cards.Get(c) is CardInfo info && CardVersions.KindOf(info) == CardVersions.TextKind.Goat).ToList();
            var free = new Dictionary<uint, List<CardCopy>>();
            List<CardCopy> Free(uint baseCode) => free.TryGetValue(baseCode, out var l) ? l : free[baseCode] = CopiesOf(baseCode).Where(c => !c.AtDesk).ToList();
            var slots = new CardCopy?[codes.Count];
            void Pass(Func<int, bool> slot, Func<int, CardCopy, bool> fits)
            {
                for (int i = 0; i < codes.Count; i++)
                {
                    if (slots[i] != null || !slot(i)) continue;
                    var list = Free(bases[i]);
                    var copy = list.FirstOrDefault(c => fits(i, c));
                    if (copy == null) continue;
                    list.Remove(copy);
                    slots[i] = copy;
                }
            }
            Pass(i => asked[i] != 0 && !goat[i], (i, c) => Canonical(c.Text) == asked[i]);
            Pass(i => goat[i], (i, c) => Canonical(c.Text) != 0);
            Pass(i => asked[i] == 0, (i, c) => Canonical(c.Text) == 0);
            Pass(i => asked[i] == 0, (i, c) => true);
            Pass(i => true, (i, c) => true);
            return slots.ToList();
        }

        /// <summary>
        /// Rebuilds a save's old "card code rarity copies" line as copies: Raw, graded 7 to 10 from a seed of the card,
        /// rarity and copy number (the same for every save), so collections look as they did. They count as paid twice
        /// the old 4th-copy price, so the Sell desk pays that.
        /// </summary>
        public void AddOld(uint baseCode, Rarity rarity, int copies)
        {
            for (int i = 0; i < copies; i++)
            {
                uint seed = Hash.Mix(baseCode, (uint)rarity, (uint)(Owned(baseCode) + 1));
                Insert(new CardCopy
                {
                    Code = baseCode, Rarity = rarity, Id = NextId++, Seed = seed, Source = CopySource.Old,
                    Grade = Grades.Roll(CopySource.Old, seed), Paid = 2 * SellValue(rarity),
                });
            }
        }

        internal IEnumerable<(uint code, Rarity rarity, int copies)> Entries() =>
            _cards.SelectMany(c => c.Value.GroupBy(x => x.Rarity).Select(g => (c.Key, g.Key, g.Count())));
    }

    /// <summary>
    /// The DP ledger's names (plan step 11): every DP change goes through <see cref="Profile.Earn"/> from one of these sources
    /// or <see cref="Profile.Spend"/> at one of these sinks. Stable: they're saved ("earned"/"spent" lines) and measured.
    /// </summary>
    public static class Ledger
    {
        // ---- Sources ----
        /// <summary>Duel wins by kind (first-win bonus included): circuits, Tag Cups, guests, Library cups, the final's rounds.</summary>
        public const string Circuit = "circuit", Tag = "tag", Guest = "guest", Cup = "cup", Final = "final";
        /// <summary>A Library cup's Locals placement pay.</summary>
        public const string Locals = "locals";
        /// <summary>The DP a lost or drawn duel pays: "circuit.loss", "tag.loss", ...</summary>
        public static string Loss(string kind) => kind + ".loss";
        /// <summary>A clear's prize (or a guest's first-win prize): "circuit.prize", "tag.prize", "guest.prize", "cup.prize", "final.prize".</summary>
        public static string Prize(string kind) => kind + ".prize";
        public const string LevelUp = "levelup";
        /// <summary>4th copies turned into DP: boosters and selection packs, recipe packs, structure decks, the used market, special orders.</summary>
        public const string PackRefund = "refund.pack", RecipeRefund = "refund.recipe", StructureRefund = "refund.structure",
            UsedRefund = "refund.used", OrderRefund = "refund.order";
        public const string Sell = "sell";
        /// <summary>Ban Pass slots bought when they were sold, refunded at load.</summary>
        public const string PassRefund = "passrefund";
        /// <summary>DP a check or a debug tool added (never in play).</summary>
        public const string Debug = "debug";
        /// <summary>The Back Alley's: bracket placement pay (Alley Brawl, the Midnight Open), and a copy a van purchase or a rail claim displaced (the 4th-copy rule).</summary>
        public const string Alley = "alley", AlleyRefund = "refund.alley";

        // ---- Sinks ----
        /// <summary>Real boosters and selection (house) packs (<see cref="PackSet.Sink"/>), and booster boxes (<see cref="BoosterBox"/>).</summary>
        public const string Packs = "packs", SelectionPacks = "packs.selection", Boxes = "packs.box";
        /// <summary>Recipe packs, a recipe bought outright, and Make it yours (a loaner's list bought outright).</summary>
        public const string RecipePack = "recipe.pack", RecipeOutright = "recipe.outright", MakeItYours = "makeityours";
        public const string Structure = "structure", Tokens = "tokens", Grading = "grading", Used = "used", Order = "order";
        /// <summary>The Library's sinks: era mats, engraved plates, Locals entries (<see cref="Cosmetics"/>, <see cref="Brackets"/>).</summary>
        public const string Mats = "mats", Engrave = "engrave", Brackets = "brackets";
        /// <summary>
        /// The Back Alley's sinks: a held DP stake lost (a loss or a quit; a stake that comes back writes no line), Moth's rail
        /// buy-backs, the contraband shelf, Moth's used contraband, Alley Brawl and Midnight Open entries.
        /// </summary>
        public const string AlleyStake = "alley.stake", AlleyRail = "alley.rail", AlleyShelf = "alley.shelf", AlleyUsed = "alley.used",
            AlleyBrackets = "alley.brackets";
        /// <summary>
        /// No Limits (docs/dev/NO-LIMITS-ECONOMY-2026-11-02.md 6.2): a No Limits stake lost (a loss or a quit; a stake that comes
        /// back writes no line), Invitational entries; and, a source, Invitational placement pay.
        /// </summary>
        public const string NoLimitsStake = "nolimits.stake", NoLimitsEntry = "nolimits.entry", NoLimits = "nolimits";
        /// <summary>The Champion's Ladder's duels (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 5): "ladder", "ladder.loss", "ladder.prize".</summary>
        public const string Ladder = Campaign.Ladder.LedgerKind;

        /// <summary>sum(earned) - sum(spent): what the ledger says DP moved since a save started (== Dp - StartingDp for a save started with it).</summary>
        public static int Net(Profile profile) => profile.DpEarned.Values.Sum() - profile.DpSpent.Values.Sum();
    }

    /// <summary>
    /// Your campaign save: Duel Points, wins and losses against each character, your cards, and your duel deck.
    /// Stored as plain text (mods/save/profile.txt), so it's easy to back up or inspect.
    /// </summary>
    public sealed class Profile
    {
        public const int StartingDp = 1000;

        public int Dp = StartingDp;
        public Dictionary<string, int> Wins { get; } = new Dictionary<string, int>();
        /// <summary>The Tag Cup partner you picked last (a character id; "" = the default), and the deck each partner brings.</summary>
        public string TagPartner = "";
        public Dictionary<string, string> PartnerDecks { get; } = new Dictionary<string, string>();
        public Dictionary<string, int> Losses { get; } = new Dictionary<string, int>();
        /// <summary>Campaign matches won, keyed "circuit.match" (0-based), with how many times.</summary>
        public Dictionary<string, int> MatchWins { get; } = new Dictionary<string, int>();
        /// <summary>Losses in a row at a match (circuit, Tag Cup or guest, by its match key), until you win it.</summary>
        public Dictionary<string, int> LossStreaks { get; } = new Dictionary<string, int>();
        /// <summary>
        /// The circuits and Tag Cups (names, in order) this save's match keys count by; null for a save from before they
        /// were recorded. <see cref="Circuits.Migrate"/> moves the keys when circuits are added in between.
        /// </summary>
        public List<string>? CircuitLayout, TagCupLayout;

        /// <summary>After a duel at a match: a loss adds to its run of losses, a win ends it (a draw leaves it).</summary>
        public void NoteResult(string matchKey, int winner)
        {
            DuelsPlayed++;
            if (winner == 0) LossStreaks.Remove(matchKey);
            else if (winner == 1) LossStreaks[matchKey] = LossesInARow(matchKey) + 1;
        }

        public int LossesInARow(string matchKey) => LossStreaks.TryGetValue(matchKey, out var n) ? n : 0;

        public static string MatchKey(int circuit, int match) => $"{circuit}.{match}";

        /// <summary>Campaign duels recorded (circuits, Tag Cups, guests): the Grading Desk and the used market count by them.</summary>
        public int DuelsPlayed;
        /// <summary>The used market's period on show and the listings sold in it (they come back when it changes).</summary>
        public int MarketEpoch = -1;
        public HashSet<int> MarketSold { get; } = new HashSet<int>();
        public bool HasWon(int circuit, int match) => MatchWins.ContainsKey(MatchKey(circuit, match));
        public Collection Collection { get; } = new Collection();
        /// <summary>Name of the deck you take into campaign duels.</summary>
        public string Deck = "";
        public int PacksOpened;
        /// <summary>Duelist EXP (see <see cref="Leveling"/>); the level follows from it.</summary>
        public int Exp;
        public int Level => Leveling.LevelFor(Exp);
        /// <summary>Card Tokens to spend at the Card Trader, and how many were spent.</summary>
        public int Tokens, TokensSpent;
        /// <summary>Card Tokens bought with DP (see <see cref="DpServices"/>; each one raises the next price).</summary>
        public int TokensBought;
        /// <summary>
        /// Ban Pass slots bought with DP, from saves made when they were sold. Loading refunds them (<see cref="PassRefund"/>)
        /// and sets this to 0.
        /// </summary>
        public int PassesBought;
        /// <summary>DP refunded at load for bought Ban Pass slots (not saved; for a one-time note).</summary>
        public int PassRefund;
        /// <summary>Story scenes already shown (see <see cref="Story"/>).</summary>
        public HashSet<string> Seen { get; } = new HashSet<string>();

        /// <summary>
        /// Story scenes queued and not played yet, a batch per result or notice in the order queued (the app's queue: a result
        /// whose results screen never showed, or was left before its Continue), so quitting the app can't lose them; the next
        /// campaign screen after a load plays them. Saved as optional "storyq" lines (an older build ignores them; an old save
        /// loads with none). A batch's grants are display only (<see cref="StoryBatch.Shown"/>): the card unlocks were paid at
        /// the duel, and a batch keeps only what its scenes' {card} and {set} need, never the copy.
        /// </summary>
        public List<StoryBatch> StoryQueue { get; } = new List<StoryBatch>();

        /// <summary>
        /// No Limits Packs owed by an Invitational finished while its packs couldn't be opened (<see cref="Brackets.SettleHeld"/>):
        /// each pack's seed, the one its Finish would have opened it from. <see cref="Brackets.PayOwedPacks"/> opens them once
        /// the pool can fill a pack. Saved as optional "nlowed" lines; New Game+ keeps them.
        /// </summary>
        public List<uint> NoLimitsPacksOwed { get; } = new List<uint>();

        /// <summary>New Game+ cycles started (0 = the first playthrough).</summary>
        public int Cycle;

        /// <summary>
        /// Card unlocks paid or offered (see <see cref="CardUnlocks"/>), each once per save: "gift:&lt;id&gt;" (a rival's
        /// first-win gift), "giftowed:&lt;id&gt;" (held until it's in the shop's era), "bondtrade:&lt;id&gt;" (the bond 3
        /// trade offered), "bondtraded:&lt;id&gt;" (taken), "bondgift:&lt;id&gt;" (the bond 5 gift), "bondgiftowed:&lt;id&gt;",
        /// "bounty:&lt;id&gt;" (claimed) and "bountytoken:&lt;band&gt;" (that band's one token paid). New Game+ keeps them.
        /// </summary>
        public HashSet<string> Unlocks { get; } = new HashSet<string>();
        /// <summary>Shop sets completed (set code -> the card Gus framed for it, its poster's art).</summary>
        public Dictionary<string, uint> SetsDone { get; } = new Dictionary<string, uint>();
        /// <summary>Bounties claimed while there was no Recipe to pay from (and the band's token was paid): paid later.</summary>
        public int BountyOwed;
        /// <summary>
        /// Whether a save from before card unlocks has had the gifts for duelists it already beat (a new save: true).
        /// Saved as "unlocks 1"; <see cref="CardUnlocks.CatchUp"/> pays them once.
        /// </summary>
        public bool UnlocksCaughtUp;

        /// <summary>
        /// Counts a win against a duelist and notes it on the reward (<see cref="DuelReward.WinsAdded"/>), so
        /// <see cref="CardUnlocks.AfterDuel"/> can see which wins crossed a bond. Every win against a character goes through here.
        /// </summary>
        public void AddWin(string id, DuelReward reward)
        {
            int wins = WinsAgainst(id) + 1;
            Wins[id] = wins;
            reward.WinsAdded.Add((id, wins));
        }

        /// <summary>
        /// The Format Library (see <see cref="Library"/>): open once the Tier 0 Gauntlet was cleared, its match wins (keyed
        /// <see cref="Library.Key"/>, by cup name) and the wins taken with a deck of yours and no Ban Passes (for its stamps).
        /// New Game+ keeps all three: the Library is the post-game, and its first clears pay once.
        /// </summary>
        public bool LibraryOpen;
        public Dictionary<string, int> LibraryWins { get; } = new Dictionary<string, int>();
        public HashSet<string> LibraryBuilt { get; } = new HashSet<string>();
        /// <summary>Your own decks (no loaners) that have won a match at each Library cup (cup id -> deck names): the ones its binder can be engraved with.</summary>
        public Dictionary<string, HashSet<string>> LibraryDecks { get; } = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// The Library's cosmetics (see <see cref="Cosmetics"/>): the era mats you own, the one on your side of the board
        /// ("" for the free space mat), and the cups whose binders are engraved (cup id -> the deck's title as engraved:
        /// a later rename doesn't change the plate).
        /// </summary>
        public HashSet<string> Mats { get; } = new HashSet<string>();
        public string Mat = "";
        public Dictionary<string, string> Engravings { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Brackets (see <see cref="Brackets"/>): the one running (null for none), brackets entered (each entry's seed counts
        /// by it), and each cup's Locals history (cup id -> entries, titles, best placement). New Game+ keeps all three.
        /// </summary>
        public Bracket? Bracket;
        public int BracketsEntered;
        public Dictionary<string, LocalsRecord> Locals { get; } = new Dictionary<string, LocalsRecord>();

        /// <summary>
        /// The Back Alley (see <see cref="BackAlley"/>, <see cref="Ante"/>; docs/dev/ALLEY-SPEC-2026-11-01.md section 14): open once a
        /// Library cup is cleared; alley duels played (binder refills count by them); each table's night and first clear
        /// (table id -> state); the alley brackets' records (kind -> entries, titles, best; apart from <see cref="Locals"/>);
        /// tonight's opponents as built (and an encore, seat 0); your record against each headliner; each CPU's stake binder
        /// (owner key -> binder); the stakes of a duel being played (null between duels: a save loaded with one settles it
        /// as a loss); your cards on Moth's rail; the Unlisted binder's pages; the van's periods on show and their sold
        /// slots; stakes won and lost. New Game+ keeps all of it: the alley is post-game, and its first clears pay once.
        /// </summary>
        public bool AlleyOpen;
        public int AlleyDuels;
        public Dictionary<string, AlleyTableState> AlleyTables { get; } = new Dictionary<string, AlleyTableState>();
        public Dictionary<string, LocalsRecord> AlleyBrackets { get; } = new Dictionary<string, LocalsRecord>();
        public List<AlleyOpponent> AlleyOpponents { get; } = new List<AlleyOpponent>();
        public Dictionary<string, HeadlinerState> Headliners { get; } = new Dictionary<string, HeadlinerState>();
        public Dictionary<string, StakeBinder> Binders { get; } = new Dictionary<string, StakeBinder>();
        public AnteLive? AnteLive;
        public List<RailEntry> Rail { get; } = new List<RailEntry>();
        public List<UnlistedPage> UnlistedPages { get; } = new List<UnlistedPage>();
        public int VanUsedEpoch = -1, VanShelfEpoch = -1;
        public HashSet<int> VanUsedSold { get; } = new HashSet<int>();
        public HashSet<int> VanShelfSold { get; } = new HashSet<int>();
        /// <summary>The shelf's slabs bought this period, by what they are (<see cref="MothsVan.ShelfKey"/>); VanShelfSold keeps an older save's sold slots.</summary>
        public HashSet<string> VanShelfSoldCards { get; } = new HashSet<string>();
        public int AnteWins, AnteLosses;

        /// <summary>
        /// The campaign duel of yours being played (a circuit match, a Tag Cup match, a guest, a Library cup match or the
        /// champion's table, an "Every Era" round, a Locals game), saved from its Duel! until its result is recorded; null
        /// between duels. A save loaded with one settles it as a loss (<see cref="LiveDuels.Settle"/>). New Game+ clears it.
        /// </summary>
        public LiveDuel? LiveDuel;

        /// <summary>
        /// The Champion's Ladder (see <see cref="Campaign.Ladder"/>): your rungs, the visitors beaten, the top, and Ilse's sitting.
        /// Saved as optional "ladwin"/"ladsit" lines (a save without them reads as unclimbed). New Game+ leaves it alone, so a
        /// first climb never pays twice.
        /// </summary>
        public LadderState Ladder = new LadderState();

        /// <summary>
        /// The DP ledger (plan step 11): DP spent at each sink and earned from each source (<see cref="Ledger"/> names them).
        /// Every DP change goes through <see cref="Spend"/> or <see cref="Earn"/>, so for a save started with the ledger,
        /// sum(earned) - sum(spent) == Dp - <see cref="StartingDp"/>. Saved as "spent" and "earned" lines; New Game+ keeps them.
        /// </summary>
        public Dictionary<string, int> DpSpent { get; } = new Dictionary<string, int>();
        public Dictionary<string, int> DpEarned { get; } = new Dictionary<string, int>();

        /// <summary>Pays <paramref name="dp"/> at a sink and counts it; false (nothing paid) when you can't afford it.</summary>
        public bool Spend(string sink, int dp)
        {
            if (dp < 0 || Dp < dp) return false;
            Dp -= dp;
            if (dp > 0) DpSpent[sink] = DpSpent.GetValueOrDefault(sink) + dp;
            return true;
        }

        /// <summary>Credits <paramref name="dp"/> from a source and counts it (0 or less: nothing happens).</summary>
        public void Earn(string source, int dp)
        {
            if (dp <= 0) return;
            Dp += dp;
            DpEarned[source] = DpEarned.GetValueOrDefault(source) + dp;
        }

        /// <summary>
        /// The one DP move with no ledger line: a duel's alley stakes put on the table (<see cref="Ante.Hold"/>). The DP stake
        /// leaves Dp into <paramref name="live"/>'s <see cref="AnteLive.Held"/>, since it isn't spent yet (mid-duel the ledger
        /// reconciles as Dp + held), and <paramref name="live"/> becomes <see cref="AnteLive"/>. Only when nothing is held
        /// (false, and nothing moves, otherwise): every DP held is <see cref="AnteLive"/>'s, so it can't go missing.
        /// </summary>
        public bool HoldStake(AnteLive live)
        {
            if (AnteLive != null) return false;
            if (live.Held > 0) Dp -= live.Held;
            AnteLive = live;
            return true;
        }

        /// <summary>
        /// The held stakes taken off the table as a duel settles (<see cref="Ante.Settle"/>'s first step, still no ledger
        /// line): <paramref name="live"/>'s held DP back into Dp, before a loss <see cref="Spend"/>s it, and
        /// <see cref="AnteLive"/> cleared. Returns the DP returned; 0 (a no-op) unless <paramref name="live"/> is the
        /// stake being held, so it returns only what <see cref="HoldStake"/> took, once.
        /// </summary>
        public int ReturnStake(AnteLive live)
        {
            if (AnteLive == null || !ReferenceEquals(AnteLive, live)) return 0;
            AnteLive = null;
            int held = Math.Max(0, live.Held);
            Dp += held;
            return held;
        }

        /// <summary>
        /// New Game+: the circuits start over from the first one, and everything you've earned stays (cards, decks, DP,
        /// level, tokens, and your record against each duelist). Seen story and shop lines stay seen; only the shop's
        /// once-per-opponent-per-circuit marks go, since the circuits they count are played again. The Library, the
        /// Back Alley and the Champion's Ladder are untouched (New Game+ can't start mid-duel, so no alley stakes are held across it).
        /// </summary>
        public void NewGamePlus()
        {
            LiveDuel = null;
            // A guest's one-on-one prize won this cycle stays paid (GuestList.PrizeKey; read for "prize: <DP> once" guests): marked
            // here too, so a save from before the mark keeps it through its next New Game+.
            foreach (var key in MatchWins.Keys.Where(k => k.StartsWith("guest:", StringComparison.Ordinal)).ToList()) Unlocks.Add(GuestList.PrizeKey(key.Substring(6)));
            MatchWins.Clear();
            LossStreaks.Clear();
            Seen.RemoveWhere(key => key.StartsWith(ShopVoice.OpponentSeen, StringComparison.Ordinal));
            Cycle++;
        }

        /// <summary>
        /// The historic challenge, chosen when the campaign starts (off by default): tokens only buy cards released up to
        /// the shop's era, and a circuit with a ban list only allows cards released by its format.
        /// </summary>
        public bool EraLimited;
        /// <summary>The poster on your plate (a <see cref="Posters"/> id, "emblem" for the gold emblem, "" for the first poster).</summary>
        public string Poster = "";
        /// <summary>Cards you're after (base code -> copies wanted), set by the deck editor's Recipe; the shop ranks sets by it.</summary>
        public Dictionary<uint, int> Wishlist { get; } = new Dictionary<uint, int>();
        /// <summary>Your level when each band's era opened in the shop (era -> level): levels since then open later sets.</summary>
        public Dictionary<int, int> BandStartLevel { get; } = new Dictionary<int, int>();

        /// <summary>The deck the wishlist is for.</summary>
        public string WishlistFor = "";

        /// <summary>
        /// The card shop's memory (SHOP-REWORK-SPEC section 7): visits from the campaign (<see cref="Arrivals.Visit"/>), the
        /// newest shop year seen (0: never visited; it only grows, so New Game+ brings nothing back as new), the years whose
        /// sets arrived on which visit (dropped once they're no longer new), and each voice hook's count of lines played once
        /// all were heard (<see cref="ShopVoice"/>). New Game+ leaves all four.
        /// </summary>
        public int ShopVisits, ShopYearSeen;
        public Dictionary<int, int> Arrivals { get; } = new Dictionary<int, int>();
        public Dictionary<string, int> VoiceCounts { get; } = new Dictionary<string, int>();

        /// <summary>
        /// Opens a pack you've paid for: every card goes into the collection, and extra copies (past 3) turn into DP, at
        /// most half the pack's price per pack, so packs of a finished set never make DP. <paramref name="refund"/> is
        /// the DP credited.
        /// </summary>
        public List<(Pull pull, bool isNew, Added added)> OpenPack(PackSet set, Random rng, out int refund)
        {
            var opened = new List<(Pull, bool, Added)>();
            int sold = 0;
            var pulls = PackCatalog.Open(set, rng).ToList();
            // Each pull's share of the price (by what it's worth), so the Sell desk knows what you paid for it.
            var shares = Pricing.Shares(set.Price, pulls.Select(p => Pricing.Basis(p.Rarity, Collection.CommonestOf?.Invoke(p.Code) ?? p.Rarity)).ToList());
            for (int i = 0; i < pulls.Count; i++)
            {
                var pull = pulls[i];
                bool isNew = !Collection.Owns(pull.Code);
                var (setCode, printed) = PackPrint(set, pull, Collection.Printings);
                var added = Collection.AddPulled(pull.Code, pull.Rarity, CopySource.Pack, shares[i], setCode, printed);
                sold += added.Dp;
                opened.Add((pull, isNew, added));
            }
            refund = Math.Min(sold, set.Price / 2);
            Earn(Ledger.PackRefund, refund);
            PacksOpened++;
            return opened;
        }

        /// <summary>
        /// The printing a pack's pull is (spec 4.2), as (set code, date), ("", "") for none: a real booster's card in that set
        /// at the pulled rarity (<see cref="CopyPrints.InSet"/>), dated as the pack's set (so a 2002 booster's pull is the 2002
        /// printing even where a later reprint shares its code); a house pack's, as a shop copy by the shop's year it was
        /// built for; an OCG import's, none (no TCG printing: current text). Nothing here touches the pack's Random.
        /// </summary>
        public static (string setCode, string printed) PackPrint(PackSet set, Pull pull, PrintingIndex? printings)
        {
            if (printings == null || ImportShelf.IsImport(set)) return ("", "");
            if (SelectionPacks.IsHousePack(set))
                return CopyPrints.Shop(pull.Code, pull.Rarity, set.StampYear, printings, null) is Printing h ? (h.SetCode, printings.DateOf(h)) : ("", "");
            var p = CopyPrints.InSet(pull.Code, pull.Rarity, set.Info.Code, printings, null);
            if (p == null) return ("", "");
            return (p.Value.SetCode, set.Info.Date.Length >= 7 ? set.Info.Date : printings.DateOf(p.Value));
        }

        /// <summary>
        /// Buys a structure deck: every card goes into the collection (up to 3 of each; extra copies pay DP like a
        /// pack's would). Returns the cards kept. The caller saves the deck as a campaign deck. Each copy is the deck's set's
        /// printing when the deck file names it (`#set &lt;prefix&gt;`), else a shop copy by <paramref name="shopYear"/>.
        /// </summary>
        public int BuyStructureDeck(Deck deck, int price, ICardDataProvider cards, int shopYear = 9999)
        {
            if (!Spend(Ledger.Structure, price)) return 0;
            int kept = 0, sold = 0;
            var codes = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, cards)).ToList();
            var shares = Pricing.Shares(price, codes.Select(c => Pricing.Basis(Rarity.Common, Collection.CommonestOf?.Invoke(c) ?? Rarity.Common)).ToList());
            var prefix = deck.Note("set").Trim();
            for (int i = 0; i < codes.Count; i++)
            {
                string setCode = "", printed = "";
                if (Collection.Printings is PrintingIndex printings)
                {
                    Printing? p = null;
                    if (prefix.Length > 0)
                        p = CopyPrints.InSet(codes[i], Rarity.Common, prefix, printings, cards)
                            ?? printings.Of(codes[i], cards.Get(codes[i])?.Alias ?? 0).Where(x => PrintingIndex.SetPrefix(x.SetCode) == prefix)
                                .OrderBy(x => x.SetCode, StringComparer.Ordinal).Select(x => (Printing?)x).FirstOrDefault();
                    p ??= CopyPrints.Shop(codes[i], Rarity.Common, shopYear, printings, cards) ?? CopyPrints.NewestOfAny(codes[i], shopYear, printings, cards);
                    if (p is Printing found) { setCode = found.SetCode; printed = printings.DateOf(found); }
                }
                var added = Collection.AddPulled(codes[i], Rarity.Common, CopySource.Shop, shares[i], setCode, printed);
                if (added.Kept) kept++;
                sold += added.Dp;
            }
            // Extra copies pay back at most half the price, like a pack's.
            Earn(Ledger.StructureRefund, Math.Min(sold, price / 2));
            return kept;
        }

        /// <summary>Wishlist cards you still have fewer copies of than you want.</summary>
        public HashSet<uint> Wanted() => new HashSet<uint>(Wishlist.Where(w => Collection.Count(w.Key) < w.Value).Select(w => w.Key));
        public DateTime Started = DateTime.Now;

        public int WinsAgainst(string id) => Wins.TryGetValue(id, out var n) ? n : 0;
        public bool HasBeaten(string id) => WinsAgainst(id) > 0;

        /// <summary>
        /// A new save: the starter deck's cards (at their most common printing, 3 copies at most, each the newest printing at
        /// that rarity: current text) and some DP. <paramref name="texts"/> is the text timeline the collection stamps by
        /// (<see cref="Collection.Texts"/>).
        /// </summary>
        public static Profile New(Deck starter, ICardDataProvider cards, PrintingIndex? printings, CardTexts? texts = null)
        {
            var profile = new Profile { Deck = starter.Name, UnlocksCaughtUp = true };
            profile.Collection.Salt = (uint)Guid.NewGuid().GetHashCode();
            if (texts != null) profile.Collection.Texts = texts;
            profile.Collection.Printings = printings;
            if (printings != null) profile.Collection.CommonestOf = code => printings.Commonest(code, cards.Get(code)?.Alias ?? 0);
            foreach (var code in starter.Main.Concat(starter.Extra).Concat(starter.Side))
            {
                var baseCode = Banlist.BaseCode(code, cards);
                var info = cards.Get(baseCode);
                var list = printings?.Of(baseCode, info?.Alias ?? 0);
                var rarity = list != null && list.Length > 0 ? list[list.Length - 1].Rarity : Rarity.Common;
                var print = printings != null ? CopyPrints.Newest(baseCode, rarity, 9999, printings, cards) : null;
                if (profile.Collection.Count(baseCode) < Collection.MaxCopies)
                    profile.Collection.Add(baseCode, rarity, 1, CopySource.Starter, 0, print?.SetCode ?? "", print is Printing p ? printings!.DateOf(p) : "");
            }
            return profile;
        }

        // ---- Save file -------------------------------------------------------------------------------

        /// <summary>
        /// Writes the save (via a temporary file, so a crash mid-save never loses the old one). <paramref name="backup"/>: the old
        /// save becomes profile.txt.bak first (one step back); a progress-only save (the story queue, a scene seen) passes false,
        /// so the backup stays the last real save's.
        /// </summary>
        public void Save(string path, bool backup = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# YgoCampaign save. One fact per line; one line per card copy: copy <id> <rarity> <grade> <wear seed> <copy id> <source>.");
            sb.AppendLine("version 1");
            sb.AppendLine($"started {Started.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"dp {Dp}");
            sb.AppendLine($"deck {Deck}");
            sb.AppendLine($"packs {PacksOpened}");
            sb.AppendLine($"exp {Exp}");
            sb.AppendLine($"tokens {Tokens} {TokensSpent}");
            sb.AppendLine($"bought {TokensBought} {PassesBought}");
            if (TagPartner.Length > 0) sb.AppendLine($"partner {TagPartner}");
            foreach (var d in PartnerDecks.OrderBy(p => p.Key)) sb.AppendLine($"partnerdeck {d.Key} {d.Value}");
            sb.AppendLine($"eralimited {(EraLimited ? 1 : 0)}");
            if (Poster.Length > 0) sb.AppendLine($"poster {Poster}");
            sb.AppendLine($"cycle {Cycle}");
            if (UnlocksCaughtUp) sb.AppendLine("unlocks 1");
            foreach (var u in Unlocks.OrderBy(k => k, StringComparer.Ordinal)) sb.AppendLine($"unlock {u}");
            foreach (var d in SetsDone.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.AppendLine($"setdone {d.Key} {d.Value}");
            if (BountyOwed > 0) sb.AppendLine($"bountyowed {BountyOwed}");
            if (LibraryOpen) sb.AppendLine("library 1");
            foreach (var w in LibraryWins.OrderBy(p => p.Key)) sb.AppendLine($"libwin {w.Key} {w.Value}");
            foreach (var b in LibraryBuilt.OrderBy(k => k)) sb.AppendLine($"libbuilt {b}");
            foreach (var d in LibraryDecks.OrderBy(p => p.Key)) foreach (var deck in d.Value.OrderBy(n => n)) sb.AppendLine($"libdeck {d.Key} {deck}");
            foreach (var m in Mats.OrderBy(k => k)) sb.AppendLine($"matown {m}");
            if (Mat.Length > 0) sb.AppendLine($"mat {Mat}");
            foreach (var e in Engravings.OrderBy(p => p.Key)) sb.AppendLine($"engrave {e.Key} {e.Value}");
            foreach (var s in DpSpent.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.AppendLine($"spent {s.Key} {s.Value}");
            foreach (var e in DpEarned.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.AppendLine($"earned {e.Key} {e.Value}");
            Brackets.Write(this, sb);
            LiveDuels.Write(this, sb);
            WriteAlley(sb);
            Ladder.Write(sb);
            foreach (var id in Seen.OrderBy(s => s)) sb.AppendLine($"seen {id}");
            WriteStoryQueue(sb);
            foreach (var w in Wins.OrderBy(p => p.Key)) sb.AppendLine($"win {w.Key} {w.Value}");
            foreach (var l in Losses.OrderBy(p => p.Key)) sb.AppendLine($"loss {l.Key} {l.Value}");
            foreach (var m in MatchWins.OrderBy(p => p.Key)) sb.AppendLine($"match {m.Key} {m.Value}");
            foreach (var r in LossStreaks.OrderBy(p => p.Key)) sb.AppendLine($"streak {r.Key} {r.Value}");
            if (CircuitLayout != null) sb.AppendLine($"circuits {string.Join("|", CircuitLayout)}");
            if (TagCupLayout != null) sb.AppendLine($"tagcups {string.Join("|", TagCupLayout)}");
            if (WishlistFor.Length > 0) sb.AppendLine($"wishfor {WishlistFor}");
            foreach (var w in Wishlist.OrderBy(p => p.Key)) sb.AppendLine($"wish {w.Key} {w.Value}");
            foreach (var b in BandStartLevel.OrderBy(p => p.Key)) sb.AppendLine($"bandlevel {b.Key} {b.Value}");
            if (ShopVisits > 0) sb.AppendLine($"shopvisits {ShopVisits}");
            if (ShopYearSeen > 0) sb.AppendLine($"shopseen {ShopYearSeen}");
            foreach (var a in Arrivals.OrderBy(p => p.Key)) sb.AppendLine($"arrival {a.Key} {a.Value}");
            foreach (var v in VoiceCounts.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.AppendLine($"voice {v.Key} {v.Value}");
            // One line per copy: card, rarity, grade, wear seed, id, source.
            sb.AppendLine($"copies {Collection.Salt} {Collection.NextId}");
            foreach (var c in Collection.All.OrderBy(c => c.Code).ThenBy(c => c.Id))
                sb.AppendLine($"copy {CopyFields(c)}");
            sb.AppendLine($"duels {DuelsPlayed}");
            if (MarketSold.Count > 0) sb.AppendLine($"market {MarketEpoch} {string.Join(",", MarketSold.OrderBy(x => x))}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Write to a temporary file first so a crash mid-save never loses the old one.
            var temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString());
            if (File.Exists(path))
            {
                if (backup) File.Copy(path, path + ".bak", overwrite: true);
                File.Delete(path);
            }
            File.Move(temp, path);
        }

        public static Profile? Load(string path)
        {
            if (!File.Exists(path)) return null;
            var profile = new Profile();
            var oldCards = new List<(uint code, Rarity rarity, int copies)>();
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var space = line.IndexOf(' ');
                var key = space < 0 ? line : line.Substring(0, space);
                var rest = space < 0 ? "" : line.Substring(space + 1);
                var parts = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                switch (key)
                {
                    case "dp": int.TryParse(rest, out profile.Dp); break;
                    case "deck": profile.Deck = rest; break;
                    case "packs": int.TryParse(rest, out profile.PacksOpened); break;
                    case "exp": int.TryParse(rest, out profile.Exp); break;
                    case "tokens" when parts.Length >= 1:
                        int.TryParse(parts[0], out profile.Tokens);
                        if (parts.Length > 1) int.TryParse(parts[1], out profile.TokensSpent);
                        break;
                    case "bought" when parts.Length >= 1:
                        int.TryParse(parts[0], out profile.TokensBought);
                        if (parts.Length > 1) int.TryParse(parts[1], out profile.PassesBought);
                        break;
                    case "partner": profile.TagPartner = rest; break;
                    case "partnerdeck" when parts.Length >= 2:
                        profile.PartnerDecks[parts[0]] = rest.Substring(parts[0].Length).Trim();
                        break;
                    case "eralimited": profile.EraLimited = rest != "0"; break;
                    case "poster": profile.Poster = rest; break;
                    case "duels": int.TryParse(rest, out profile.DuelsPlayed); break;
                    case "market" when parts.Length >= 1 && int.TryParse(parts[0], out var epoch):
                        profile.MarketEpoch = epoch;
                        if (parts.Length > 1) foreach (var slot in parts[1].Split(',')) if (int.TryParse(slot, out var k)) profile.MarketSold.Add(k);
                        break;
                    case "cycle": int.TryParse(rest, out profile.Cycle); break;
                    case "unlocks": profile.UnlocksCaughtUp = rest != "0"; break;
                    case "unlock": if (rest.Length > 0) profile.Unlocks.Add(rest); break;
                    case "setdone" when parts.Length == 2 && uint.TryParse(parts[1], out var doneCard): profile.SetsDone[parts[0]] = doneCard; break;
                    case "bountyowed": int.TryParse(rest, out profile.BountyOwed); break;
                    case "library": profile.LibraryOpen = rest != "0"; break;
                    case "libwin" when parts.Length == 2 && int.TryParse(parts[1], out var lw): profile.LibraryWins[parts[0]] = lw; break;
                    case "libbuilt": if (rest.Length > 0) profile.LibraryBuilt.Add(rest); break;
                    case "libdeck" when parts.Length >= 2:
                        if (!profile.LibraryDecks.TryGetValue(parts[0], out var libDecks)) profile.LibraryDecks[parts[0]] = libDecks = new HashSet<string>();
                        libDecks.Add(rest.Substring(parts[0].Length + 1).Trim());
                        break;
                    case "spent" when parts.Length == 2 && int.TryParse(parts[1], out var spentDp): profile.DpSpent[parts[0]] = spentDp; break;
                    case "earned" when parts.Length == 2 && int.TryParse(parts[1], out var earnedDp): profile.DpEarned[parts[0]] = earnedDp; break;
                    case "matown": if (rest.Length > 0) profile.Mats.Add(rest); break;
                    case "mat": profile.Mat = rest; break;
                    case "engrave" when parts.Length >= 2: profile.Engravings[parts[0]] = rest.Substring(parts[0].Length + 1).Trim(); break;
                    case "seen": if (rest.Length > 0) profile.Seen.Add(rest); break;
                    case "storyq": case "storyqevent": case "storyqfill": case "storyqgrant": profile.ReadStoryQueue(key, rest, parts); break;
                    case "brackets": case "locals": case "bracket": case "bracketslot": case "bracketresult": case "bracketsize": case "bracketgames": case "bracketsplash":
                    case "nlowed":
                        Brackets.Read(profile, key, rest, parts);
                        break;
                    case "liveduel": profile.LiveDuel = LiveDuels.Read(parts); break;
                    case LadderState.WinLine: case LadderState.SittingLine: profile.Ladder.ReadLine(key, parts); break;
                    case "alley": case "alleyduels": case "alleytable": case "alleybracket": case "alleyopp": case "headliner": case "binder": case "stake":
                    case "antelive": case "antetheirs": case "anteyours": case "rail": case "unlisted": case "vanused": case "vanshelf": case "ante":
                        profile.ReadAlley(key, rest, parts);
                        break;
                    case "started":
                        if (DateTime.TryParse(rest, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
                            profile.Started = started.ToLocalTime();
                        break;
                    case "win" when parts.Length == 2 && int.TryParse(parts[1], out var w): profile.Wins[parts[0]] = w; break;
                    case "loss" when parts.Length == 2 && int.TryParse(parts[1], out var l): profile.Losses[parts[0]] = l; break;
                    case "match" when parts.Length == 2 && int.TryParse(parts[1], out var m): profile.MatchWins[parts[0]] = m; break;
                    case "streak" when parts.Length == 2 && int.TryParse(parts[1], out var r): profile.LossStreaks[parts[0]] = r; break;
                    case "circuits": profile.CircuitLayout = rest.Split('|').ToList(); break;
                    case "tagcups": profile.TagCupLayout = rest.Split('|').ToList(); break;
                    case "wishfor": profile.WishlistFor = rest; break;
                    case "bandlevel" when parts.Length == 2 && int.TryParse(parts[0], out var era) && int.TryParse(parts[1], out var lvl):
                        profile.BandStartLevel[era] = lvl;
                        break;
                    case "shopvisits": int.TryParse(rest, out profile.ShopVisits); break;
                    case "shopseen": int.TryParse(rest, out profile.ShopYearSeen); break;
                    case "arrival" when parts.Length == 2 && int.TryParse(parts[0], out var arrivedYear) && int.TryParse(parts[1], out var arrivedAt):
                        profile.Arrivals[arrivedYear] = arrivedAt;
                        break;
                    // "voice <hook> N": the hook may contain spaces ("shelf:Beginner Circuit"), so the count is the last word.
                    case "voice" when parts.Length >= 2 && int.TryParse(parts[parts.Length - 1], out var voiced):
                        profile.VoiceCounts[rest.Substring(0, rest.LastIndexOf(' ')).Trim()] = voiced;
                        break;
                    case "wish" when parts.Length == 2 && uint.TryParse(parts[0], out var wish) && int.TryParse(parts[1], out var want):
                        profile.Wishlist[wish] = want;
                        break;
                    case "copies" when parts.Length == 2 && uint.TryParse(parts[0], out var salt) && int.TryParse(parts[1], out var next):
                        profile.Collection.Salt = salt;
                        profile.Collection.NextId = Math.Max(profile.Collection.NextId, next);
                        break;
                    case "copy" when parts.Length >= 5 && uint.TryParse(parts[0], out var copyCode) && Enum.TryParse<Rarity>(parts[1], out var copyRarity)
                                     && int.TryParse(parts[2], out var grade) && uint.TryParse(parts[3], out var seed) && int.TryParse(parts[4], out var id):
                        profile.Collection.Insert(new CardCopy
                        {
                            Code = copyCode, Rarity = copyRarity, Grade = Math.Max(1, Math.Min(10, grade)), Seed = seed, Id = id,
                            Source = parts.Length > 5 && Enum.TryParse<CopySource>(parts[5], out var source) ? source : CopySource.Old,
                            // A copy saved before prices: paid twice the old 4th-copy price.
                            Paid = parts.Length > 6 && int.TryParse(parts[6], out var paid) ? paid : 2 * Collection.SellValue(copyRarity),
                            Slab = parts.Length > 7 && int.TryParse(parts[7], out var slab) ? Math.Max(0, Math.Min(10, slab)) : 0,
                            Floor = parts.Length > 8 && int.TryParse(parts[8], out var floor) ? floor : 0,
                            Submissions = parts.Length > 9 && int.TryParse(parts[9], out var subs) ? subs : 0,
                            ReadyAt = parts.Length > 10 && int.TryParse(parts[10], out var ready) ? ready : 0,
                            // Its printing (card versions, phase 2: optional trailing fields; an older line is current text, printing unknown).
                            Text = parts.Length > 11 && uint.TryParse(parts[11], out var copyText) ? copyText : 0,
                            Set = parts.Length > 12 ? Undash(parts[12]) : "",
                            Printed = parts.Length > 13 ? Undash(parts[13]) : "",
                        });
                        break;
                    // Saves from before copies were recorded one by one: counts by rarity.
                    case "card" when parts.Length == 3 && uint.TryParse(parts[0], out var code)
                                     && Enum.TryParse<Rarity>(parts[1], out var rarity) && int.TryParse(parts[2], out var copies):
                        oldCards.Add((code, rarity, copies));
                        break;
                }
            }
            // An old save's counts become copies, in a fixed order (by card, then rarity) so their grades never change.
            if (oldCards.Count > 0)
            {
                if (profile.Collection.Salt == 0) profile.Collection.Salt = (uint)Guid.NewGuid().GetHashCode();
                foreach (var (code, rarity, copies) in oldCards.OrderBy(c => c.code).ThenBy(c => c.rarity))
                    profile.Collection.AddOld(code, rarity, copies);
            }
            Brackets.AfterLoad(profile);
            foreach (var b in profile.Binders.Values) b.Cards.RemoveAll(c => c == null);
            // Ban Passes are earned only now: slots bought when they were sold come back as DP.
            if (profile.PassesBought > 0)
            {
                profile.PassRefund = DpServices.BoughtPassRefund(profile.PassesBought);
                profile.Earn(Ledger.PassRefund, profile.PassRefund);
                profile.PassesBought = 0;
            }
            return profile;
        }

        // ---- The story queue's save lines (TODO 2026-11-06: queued scenes survive quitting) ------------------------------

        /// <summary>A text field as one space-free token: "-" for empty, else escaped (a literal "-" as "%2D").</summary>
        private static string Esc(string? field)
        {
            field ??= "";
            if (field.Length == 0) return "-";
            if (field == "-") return "%2D";
            // A text the escaper refuses (a lone surrogate) is written empty rather than breaking every save.
            try { return Uri.EscapeDataString(field); }
            catch (Exception) { return "-"; }
        }
        private static string Unesc(string field) => field == "-" ? "" : Uri.UnescapeDataString(field);

        /// <summary>
        /// The queue, a batch at a time: "storyq &lt;partner&gt;" starts a batch, then its "storyqevent &lt;event&gt;", "storyqfill
        /// &lt;key&gt; &lt;value&gt;" and "storyqgrant &lt;kind&gt; &lt;code&gt; &lt;rarity&gt; &lt;owed&gt; &lt;character&gt; &lt;event&gt; &lt;set code&gt;
        /// &lt;card name&gt; &lt;set name&gt; &lt;text&gt;" lines (text fields escaped, <see cref="Esc"/>). All optional: an older build skips them.
        /// </summary>
        private void WriteStoryQueue(StringBuilder sb)
        {
            foreach (var batch in StoryQueue)
            {
                sb.AppendLine($"storyq {Esc(batch.Partner)}");
                foreach (var e in batch.Events) sb.AppendLine($"storyqevent {Esc(e)}");
                foreach (var f in batch.Fill.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.AppendLine($"storyqfill {Esc(f.Key)} {Esc(f.Value)}");
                foreach (var g in batch.Grants)
                    sb.AppendLine($"storyqgrant {g.Kind} {g.Code} {g.Rarity} {(g.Owed ? 1 : 0)} {Esc(g.Character)} {Esc(g.Event)} {Esc(g.SetCode)} {Esc(g.CardName)} {Esc(g.SetName)} {Esc(g.Text)}");
            }
        }

        /// <summary>Reads one of <see cref="WriteStoryQueue"/>'s lines (a batch's lines before any "storyq" are skipped).</summary>
        private void ReadStoryQueue(string key, string rest, string[] p)
        {
            if (key == "storyq") { StoryQueue.Add(new StoryBatch { Partner = p.Length > 0 ? Unesc(p[0]) : "" }); return; }
            if (StoryQueue.Count == 0) return;
            var batch = StoryQueue[StoryQueue.Count - 1];
            switch (key)
            {
                case "storyqevent" when p.Length == 1 && Unesc(p[0]) is { Length: > 0 } e:
                    batch.Events.Add(e);
                    break;
                case "storyqfill" when p.Length == 2:
                    batch.Fill[Unesc(p[0])] = Unesc(p[1]);
                    break;
                case "storyqgrant" when p.Length == 10 && Enum.TryParse<GrantKind>(p[0], out var kind) && uint.TryParse(p[1], out var code)
                                       && Enum.TryParse<Rarity>(p[2], out var rarity):
                    batch.Grants.Add(new Grant
                    {
                        Kind = kind, Code = code, Rarity = rarity, Owed = p[3] == "1", Character = Unesc(p[4]), Event = Unesc(p[5]),
                        SetCode = Unesc(p[6]), CardName = Unesc(p[7]), SetName = Unesc(p[8]), Text = Unesc(p[9]),
                    });
                    break;
            }
        }

        // ---- The Back Alley's save lines (docs/dev/ALLEY-SPEC-2026-11-01.md section 14) ------------------------------------

        /// <summary>A positional field that may be empty ("-").</summary>
        private static string Dash(string field) => field.Length > 0 ? field : "-";
        private static string Undash(string field) => field == "-" ? "" : field;

        private static string Csv(IEnumerable<uint> codes) => codes.Any() ? string.Join(",", codes) : "-";

        private static List<uint> Codes(string csv) =>
            csv == "-" ? new List<uint>() : csv.Split(',').Select(c => uint.TryParse(c, out var v) ? v : 0).Where(v => v != 0).ToList();

        private static string Csv(IEnumerable<int> slots) => slots.Any() ? string.Join(",", slots.OrderBy(x => x)) : "-";

        private static IEnumerable<int> Slots(string csv) =>
            csv == "-" ? Enumerable.Empty<int>() : csv.Split(',').Select(c => int.TryParse(c, out var v) ? v : -1).Where(v => v >= 0);

        /// <summary>
        /// A copy's fields as the "copy" line writes them: code rarity grade seed id source paid slab floor submissions readyAt,
        /// then its printing (text set printed; "-" for none) when it has one (spec 9.2: optional trailing fields, which an
        /// older build ignores; an unstamped copy writes the 11 fields it always did).
        /// </summary>
        private static string CopyFields(CardCopy c) =>
            $"{c.Code} {c.Rarity} {c.Grade} {c.Seed} {c.Id} {c.Source} {c.Paid} {c.Slab} {c.Floor} {c.Submissions} {c.ReadyAt}"
            + (c.Text != 0 || c.Set.Length > 0 || c.Printed.Length > 0 ? $" {c.Text} {Dash(c.Set)} {Dash(c.Printed)}" : "");

        private static CardCopy? ReadCopy(string[] p, int at)
        {
            if (p.Length < at + 11 || !uint.TryParse(p[at], out var code) || !Enum.TryParse<Rarity>(p[at + 1], out var rarity)
                || !int.TryParse(p[at + 2], out var grade) || !uint.TryParse(p[at + 3], out var seed) || !int.TryParse(p[at + 4], out var id)
                || !Enum.TryParse<CopySource>(p[at + 5], out var source)) return null;
            int.TryParse(p[at + 6], out var paid);
            int.TryParse(p[at + 7], out var slab);
            int.TryParse(p[at + 8], out var floor);
            int.TryParse(p[at + 9], out var subs);
            int.TryParse(p[at + 10], out var ready);
            uint text = 0;
            if (p.Length > at + 11) uint.TryParse(p[at + 11], out text);
            return new CardCopy
            {
                Code = code, Rarity = rarity, Grade = Math.Max(1, Math.Min(10, grade)), Seed = seed, Id = id, Source = source, Paid = paid,
                Slab = Math.Max(0, Math.Min(10, slab)), Floor = floor, Submissions = subs, ReadyAt = ready,
                Text = text, Set = p.Length > at + 12 ? Undash(p[at + 12]) : "", Printed = p.Length > at + 13 ? Undash(p[at + 13]) : "",
            };
        }

        /// <summary>A stake's fields: code rarity grade slab seed flag ("-" for none), then its printing (set printed) when it has one (spec 9.2).</summary>
        private static string StakeFields(Stake s) => $"{s.Code} {s.Rarity} {s.Grade} {s.Slab} {s.Seed} {(s.Flag.Length > 0 ? s.Flag : "-")}"
            + (s.Set.Length > 0 || s.Printed.Length > 0 ? $" {Dash(s.Set)} {Dash(s.Printed)}" : "");

        private static Stake? ReadStake(string[] p, int at)
        {
            if (p.Length < at + 6 || !uint.TryParse(p[at], out var code) || !Enum.TryParse<Rarity>(p[at + 1], out var rarity)
                || !int.TryParse(p[at + 2], out var grade) || !int.TryParse(p[at + 3], out var slab) || !uint.TryParse(p[at + 4], out var seed)) return null;
            return new Stake
            {
                Code = code, Rarity = rarity, Grade = Math.Max(1, Math.Min(10, grade)), Slab = Math.Max(0, Math.Min(10, slab)), Seed = seed, Flag = p[at + 5] == "-" ? "" : p[at + 5],
                Set = p.Length > at + 6 ? Undash(p[at + 6]) : "", Printed = p.Length > at + 7 ? Undash(p[at + 7]) : "",
            };
        }

        /// <summary>The words after the first <paramref name="skip"/> in a line (a handle with spaces in it).</summary>
        private static string Tail(string rest, int skip)
        {
            var fields = rest.Split(new[] { ' ' }, skip + 1, StringSplitOptions.RemoveEmptyEntries);
            return fields.Length > skip ? fields[skip].Trim() : "";
        }

        private void WriteAlley(StringBuilder sb)
        {
            if (AlleyOpen) sb.AppendLine("alley 1");
            if (AlleyDuels > 0) sb.AppendLine($"alleyduels {AlleyDuels}");
            if (AnteWins + AnteLosses > 0) sb.AppendLine($"ante {AnteWins} {AnteLosses}");
            foreach (var t in AlleyTables.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine($"alleytable {t.Key} {t.Value.Night} {t.Value.WonMask} {(t.Value.Cleared ? 1 : 0)}");
            foreach (var r in AlleyBrackets.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine($"alleybracket {r.Key} {r.Value.Entries} {r.Value.Titles} {r.Value.Best}");
            foreach (var o in AlleyOpponents.OrderBy(o => o.Table, StringComparer.Ordinal).ThenBy(o => o.Seat))
                sb.AppendLine($"alleyopp {o.Table} {o.Seat} {o.Night} {Dash(o.Kind)} {Dash(o.Character)} {Dash(o.Base)} {o.Tint} {Csv(o.Added)} {Csv(o.Cut)} {o.Handle}");
            foreach (var h in Headliners.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine($"headliner {h.Key} {h.Value.Met} {h.Value.Beaten} {h.Value.Encores} {h.Value.Breaker}");
            foreach (var b in Binders.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"binder {b.Key} {b.Value.Serial} {b.Value.RefilledAt}");
                for (int i = 0; i < b.Value.Cards.Count; i++) sb.AppendLine($"stake {b.Key} {i} {StakeFields(b.Value.Cards[i])}");
            }
            if (AnteLive is AnteLive live)
            {
                sb.AppendLine($"antelive {live.Table} {live.Seat} {live.Held} {live.StartedAt} {(live.Binder.Length > 0 ? live.Binder : "-")}");
                if (live.Theirs != null) sb.AppendLine($"antetheirs {StakeFields(live.Theirs)}");
                if (live.Yours != null) sb.AppendLine($"anteyours {CopyFields(live.Yours)}");
            }
            foreach (var r in Rail) sb.AppendLine($"rail {r.Price} {r.ExpiresAt} {CopyFields(r.Copy)}");
            foreach (var u in UnlistedPages)
                sb.AppendLine($"unlisted {u.Key} {u.Table} {u.Kind} {(u.Base.Length > 0 ? u.Base : "-")} {Csv(u.Added)} {Csv(u.Cut)} {u.Handle}");
            if (VanUsedEpoch >= 0) sb.AppendLine($"vanused {VanUsedEpoch} {Csv(VanUsedSold)}");
            if (VanShelfEpoch >= 0)
                sb.AppendLine($"vanshelf {VanShelfEpoch} {Csv(VanShelfSold)} {(VanShelfSoldCards.Count > 0 ? string.Join(",", VanShelfSoldCards.OrderBy(k => k, StringComparer.Ordinal)) : "-")}");
        }

        private void ReadAlley(string key, string rest, string[] p)
        {
            StakeBinder BinderOf(string owner)
            {
                if (!Binders.TryGetValue(owner, out var b)) Binders[owner] = b = new StakeBinder { Owner = owner };
                return b;
            }
            switch (key)
            {
                case "alley": AlleyOpen = rest != "0"; break;
                case "alleyduels": int.TryParse(rest, out AlleyDuels); break;
                case "ante" when p.Length == 2:
                    int.TryParse(p[0], out AnteWins);
                    int.TryParse(p[1], out AnteLosses);
                    break;
                case "alleytable" when p.Length == 4 && int.TryParse(p[1], out var night) && int.TryParse(p[2], out var mask):
                    AlleyTables[p[0]] = new AlleyTableState { Night = night, WonMask = mask, Cleared = p[3] != "0" };
                    break;
                case "alleybracket" when p.Length == 4 && int.TryParse(p[1], out var entries) && int.TryParse(p[2], out var titles) && int.TryParse(p[3], out var best):
                    AlleyBrackets[p[0]] = new LocalsRecord { Entries = entries, Titles = titles, Best = best };
                    break;
                case "alleyopp" when p.Length >= 10 && int.TryParse(p[1], out var seat) && int.TryParse(p[2], out var oppNight) && uint.TryParse(p[6], out var tint):
                    AlleyOpponents.Add(new AlleyOpponent
                    {
                        Table = p[0], Seat = seat, Night = oppNight, Kind = Undash(p[3]), Character = Undash(p[4]), Base = Undash(p[5]), Tint = tint,
                        Added = Codes(p[7]), Cut = Codes(p[8]), Handle = Tail(rest, 9),
                    });
                    break;
                case "headliner" when p.Length == 5:
                    int.TryParse(p[1], out var met); int.TryParse(p[2], out var beaten); int.TryParse(p[3], out var encores); int.TryParse(p[4], out var breaker);
                    Headliners[p[0]] = new HeadlinerState { Met = met, Beaten = beaten, Encores = encores, Breaker = breaker };
                    break;
                case "binder" when p.Length == 3:
                {
                    var b = BinderOf(p[0]);
                    int.TryParse(p[1], out b.Serial);
                    int.TryParse(p[2], out b.RefilledAt);
                    break;
                }
                case "stake" when p.Length >= 8 && int.TryParse(p[1], out var i) && i >= 0 && i < 64 && ReadStake(p, 2) is Stake stake:
                {
                    var cards = BinderOf(p[0]).Cards;
                    while (cards.Count <= i) cards.Add(null!);
                    cards[i] = stake;
                    break;
                }
                case "antelive" when p.Length >= 4 && int.TryParse(p[1], out var liveSeat) && int.TryParse(p[2], out var held) && int.TryParse(p[3], out var started):
                    AnteLive = new AnteLive { Table = p[0], Seat = liveSeat, Held = held, StartedAt = started, Binder = p.Length > 4 && p[4] != "-" ? p[4] : "" };
                    break;
                case "antetheirs" when AnteLive != null: AnteLive.Theirs = ReadStake(p, 0); break;
                case "anteyours" when AnteLive != null: AnteLive.Yours = ReadCopy(p, 0); break;
                case "rail" when p.Length >= 13 && int.TryParse(p[0], out var price) && int.TryParse(p[1], out var expires) && ReadCopy(p, 2) is CardCopy railed:
                    Rail.Add(new RailEntry { Copy = railed, Price = price, ExpiresAt = expires });
                    if (railed.Id >= Collection.NextId) Collection.NextId = railed.Id + 1;
                    break;
                case "unlisted" when p.Length >= 6:
                    UnlistedPages.Add(new UnlistedPage
                    {
                        Key = p[0], Table = p[1], Kind = p[2], Base = p[3] == "-" ? "" : p[3], Added = Codes(p[4]), Cut = Codes(p[5]), Handle = Tail(rest, 6),
                    });
                    break;
                case "vanused" when p.Length >= 1 && int.TryParse(p[0], out var usedEpoch):
                    VanUsedEpoch = usedEpoch;
                    if (p.Length > 1) foreach (var slot in Slots(p[1])) VanUsedSold.Add(slot);
                    break;
                case "vanshelf" when p.Length >= 1 && int.TryParse(p[0], out var shelfEpoch):
                    VanShelfEpoch = shelfEpoch;
                    if (p.Length > 1) foreach (var slot in Slots(p[1])) VanShelfSold.Add(slot);
                    // The slabs bought by what they are (an older save has no third field).
                    if (p.Length > 2 && p[2] != "-") foreach (var k in p[2].Split(',')) if (k.Length > 0) VanShelfSoldCards.Add(k);
                    break;
            }
        }
    }

    /// <summary>What a campaign duel paid out, and what it unlocked.</summary>
    public sealed class DuelReward
    {
        public int Dp;
        /// <summary>Card Tokens paid for clearing a circuit or Tag Cup.</summary>
        public int Tokens;
        public bool FirstWin;
        public Circuit? ClearedCircuit, UnlockedCircuit;
        /// <summary>A Library cup whose Format Master stamp this win earned.</summary>
        public Circuit? StampedCup;
        /// <summary>A Library cup whose Champion's table this win took for the first time.</summary>
        public Circuit? TableCup;
        /// <summary>The "Every Era" final's round this win opened (1-based, the next to play), or 0.</summary>
        public int RoundOpened;
        public string Summary = "";
        public int Exp;
        public List<LevelUp> LevelUps = new List<LevelUp>();
        /// <summary>Every win this result counted against a character, with their wins after it (see <see cref="Profile.AddWin"/>).</summary>
        public List<(string id, int wins)> WinsAdded = new List<(string id, int wins)>();
        /// <summary>The card unlocks this result paid or offered (filled by <see cref="CardUnlocks.AfterDuel"/>).</summary>
        public List<Grant> Grants = new List<Grant>();
    }

    /// <summary>
    /// A Tag Cup: tag duels (you and a partner against two duelists) that sit beside the circuits. It opens when its
    /// <see cref="After"/> circuit is cleared and doesn't block anything; clearing it pays a prize. Your partner is a
    /// duelist you've beaten, playing their own deck.
    /// </summary>
    public sealed class TagCup
    {
        public string Name = "";
        public string Blurb = "";
        public int Prize;
        /// <summary>The circuit (index) that opens this cup when cleared; its band and ban list apply.</summary>
        public int After;
        public List<(CircuitMatch lead, CircuitMatch partner)> Matches = new List<(CircuitMatch, CircuitMatch)>();
    }

    /// <summary>One opponent in a circuit: a character with a particular deck.</summary>
    public sealed class CircuitMatch
    {
        public Personality Character = Personality.Balanced;
        public string Deck = "";
        /// <summary>
        /// A Library cup's "Champion's table" ("champion:" in library.txt): an optional match the cup's clear doesn't need,
        /// though its Format Master stamp does.
        /// </summary>
        public bool Champion;
    }

    /// <summary>A campaign tournament: a few matches to clear, and a prize the first time you clear them all.</summary>
    public sealed class Circuit
    {
        public string Name = "";
        public string Blurb = "";
        public int Prize;
        /// <summary>Difficulty band the circuit belongs to ("Beginner", "Intermediate"...), shown as a heading.</summary>
        public string Band = "";
        /// <summary>The latest year its decks come from; card shop sets up to that year open with it.</summary>
        public int Era;
        /// <summary>The Forbidden/Limited list your deck must follow here (a list name from lflists/), or "" for none.</summary>
        public string Banlist = "";
        /// <summary>A Library cup's card pool: cards first printed up to this month ("2005-09"), or "" for none.</summary>
        public string Pool = "";
        public List<CircuitMatch> Matches = new List<CircuitMatch>();
        /// <summary>
        /// A Library cup's Locals field beyond its own decks ("local:" lines in library.txt, same syntax as "match:"): the
        /// three extra CPUs of its 8-player bracket (see <see cref="Brackets"/>). Not matches: nothing else counts them.
        /// </summary>
        public List<CircuitMatch> Locals = new List<CircuitMatch>();
    }

    /// <summary>
    /// The campaign: circuits in order (campaign/circuits.txt). Only the first is open at the start; clearing one (every
    /// match won) opens the next. Wins pay DP by circuit, more the first time; losses and draws pay a little.
    /// </summary>
    public sealed class Circuits
    {
        public const int LossDp = 50, DrawDp = 100, FirstWinBonus = 200;

        public IReadOnlyList<Circuit> All { get; }
        public IReadOnlyList<TagCup> TagCups { get; }

        public Circuits(IReadOnlyList<Circuit> circuits, IReadOnlyList<TagCup>? tagCups = null)
        {
            All = circuits;
            TagCups = tagCups ?? new List<TagCup>();
        }

        /// <summary>Reads circuits.txt; matches with an unknown character are skipped.</summary>
        public static Circuits Parse(string text, IEnumerable<Personality> roster)
        {
            var byId = roster.ToDictionary(p => p.Id);
            var circuits = new List<Circuit>();
            var cups = new List<TagCup>();
            Circuit? current = null;
            TagCup? cup = null;
            // "<character> <deck name>" (the deck may contain spaces; missing = the character's first deck).
            CircuitMatch? Match(string text)
            {
                var t = text.Trim();
                var gap = t.IndexOf(' ');
                var id = gap < 0 ? t : t.Substring(0, gap);
                var deckName = gap < 0 ? "" : t.Substring(gap + 1).Trim();
                return byId.TryGetValue(id, out var who)
                    ? new CircuitMatch { Character = who, Deck = deckName.Length > 0 ? deckName : who.Decks.FirstOrDefault() ?? "" } : null;
            }
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim().ToLowerInvariant();
                var value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "circuit":
                        current = new Circuit { Name = value };
                        circuits.Add(current);
                        cup = null;
                        break;
                    // A Tag Cup: "tagcup: <name>", then "after: <circuit name>", "blurb:", "prize:", and
                    // "tag: <character> <deck> + <character> <deck>" for each match.
                    case "tagcup":
                        cup = new TagCup { Name = value };
                        cups.Add(cup);
                        break;
                    case "after" when cup != null:
                        cup.After = circuits.FindIndex(c => c.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                        break;
                    case "blurb" when cup != null: cup.Blurb = value; break;
                    case "prize" when cup != null: int.TryParse(value, out cup.Prize); break;
                    case "tag" when cup != null:
                        var pair = value.Split('+');
                        if (pair.Length == 2 && Match(pair[0]) is CircuitMatch first && Match(pair[1]) is CircuitMatch second)
                            cup.Matches.Add((first, second));
                        break;
                    case "blurb" when current != null && cup == null: current.Blurb = value; break;
                    case "prize" when current != null && cup == null: int.TryParse(value, out current.Prize); break;
                    case "band" when current != null: current.Band = value; break;
                    case "era" when current != null: int.TryParse(value, out current.Era); break;
                    case "banlist" when current != null: current.Banlist = value; break;
                    case "pool" when current != null: current.Pool = value; break;
                    case "match" when current != null:
                    case "champion" when current != null:
                    case "local" when current != null:
                        // "match: <character> <deck name>": the deck name is the rest of the line and may contain spaces.
                        var space = value.IndexOf(' ');
                        var who = space < 0 ? value : value.Substring(0, space);
                        var deck = space < 0 ? "" : value.Substring(space + 1).Trim();
                        if (byId.TryGetValue(who, out var character))
                            (key == "local" ? current.Locals : current.Matches).Add(new CircuitMatch { Character = character, Deck = deck.Length > 0 ? deck : character.Decks.FirstOrDefault() ?? "", Champion = key == "champion" });
                        break;
                }
            }
            var kept = circuits.Where(c => c.Matches.Count > 0).ToList();
            // Re-resolve "after" against the kept list (cups whose circuit is missing are dropped).
            foreach (var c in cups) c.After = c.After >= 0 && c.After < circuits.Count ? kept.IndexOf(circuits[c.After]) : -1;
            return new Circuits(kept, cups.Where(c => c.After >= 0 && c.Matches.Count > 0).ToList());
        }

        public static Circuits Load(string path, IEnumerable<Personality> roster) => Parse(File.ReadAllText(path), roster);

        /// <summary>Circuits and Tag Cups added on 2026-10-28, between existing ones: saves from before don't count them.</summary>
        private static readonly string[] AddedOctober2026 = { "Expert Series: Fall Format", "Master League: October 2026", "Fall Format Tag Cup" };

        /// <summary>
        /// Match keys count circuits and Tag Cups by position ("3.1", "tag2.0"), so adding one in between would move a
        /// save's wins onto other matches. This moves each key from the layout the save was written with (recorded in it;
        /// older saves: the current one without the circuits added since) to the same circuit's place now, by name, and
        /// records the current layout. Keys for circuits no longer in the campaign are dropped.
        /// </summary>
        public void Migrate(Profile profile)
        {
            var circuitNames = All.Select(c => c.Name).ToList();
            var cupNames = TagCups.Select(c => c.Name).ToList();
            var oldCircuits = profile.CircuitLayout ?? circuitNames.Where(n => !AddedOctober2026.Contains(n)).ToList();
            var oldCups = profile.TagCupLayout ?? cupNames.Where(n => !AddedOctober2026.Contains(n)).ToList();
            if (!oldCircuits.SequenceEqual(circuitNames) || !oldCups.SequenceEqual(cupNames))
            {
                string? Moved(string key)
                {
                    bool tag = key.StartsWith("tag");
                    var dot = key.IndexOf('.');
                    if (dot < 0 || !int.TryParse(key.Substring(tag ? 3 : 0, dot - (tag ? 3 : 0)), out var at)) return key; // guests and others
                    var (from, to) = tag ? (oldCups, cupNames) : (oldCircuits, circuitNames);
                    if (at < 0 || at >= from.Count) return null;
                    var now = to.IndexOf(from[at]);
                    return now < 0 ? null : (tag ? "tag" : "") + now + key.Substring(dot);
                }
                foreach (var keys in new[] { profile.MatchWins, profile.LossStreaks })
                {
                    var moved = keys.Select(p => (key: Moved(p.Key), p.Value)).Where(p => p.key != null).ToList();
                    keys.Clear();
                    foreach (var (key, value) in moved) keys[key!] = value;
                }
                // A duel left live moves with its circuit or Tag Cup (-1, so it isn't settled, when that one is gone).
                if (profile.LiveDuel is LiveDuel live && (live.Kind == LiveDuels.Circuit || live.Kind == LiveDuels.TagCup))
                {
                    var (from, to) = live.Kind == LiveDuels.TagCup ? (oldCups, cupNames) : (oldCircuits, circuitNames);
                    live.Event = live.Event >= 0 && live.Event < from.Count ? to.IndexOf(from[live.Event]) : -1;
                }
            }
            profile.CircuitLayout = circuitNames;
            profile.TagCupLayout = cupNames;
        }

        public bool IsCleared(int circuit, Profile profile) =>
            circuit >= 0 && circuit < All.Count && Enumerable.Range(0, All[circuit].Matches.Count).All(m => profile.HasWon(circuit, m));

        public bool IsUnlocked(int circuit, Profile profile) => circuit == 0 || IsCleared(circuit - 1, profile);

        /// <summary>The first circuit you haven't cleared (or the last one when you've cleared them all).</summary>
        public int Current(Profile profile)
        {
            for (int i = 0; i < All.Count; i++)
                if (!IsCleared(i, profile)) return i;
            return All.Count - 1;
        }

        /// <summary>The era of the latest circuit you've opened (2005 before any circuit has one): the shop's floor.</summary>
        public int BandEra(Profile profile)
        {
            int year = 2005;
            for (int i = 0; i < All.Count && IsUnlocked(i, profile); i++) year = Math.Max(year, All[i].Era);
            return year;
        }

        /// <summary>The next band's era after <see cref="BandEra"/>: the most the shop opens early (the floor itself at the end).</summary>
        public int NextEra(Profile profile)
        {
            int floor = BandEra(profile);
            return All.Select(c => c.Era).Where(e => e > floor).DefaultIfEmpty(floor).Min();
        }

        /// <summary>
        /// The newest year of card shop sets you can buy: your band's era, plus a year for every level you've gained
        /// since that era opened, up to the next band's era. You can start collecting for the next band's decks while
        /// you play this one, but end-game sets never open in the first circuits.
        /// </summary>
        public int ShopYear(Profile profile)
        {
            int floor = BandEra(profile);
            int start = profile.BandStartLevel.TryGetValue(floor, out var l) ? l : profile.Level;
            return Math.Min(NextEra(profile), floor + Math.Max(0, profile.Level - start));
        }

        /// <summary>Every circuit cleared (the Tier 0 Gauntlet too): the post-game.</summary>
        public bool AllCleared(Profile profile) => All.Count > 0 && ClearedCount(profile) == All.Count;

        /// <summary>
        /// The circuits whose decks have house (selection) packs in the shop: the current circuit and the next one, so you
        /// can prepare for the decks you're about to face. Empty once every circuit is cleared.
        /// </summary>
        public List<int> SelectionPackCircuits(Profile profile)
        {
            if (All.Count == 0 || AllCleared(profile)) return new List<int>();
            int current = Current(profile);
            return new[] { current, current + 1 }.Where(i => i < All.Count).ToList();
        }

        /// <summary>
        /// The matches whose decks get a house pack (SHOP-REWORK-SPEC section 3): walking the matches of
        /// <see cref="SelectionPackCircuits"/> in order, the first match of each deck that isn't won. One per deck; a beaten
        /// deck's pack leaves (its recipe unlocks on that win), and so does a deck met again later once any recipe of it is
        /// unlocked, so you never see both for one deck. Tag Cups add none.
        /// </summary>
        public List<(int circuit, int match, CircuitMatch entry)> HouseMatches(Profile profile)
        {
            var matches = new List<(int, int, CircuitMatch)>();
            var decks = new HashSet<string>(RecipeBook.Unlocked(this, profile).Select(r => r.Deck));
            foreach (var ci in SelectionPackCircuits(profile))
                for (int mi = 0; mi < All[ci].Matches.Count; mi++)
                {
                    var m = All[ci].Matches[mi];
                    if (profile.HasWon(ci, mi) || m.Deck.Length == 0 || !decks.Add(m.Deck)) continue;
                    matches.Add((ci, mi, m));
                }
            return matches;
        }

        /// <summary>Every character who plays a deck in the circuits, in match order (each once): a house pack's {opponents}.</summary>
        public List<Personality> PlayersOf(string deck) =>
            All.SelectMany(c => c.Matches).Where(m => m.Deck == deck).Select(m => m.Character).GroupBy(p => p.Id).Select(g => g.First()).ToList();

        /// <summary>Remembers your level when a band's era first opens (call after a win; old saves start counting now).</summary>
        public void NoteBand(Profile profile)
        {
            int floor = BandEra(profile);
            if (!profile.BandStartLevel.ContainsKey(floor)) profile.BandStartLevel[floor] = profile.Level;
        }

        public int ClearedCount(Profile profile) => Enumerable.Range(0, All.Count).Count(i => IsCleared(i, profile));

        // ---- Tag Cups ----

        public static string TagKey(int cup, int match) => $"tag{cup}.{match}";

        /// <summary>
        /// Open once its <see cref="TagCup.After"/> circuit is cleared, or once you've won a match there (a save from
        /// before the cup moved later keeps its cup).
        /// </summary>
        public bool IsTagCupOpen(int cup, Profile profile) => cup >= 0 && cup < TagCups.Count
            && (IsCleared(TagCups[cup].After, profile) || Enumerable.Range(0, TagCups[cup].Matches.Count).Any(m => profile.MatchWins.ContainsKey(TagKey(cup, m))));

        public bool IsTagCupCleared(int cup, Profile profile) =>
            cup >= 0 && cup < TagCups.Count && Enumerable.Range(0, TagCups[cup].Matches.Count).All(m => profile.MatchWins.ContainsKey(TagKey(cup, m)));

        /// <summary>Duelists who can partner you: everyone you've beaten in the campaign, best record first.</summary>
        public List<Personality> Partners(Profile profile) =>
            All.SelectMany(c => c.Matches).Select(m => m.Character).Concat(TagCups.SelectMany(t => t.Matches.SelectMany(m => new[] { m.lead.Character, m.partner.Character })))
                .GroupBy(p => p.Id).Select(g => g.First()).Where(p => profile.HasBeaten(p.Id))
                .OrderByDescending(p => profile.WinsAgainst(p.Id)).ToList();

        /// <summary>
        /// Records a tag match (winner 0 your team, 1 theirs, 2 draw): pays like a match of the cup's circuit, with the
        /// cup's prize the first time it is cleared, and EXP. A win with your <paramref name="loaner"/> deck pays half the
        /// duel's DP (the prize in full).
        /// </summary>
        public DuelReward RecordTag(Profile profile, int cup, int match, int winner, bool loaner = false)
        {
            var reward = new DuelReward();
            var c = TagCups[cup];
            var key = TagKey(cup, match);
            int prize = 0;
            if (winner == 0)
            {
                bool wasCleared = IsTagCupCleared(cup, profile);
                reward.FirstWin = !profile.MatchWins.ContainsKey(key);
                reward.Dp = WinDp(c.After) + (reward.FirstWin ? FirstWinBonus : 0);
                if (loaner) reward.Dp = (int)Math.Round(reward.Dp * Loaners.DpShare);
                profile.MatchWins[key] = (profile.MatchWins.TryGetValue(key, out var n) ? n : 0) + 1;
                foreach (var who in new[] { c.Matches[match].lead.Character, c.Matches[match].partner.Character })
                    profile.AddWin(who.Id, reward);
                var parts = new List<string> { $"+{reward.Dp} DP" + (reward.FirstWin ? " (first win)" : "") + (loaner ? " (half: loaner deck)" : "") };
                if (!wasCleared && IsTagCupCleared(cup, profile))
                {
                    reward.Dp += c.Prize;
                    prize = c.Prize;
                    reward.Tokens = TagCupClearTokens;
                    profile.Tokens += reward.Tokens;
                    parts.Add($"{c.Name} cleared! +{c.Prize} DP prize, +{reward.Tokens} Card Token");
                }
                reward.Summary = string.Join(" · ", parts);
            }
            else
            {
                reward.Dp = winner == 2 ? DrawDp : LossDp;
                reward.Summary = $"+{reward.Dp} DP for the duel";
            }
            PayDuel(profile, Ledger.Tag, winner, reward.Dp, prize);
            profile.NoteResult(key, winner);
            reward.Exp = Leveling.DuelExp(c.After, winner, reward.FirstWin);
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp);
            foreach (var up in reward.LevelUps)
                reward.Summary += $" · Level {up.Level}! +{up.Dp} DP" + (up.Tokens > 0 ? $", +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : "");
            return reward;
        }

        /// <summary>
        /// Pays a duel's DP into the ledger (<see cref="Ledger"/>): a win's DP under its <paramref name="kind"/>, the prize in it
        /// (<paramref name="prize"/>, part of <paramref name="dp"/>) as "&lt;kind&gt;.prize", a loss or draw as "&lt;kind&gt;.loss".
        /// </summary>
        public static void PayDuel(Profile profile, string kind, int winner, int dp, int prize = 0)
        {
            if (winner != 0) { profile.Earn(Ledger.Loss(kind), dp); return; }
            profile.Earn(kind, dp - prize);
            profile.Earn(Ledger.Prize(kind), prize);
        }

        /// <summary>
        /// DP for winning a match in this circuit (before the first-win bonus). The late bands (Expert, Master, the
        /// Gauntlet) pay half as much again: by then a player needs several top decks, not one.
        /// </summary>
        public int WinDp(int circuit) => (int)Math.Round((150 + 75 * circuit) * (IsLateBand(circuit) ? 1.5 : 1.0));

        /// <summary>Card Tokens for clearing a circuit the first time: one, and three in the late bands.</summary>
        public const int ClearTokens = 1, LateClearTokens = 3, TagCupClearTokens = 1;

        public int TokensForClearing(int circuit) => IsLateBand(circuit) ? LateClearTokens : ClearTokens;

        /// <summary>The late bands: they pay more (<see cref="WinDp"/>, <see cref="TokensForClearing"/>) and open the OCG import shelf.</summary>
        public static readonly string[] LateBands = { "Expert", "Master", "Gauntlet" };

        public bool IsLateBand(int circuit) => circuit >= 0 && circuit < All.Count && LateBands.Contains(All[circuit].Band);

        /// <summary>
        /// Records a match result and pays out. <paramref name="winner"/>: 0 you, 1 them, 2 draw. A win with a
        /// <paramref name="loaner"/> deck pays half the duel's DP (the circuit's prize still in full).
        /// </summary>
        public DuelReward Record(Profile profile, int circuit, int match, int winner, bool loaner = false)
        {
            var reward = new DuelReward();
            var opponent = All[circuit].Matches[match].Character;
            int prize = 0;
            if (winner == 0)
            {
                bool wasCleared = IsCleared(circuit, profile);
                reward.FirstWin = !profile.HasWon(circuit, match);
                reward.Dp = WinDp(circuit) + (reward.FirstWin ? FirstWinBonus : 0);
                if (loaner) reward.Dp = (int)Math.Round(reward.Dp * Loaners.DpShare);
                var key = Profile.MatchKey(circuit, match);
                profile.MatchWins[key] = (profile.MatchWins.TryGetValue(key, out var n) ? n : 0) + 1;
                profile.AddWin(opponent.Id, reward);
                var parts = new List<string> { $"+{reward.Dp} DP" + (reward.FirstWin ? " (first win)" : "") + (loaner ? " (half: loaner deck)" : "") };
                if (!wasCleared && IsCleared(circuit, profile))
                {
                    reward.ClearedCircuit = All[circuit];
                    reward.Dp += All[circuit].Prize;
                    prize = All[circuit].Prize;
                    parts.Add($"{All[circuit].Name} cleared! +{All[circuit].Prize} DP prize");
                    reward.Tokens = TokensForClearing(circuit);
                    profile.Tokens += reward.Tokens;
                    parts.Add($"+{reward.Tokens} Card Token{(reward.Tokens == 1 ? "" : "s")}");
                    if (circuit + 1 < All.Count)
                    {
                        reward.UnlockedCircuit = All[circuit + 1];
                        parts.Add($"{All[circuit + 1].Name} unlocked");
                    }
                }
                reward.Summary = string.Join(" · ", parts);
            }
            else
            {
                reward.Dp = winner == 2 ? DrawDp : LossDp;
                if (winner == 1) profile.Losses[opponent.Id] = (profile.Losses.TryGetValue(opponent.Id, out var l) ? l : 0) + 1;
                reward.Summary = $"+{reward.Dp} DP for the duel";
            }
            PayDuel(profile, Ledger.Circuit, winner, reward.Dp, prize);
            profile.NoteResult(Profile.MatchKey(circuit, match), winner);
            // EXP, and whatever the levels it reaches pay.
            reward.Exp = Leveling.DuelExp(circuit, winner, reward.FirstWin) + (reward.ClearedCircuit != null ? Leveling.CircuitClearExp : 0);
            NoteBand(profile); // before the level-ups, so a new band counts from the level you reach it at
            reward.LevelUps = Leveling.AddExp(profile, reward.Exp);
            foreach (var up in reward.LevelUps)
                reward.Summary += $" · Level {up.Level}! +{up.Dp} DP" + (up.Tokens > 0 ? $", +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : "");
            return reward;
        }

        /// <summary>Characters you've met in the campaign (any match in an unlocked circuit), e.g. to unlock them elsewhere.</summary>
        public IEnumerable<Personality> Beaten(Profile profile) =>
            All.SelectMany((c, ci) => c.Matches.Where((m, mi) => profile.HasWon(ci, mi)).Select(m => m.Character)).Distinct();
    }

    /// <summary>Checks a deck against the cards you own.</summary>
    public static class CollectionRules
    {
        /// <summary>Cards the deck uses more copies of than you own (Main + Extra + Side together). Texts never refuse a deck: see <see cref="TextShortfalls"/>.</summary>
        public static List<DeckProblem> Validate(Deck deck, Collection collection, ICardDataProvider cards)
        {
            var problems = new List<DeckProblem>();
            foreach (var group in deck.Main.Concat(deck.Extra).Concat(deck.Side).GroupBy(c => Banlist.BaseCode(c, cards)))
            {
                var owned = collection.Count(group.Key);
                if (group.Count() <= owned) continue;
                var name = cards.Get(group.Key)?.Name ?? group.Key.ToString();
                problems.Add(new DeckProblem
                {
                    Code = group.Key,
                    Message = owned == 0 ? $"{name}: you don't own it." : $"{name}: the deck uses {group.Count()}, you own {owned}.",
                });
            }
            return problems;
        }

        /// <summary>
        /// Notes, not problems (spec 4.4, D3: downgrade, don't refuse): for each card whose Main/Extra slots ask for an
        /// original text (<see cref="Collection.AskedText"/>) more often than you own copies carrying it, one line: "Sangan: 2
        /// slots ask for the original text; you own 1 original copy, so 1 plays its current text". Those slots get current
        /// copies from <see cref="Collection.Assign"/> and play the current text (<see cref="PlayTexts.ForTable"/>). A card
        /// short of copies altogether is <see cref="Validate"/>'s, and isn't repeated here. The editor and versus show these.
        /// </summary>
        public static List<DeckProblem> TextShortfalls(Deck deck, Collection collection, CardTexts texts, ICardDataProvider cards)
        {
            var notes = new List<DeckProblem>();
            var all = deck.Main.Concat(deck.Extra).Concat(deck.Side).GroupBy(c => Banlist.BaseCode(c, cards)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in deck.Main.Concat(deck.Extra).GroupBy(c => Banlist.BaseCode(c, cards)))
            {
                if (all[group.Key] > collection.Count(group.Key)) continue;
                var name = cards.Get(group.Key)?.Name ?? group.Key.ToString();
                // As Assign fills them: a Pre-Errata slot needs its own text, a GOAT slot any original left over.
                int need = 0, playing = 0;
                var left = collection.CopiesOf(group.Key).Where(c => !c.AtDesk).Select(c => texts.Row(c.Text) is TextRow r && r.Collectible ? r.Code : 0u).ToList();
                int originals = left.Count(t => t != 0);
                var goatSlots = 0;
                foreach (var slot in group)
                {
                    var asked = Collection.AskedText(slot, texts, cards);
                    if (asked == 0) continue;
                    need++;
                    if (cards.Get(slot) is CardInfo info && CardVersions.KindOf(info) == CardVersions.TextKind.Goat) { goatSlots++; continue; }
                    if (left.Remove(asked)) playing++;
                }
                for (int g = 0; g < goatSlots; g++)
                {
                    int at = left.FindIndex(t => t != 0);
                    if (at < 0) break;
                    left.RemoveAt(at);
                    playing++;
                }
                int short_ = need - playing;
                if (short_ <= 0) continue;
                notes.Add(new DeckProblem
                {
                    Code = group.Key,
                    Message = $"{name}: {need} slot{(need == 1 ? " asks" : "s ask")} for the original text; you own "
                              + $"{(originals == 0 ? "no" : originals.ToString())} original cop{(originals == 1 ? "y" : "ies")}, so {short_} play{(short_ == 1 ? "s its" : " their")} current text",
                });
            }
            return notes;
        }
    }
}
