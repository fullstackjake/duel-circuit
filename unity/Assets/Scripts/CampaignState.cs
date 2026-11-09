using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>A deck you can start the campaign with.</summary>
    public sealed class StarterDeck
    {
        public string Deck = "", Title = "", Blurb = "";
        /// <summary>One line on its era and feel, above the title (for players picking by what they know).</summary>
        public string Tag = "";
        /// <summary>A ribbon across the top of its tile ("Best first deck"), or empty.</summary>
        public string Badge = "";
        /// <summary>Cards shown on its tile.</summary>
        public uint[] Showcase = System.Array.Empty<uint>();
        /// <summary>
        /// The edition a Historic Rules campaign starts with ("classic-2011": legal under the first list, every card released
        /// by its format's end), or empty to start with <see cref="Deck"/> either way.
        /// </summary>
        public string HistoricDeck = "";
    }

    /// <summary>
    /// The campaign save (mods/save/profile.txt) plus what's built from game data: the circuits and the card shop's
    /// stock. There's no save until you pick a starter deck; its cards become your collection and your first campaign deck.
    /// </summary>
    public sealed partial class CampaignState
    {
        public static readonly StarterDeck[] Starters =
        {
            new StarterDeck { Deck = "classic", HistoricDeck = "classic-2011", Title = "Legendary Dragons", Tag = "Classic era · big monsters, simple plays", Badge = "Best first deck",
                Blurb = "Lord of D. and The Flute of Summoning Dragon call Blue-Eyes straight from your hand; Trade-In and Silver's Cry turn spare dragons into cards and comebacks.",
                Showcase = new uint[] { 89631139, 17985575, 43973174 } },
            new StarterDeck { Deck = "warriors", HistoricDeck = "warriors-2011", Title = "Warrior's Charge", Tag = "Classic era · equip and attack",
                Blurb = "Gearfried the Swordmaster destroys a monster each time you equip it; Marauding Captain and Command Knight keep it safe.",
                Showcase = new uint[] { 57046845, 423705, 10375182 } },
            new StarterDeck { Deck = "zombies", HistoricDeck = "zombies-2011", Title = "Undead Legion", Tag = "Classic era · comebacks from the Graveyard",
                Blurb = "Zombie World and Book of Life keep your undead coming back; Pyramid Turtle and Goblin Zombie fetch the next one whenever one falls.",
                Showcase = new uint[] { 5186893, 4064256, 77044671 } },
            new StarterDeck { Deck = "mekk-knight-invoked", Title = "Mekk-Knight Invoked Evolved", Tag = "Modern · strong from the start, hard to play well",
                Blurb = "A modern deck that's best going second: cards from your hand stop the opponent's big turn, then you break their field and strike back. Strong from the first duel, but its plays take practice.",
                Showcase = new uint[] { 86120751, 28692962, 75286621 } },
        };

        private readonly string _modsRoot;
        private readonly ICardDataProvider _cards;
        private readonly PrintingIndex? _printings;
        private readonly DeckLibrary _library;

        private readonly IReadOnlyList<Banlist> _banlists;

        public Profile? Profile { get; private set; }
        public Circuits Circuits { get; }
        public PackCatalog? Shop { get; }
        /// <summary>The card shop's OCG import shelf (campaign/ocg-imports.txt): cards the TCG never printed.</summary>
        public ImportShelf Imports { get; }
        public string SavePath { get; }
        public string ModsRoot => _modsRoot;

        /// <summary>Kit, who partners you in the Tag Cups with one of your own decks (null if the profile is missing).</summary>
        public Personality? ShopRegular { get; }

        /// <summary>The special guests (campaign/guests.txt).</summary>
        public GuestList Guests { get; }

        /// <summary>Measured deck strength (campaign/deck-ratings.txt: bot-vs-bot Elo by deck), for advice; 0 when unrated.</summary>
        public double Rating(string deck) => _ratings.TryGetValue(Ygo.Engine.Campaign.Loaners.RecipeOf(deck), out var elo) ? elo : 0;
        private readonly Dictionary<string, double> _ratings;

        /// <summary>The deck ratings as read (<see cref="DeckRatings.Load"/>): bracket seeding and odds take them as they are.</summary>
        public IReadOnlyDictionary<string, double> Ratings => _ratings;

        /// <summary>Measured head-to-head results (campaign/matchups.txt, once a study writes it; empty before): bracket odds blend them in.</summary>
        public Matchups Matchups { get; }

        /// <summary>
        /// The Prize Packs a finished bracket opens (<see cref="Brackets.Finish"/>): built on first use (it reads every
        /// printing), null without printings.bin.
        /// </summary>
        public PrizePacks? Prizes => _printings == null ? null
            : _prizes ??= new PrizePacks(_cards, _printings, name => _library.Contains(name) ? _library.Load(name) : null);
        private PrizePacks? _prizes;

        /// <summary>Scouting reports (campaign/scouting.txt), for the versus screen and after a loss.</summary>
        public Scouting Scouting { get; }

        /// <summary>What the wall study measured at the hard matches (campaign/wall-advice.txt), for the Stuck? advice.</summary>
        public WallAdvice Advice { get; }
        /// <summary>The coin toss's measured tips at circuit matches (campaign/toss-advice.txt, `--circuit-study --write`).</summary>
        public WallAdvice TossAdvice { get; }

        /// <summary>The Format Library's cups (campaign/library.txt), open after the Tier 0 Gauntlet.</summary>
        public Library Library { get; }

        /// <summary>The card text timeline (campaign/texts.txt): where an original text plays as printed (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md).</summary>
        public CardTexts Texts { get; }
        /// <summary>What `--library-study` measured at each cup match (campaign/library-advice.txt, keyed by cup and match index).</summary>
        public WallAdvice LibraryAdvice { get; }

        /// <summary>How many copies of a card (by name, any printing) you own.</summary>
        public int Owned(string cardName)
        {
            if (Profile == null) return 0;
            var card = _cards.All.FirstOrDefault(c => c.Name == cardName);
            return card == null ? 0 : Profile.Collection.Count(Banlist.BaseCode(card.Code, _cards));
        }

        /// <summary>
        /// The scouting report on a deck as <paramref name="character"/> plays it (written for the late walls, put together from
        /// the deck otherwise; a report written for one character only reaches them).
        /// </summary>
        public ScoutReport Scout(string deck, string character = "") =>
            Scouting.For(deck, _library.Contains(Ygo.Engine.Campaign.Loaners.RecipeOf(deck)) ? _library.Load(Ygo.Engine.Campaign.Loaners.RecipeOf(deck)) : new Deck(), _cards, character);

        /// <summary>Who can partner you in a tag duel: everyone you've beaten (best record first), guests you've beaten, then Kit.</summary>
        public List<Personality> TagPartnerChoices()
        {
            var list = Profile == null ? new List<Personality>() : Circuits.Partners(Profile);
            if (Profile != null)
                list.AddRange(Guests.All.Select(g => g.Character).Where(c => Profile.HasBeaten(c.Id) && list.All(p => p.Id != c.Id)));
            if (ShopRegular != null) list.Add(ShopRegular);
            return list;
        }

        /// <summary>
        /// The decks a partner can bring (see <see cref="TagPartners"/>): Kit's from your campaign decks, then any recipe
        /// you can borrow (a loaner, made legal for the duel's ban list).
        /// </summary>
        public List<string> PartnerDeckChoices(Personality partner)
        {
            var yours = _library.CampaignNames.Where(IsYours);
            if (TagPartners.IsShopRegular(partner)) yours = yours.Concat(LoanerDecks());
            var choices = TagPartners.DeckChoices(partner, Circuits, Profile!, yours)
                .Where(d => _library.Contains(d) || Ygo.Engine.Campaign.Loaners.IsLoaner(d)).ToList();
            // Then the decks of theirs you edited.
            if (!TagPartners.IsShopRegular(partner))
                choices.AddRange(_library.CampaignNames.Where(d => PartnerEditOf(d) == partner.Id && !choices.Contains(d)));
            return choices;
        }

        /// <summary>Whose deck a campaign deck is an edit of (its "#partner" note), or "" when it's simply yours.</summary>
        public string PartnerEditOf(string deck) => _library.Contains(deck) ? Ygo.Engine.Campaign.Loaners.PartnerEditOf(_library.Load(deck)) : "";

        /// <summary>
        /// A saved deck you built (<see cref="Ygo.Engine.Campaign.Loaners.IsYours"/>: no loaner, custom or not, and not your
        /// edit of a partner's deck). The final's rounds and the alley take only these, and a Library cup's Format Master
        /// stamp counts only these (<see cref="Library.CountsForStamp"/>), so they all agree. False for a loaner name.
        /// </summary>
        public bool IsYours(string deck) => _library.Contains(deck) && Ygo.Engine.Campaign.Loaners.IsYours(_library.Load(deck));

        /// <summary>
        /// Why a deck can't play an "Every Era" round, or nothing when it can: a deck you built (<see cref="IsYours"/>), legal
        /// under the round's list and pool with no Ban Passes. The final's panel and the round's versus both ask this.
        /// </summary>
        public List<DeckProblem> RoundProblems(string deck, int round)
        {
            if (IsLoanerDeck(deck))
                return new List<DeckProblem> { new DeckProblem { Message = "It's a loaner: the final takes only decks you built." } };
            if (!_library.Contains(deck)) return new List<DeckProblem> { new DeckProblem { Message = "Build a deck first (New deck)." } };
            if (!IsYours(deck))
                return new List<DeckProblem> { new DeckProblem { Message = "It's your edit of a partner's deck, theirs to bring: the final takes only decks you built." } };
            return Problems(_library.Load(deck), RoundRules(round));
        }

        /// <summary>
        /// Your edit of the deck <paramref name="partner"/> brings: the edit itself if that's what they bring, else a copy of
        /// their deck saved as a deck of yours ("Vera's Warriors (your edit)"). It plays by their deck's playbook, uses your
        /// collection plus their deck's own cards (<see cref="CollectionFor"/>), and answers to the ban lists like your decks,
        /// unlike their own deck, which plays as built. The partner then brings it. Null when they have no deck of their own.
        /// </summary>
        public string? EditPartnerDeck(Personality partner)
        {
            if (Profile == null || TagPartners.IsShopRegular(partner)) return null;
            var current = PartnerDeck(partner);
            if (PartnerEditOf(current) == partner.Id) return current;
            if (!_library.Contains(current) || _library.IsCampaign(current)) return null;
            var deck = _library.Load(current).Clone();
            var title = $"{partner.Name}'s {DeckTitle(current)}";
            var file = DeckLibrary.SafeName(title);
            deck.Name = $"{file} (your edit)";
            for (int n = 2; _library.Contains(deck.Name); n++) deck.Name = $"{file} (your edit {n})";
            deck.Campaign = true;
            deck.Notes.Remove("loaner");
            deck.Notes["title"] = $"{title} (your edit)";
            deck.Notes["recipe"] = Ygo.Engine.AI.Playbooks.Playbooks.RecipeOf(_library.Load(current)); // their playbook still
            deck.Notes["partner"] = partner.Id;
            _library.Save(deck);
            Profile.PartnerDecks[partner.Id] = deck.Name;
            Save();
            return deck.Name;
        }

        /// <summary>The partner brings their own deck again (it plays as built); your edits stay among their choices.</summary>
        public void UsePartnersOwnDeck(Personality partner)
        {
            if (Profile == null) return;
            var own = PartnerDeckChoices(partner).FirstOrDefault(d => PartnerEditOf(d).Length == 0);
            if (own == null) return;
            Profile.PartnerDecks[partner.Id] = own;
            Save();
        }

        public string PartnerDeck(Personality partner)
        {
            var choices = PartnerDeckChoices(partner);
            return Profile!.PartnerDecks.TryGetValue(partner.Id, out var picked) && choices.Contains(picked) ? picked : choices.FirstOrDefault() ?? "";
        }

        public CampaignState(string modsRoot, string circuitsFile, ICardDataProvider cards, PrintingIndex? printings,
            DeckLibrary library, IEnumerable<Personality> roster, IReadOnlyList<Banlist>? banlists = null)
        {
            _banlists = banlists ?? new List<Banlist>();
            _modsRoot = modsRoot;
            _cards = cards;
            _printings = printings;
            _library = library;
            SavePath = Path.Combine(modsRoot, "save", "profile.txt");
            MyRecipes = new MyRecipes(SavePath);
            Circuits = File.Exists(circuitsFile) ? Circuits.Load(circuitsFile, roster) : new Circuits(new List<Circuit>());
            ShopRegular = roster.FirstOrDefault(p => p.Id == TagPartners.ShopRegularId);
            // The Library before the guests: Ilse's "final" unlock asks it.
            Library = Library.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", Library.CupsFile), roster, Circuits);
            Guests = GuestList.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "guests.txt"), roster, Library);
            // The Champion's Ladder after them: its opening asks the final (and Ilse beaten). CampaignState.Ladder.
            LoadLadder(Path.GetDirectoryName(circuitsFile) ?? "", roster);
            Scouting = Scouting.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "scouting.txt"));
            Advice = WallAdvice.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "wall-advice.txt"));
            TossAdvice = WallAdvice.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "toss-advice.txt"));
            Texts = CardTexts.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", CardTexts.File), cards);
            LibraryAdvice = WallAdvice.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", Library.AdviceFile));
            // The peak builds' ratings (No Limits) merge over the field's.
            _ratings = DeckRatings.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", DeckRatings.FileName),
                Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", DeckRatings.PeakFileName));
            Matchups = Matchups.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", Matchups.FileName));
            Shop = printings != null ? new PackCatalog(printings, cards) : null;
            Imports = ImportShelf.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "ocg-imports.txt"), cards);
            _roster = roster.ToList();
            Featured = FeaturedShelves.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", FeaturedShelves.FileName));
            Voice = ShopVoice.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", ShopVoice.FileName));
            Voice.CardName = code => cards.Get(code)?.Name ?? code.ToString();
            Profile = Profile.Load(SavePath);
            WireCollection(Profile);
            // Saves count circuits by position: move their wins if circuits were added in between since.
            if (Profile != null) Circuits.Migrate(Profile);
            // Ban Pass slots bought when they were sold came back as DP at load: keep that.
            if (Profile?.PassRefund > 0) Save();
            // A champion's save from before the Library: it opens now (the story's "library" scene plays on the campaign screen).
            if (Profile != null && Library.Open(Profile, Circuits)) Save();
            NoPass = BanPasses.LoadNoPass(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", "no-pass.txt"), cards);
            // Card unlocks (rivals' gifts, bonds, Gus's bounties, set completion); this also names the set posters.
            Unlocks = new CardUnlocks(cards, printings, Circuits, roster, NoPass, name => _library.Contains(name) ? _library.Load(name) : null, Shop,
                BountyBoard.Load(Path.Combine(Path.GetDirectoryName(circuitsFile) ?? "", BountyBoard.FileName)), Guests, Library);
            CatchUpUnlocks();
            // The Back Alley (CampaignState.Alley.cs): its tables, the Alley list, and a save left mid-duel settled as a loss.
            InitAlley(Path.GetDirectoryName(circuitsFile) ?? "");
            // Any other campaign duel left mid-way settles as a loss too (CampaignState.LiveDuel.cs).
            AfterLiveLoad();
        }

        // ---- Card unlocks -------------------------------------------------------------------------------

        /// <summary>Card unlocks (see <see cref="CardUnlocks"/>): what a result, a set or a Recipe pays.</summary>
        public CardUnlocks Unlocks { get; }

        /// <summary>Every grant paid, held or offered, as it happens (the QA stand-in logs them).</summary>
        public event Action<Grant>? Granted;

        /// <summary>
        /// What an old save was paid when it loaded (<see cref="CardUnlocks.CatchUp"/>): "Gus kept these for you: ..." for
        /// the campaign screen to show once; "" when nothing.
        /// </summary>
        public string CatchUpNote = "";

        /// <summary>The grants as one status line ("" when there are none).</summary>
        public static string GrantsLine(IEnumerable<Grant> grants) => string.Join("  ·  ", grants.Select(g => g.Text));

        private List<Grant> Announce(List<Grant> grants)
        {
            foreach (var g in grants) Granted?.Invoke(g);
            return grants;
        }

        /// <summary>A save from before card unlocks: the gifts it already earned, once, then saved.</summary>
        private void CatchUpUnlocks()
        {
            if (Profile == null || Profile.UnlocksCaughtUp) return;
            var grants = Announce(Unlocks.CatchUp(Profile));
            Save();
            if (grants.Count > 0) CatchUpNote = "Gus kept these for you: " + GrantsLine(grants);
        }

        /// <summary>
        /// A campaign result's unlocks (call after the result is recorded, before saving): <paramref name="band"/> is the
        /// circuit's band for a one-on-one circuit duel, "" otherwise. The grants are on <see cref="DuelReward.Grants"/> too.
        /// </summary>
        public List<Grant> AfterDuel(DuelReward reward, string band, DuelTally? tally, bool loaner, bool tag) =>
            Profile == null ? new List<Grant>() : Announce(Unlocks.AfterDuel(Profile, reward, band, tally, loaner, tag));

        /// <summary>Set completion after the collection grew (packs, structure decks, trades, the used market): saves; the grants' line.</summary>
        public string CheckSets()
        {
            if (Profile == null) return "";
            var grants = Announce(Unlocks.CheckSets(Profile));
            if (grants.Count > 0) Save();
            return GrantsLine(grants);
        }

        /// <summary>What's held and can be paid now (a Recipe set, the campaign screen opened): saves; the grants' line.</summary>
        public string PayOwed()
        {
            if (Profile == null) return "";
            var grants = Announce(Unlocks.PayOwed(Profile));
            grants.AddRange(Announce(Unlocks.CheckSets(Profile)));
            if (grants.Count > 0) Save();
            return GrantsLine(grants);
        }

        /// <summary>
        /// The bond 3 trade: <paramref name="give"/> for <paramref name="take"/> (a copy a saved deck needs is refused), then
        /// sets checked and saved. The grants (the trade first), or null with the reason in <paramref name="refused"/>.
        /// </summary>
        public List<Grant>? BondTrade(string id, uint take, CardCopy give, out string? refused)
        {
            refused = "no campaign";
            if (Profile == null) return null;
            var trade = Unlocks.Trade(Profile, id, take, give, NeededByDecks, out refused);
            if (trade == null) return null;
            var grants = new List<Grant> { trade };
            grants.AddRange(Unlocks.CheckSets(Profile));
            Save();
            return Announce(grants);
        }

        /// <summary>
        /// A loaded or new save's collection hooked up to the game's data: its value floors (<see cref="Collection.CommonestOf"/>),
        /// and the text timeline and printings its new copies are stamped by (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.1-4.3:
        /// <see cref="Collection.Texts"/>, <see cref="Collection.Printings"/>), which Assign and the 4th-copy rule tell texts apart by.
        /// </summary>
        private void WireCollection(Profile? profile)
        {
            if (profile == null) return;
            profile.Collection.CommonestOf = Commonest;
            profile.Collection.Texts = Texts;
            profile.Collection.Printings = _printings;
        }

        /// <summary>A card's commonest printing (base code): its value floor.</summary>
        private Rarity Commonest(uint code) => _printings?.Commonest(code, _cards.Get(code)?.Alias ?? 0) ?? Rarity.Common;

        /// <summary>
        /// How many copies of a card your saved decks need at most (main, extra and side, partner edits included): the
        /// Grading Desk and the Sell desk won't take a copy that leaves one short.
        /// </summary>
        public int NeededByDecks(uint baseCode)
        {
            int most = 0;
            foreach (var name in _library.CampaignNames)
            {
                if (IsLoanerDeck(name)) continue;
                var deck = _library.Load(name);
                most = Math.Max(most, deck.Main.Concat(deck.Extra).Concat(deck.Side).Count(c => Banlist.BaseCode(c, _cards) == baseCode));
            }
            return most;
        }

        /// <summary>Every saved deck that plays a card (for the desks' warnings).</summary>
        public List<string> DecksUsing(uint baseCode) =>
            _library.CampaignNames.Where(n => !IsLoanerDeck(n) && _library.Load(n).Main.Concat(_library.Load(n).Extra).Any(c => Banlist.BaseCode(c, _cards) == baseCode)).ToList();

        private List<uint>? _marketPool;

        /// <summary>The cards the used market deals in: every card the campaign's duelists play (base codes).</summary>
        public IReadOnlyList<uint> MarketPool => _marketPool ??= RecipeBook.All(Circuits).Select(r => r.Deck)
            .Where(_library.Contains).SelectMany(d => { var deck = _library.Load(d); return deck.Main.Concat(deck.Extra); })
            .Select(c => Banlist.BaseCode(c, _cards)).Distinct().OrderBy(c => c).ToList();

        /// <summary>The used market's listings now.</summary>
        public List<Listing> MarketListings() =>
            Profile == null || _printings == null ? new List<Listing>() : UsedMarket.Listings(Profile, MarketPool, ShopYear, _printings, _cards);

        /// <summary>Starts a new campaign with a starter deck: its cards are your collection, the deck your first campaign deck.</summary>
        public void Start(StarterDeck starter, bool eraLimited = false)
        {
            // Under Historic Rules a classic starter comes in its 2011 edition, so it's legal at the first list.
            var source = eraLimited && starter.HistoricDeck.Length > 0 && _library.Contains(starter.HistoricDeck) ? starter.HistoricDeck : starter.Deck;
            var deck = _library.Contains(source) ? _library.Load(source) : new Deck();
            deck.Name = source == starter.Deck ? starter.Title : $"{starter.Title} (2011)";
            deck.Campaign = true;
            while (_library.Contains(deck.Name) && !_library.IsCampaign(deck.Name)) deck.Name += " (campaign)";
            _library.Save(deck);
            // A new game starts with no story queued and no notice: the last run's never play here (CampaignState.Alley).
            _storyQueue.Clear();
            _storyInHand.Clear();
            AlleyNotice = "";
            Profile = Profile.New(deck, _cards, _printings, Texts);
            WireCollection(Profile);
            Profile.EraLimited = eraLimited;
            Circuits.Migrate(Profile);
            Save();
        }

        /// <summary>Starts over: the save is kept as a dated backup next to it, and the next screen picks a starter deck.</summary>
        public void StartOver()
        {
            if (File.Exists(SavePath))
                File.Copy(SavePath, Path.Combine(Path.GetDirectoryName(SavePath)!, $"profile-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), overwrite: true);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            Profile = null;
            // The old run's queued story and notice go with it (cleared as they are: there's no save to write now).
            _storyQueue.Clear();
            _storyInHand.Clear();
            AlleyNotice = "";
        }

        /// <summary>New Game+: circuits from the start, keeping everything earned.</summary>
        public void NewGamePlus()
        {
            Profile?.NewGamePlus();
            Save();
        }

        public void Save()
        {
            if (Profile == null) return;
            try { Profile.Save(SavePath); }
            catch (IOException ex) { Debug.LogWarning($"Could not save the campaign: {ex.Message}"); }
        }

        /// <summary>A deck's name to show: its #title, else the file name in words ("stall-burn" is "Stall Burn").</summary>
        public string DeckTitle(string deck)
        {
            if (MyRecipes.IsMine(deck)) return MyRecipes.NameOf(deck); // one of your own recipes (CampaignState.MyRecipes)
            if (Alley.IsAlleyDeck(deck)) return AlleyDeckTitle(deck);
            if (Ygo.Engine.Campaign.Loaners.IsLoaner(deck)) return $"{DeckTitle(Ygo.Engine.Campaign.Loaners.RecipeOf(deck))} (loaner)";
            var title = _library.Contains(deck) ? _library.Load(deck).Note("title") : "";
            return title.Length > 0 ? title : SlugTitle(deck);
        }

        /// <summary>
        /// A deck's name as a duelist says it in a story line: its title without a copy's suffix ("Mekk-Knight Invoked
        /// Evolved (new)" is "Mekk-Knight Invoked Evolved", "Warriors (2)" is "Warriors").
        /// </summary>
        public string SpokenTitle(string deck) =>
            System.Text.RegularExpressions.Regex.Replace(DeckTitle(deck), @"\s*\((\d+|new|copy|campaign|loaner|custom loaner( \d+)?|recipe( \d+)?)\)$", "");

        /// <summary>A file slug ("stall-burn") in words ("Stall Burn"); a name you gave a deck ("Mekk-Knight Invoked (new)") stays as it is.</summary>
        public static string SlugTitle(string deck)
        {
            return deck.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-')
                ? string.Join(" ", deck.Split('-').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)))
                : deck;
        }

        /// <summary>
        /// The ban list your team answers to at a circuit: its era's list in era-limited play, none otherwise (null; also
        /// when the list isn't installed). The CPUs never answer to one: they play their decks as they were built.
        /// </summary>
        public Banlist? ListFor(int circuit) => (DebugListsApply ?? Profile?.EraLimited == true) ? CircuitList(circuit) : null;

        /// <summary>For automated checks: the lists apply (true) or don't (false) whatever the mode; null follows the mode.</summary>
        public bool? DebugListsApply;

        /// <summary>The list a circuit's era played under (circuits.txt), whatever the campaign's mode.</summary>
        public Banlist? CircuitList(int circuit) =>
            circuit >= 0 && circuit < Circuits.All.Count && Circuits.All[circuit].Banlist.Length > 0
                ? _banlists.FirstOrDefault(b => b.Name == Circuits.All[circuit].Banlist) : null;

        /// <summary>Cards a Ban Pass can never cover (campaign/no-pass.txt).</summary>
        public HashSet<uint> NoPass { get; } = new HashSet<uint>();

        /// <summary>How many Ban Passes a deck may use now (3, plus one per Advanced / Expert band cleared).</summary>
        public int PassAllowance => Profile == null ? BanPasses.Starting : BanPasses.Allowance(Circuits, Profile);

        /// <summary>
        /// The rules your deck answers to at a circuit: its list in era-limited play (see <see cref="ListFor"/>), the
        /// format's card pool in era-limited play, your Ban Passes, and its texts (<see cref="CircuitTexts"/>, either mode).
        /// </summary>
        public FormatRules Rules(int circuit)
        {
            var list = ListFor(circuit);
            return new FormatRules
            {
                List = list, Passes = PassAllowance,
                Pool = Profile?.EraLimited == true ? DeckRules.FormatEnd(list ?? Banlist.None, _banlists) : "",
                Texts = CircuitTexts(circuit),
            };
        }

        /// <summary>
        /// The texts a circuit's tables play (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.1): its era's list in both modes
        /// (texts are rules, not restrictions: F2), else the end of its era (the Beginner band has no list); today's texts
        /// for no circuit. Tag Cups and guests play their circuit's.
        /// </summary>
        public TextRules CircuitTexts(int circuit) =>
            circuit >= 0 && circuit < Circuits.All.Count
                ? TextRules.ForList(CircuitList(circuit), _banlists, "", Circuits.All[circuit].Era)
                : TextRules.Today;

        /// <summary>
        /// The versus screen's note when slots of your deck that ask for originals play current text here (spec 5.5, D3: a
        /// note, not a refusal), else "". The same mapping the duel's start makes (<see cref="PlayTexts.ForTable"/> over
        /// <see cref="Collection.Assign"/>'s copies): an original past its errata at this table, or one you don't own enough
        /// original copies of (<see cref="CollectionRules.TextShortfalls"/>, named again after the list). In a tag duel,
        /// <paramref name="partnerDeck"/> (the deck your partner brings) adds its cards when it is one of yours
        /// (<see cref="PlaysYourTexts"/>).
        /// </summary>
        public string TextsLine(Deck deck, FormatRules rules, string partnerDeck = "") => TextsLine(deck, rules.Texts, Profile?.Collection, partnerDeck);

        /// <summary>The versus line for a deck at a table's texts, played from <paramref name="owned"/>'s copies (null: as written, no collection).</summary>
        public string TextsLine(Deck deck, TextRules texts, Collection? owned, string partnerDeck = "")
        {
            var down = new List<uint>();
            var byList = new List<uint>();
            var shortOf = new List<uint>();
            void Map(Deck d)
            {
                PlayTexts.ForTable(d, owned?.Assign(d, _cards), texts, Texts, _cards, out var played);
                down.AddRange(played.Where(c => !down.Contains(c)));
                // The cards the table itself plays as current text (owning every copy wouldn't help).
                PlayTexts.ForTable(d, null, texts, Texts, _cards, out var listed);
                byList.AddRange(listed.Where(c => !byList.Contains(c)));
                if (owned != null)
                    shortOf.AddRange(CollectionRules.TextShortfalls(d, owned, Texts, _cards).Select(p => p.Code).Where(c => !shortOf.Contains(c)));
            }
            Map(deck);
            if (PlaysYourTexts(partnerDeck)) Map(_library.Load(partnerDeck));
            if (down.Count == 0) return "";
            string Names(IEnumerable<uint> codes) => string.Join(", ", codes.Select(c => _cards.Get(c)?.Name ?? c.ToString()));
            var line = down.Count == 1 ? $"1 card plays its current text here: {Names(down)}." : $"{down.Count} cards play their current text here: {Names(down)}.";
            // Those whose original would play here but you're short of original copies of: say why.
            var shortHere = down.Where(c => shortOf.Contains(c) && !byList.Contains(c)).ToList();
            if (shortHere.Count > 0) line += $" You own too few original copies of {Names(shortHere)}.";
            return line;
        }

        /// <summary>A Library cup's rules: its list and pool whatever the mode, and 5 Ban Passes.</summary>
        public FormatRules CupRules(int cup) => Library.Rules(cup, _banlists);

        /// <summary>An "Every Era" final round's rules: its cup's list and pool, no Ban Passes.</summary>
        public FormatRules RoundRules(int round) => Library.RoundRules(round, _banlists);

        /// <summary>A circuit's ban list as it applies to this deck, with the deck's Ban Passes.</summary>
        public Banlist ListForDeck(Deck deck, int circuit) => ListForDeck(deck, Rules(circuit));

        public Banlist ListForDeck(Deck deck, FormatRules rules) =>
            BanPasses.For(deck, rules.List ?? Banlist.None, rules.Passes, NoPass, _cards);

        /// <summary>
        /// Problems that stop a deck from being used in the campaign: deck rules, the circuit's ban list (when
        /// <paramref name="circuit"/> is given), and cards you don't own.
        /// </summary>
        public List<DeckProblem> Problems(Deck deck, int circuit = -1) => Problems(deck, Rules(circuit));

        /// <summary>The same under a set of rules (a Library cup's): its list, its pool, and cards you don't own.</summary>
        public List<DeckProblem> Problems(Deck deck, FormatRules rules)
        {
            // The ban list as it applies to this deck: its Ban Passes let some cards past it.
            var list = ListForDeck(deck, rules);
            var problems = DeckRules.Validate(deck, list, _cards);
            if (Profile != null) problems.AddRange(CollectionRules.Validate(deck, CollectionFor(deck), _cards));
            // A format's pool (era-limited play, a Library cup) keeps out cards that weren't released yet.
            if (rules.Pool.Length > 0) problems.AddRange(DeckRules.Releases(deck, list, rules.Pool, _printings, _cards));
            return problems;
        }

        /// <summary>Ban Passes a deck uses under a cup's list (a Format Master stamp needs none).</summary>
        public int PassesUsed(Deck deck, FormatRules rules) => BanPasses.Used(deck, rules.List ?? Banlist.None, _cards, NoPass);

        /// <summary>Whether a card is in a pool ("" = any).</summary>
        public bool InPool(uint code, string pool) => DeckRules.Released(code, pool, _printings, _cards);

        /// <summary>
        /// Where to get a card (base code): the first Card Shop set on sale that has it, a set that goes on sale later, or
        /// a Card Token at the Card Trader.
        /// </summary>
        public string WhereToGet(uint baseCode)
        {
            int year = Profile == null ? 2005 : Circuits.ShopYear(Profile);
            var sets = Shop?.Sets.Where(s => s.Pool.Values.Any(p => p.Contains(baseCode))).ToList() ?? new List<PackSet>();
            var now = sets.FirstOrDefault(s => int.TryParse(s.Year, out var y) && y <= year);
            int cost = CardTrader.Cost(baseCode, _printings, _cards);
            var tokens = $"{cost} Card Token{(cost == 1 ? "" : "s")}";
            if (now != null) return $"in {now.Info.Name} ({now.Price} DP a pack), or {tokens}";
            var later = sets.FirstOrDefault();
            return later != null ? $"in {later.Info.Name} (on sale from {later.Year}), or {tokens}" : $"{tokens} at the Card Trader";
        }

        /// <summary>Your best copy of a card, for foils.</summary>
        public Rarity? Owned(uint baseCode) => Profile?.Collection.Best(baseCode);

        /// <summary>The OCG import packs on sale: from the Expert band on, never in era-limited play.</summary>
        public IReadOnlyList<PackSet> ImportPackSets() =>
            Profile != null && ImportShelf.IsOpen(Profile, Circuits) ? Imports.Packs : (IReadOnlyList<PackSet>)Array.Empty<PackSet>();

        public int OwnedFromSet(PackSet set) =>
            Profile == null ? 0 : set.Pool.Values.SelectMany(c => c).Distinct().Count(Profile.Collection.Owns);

        public PrintingIndex? Printings => _printings;
        public int ShopYear => Profile == null ? 2005 : Circuits.ShopYear(Profile);

        // ---- Decks you don't own: loaners and recipe packs ----------------------------------------------------

        /// <summary>The unlocked recipes you can borrow, as loaner deck names ("loaner:tearlaments"), in campaign order.</summary>
        public List<string> LoanerDecks() => Profile == null ? new List<string>()
            : RecipeBook.Unlocked(Circuits, Profile).Select(r => r.Deck).Concat(LibraryRecipes()).Where(_library.Contains).Distinct()
                .Select(Ygo.Engine.Campaign.Loaners.NameFor).ToList();

        /// <summary>The Library decks you've beaten (Hollis's Machine Re-Volt, Pepe's Performapal...), lent like any recipe.</summary>
        private IEnumerable<string> LibraryRecipes() =>
            Enumerable.Range(0, Library.Cups.Count).SelectMany(c => Enumerable.Range(0, Library.Cups[c].Matches.Count)
                .Where(m => Library.HasWon(Profile!, c, m)).Select(m => Library.Cups[c].Matches[m].Deck));

        /// <summary>The newest circuit you've opened (where you'd take a deck now).</summary>
        public int CurrentCircuit => Profile == null ? 0 : Math.Max(0, Enumerable.Range(0, Circuits.All.Count).LastOrDefault(i => Circuits.IsUnlocked(i, Profile)));

        /// <summary>What a circuit's ban list changes in a loaner ("Removed Pot of Prosperity (Forbidden)", "Added ...").</summary>
        public List<string> LoanerChanges(string recipeDeck, int circuit) => LoanerChanges(recipeDeck, Rules(circuit));

        public List<string> LoanerChanges(string recipeDeck, FormatRules rules)
        {
            var recipe = _library.Load(recipeDeck);
            recipe.Name = recipeDeck;
            return Ygo.Engine.Campaign.Loaners.Adjust(recipe, rules.List ?? Banlist.None, _cards, LoanerPool(rules)).Changes;
        }

        /// <summary>The pool a loaner keeps to: a Library cup's (circuits lend recipes as built, list aside).</summary>
        private Func<uint, bool>? LoanerPool(FormatRules rules) =>
            rules.Forced && rules.Pool.Length > 0 ? c => InPool(c, rules.Pool) : (Func<uint, bool>?)null;

        /// <summary>Saves a custom loaner of a recipe (adjusted for the current circuit's list) and returns its deck name.</summary>
        public string CustomizeLoaner(string recipeDeck, string title)
        {
            var recipe = _library.Load(recipeDeck);
            recipe.Name = recipeDeck;
            var deck = Ygo.Engine.Campaign.Loaners.Customize(recipe, title, ListFor(CurrentCircuit) ?? Banlist.None, _cards);
            for (int n = 2; _library.Contains(deck.Name); n++) deck.Name = $"{title} (custom loaner {n})";
            _library.Save(deck);
            return deck.Name;
        }

        /// <summary>
        /// Owning a loaner's deck (the Stuck? advice's purchase): <see cref="Price"/> DP buys the cards you're missing from
        /// the list the loaner plays at the circuit, so you'd play the very deck that was measured. <see cref="StandIns"/>:
        /// cards the shop doesn't sell yet (era-limited play), filled from your collection. <see cref="Existing"/>: a deck
        /// of yours already built from this recipe (use it instead; nothing to buy).
        /// </summary>
        public sealed class OwnQuote
        {
            public string Recipe = "", Existing = "";
            public int Price, Cards, StandIns;
            /// <summary>The loaner's list at this circuit (what gets saved as yours).</summary>
            public Deck Deck = new Deck();
        }

        /// <summary>What owning <paramref name="loaner"/> ("loaner:tearlaments") at <paramref name="circuit"/> takes; null when it can't be priced.</summary>
        public OwnQuote? QuoteOwn(string loaner, int circuit) => QuoteOwn(loaner, Rules(circuit));

        /// <summary>
        /// The same under a set of rules. At a Library cup the loaner already keeps to the cup's list and pool, so its
        /// cards are the ones to buy, whatever the shop's era (the Library's own format sets the pool, not your band).
        /// </summary>
        public OwnQuote? QuoteOwn(string loaner, FormatRules rules)
        {
            var recipe = Ygo.Engine.Campaign.Loaners.RecipeOf(loaner);
            if (Profile == null || _printings == null || !Ygo.Engine.Campaign.Loaners.IsLoaner(loaner) || !_library.Contains(recipe)) return null;
            var deck = Loaner(loaner, rules);
            var wants = AutoDeck.Wants(deck, Banlist.None, _cards);
            int year = OwnYear(rules);
            return new OwnQuote
            {
                Recipe = recipe, Deck = deck,
                Existing = _library.CampaignNames.FirstOrDefault(d => _library.Load(d).Note("recipe") == recipe) ?? "",
                Price = RecipeShop.CompletePrice(deck, Profile, Banlist.None, year, _printings, _cards, RecipeShop.PackPriceFor(recipe, Circuits)),
                Cards = RecipeShop.Missing(deck, Profile, Banlist.None, year, _printings, _cards).Values.Sum(),
                StandIns = rules.Forced ? 0 : wants.Where(w => !RecipeShop.InEra(w.Key, Profile, ShopYear, _printings, _cards))
                    .Sum(w => Math.Max(0, w.Value - Profile.Collection.Count(w.Key))),
            };
        }

        /// <summary>The shop year Make it yours buys at: yours, or any year at a Library cup (its loaner is in its pool already).</summary>
        private int OwnYear(FormatRules rules) => rules.Forced ? 9999 : ShopYear;

        /// <summary>
        /// Buys what <paramref name="quote"/> prices and saves the loaner's list (no Side Deck: those cards weren't bought) as
        /// your campaign deck ("Tearlaments (recipe)", full DP, yours to edit). When some cards aren't sold yet, or the list
        /// isn't legal here for you (era-limited play), it's built from your collection instead, stand-ins and all. Returns
        /// its name, or null when the purchase didn't go through (nothing is spent then).
        /// </summary>
        public string? Own(OwnQuote quote, int circuit) => Own(quote, Rules(circuit));

        public string? Own(OwnQuote quote, FormatRules rules)
        {
            if (Profile == null || _printings == null || Profile.Dp < quote.Price) return null;
            if (quote.Price > 0 && RecipeShop.Complete(quote.Deck, Profile, Banlist.None, OwnYear(rules), _printings, _cards, RecipeShop.PackPriceFor(quote.Recipe, Circuits),
                    Ygo.Engine.Campaign.Ledger.MakeItYours) == 0) return null;
            var deck = quote.Deck.Clone();
            deck.Side.Clear();
            if (quote.StandIns > 0 || Problems(deck, rules).Count > 0)
            {
                var list = rules.List ?? Banlist.None;
                deck = AutoDeck.FromRecipe(quote.Deck, Profile.Collection, list, _cards, c => DeckRules.Released(c, rules.Pool, _printings, _cards)).Deck;
                deck.Side.Clear();
            }
            // Named as the library saves it ("Fire King Snake-Eye / Azamina" has to lose its slash), so your deck is found.
            var title = DeckTitle(quote.Recipe);
            var file = DeckLibrary.SafeName(title);
            deck.Name = $"{file} (recipe)";
            for (int n = 2; _library.Contains(deck.Name); n++) deck.Name = $"{file} (recipe {n})";
            deck.Campaign = true;
            foreach (var key in Deck.NoteKeys) if (quote.Deck.Note(key) is { Length: > 0 } v && key != "loaner") deck.Notes[key] = v;
            deck.Notes.Remove("loaner");
            deck.Notes["title"] = title;
            deck.Notes["recipe"] = quote.Recipe; // it plays by its recipe's playbook
            _library.Save(deck);
            Profile.Deck = deck.Name;
            Save();
            return deck.Name;
        }

        /// <summary>A deck that's a loaner (borrowed recipe) or a custom loaner: half DP, no Ban Passes.</summary>
        public bool IsLoanerDeck(string name) =>
            Ygo.Engine.Campaign.Loaners.IsLoaner(name) || (_library.Contains(name) && _library.Load(name).Note("loaner").Length > 0);

        /// <summary>
        /// The cards a deck may use: your collection, plus its recipe's cards for a custom loaner or for your edit of a
        /// partner's deck (their own cards stay theirs to bring).
        /// </summary>
        public Collection CollectionFor(Deck deck)
        {
            var owned = Profile?.Collection ?? new Collection();
            var recipe = deck.Note("loaner").Length > 0 ? deck.Note("loaner") : deck.Note("partner").Length > 0 ? deck.Note("recipe") : "";
            if (recipe.Length == 0 || !_library.Contains(recipe)) return owned;
            return Ygo.Engine.Campaign.Loaners.Borrowed(_library.Load(recipe), _cards, owned);
        }

        /// <summary>
        /// Your partner's deck as it plays at a circuit: a loaner made legal for the list. A partner's own recipe is their
        /// exhibition deck and plays as built under any list, as the CPUs' decks do. Your decks (lent to Kit, or a partner's
        /// deck you edited) play as they are: the Tag Cup checks them like yours.
        /// </summary>
        public Deck PartnerPlays(string name, int circuit)
        {
            if (Ygo.Engine.Campaign.Loaners.IsLoaner(name)) return Loaner(name, Rules(circuit));
            return _library.Contains(name) ? _library.Load(name) : new Deck();
        }

        /// <summary>
        /// Whether a partner's deck plays the table's texts as yours do (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.2): one of
        /// your campaign decks lent to Kit, or your edit of a partner's deck ("#partner"). A roster deck and a loaner play
        /// as authored (5.6: CPU decks keep full strength).
        /// </summary>
        public bool PlaysYourTexts(string partnerDeck) =>
            partnerDeck.Length > 0 && _library.Contains(partnerDeck) && _library.IsCampaign(partnerDeck) && !IsLoanerDeck(partnerDeck);

        /// <summary>What a list changes in a partner's deck (see <see cref="PartnerPlays"/>): only a loaner's; empty otherwise.</summary>
        public List<string> PartnerChanges(string name, int circuit) =>
            Ygo.Engine.Campaign.Loaners.IsLoaner(name) ? LoanerChanges(Ygo.Engine.Campaign.Loaners.RecipeOf(name), circuit) : new List<string>();

        /// <summary>A loaner deck for a circuit: the recipe, made legal for its ban list.</summary>
        public Deck Loaner(string name, int circuit) => Loaner(name, Rules(circuit));

        public Deck Loaner(string name, FormatRules rules)
        {
            var recipe = _library.Load(Ygo.Engine.Campaign.Loaners.RecipeOf(name));
            recipe.Name = Ygo.Engine.Campaign.Loaners.RecipeOf(name);
            return Ygo.Engine.Campaign.Loaners.Build(recipe, rules.List ?? Banlist.None, _cards, LoanerPool(rules));
        }

        // ---- The card shop: shelves, house packs, tags and Gus's lines (SHOP-REWORK-SPEC) --------------------------

        /// <summary>The featured shelves (campaign/featured.txt): the sets Gus puts up front for each circuit.</summary>
        public FeaturedShelves Featured { get; }
        /// <summary>The shop's lines (campaign/shop.txt): Gus and Kit, the house pack labels.</summary>
        public ShopVoice Voice { get; }
        private readonly List<Personality> _roster;
        /// <summary>Old Gus, who owns the shop (his house packs read house-pack:about-gus).</summary>
        public const string GusId = "old-gus";

        /// <summary>A speaker's short name for a shop line: the last word of their name ("Old Gus" is "Gus", "Professor Hollis" is "Hollis").</summary>
        public string SpeakerName(string id)
        {
            var name = _roster.FirstOrDefault(p => p.Id == id)?.Name ?? id;
            var words = name.Split(' ');
            return words[words.Length - 1];
        }

        /// <summary>"Captain Brask, Ryo and Vera": names joined as a line says them.</summary>
        public static string JoinNames(IReadOnlyList<string> names) =>
            names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

        private readonly Dictionary<(string deck, int year), PackSet> _housePacks = new Dictionary<(string, int), PackSet>();

        /// <summary>
        /// Gus's house packs (the code's selection packs): one per deck of a match not yet won in the current circuit and
        /// the next one (<see cref="Circuits.HouseMatches"/>), built from that deck's cards within the shop's era, priced by
        /// the deck's band (<see cref="SelectionPacks.PriceFor"/>) and named by the house-pack:name line. None in the post-game.
        /// </summary>
        public List<PackSet> HousePackSets()
        {
            var packs = new List<PackSet>();
            if (Profile == null || _printings == null) return packs;
            int year = ShopYear;
            foreach (var (_, _, m) in Circuits.HouseMatches(Profile))
            {
                if (!_library.Contains(m.Deck)) continue;
                if (!_housePacks.TryGetValue((m.Deck, year), out var set))
                {
                    var deck = _library.Load(m.Deck);
                    var title = DeckTitle(m.Deck);
                    var pool = deck.Main.Concat(deck.Extra).Select(c => Banlist.BaseCode(c, _cards));
                    var name = Voice.Pick("house-pack:name", Profile, Circuits, new Dictionary<string, string> { ["deck"] = title })?.Text;
                    set = Ygo.Engine.Campaign.SelectionPacks.Build(title, "SP-" + m.Deck, pool, Profile, year, _printings, _cards,
                        Ygo.Engine.Campaign.SelectionPacks.PriceFor(m.Deck, Circuits), name);
                    _housePacks[(m.Deck, year)] = set;
                }
                if (set.CardCount > 0) packs.Add(set);
            }
            return packs;
        }

        /// <summary>
        /// A house pack's description line: house-pack:about with every character who plays its deck ({opponents}), or
        /// house-pack:about-gus when Old Gus is one of them; "" for any other pack.
        /// </summary>
        public string HouseAbout(PackSet set)
        {
            if (Profile == null || !Ygo.Engine.Campaign.SelectionPacks.IsHousePack(set)) return "";
            var deck = set.Info.Code.Substring(3);
            var players = Circuits.PlayersOf(deck);
            var fill = new Dictionary<string, string> { ["opponents"] = JoinNames(players.Select(p => p.Name).ToList()), ["deck"] = DeckTitle(deck) };
            bool gus = players.Any(p => p.Id == GusId);
            return (gus ? Voice.Pick("house-pack:about-gus", Profile, Circuits, fill) : null)?.Text
                   ?? Voice.Pick("house-pack:about", Profile, Circuits, fill)?.Text ?? "";
        }

        /// <summary>The shop's list for this visit: the featured shelf, New arrivals, house packs, imports and the back shelves.</summary>
        public ShopShelves? Shelves() =>
            Profile == null || Shop == null ? null : ShopShelves.Build(Profile, Circuits, Shop, Featured, HousePackSets(), ImportPackSets());

        /// <summary>What the shop's tags know this visit: your wishlist, the house-pack opponents' cards, your decks' cards.</summary>
        public TagContext? Tags()
        {
            if (Profile == null) return null;
            var names = _library.CampaignNames.Append(Profile.Deck).Where(_library.Contains).Distinct();
            var decks = names.Select(n => { var d = _library.Load(n); d.Name = n; return d; }).ToList();
            return TagContext.Build(Profile, Circuits, decks, name => _library.Contains(name) ? _library.Load(name) : null, DeckTitle, _cards, Scouting);
        }

        /// <summary>A shop set's progress as set completion counts it (owned, total); (0, 0) for house packs and imports.</summary>
        public (int owned, int total) SetProgress(PackSet set) => Profile == null ? (0, 0) : Unlocks.Progress(Profile, set.Info.Code);
    }
}
