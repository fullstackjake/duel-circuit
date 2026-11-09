using System.Collections.Generic;

namespace Ygo.Engine.AI.Playbooks
{
    /// <summary>Finds the playbook for a deck. Each bot gets its own instance (playbooks track per-turn use).</summary>
    public static class Playbooks
    {
        /// <summary>
        /// Decks that would rather go second, with why: the ones whose own cards need the opponent's board or a Battle
        /// Phase, as players build and play them. `--rate-decks` measures each deck both ways too, but it measures how our
        /// bots win with it: a deck played card by card, without a playbook, can't build the turn-1 board it's made for, so
        /// it looks better second than it is (Exosister, Unchained, ABC, Blackwing and Six Samurai did, until playbooks
        /// built their Magnifica, Caesar + Abomination, ABC-Dragon Buster, Armor Master and Shi En). Every other deck goes
        /// first; the flexible ones (Unchained, Danger! Dark World, ABC) still play well when they lose the toss.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> SecondDecks = new Dictionary<string, string>
        {
            ["mekk-knight-invoked"] = "Mekk-Knights summon themselves into columns holding the opponent's cards",
            ["mekk-knight-invoked-evolved"] = "Mekk-Knights summon themselves into columns holding the opponent's cards",
            ["mekk-knight"] = "Mekk-Knights summon themselves into columns holding the opponent's cards",
            ["tenpai-dragon"] = "it clears the board and Synchro Summons in the Battle Phase, which turn 1 doesn't have",
        };

        /// <summary>
        /// Coin toss: whether a duelist who won it with this deck goes first. Yes, unless it's one of
        /// <see cref="SecondDecks"/>. Campaign copies count as their deck ("Stall Burn", "Mekk-Knight Invoked (3)").
        /// </summary>
        public static bool GoesFirst(string deckName) => !SecondDecks.ContainsKey(Slug(deckName));

        /// <summary>Why a deck goes second, or null.</summary>
        public static string? WhySecond(string deckName) => SecondDecks.TryGetValue(Slug(deckName), out var why) ? why : null;

        /// <summary>A deck's file name from its title: "Mekk-Knight Invoked (3)" becomes mekk-knight-invoked.</summary>
        public static string Slug(string deckName)
        {
            var name = System.Text.RegularExpressions.Regex.Replace(deckName.Trim(), @"\s*\((\d+|new|campaign|copy)\)$", "");
            return System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        }

        /// <summary>Decks played without their playbook (for measuring what a playbook is worth: `--playbook-ab`).</summary>
        public static readonly HashSet<string> Off = new HashSet<string>();

        /// <summary>
        /// The playbook for a deck by its file name, else by its name as a copy is called ("Mekk-Knight Invoked Evolved
        /// (new)" plays as mekk-knight-invoked-evolved). A loaner goes by its recipe (see <see cref="Deck.Notes"/> "loaner").
        /// </summary>
        public static Playbook? ForDeck(string deckName)
        {
            if (Off.Contains(deckName)) return null;
            var slug = Slug(deckName);
            return Lookup(deckName) ?? (slug != deckName && !Off.Contains(slug) ? Lookup(slug) : null);
        }

        /// <summary>The playbook a deck plays by: its recipe's, for a loaner, a custom loaner or a deck built from a recipe.</summary>
        public static Playbook? ForDeck(Deck deck) => ForDeck(RecipeOf(deck));

        /// <summary>The recipe a deck plays as: a loaner's ("#loaner"), a deck built from a recipe ("#recipe"), else its own name.</summary>
        public static string RecipeOf(Deck deck) =>
            deck.Note("loaner") is { Length: > 0 } loaner ? loaner : deck.Note("recipe") is { Length: > 0 } recipe ? recipe : deck.Name;

        private static Playbook? Lookup(string deckName)
        {
            switch (deckName)
            {
                case "synchron":
                case "modern":
                    return new SynchronPlaybook(deckName);
                case "zoodiac": return new ZoodiacPlaybook();
                case "mekk-knight-invoked":
                case "Mekk-Knight Invoked": // the campaign's copy of the starter (before the 2026 update)
                case "Mekk-Knight Invoked Evolved": // the campaign's copy of the starter
                case "mekk-knight-invoked-evolved": // a copy of it ("Mekk-Knight Invoked Evolved (new)")
                    return new MekkKnightPlaybook(deckName);
                case "dragon-ruler": return new DragonRulerPlaybook();
                case "burning-abyss": return new BurningAbyssPlaybook();
                case "nekroz": return new NekrozPlaybook();
                case "spyral": return new SpyralPlaybook();
                case "snake-eye": return new SnakeEyePlaybook();
                case "sky-striker": return new SkyStrikerPlaybook();
                // The late campaign's bosses (Expert Series, Master League, Tier 0 Gauntlet).
                case "wind-up": return new WindUpPlaybook();
                case "shaddoll": return new ShaddollPlaybook();
                case "kozmo": return new KozmoPlaybook();
                case "danger-dark-world": return new DangerDarkWorldPlaybook();
                case "adamancipator": return new AdamancipatorPlaybook();
                case "drytron": return new DrytronPlaybook();
                case "yubel": return new YubelPlaybook();
                case "tenpai-dragon": return new TenpaiDragonPlaybook();
                case "fire-king-snake-eye": return new FireKingSnakeEyePlaybook();
                case "ryzeal": return new RyzealPlaybook();
                case "angelechy": return new AngelechyPlaybook();
                case "elfnote": return new ElfnotePlaybook();
                case "thunder-dragon": return new ThunderDragonPlaybook();
                case "eldlich": return new EldlichPlaybook();
                case "bystial-dragon-link": return new BystialDragonLinkPlaybook();
                case "spright": return new SprightPlaybook();
                case "tearlaments":
                case "ishizu-tearlaments":
                    return new TearlamentsPlaybook(deckName);
                case "kashtira": return new KashtiraPlaybook();
                case "k9-vanquish-soul": return new K9VanquishSoulPlaybook();
                case "mitsurugi": return new MitsurugiPlaybook();
                case "light-and-darkness": return new LightAndDarknessPlaybook();
                case "branded-despia": return new BrandedDespiaPlaybook();
                // The October 2026 meta decks whose plans the generic bot misses.
                case "radiant-typhoon": return new RadiantTyphoonPlaybook();
                case "artmage": return new ArtmagePlaybook();
                case "magistus-fairy-tail": return new MagistusFairyTailPlaybook();
                case "power-patron": return new PowerPatronPlaybook();
                // Decks that go first to build their turn-1 board (they looked better second only without these).
                case "exosister": return new ExosisterPlaybook();
                case "unchained": return new UnchainedPlaybook();
                case "abc": return new AbcPlaybook();
                case "blackwing": return new BlackwingPlaybook();
                case "six-samurai": return new SixSamuraiPlaybook();
                // The Alley's headliners (docs/dev/HEADLINERS-2026-11-01.md).
                case "frog-ftk": return new FrogFtkPlaybook();
                case "chaos-yata": return new YataLockPlaybook();
                // Headliners that play their own route ("#recipe <id>"; "#rating <deck>" names the rated deck their panel shows).
                case "trishula-trish": return new TrishulaPlaybook();
                case "return-rook": return new ReturnRookPlaybook();
                case "gumblar-gail": return new GumblarGailPlaybook();
                case "master-plan-marlo": return new SpyralPlaybook(masterPlan: true);
                case "scientist-turtle": return new ScientistTurtlePlaybook();
                case "gateway-gwen": return new SixSamuraiPlaybook(gateway: true);
                default: return null;
            }
        }
    }
}
