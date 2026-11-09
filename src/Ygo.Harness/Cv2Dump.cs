using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// `--cv2-dump &lt;prefix&gt;` (card versions phase 2, chunk C1's determinism proof; temporary): what every copy-creation
/// site makes from fixed seeds, written to &lt;prefix&gt;.content.txt (what a pack, listing, binder or prize holds),
/// &lt;prefix&gt;.take.txt (what the 4th-copy rule kept and paid) and &lt;prefix&gt;.stamps.txt (each copy's printing and
/// text). Run it before and after a change: content must be identical.
/// </summary>
public static class Cv2Dump
{
    public static int Run(ICardDataProvider cards, string root, string prefix)
    {
        var content = new StringBuilder();
        var take = new StringBuilder();
        var stamps = new StringBuilder();
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var texts = CardTexts.Load(Path.Combine(root, "campaign", CardTexts.File), cards);
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);

        // The printings themselves (v3 and v4 must hold the same printings and sets).
        var digest = new StringBuilder();
        foreach (var pair in printings.All.OrderBy(p => p.Key))
            digest.Append(pair.Key).Append(':').Append(string.Join(",", pair.Value.Select(p => $"{p.SetCode}|{p.SetName}|{p.Rarity}"))).Append('\n');
        foreach (var s in printings.Sets.Values.OrderBy(s => s.Code, StringComparer.Ordinal)) digest.Append($"set {s.Code}|{s.Name}|{s.Date}|{s.CardCount}\n");
        content.AppendLine($"printings {printings.Count} ids, {printings.Sets.Count} sets, sha256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString())))}");

        Profile Fresh(uint salt)
        {
            var p = new Profile { Dp = 100_000_000 };
            p.Collection.Salt = salt;
            p.Collection.CommonestOf = Commonest;
            // Phase 2's stamping context, when the build has it (by reflection, so the same file builds before and after).
            typeof(Collection).GetField("Texts")?.SetValue(p.Collection, texts);
            typeof(Collection).GetField("Printings")?.SetValue(p.Collection, printings);
            return p;
        }
        string Stamp(CardCopy? c) => c == null ? "-" : $"{c.Code} set={(c.Set.Length > 0 ? c.Set : "?")} printed={(c.Printed.Length > 0 ? c.Printed : "?")} text={c.Text}";
        string Took(Added a) => $"kept={a.Kept} replaced={a.Replaced?.ToString() ?? "-"} dp={a.Dp} copy={(a.Copy == null ? "-" : $"{a.Copy.Id}/{a.Copy.Grade}/{a.Copy.Paid}")}";

        // A. Every real booster, two packs each, into one collection (so 4th copies happen).
        var catalog = new PackCatalog(printings, cards);
        var shop = Fresh(42);
        foreach (var set in catalog.Sets.OrderBy(s => s.Info.Code, StringComparer.Ordinal))
            for (int k = 0; k < 2; k++)
            {
                var rng = new Random((int)Hash.Mix(CardUnlocks.StableHash(set.Info.Code), (uint)k));
                var opened = shop.OpenPack(set, rng, out var refund);
                content.AppendLine($"pack {set.Info.Code} {k}: {string.Join(" ", opened.Select(o => $"{o.pull.Code}/{o.pull.Rarity}"))}");
                take.AppendLine($"pack {set.Info.Code} {k}: refund {refund}; {string.Join("; ", opened.Select(o => Took(o.added)))}");
                foreach (var o in opened) stamps.AppendLine($"pack {set.Info.Code} {k}: {Stamp(o.added.Copy)}");
            }

        // B. Booster boxes of 12 sets.
        var box = Fresh(43);
        foreach (var set in catalog.Sets.OrderBy(s => s.Info.Code, StringComparer.Ordinal).Where((s, i) => i % 40 == 0))
        {
            var r = BoosterBox.Open(box, set, 1000, new Random((int)CardUnlocks.StableHash(set.Info.Code)));
            content.AppendLine($"box {set.Info.Code}: topper {r.Topper.Code}/{r.Topper.Rarity} {r.Upgraded}: {string.Join(" ", r.Pulls.Select(o => $"{o.pull.Code}/{o.pull.Rarity}"))}");
            take.AppendLine($"box {set.Info.Code}: refund {r.Refund}; {string.Join("; ", r.Pulls.Select(o => Took(o.added)))}");
            foreach (var o in r.Pulls) stamps.AppendLine($"box {set.Info.Code}: {Stamp(o.added.Copy)}");
        }

        // C. Structure decks, recipe packs and outright buys, the Card Trader.
        var buyer = Fresh(44);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "decks", "structure"), "*.ydk").OrderBy(f => f, StringComparer.Ordinal))
        {
            var deck = Deck.LoadYdk(file);
            int kept = buyer.BuyStructureDeck(deck, 1000, cards);
            content.AppendLine($"structure {Path.GetFileName(file)}: kept {kept}, dp {buyer.Dp}");
        }
        foreach (var name in new[] { "classic", "goat-control", "yata-mags", "chaos-yata" })
        {
            var file = Directory.GetFiles(Path.Combine(root, "decks"), name + ".ydk", SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) continue;
            var recipe = Deck.LoadYdk(file);
            foreach (var year in new[] { 2005, 2026 })
            {
                var p = Fresh(45 + (uint)year);
                var pulls = RecipeShop.OpenPack(recipe, p, Banlist.None, year, printings, cards, new Random(year));
                content.AppendLine($"recipe pack {name} {year}: {string.Join(" ", pulls.Select(x => $"{x.pull.Code}/{x.pull.Rarity}"))}");
                int added = RecipeShop.Complete(recipe, p, Banlist.None, year, printings, cards);
                content.AppendLine($"recipe outright {name} {year}: {added} copies, dp {p.Dp}");
                foreach (var c in p.Collection.All.OrderBy(c => c.Id)) stamps.AppendLine($"recipe {name} {year}: {Stamp(c)}");
            }
        }
        foreach (var c in buyer.Collection.All.OrderBy(c => c.Id)) stamps.AppendLine($"structure: {Stamp(c)}");
        var trader = Fresh(46);
        trader.Tokens = 10_000;
        foreach (var code in new uint[] { 26202165, 78010363, 95727991, 82301904, 61740673, 13955608, 40640057 })
        {
            bool ok = CardTrader.Trade(code, trader, 2005, printings, cards);
            content.AppendLine($"trade {code}: {ok}");
        }
        foreach (var c in trader.Collection.All.OrderBy(c => c.Id)) stamps.AppendLine($"trade: {Stamp(c)}");

        // D. The used market and special orders.
        var pool = cards.All.Where(c => c.Alias == 0 && printings.Of(c.Code).Length > 0).Select(c => c.Code).OrderBy(c => c).ToList();
        foreach (var (era, year) in new[] { (false, 2026), (true, 2008) })
        {
            var p = Fresh(99);
            p.EraLimited = era;
            for (int epoch = 0; epoch < (era ? 5 : 20); epoch++)
            {
                p.DuelsPlayed = epoch * UsedMarket.Every;
                foreach (var l in UsedMarket.Listings(p, pool, year, printings, cards))
                {
                    content.AppendLine($"used {era} {epoch} {l.Slot}: {l.Copy.Code}/{l.Copy.Rarity} g{l.Copy.Grade} s{l.Copy.Slab} seed {l.Copy.Seed} {l.Price} {l.Wanted}");
                    stamps.AppendLine($"used {era} {epoch} {l.Slot}: {Stamp(l.Copy)}");
                }
            }
            if (!era)
            {
                var ls = UsedMarket.Listings(p, pool, year, printings, cards);
                foreach (var l in ls.Take(6))
                {
                    int paid = UsedMarket.Buy(p, l);
                    take.AppendLine($"used buy {l.Slot}: {paid}");
                }
                foreach (var c in p.Collection.All.OrderBy(c => c.Id)) stamps.AppendLine($"used bought: {Stamp(c)}");
            }
        }
        var orders = Fresh(100);
        foreach (var code in new uint[] { 26202165, 78010363, 95727991, 82301904, 61740673, 13955608, 511002631 })
            foreach (var r in UsedMarket.Orderable(code, printings, cards))
            {
                var copy = UsedMarket.Order(orders, code, r, 2026, printings, cards);
                content.AppendLine($"order {code}/{r}: {(copy == null ? "-" : $"{copy.Grade} {copy.Paid}")}");
                stamps.AppendLine($"order {code}/{r}: {Stamp(copy)}");
            }

        // E. Prize Packs: every Library cup's Locals, 40 packs each.
        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var ratings = DeckRatings.Load(Path.Combine(root, "campaign", DeckRatings.FileName));
        var deckFiles = Directory.GetFiles(Path.Combine(root, "decks"), "*.ydk", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileNameWithoutExtension).ToDictionary(g => g.Key!, g => g.First());
        Deck? LoadDeck(string name) => deckFiles.TryGetValue(name, out var f) ? Deck.LoadYdk(f) : null;
        var prizes = new PrizePacks(cards, printings, LoadDeck);
        for (int c = 0; c < library.Cups.Count; c++)
        {
            var cup = library.Cups[c];
            if (cup.Locals.Count != Brackets.LocalsPerCup) continue;
            var b = Brackets.Create(cup, ratings, Hash.Mix(20261101u, (uint)c), "");
            var p = Fresh(0xC0FFEE + (uint)c);
            for (int k = 0; k < 40; k++)
            {
                var pulls = prizes.Give(p, b, cup.Pool, k);
                content.AppendLine($"prize {cup.Name} {k}: {string.Join(" ", pulls.Select(x => $"{x.Slot}:{x.Code}/{x.Rarity}/{x.Slab}/{x.SetCode}"))}");
                take.AppendLine($"prize {cup.Name} {k}: {string.Join(" ", pulls.Select(x => $"{x.Kept}/{x.Replaced?.ToString() ?? "-"}/{x.Value}"))}");
                foreach (var x in pulls) stamps.AppendLine($"prize {cup.Name} {k}: {Stamp(x.Copy)}");
            }
        }

        // F. The alley: Contraband Packs, Moth's van, binders.
        var setup = AlleyTest.Load(cards, root, printings, roster, library);
        var ctx = setup.Ctx;
        var packs = setup.Packs;
        var alley = Fresh(0xA11E);
        for (uint seed = 0; seed < 150; seed++)
        {
            var pulls = packs.Give(alley, seed);
            content.AppendLine($"contraband {seed}: {string.Join(" ", pulls.Select(x => $"{x.Slot}:{x.Code}/{x.Rarity}/{x.Slab}/{x.SetCode}"))}");
            take.AppendLine($"contraband {seed}: {string.Join(" ", pulls.Select(x => $"{x.Kept}/{x.Replaced?.ToString() ?? "-"}/{x.Value}"))}");
            foreach (var x in pulls) stamps.AppendLine($"contraband {seed}: {Stamp(x.Copy)}");
        }
        var van = Fresh(0xFA11);
        for (int epoch = 0; epoch < 8; epoch++)
        {
            van.DuelsPlayed = epoch * AlleyEconomy.VanUsedEvery;
            van.AlleyDuels = epoch * AlleyEconomy.ShelfEvery;
            foreach (var l in MothsVan.Used(van, packs, ctx))
            {
                content.AppendLine($"van used {epoch} {l.Slot}: {l.Copy.Code}/{l.Copy.Rarity} g{l.Copy.Grade} s{l.Copy.Slab} seed {l.Copy.Seed} {l.Price}");
                stamps.AppendLine($"van used {epoch} {l.Slot}: {Stamp(l.Copy)}");
            }
            foreach (var l in MothsVan.Shelf(van, packs, ctx))
            {
                content.AppendLine($"van shelf {epoch} {l.Slot}: {l.Copy.Code}/{l.Copy.Rarity} g{l.Copy.Grade} seed {l.Copy.Seed} {l.Price} key {MothsVan.ShelfKey(l.Copy.Code, l.Copy.Rarity, l.Copy.Slab)}");
                stamps.AppendLine($"van shelf {epoch} {l.Slot}: {Stamp(l.Copy)}");
            }
        }
        var binders = Fresh(0xB1D);
        var owners = new List<string> { "orvel", BackAlley.PennyId };
        owners.AddRange(setup.Data.Headliners.Select(h => (h.IsBuild ? Ante.NoLimitsOwner : "hl:") + h.Id));
        foreach (var owner in owners)
        {
            var binder = Ante.Binder(binders, owner, ctx);
            for (int i = 0; i < binder.Cards.Count; i++)
            {
                var s = binder.Cards[i];
                content.AppendLine($"binder {owner} {i}: {s.Code}/{s.Rarity} g{s.Grade} s{s.Slab} seed {s.Seed} {s.Flag}");
                var won = Ante.ToCopy(s, binders);
                var f = typeof(Stake).GetField("Set");
                stamps.AppendLine($"binder {owner} {i}: stake set={f?.GetValue(s) ?? "?"} printed={typeof(Stake).GetField("Printed")?.GetValue(s) ?? "?"}; won {Stamp(won)}");
            }
        }

        // G. Save lines (spec 9.2): a stamped save round-trips; old lines load as text 0, "" and "".
        if (typeof(Collection).GetField("Printings") != null)
        {
            var save = prefix + ".save.txt";
            var rt = Fresh(0x5A7E);
            rt.Collection.Add(26202165, Rarity.Rare, 1, CopySource.Shop, 10);
            var orig = rt.Collection.All.First();
            rt.Collection.Stamp(orig, "MRD-069", "2002-06-26");
            rt.Collection.Add(78010363, Rarity.Rare, 1, CopySource.Shop, 10);
            var railCopy = new CardCopy { Code = 82301904, Rarity = Rarity.SecretRare, Grade = 9, Seed = 7, Id = rt.Collection.NextId++, Source = CopySource.Ante };
            rt.Collection.Stamp(railCopy, "IOC-000", "2004-03-01");
            rt.Rail.Add(new RailEntry { Copy = railCopy, Price = 500, ExpiresAt = 99 });
            var stake = new Stake { Code = 26202165, Rarity = Rarity.Rare, Grade = 8, Seed = 3, Set = "MRD-069", Printed = "2002-06" };
            rt.Binders["orvel"] = new StakeBinder { Owner = "orvel", Cards = { stake, new Stake { Code = 40640057, Rarity = Rarity.Common, Grade = 4, Seed = 5 } } };
            rt.AnteLive = new AnteLive { Table = "dumpster-row", Seat = 1, Held = 100, Theirs = stake, Yours = railCopy };
            rt.Save(save);
            var lines = File.ReadAllLines(save).Where(l => l.StartsWith("copy ") || l.StartsWith("rail ") || l.StartsWith("stake ") || l.StartsWith("ante")).ToList();
            foreach (var l in lines) Console.WriteLine("  save: " + l);
            var back = Profile.Load(save)!;
            string Copies(Profile x) => string.Join(" | ", x.Collection.All.OrderBy(c => c.Id).Select(c => $"{c.Code}:{c.Text}:{c.Set}:{c.Printed}"))
                + " || " + string.Join(" | ", x.Rail.Select(r => $"{r.Copy.Code}:{r.Copy.Text}:{r.Copy.Set}:{r.Copy.Printed}"))
                + " || " + string.Join(" | ", x.Binders.Values.SelectMany(b => b.Cards).Select(c => $"{c.Code}:{c.Set}:{c.Printed}"))
                + " || " + (x.AnteLive == null ? "-" : $"{x.AnteLive.Theirs?.Set}:{x.AnteLive.Theirs?.Printed} {x.AnteLive.Yours?.Text}:{x.AnteLive.Yours?.Set}:{x.AnteLive.Yours?.Printed}");
            Console.WriteLine($"  round trip {(Copies(rt) == Copies(back) ? "same" : "DIFFERENT")}: {Copies(back)}");
            // An old build's lines (no trailing fields) load as current text, printing unknown.
            var old = File.ReadAllLines(save).Select(l =>
            {
                var w = l.Split(' ');
                if (l.StartsWith("copy ") && w.Length > 12) return string.Join(" ", w.Take(12));
                if (l.StartsWith("rail ") && w.Length > 14) return string.Join(" ", w.Take(14));
                if (l.StartsWith("anteyours ") && w.Length > 12) return string.Join(" ", w.Take(12));
                if (l.StartsWith("stake ") && w.Length > 9) return string.Join(" ", w.Take(9));
                if (l.StartsWith("antetheirs ") && w.Length > 7) return string.Join(" ", w.Take(7));
                return l;
            }).ToList();
            File.WriteAllLines(save, old);
            var oldBack = Profile.Load(save)!;
            Console.WriteLine($"  old lines: {Copies(oldBack)}");
            // The per-text shortfall is a note, not a refusal (coordinator's decision on open question 2).
            var sh = Fresh(0x5A7F);
            sh.Collection.Add(26202165, Rarity.Rare, 2, CopySource.Shop, 10);
            sh.Collection.Add(26202165, Rarity.Rare, 1, CopySource.Shop, 10, "MRD-069", "2002-06-26");
            var pre = new Deck { Main = { 511002631, 511002631, 511002631 } };
            var shortfall = CollectionRules.TextShortfalls(pre, sh.Collection, texts, cards);
            var assigned = sh.Collection.Assign(pre, cards);
            Console.WriteLine($"  shortfall: refused {CollectionRules.Validate(pre, sh.Collection, cards).Count}; notes: {string.Join(" / ", shortfall.Select(x => x.Message))}; "
                              + $"assigned texts {string.Join(",", assigned.Select(c => c?.Text))}");
            // Review fix 1: a better copy of a text you own swaps with the worse one (A original Raw Common, B and C current slabs, D original Ultra).
            var kl = Fresh(0x5A80);
            kl.Collection.Add(26202165, Rarity.Common, 1, CopySource.Shop, 10, "MRD-069", "2002-06-26");
            kl.Collection.Add(26202165, Rarity.Rare, 2, CopySource.Shop, 10, "TOCH-EN026", "2020-11-05");
            foreach (var c in kl.Collection.CopiesOf(26202165).Where(c => c.Text == 0).ToList()) { c.Slab = c.Grade; kl.Collection.Resort(26202165); }
            var d = kl.Collection.NewCopy(26202165, Rarity.UltraRare, CopySource.Shop);
            kl.Collection.Stamp(d, "TU06-EN001", "2011-08-04");
            var took = kl.Collection.Take(d);
            Console.WriteLine($"  keep-lone swap: kept {took.Kept}, replaced {took.Replaced}; now {string.Join(",", kl.Collection.CopiesOf(26202165).Select(c => $"{c.Rarity}/{c.Text}/{(c.IsSlab ? "slab" : "raw")}"))}");
            // Review fix 3: with no timeline loaded, nothing is stamped.
            var noTexts = new Collection();
            var nt = new CardCopy { Code = 26202165 };
            noTexts.Stamp(nt, "MRD-069", "2002-06-26");
            Console.WriteLine($"  no texts: set '{nt.Set}' printed '{nt.Printed}' text {nt.Text}");
            // Review fix 6: a GOAT slot takes any original.
            var goatDeck = new Deck { Main = { 504700178, 504700178 } };
            Console.WriteLine($"  goat slots: assigned texts {string.Join(",", sh.Collection.Assign(goatDeck, cards).Select(c => c?.Text))}; notes: {string.Join(" / ", CollectionRules.TextShortfalls(goatDeck, sh.Collection, texts, cards).Select(x => x.Message))}");
            File.Delete(save);
            if (File.Exists(save + ".bak")) File.Delete(save + ".bak");
        }

        File.WriteAllText(prefix + ".content.txt", content.ToString());
        File.WriteAllText(prefix + ".take.txt", take.ToString());
        File.WriteAllText(prefix + ".stamps.txt", stamps.ToString());
        Console.WriteLine($"Wrote {prefix}.content.txt ({content.Length / 1024} KB), .take.txt, .stamps.txt");
        return 0;
    }
}
