using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace Ygo.Harness;

/// <summary>
/// What the economy harness needs to build a collection the way the app does (card versions phase 2): the text timeline
/// and the printings on <see cref="Collection"/>, so packs stamp their copies and the 4th-copy rule can tell texts apart.
/// A save loaded from text (<see cref="AlleyTest.FromText"/>) has neither until <see cref="Apply"/> gives them back.
/// Off unless a check turns it on (<see cref="Use"/>), so the other checks run as they always did.
/// </summary>
public static class VersionSetup
{
    public static bool Enabled;
    public static CardTexts Texts = CardTexts.Empty;

    public static CardTexts Load(ICardDataProvider cards, string root)
    {
        var file = Path.Combine(root, "campaign", CardTexts.File);
        return File.Exists(file) ? CardTexts.Load(file, cards) : CardTexts.Empty;
    }

    public static void Use(CardTexts texts) { Texts = texts; Enabled = true; }

    public static void Apply(Profile p, PrintingIndex printings)
    {
        if (!Enabled) return;
        p.Collection.Texts = Texts;
        p.Collection.Printings = printings;
    }
}

/// <summary>
/// `--copies-test`, card versions phase 2 (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4, 9.2, 12): a copy records its printing
/// and text; the save round-trips them and reads an older line; stamps come from seeds, not from a shared Random; a 4th
/// copy keeps the lone version; Assign and Count work per text.
/// </summary>
public static partial class CopiesTest
{
    private const uint SanganBase = 26202165, SanganPre = 511002631, SanganGoat = 504700178, CedPre = 511000819;

    public static int Versions(ICardDataProvider cards, string root)
    {
        int failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        Console.WriteLine("\nCard versions on copies (phase 2):");
        var printings = PrintingIndex.Load(Path.Combine(root, "unity", "Assets", "StreamingAssets", "printings.bin"));
        var texts = VersionSetup.Load(cards, root);
        Check($"campaign/texts.txt is loaded ({texts.Rows.Count()} rows)", texts.Rows.Any());
        Rarity Commonest(uint c) => printings.Commonest(c, cards.Get(c)?.Alias ?? 0);
        Profile Fresh(uint salt)
        {
            var p = new Profile { Dp = 100_000_000 };
            p.Collection.Salt = salt;
            p.Collection.CommonestOf = Commonest;
            p.Collection.Texts = texts;
            p.Collection.Printings = printings;
            return p;
        }
        string Tag(CardCopy? c) => c == null ? "-" : $"{c.Code}:{c.Rarity}:{c.Text}:{c.Set}:{c.Printed}";
        var darkMagician = cards.All.First(c => c.Name == "Dark Magician").Code;

        // ---- Stamp ---------------------------------------------------------------------------------------------
        var s = Fresh(1).Collection;
        var a = s.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        s.Stamp(a, "MRD-069", "2002-06-26");
        var b = s.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        s.Stamp(b, "MRD-EN069", "2023-04-20");
        var d = s.NewCopy(darkMagician, Rarity.Common, CopySource.Pack);
        s.Stamp(d, "LOB-005", "2002-03-08");
        var u1 = s.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        s.Stamp(u1, "MRD-069", "");
        var u2 = s.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        s.Stamp(u2, "", "2002-06");
        Check($"Stamp: a 2002 printing of Sangan carries the original text ({a.Set} {a.Printed} text {a.Text}), its 2023 reprint the current one "
              + $"({b.Set} {b.Printed} text {b.Text}), a card with no timeline row always current ({d.Printed} text {d.Text}); a date is cut to its month; no set or no date: unstamped",
            a.Text == SanganPre && a.Set == "MRD-069" && a.Printed == "2002-06" && b.Text == 0 && b.Printed == "2023-04" && d.Text == 0 && d.Printed == "2002-03"
            && u1.Set == "" && u1.Printed == "" && u1.Text == 0 && u2.Set == "" && u2.Printed == "" && u2.Text == 0);

        // ---- Save lines ----------------------------------------------------------------------------------------
        var tmp = Path.Combine(Path.GetTempPath(), $"ygo-copies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        var save = Path.Combine(tmp, "profile.txt");
        var rt = Fresh(0x5A7E);
        rt.Collection.Add(SanganBase, Rarity.Rare, 1, CopySource.Shop, 10, "MRD-069", "2002-06-26");
        rt.Collection.Add(SanganBase, Rarity.Rare, 1, CopySource.Shop, 10, "MRD-EN069", "2023-04-20");
        rt.Collection.Add(darkMagician, Rarity.Common, 1, CopySource.Shop, 10);
        rt.Collection.Add(78010363, Rarity.Rare, 1, CopySource.Shop, 10, "LOB-023", "2002-03-08");
        var railCopy = new CardCopy { Code = 82301904, Rarity = Rarity.SecretRare, Grade = 9, Seed = 7, Id = rt.Collection.NextId++, Source = CopySource.Ante };
        rt.Collection.Stamp(railCopy, "IOC-000", "2004-03-01");
        rt.Rail.Add(new RailEntry { Copy = railCopy, Price = 500, ExpiresAt = 99 });
        var stakeSangan = new Stake { Code = SanganBase, Rarity = Rarity.Rare, Grade = 8, Seed = 3, Set = "MRD-069", Printed = "2002-06" };
        rt.Binders["orvel"] = new StakeBinder { Owner = "orvel", Cards = { stakeSangan, new Stake { Code = 40640057, Rarity = Rarity.Common, Grade = 4, Seed = 5 } } };
        var yours = new CardCopy { Code = SanganBase, Rarity = Rarity.Rare, Grade = 6, Seed = 9, Id = rt.Collection.NextId++, Source = CopySource.Ante };
        rt.Collection.Stamp(yours, "MRD-069", "2002-06-26");
        rt.AnteLive = new AnteLive { Table = "dumpster-row", Seat = 1, Held = 100, Theirs = stakeSangan, Yours = yours };
        rt.Save(save);
        var lines = File.ReadAllLines(save);
        string Fields(Profile x) => string.Join(" | ", x.Collection.All.OrderBy(c => c.Id).Select(Tag))
            + " || " + string.Join(" | ", x.Rail.Select(r => Tag(r.Copy)))
            + " || " + string.Join(" | ", x.Binders.Values.SelectMany(bi => bi.Cards).Select(c => $"{c.Code}:{c.Set}:{c.Printed}"))
            + " || " + (x.AnteLive == null ? "-" : $"{x.AnteLive.Theirs?.Set}:{x.AnteLive.Theirs?.Printed} {Tag(x.AnteLive.Yours)}");
        Profile LoadLines(IEnumerable<string> ls)
        {
            File.WriteAllLines(save, ls);
            return Profile.Load(save)!;
        }
        var back = LoadLines(lines);
        if (Fields(rt) != Fields(back)) { Console.WriteLine("    saved:  " + Fields(rt)); Console.WriteLine("    loaded: " + Fields(back)); }
        Check("a save with the new fields round-trips: copies, a rail copy, binder stakes and a live ante's stake and copy keep their set, month and text",
            Fields(rt) == Fields(back) && back.Collection.All.Count(c => c.Text == SanganPre && c.Set == "MRD-069" && c.Printed == "2002-06") == 1
            && back.Collection.All.Count(c => c.Set == "MRD-EN069" && c.Printed == "2023-04" && c.Text == 0) == 1);
        var copyLines = lines.Where(l => l.StartsWith("copy ")).Select(l => l.Split(' ')).ToList();
        Check($"a stamped copy line has 3 trailing fields (text, set, month); an unstamped one writes the 11 it always did ({copyLines.Count(w => w.Length == 15)} of 15 words, {copyLines.Count(w => w.Length == 12)} of 12)",
            copyLines.Count(w => w.Length == 15) == 3 && copyLines.Count(w => w.Length == 12) == 1
            && copyLines.Any(w => w.Length == 15 && w[12] == SanganPre.ToString() && w[13] == "MRD-069" && w[14] == "2002-06"));
        Check("the stake and ante lines carry set and month the same way",
            lines.Any(l => l.StartsWith("stake orvel 0 ") && l.EndsWith(" MRD-069 2002-06")) && lines.Any(l => l.StartsWith("stake orvel 1 ") && l.Split(' ').Length == 9)
            && lines.Any(l => l.StartsWith("antetheirs ") && l.EndsWith(" MRD-069 2002-06")) && lines.Any(l => l.StartsWith("anteyours ") && l.EndsWith(" " + SanganPre + " MRD-069 2002-06"))
            && lines.Any(l => l.StartsWith("rail ") && l.EndsWith(" 511000819 IOC-000 2004-03")));

        // An older save's lines (no trailing fields): each kind loads as current text, printing unknown.
        string Trim(string l, string key, int words)
        {
            var w = l.Split(' ');
            return l.StartsWith(key + " ") && w.Length > words ? string.Join(" ", w.Take(words)) : l;
        }
        bool Unknown(CardCopy? c) => c != null && c.Text == 0 && c.Set == "" && c.Printed == "";
        var old = LoadLines(lines.Select(l => Trim(l, "copy", 12)));
        Check($"an old copy line (11 fields) loads as Text 0, Set \"\", Printed \"\" ({old.Collection.Total} copies, all unknown); the rest of its fields are unchanged",
            old.Collection.Total == back.Collection.Total && old.Collection.All.All(Unknown)
            && old.Collection.All.OrderBy(c => c.Id).Select(c => $"{c.Code}:{c.Rarity}:{c.Grade}:{c.Seed}:{c.Id}:{c.Paid}").SequenceEqual(back.Collection.All.OrderBy(c => c.Id).Select(c => $"{c.Code}:{c.Rarity}:{c.Grade}:{c.Seed}:{c.Id}:{c.Paid}")));
        var oldRail = LoadLines(lines.Select(l => Trim(l, "rail", 14)));
        Check("a rail line without trailing fields loads unknown; with them, stamped (a 2004 Chaos Emperor Dragon is the original text)", oldRail.Rail.Count == 1 && Unknown(oldRail.Rail[0].Copy) && back.Rail.Count == 1 && back.Rail[0].Copy.Set == "IOC-000" && back.Rail[0].Copy.Text == CedPre);
        var oldYours = LoadLines(lines.Select(l => Trim(l, "anteyours", 12)));
        Check("an anteyours line without trailing fields loads unknown; with them, stamped (Sangan MRD-069 is the original text)",
            Unknown(oldYours.AnteLive?.Yours) && back.AnteLive?.Yours?.Text == SanganPre && back.AnteLive.Yours.Set == "MRD-069");
        var oldStake = LoadLines(lines.Select(l => Trim(l, "stake", 9)));
        Check("a stake line without trailing fields loads with Set \"\" and Printed \"\"; with them, its set and month",
            oldStake.Binders["orvel"].Cards.All(c => c.Set == "" && c.Printed == "") && oldStake.Binders["orvel"].Cards.Count == 2
            && back.Binders["orvel"].Cards[0].Set == "MRD-069" && back.Binders["orvel"].Cards[0].Printed == "2002-06");
        var oldTheirs = LoadLines(lines.Select(l => Trim(l, "antetheirs", 7)));
        Check("an antetheirs line without trailing fields loads with Set \"\" and Printed \"\"; its card and grade are unchanged",
            oldTheirs.AnteLive?.Theirs is { Set: "", Printed: "" } t && t.Code == SanganBase && t.Grade == 8 && back.AnteLive?.Theirs?.Set == "MRD-069");
        var allOld = LoadLines(lines.Select(l => Trim(Trim(Trim(Trim(Trim(l, "copy", 12), "rail", 14), "anteyours", 12), "stake", 9), "antetheirs", 7)));
        Check("every line kind cut back to its old shape loads in one save, and saving it again writes only 11-field copy lines",
            allOld.Collection.All.All(Unknown) && allOld.Collection.Total == back.Collection.Total && allOld.Rail.Count == 1
            && allOld.AnteLive?.Yours != null && allOld.AnteLive.Theirs != null);
        allOld.Save(save);
        Check("(re-saved) no copy line grew trailing fields", File.ReadAllLines(save).Where(l => l.StartsWith("copy ")).All(l => l.Split(' ').Length == 12));
        // Partly there, or dashes: text alone, a set without a month, "-" for none.
        var odd = LoadLines(lines.Select(l => l.StartsWith("copy ") && l.Split(' ').Length == 15 && l.Contains(" MRD-069 ")
            ? string.Join(" ", l.Split(' ').Take(13)) : l.StartsWith("stake orvel 0 ") ? string.Join(" ", l.Split(' ').Take(10)) : l.Replace(" MRD-EN069 2023-04", " - -")));
        Check("a line with only the text field, a stake with a set and no month, and \"-\" dashes all load without error",
            odd.Collection.All.Any(c => c.Text == SanganPre && c.Set == "" && c.Printed == "") && odd.Binders["orvel"].Cards[0].Set == "MRD-069" && odd.Binders["orvel"].Cards[0].Printed == ""
            && odd.Collection.All.Count(c => c.Code == SanganBase && c.Set == "" && c.Printed == "" && c.Text == 0) >= 1);
        try { Directory.Delete(tmp, true); } catch { }

        // ---- Stamps are the same for the same seed (spec 4.2; QA runs are deterministic) -------------------------------------
        var catalog = new PackCatalog(printings, cards);
        var sets = new[] { "MRD", "LOB", "PSV", "IOC", "DPYG", catalog.Sets[0].Info.Code }.Select(catalog.Find).OfType<PackSet>().ToList();
        int packsSame = 0, packsTried = 0, stamped = 0, originals = 0, randomSame = 0, contentSame = 0;
        foreach (var set in sets)
            for (int k = 0; k < 40; k++)
            {
                var p1 = Fresh(77); var p2 = Fresh(77); var plain = Fresh(77);
                plain.Collection.Printings = null;
                var r1 = new Random(k); var r2 = new Random(k); var r3 = new Random(k);
                var o1 = p1.OpenPack(set, r1, out _); var o2 = p2.OpenPack(set, r2, out _); var o3 = plain.OpenPack(set, r3, out _);
                packsTried++;
                if (o1.Select(x => Tag(x.added.Copy)).SequenceEqual(o2.Select(x => Tag(x.added.Copy)))) packsSame++;
                stamped += o1.Count(x => x.added.Copy?.Set.Length > 0);
                originals += o1.Count(x => x.added.Copy?.Text != 0);
                if (r1.Next() == r3.Next()) randomSame++;
                if (o1.Select(x => $"{x.pull.Code}/{x.pull.Rarity}").SequenceEqual(o3.Select(x => $"{x.pull.Code}/{x.pull.Rarity}"))) contentSame++;
            }
        Check($"packs: {packsTried} packs over {sets.Count} sets opened twice from the same seed stamp the same printings ({packsSame} same; {stamped} stamped copies, {originals} with an original text)",
            packsSame == packsTried && stamped > 0 && originals > 0);
        Check($"a stamped pack draws nothing from its Random and holds the same cards as an unstamped one ({randomSame} of {packsTried} left the Random in the same place, {contentSame} the same pulls)",
            randomSame == packsTried && contentSame == packsTried);

        var roster = Personality.LoadFolder(Path.Combine(root, "personalities")).ToList();
        var circuits = Circuits.Load(Path.Combine(root, "campaign", "circuits.txt"), roster);
        var library = Library.Load(Path.Combine(root, "campaign", Library.CupsFile), roster, circuits);
        var setup = AlleyTest.Load(cards, root, printings, roster, library);

        var timeline = texts.Rows.Where(r => r.Collectible).Select(r => r.Base).Distinct().Where(c => printings.Of(c, cards.Get(c)?.Alias ?? 0).Length > 0).OrderBy(c => c).ToList();
        int usedSame = 0, usedTried = 0, usedOriginals = 0, usedStamped = 0, usedKeep = 0;
        foreach (var era in new[] { false, true })
            for (int epoch = 0; epoch < 6; epoch++)
            {
                var p1 = Fresh(99); var p2 = Fresh(99);
                p1.EraLimited = p2.EraLimited = era;
                p1.DuelsPlayed = p2.DuelsPlayed = epoch * UsedMarket.Every;
                var l1 = UsedMarket.Listings(p1, timeline, era ? 2008 : 2026, printings, cards);
                var l2 = UsedMarket.Listings(p2, timeline, era ? 2008 : 2026, printings, cards);
                var l3 = UsedMarket.Listings(p1, timeline, era ? 2008 : 2026, printings, cards); // an update: no reroll
                usedTried++;
                if (l1.Count > 0 && l1.Select(x => Tag(x.Copy)).SequenceEqual(l2.Select(x => Tag(x.Copy)))) usedSame++;
                if (l1.Select(x => Tag(x.Copy)).SequenceEqual(l3.Select(x => Tag(x.Copy)))) usedKeep++;
                usedStamped += l1.Count(x => x.Copy.Set.Length > 0);
                usedOriginals += l1.Count(x => x.Copy.Text != 0);
            }
        Check($"used market: {usedTried} periods dealt twice from the same salt list the same printings; listed again they don't reroll ({usedSame} same, {usedKeep} unchanged; {usedStamped} stamped listings, {usedOriginals} with an original text)",
            usedSame == usedTried && usedKeep == usedTried && usedStamped > 0 && usedOriginals > 0);

        int vanSame = 0, vanTried = 0, vanStamped = 0, vanShelfSame = 0;
        for (int epoch = 0; epoch < 8; epoch++)
        {
            var p1 = Fresh(0xFA11); var p2 = Fresh(0xFA11);
            p1.DuelsPlayed = p2.DuelsPlayed = epoch * AlleyEconomy.VanUsedEvery;
            p1.AlleyDuels = p2.AlleyDuels = epoch * AlleyEconomy.ShelfEvery;
            var v1 = MothsVan.Used(p1, setup.Packs, setup.Ctx); var v2 = MothsVan.Used(p2, setup.Packs, setup.Ctx);
            var h1 = MothsVan.Shelf(p1, setup.Packs, setup.Ctx); var h2 = MothsVan.Shelf(p2, setup.Packs, setup.Ctx);
            vanTried++;
            if (v1.Count > 0 && v1.Select(x => Tag(x.Copy)).SequenceEqual(v2.Select(x => Tag(x.Copy)))) vanSame++;
            if (h1.Count > 0 && h1.Select(x => Tag(x.Copy)).SequenceEqual(h2.Select(x => Tag(x.Copy)))) vanShelfSame++;
            vanStamped += v1.Count(x => x.Copy.Set.Length > 0) + h1.Count(x => x.Copy.Set.Length > 0);
        }
        Check($"Moth's van: {vanTried} periods of used listings and of the shelf dealt twice from one salt list the same printings ({vanSame} and {vanShelfSame} same; {vanStamped} stamped)",
            vanSame == vanTried && vanShelfSame == vanTried && vanStamped > 0);

        int anteSame = 0, anteTried = 0, anteStamped = 0, anteCount = 0, pennyOk = 0;
        var owners = new List<string> { "orvel", BackAlley.PennyId };
        owners.AddRange(setup.Data.Headliners.Take(6).Select(h => (h.IsBuild ? Ante.NoLimitsOwner : "hl:") + h.Id));
        foreach (var owner in owners)
        {
            var p1 = Fresh(0xB1D); var p2 = Fresh(0xB1D);
            var b1 = Ante.Binder(p1, owner, setup.Ctx); var b2 = Ante.Binder(p2, owner, setup.Ctx);
            var again = Ante.Binder(p1, owner, setup.Ctx);
            anteTried++;
            string Of(StakeBinder bi) => string.Join(" | ", bi.Cards.Select(c => $"{c.Code}:{c.Rarity}:{c.Seed}:{c.Set}:{c.Printed}"));
            if (b1.Cards.Count > 0 && Of(b1) == Of(b2) && Of(b1) == Of(again)) anteSame++;
            anteStamped += b1.Cards.Count(c => c.Set.Length > 0);
            anteCount += b1.Cards.Count;
            if (owner == BackAlley.PennyId)
            {
                var k = b1.Cards.FirstOrDefault(c => c.Flag == Ante.PennyFlag);
                var earliest = k == null ? null : CopyPrints.Earliest(k.Code, k.Rarity, "", printings, cards);
                if (k != null && earliest is Printing e && k.Set == e.SetCode && k.Printed == printings.DateOf(e).Substring(0, 7)) pennyOk++;
            }
        }
        Check($"ante stakes: {anteTried} binders (Orvel's, Penny's, headliners') drawn twice from one salt hold the same printings, and again on a second read ({anteSame} same; {anteStamped} of {anteCount} stakes stamped); Penny's Kuriboh is its earliest Common printing ({pennyOk})",
            anteSame == anteTried && anteStamped == anteCount && anteCount > 0 && pennyOk == 1);
        var wonStake = new Stake { Code = SanganBase, Rarity = Rarity.Common, Grade = 7, Seed = 11, Set = "MRD-069", Printed = "2002-06" };
        var wonCurrent = new Stake { Code = SanganBase, Rarity = Rarity.Common, Grade = 7, Seed = 12, Set = "MRD-EN069", Printed = "2023-04" };
        var winner = Fresh(5);
        var won1 = Ante.ToCopy(wonStake, winner); var won2 = Ante.ToCopy(wonCurrent, winner); var won3 = Ante.ToCopy(new Stake { Code = SanganBase, Rarity = Rarity.Common, Grade = 7, Seed = 13 }, winner);
        Check($"a won stake's text is derived when it is won, from the printing's month (2002 Sangan: text {won1.Text}; 2023: {won2.Text}; no printing: {won3.Text}); Paid stays 0",
            won1.Text == SanganPre && won1.Set == "MRD-069" && won2.Text == 0 && won2.Printed == "2023-04" && won3.Text == 0 && won3.Set == "" && won1.Paid == 0 && won1.Source == CopySource.Ante);

        // ---- The lone-text keep (4.3) -----------------------------------------------------------------------------
        Collection Owning(params (Rarity rarity, uint text, int slab)[] copies)
        {
            var col = Fresh(0xC0).Collection;
            foreach (var (rarity, text, slab) in copies)
            {
                var c = col.NewCopy(SanganBase, rarity, CopySource.Pack);
                c.Grade = 8; c.Paid = 2 * Collection.SellValue(rarity);
                if (slab > 0) { c.Slab = c.Floor = slab; }
                col.Stamp(c, text == 0 ? "MRD-EN069" : "MRD-069", text == 0 ? "2023-04" : "2002-06");
                col.Insert(c);
            }
            return col;
        }
        CardCopy NewOne(Collection col, Rarity rarity, uint text)
        {
            var c = col.NewCopy(SanganBase, rarity, CopySource.Pack);
            c.Grade = 8; c.Paid = 2 * Collection.SellValue(rarity);
            col.Stamp(c, text == 0 ? "MRD-EN069" : "MRD-069", text == 0 ? "2023-04" : "2002-06");
            return c;
        }
        var saved = Collection.KeepLoneText;
        try
        {
            foreach (var keep in new[] { true, false })
            {
                Collection.KeepLoneText = keep;
                string mode = keep ? "KeepLoneText" : "KeepLoneText off";
                // A: three current copies worth more, a lone original arrives (the 2002 Sangan worth less than your three 2023 ones).
                var col = Owning((Rarity.UltraRare, 0, 0), (Rarity.UltraRare, 0, 0), (Rarity.UltraRare, 0, 0));
                var incoming = NewOne(col, Rarity.Common, SanganPre);
                bool wouldKeep = col.WouldKeep(incoming);
                var got = col.Take(incoming);
                Check(keep
                    ? $"{mode}: a 4th copy that is a new original is kept, and a current one goes (kept {got.Kept}, {col.Count(SanganBase, SanganPre)} original, {col.Count(SanganBase, 0)} current, {got.Dp} DP)"
                    : $"{mode}: the 4th copy, a cheaper original, is sold on the spot as before (kept {got.Kept}, {col.Count(SanganBase, SanganPre)} original, {got.Dp} DP)",
                    keep ? got.Kept && wouldKeep && col.Count(SanganBase, SanganPre) == 1 && col.Count(SanganBase, 0) == 2 && col.Owned(SanganBase) == 3 && got.Dp > 0
                         : !got.Kept && !wouldKeep && col.Count(SanganBase, SanganPre) == 0 && col.Count(SanganBase, 0) == 3 && got.Dp == col.SellPrice(incoming));
                // B: a lone original (the cheapest) and two current copies; a current one arrives. Its text is a duplicate: it goes, the lone original stays.
                col = Owning((Rarity.Common, SanganPre, 0), (Rarity.UltraRare, 0, 0), (Rarity.UltraRare, 0, 0));
                incoming = NewOne(col, Rarity.UltraRare, 0);
                wouldKeep = col.WouldKeep(incoming);
                got = col.Take(incoming);
                Check(keep
                    ? $"{mode}: a lone original is never the copy that goes: a 4th current copy displaces a duplicate text ({col.Count(SanganBase, SanganPre)} original kept, {col.Count(SanganBase, 0)} current, owned {col.Owned(SanganBase)})"
                    : $"{mode}: today's rule: the cheapest copy goes even if it's the only original ({col.Count(SanganBase, SanganPre)} original left, {col.Count(SanganBase, 0)} current)",
                    keep ? col.Count(SanganBase, SanganPre) == 1 && col.Count(SanganBase, 0) == 2 && col.Owned(SanganBase) == 3
                         : col.Count(SanganBase, SanganPre) == 0 && col.Count(SanganBase, 0) == 3 && got.Kept && wouldKeep);
                // C: three originals, a current copy you don't own a text of arrives: it stays, the cheapest original goes.
                col = Owning((Rarity.Common, SanganPre, 0), (Rarity.SuperRare, SanganPre, 0), (Rarity.UltraRare, SanganPre, 0));
                incoming = NewOne(col, Rarity.Common, 0);
                wouldKeep = col.WouldKeep(incoming);
                got = col.Take(incoming);
                Check(keep
                    ? $"{mode}: a new copy of a text you don't own yet stays; the cheapest of the duplicates goes ({col.Count(SanganBase, 0)} current, {col.Count(SanganBase, SanganPre)} original)"
                    : $"{mode}: the new Common current copy is the cheapest, so it is sold ({col.Count(SanganBase, 0)} current, {col.Count(SanganBase, SanganPre)} original)",
                    keep ? got.Kept && wouldKeep && col.Count(SanganBase, 0) == 1 && col.Count(SanganBase, SanganPre) == 2 && !col.All.Any(c => c.Text == SanganPre && c.Rarity == Rarity.Common)
                         : !got.Kept && !wouldKeep && col.Count(SanganBase, 0) == 0 && col.Count(SanganBase, SanganPre) == 3);
                // D: no copy qualifies (three slabs: nothing Raw to swap, today's rule): the new one sells either way.
                col = Owning((Rarity.UltraRare, 0, 9), (Rarity.UltraRare, 0, 9), (Rarity.UltraRare, 0, 9));
                incoming = NewOne(col, Rarity.UltraRare, SanganPre);
                wouldKeep = col.WouldKeep(incoming);
                got = col.Take(incoming);
                Check($"{mode}: with three slabs and no Raw copy to swap, the 4th copy sells as it always did (kept {got.Kept}, owned {col.Owned(SanganBase)})", !got.Kept && !wouldKeep && col.Owned(SanganBase) == 3);
                // F: same-text copies swap (a lone original at the bottom, two current slabs, a better original arrives): the better one stays, the worse goes.
                col = Owning((Rarity.Common, SanganPre, 0), (Rarity.UltraRare, 0, 9), (Rarity.UltraRare, 0, 9));
                var worse = col.CopiesOf(SanganBase).First(c => c.Text == SanganPre);
                incoming = NewOne(col, Rarity.UltraRare, SanganPre);
                wouldKeep = col.WouldKeep(incoming);
                got = col.Take(incoming);
                Check($"{mode}: a better original replaces the Common one beside two current slabs (kept {got.Kept}, the Common original {(col.CopiesOf(SanganBase).Contains(worse) ? "stays" : "goes")}, {col.Count(SanganBase, SanganPre)} original, {col.Count(SanganBase, 0)} current)",
                    got.Kept && wouldKeep && !col.CopiesOf(SanganBase).Contains(worse) && col.Count(SanganBase, SanganPre) == 1 && col.Count(SanganBase, 0) == 2 && col.CopiesOf(SanganBase).Any(c => c.Rarity == Rarity.UltraRare && c.Text == SanganPre));
                // G: and the reverse: a worse original does not displace the better one; it is the copy that goes (a text held twice).
                col = Owning((Rarity.UltraRare, SanganPre, 0), (Rarity.UltraRare, 0, 9), (Rarity.UltraRare, 0, 9));
                incoming = NewOne(col, Rarity.Common, SanganPre);
                wouldKeep = col.WouldKeep(incoming);
                got = col.Take(incoming);
                Check($"{mode}: a worse original beside two current slabs is the copy that goes (kept {got.Kept}, {col.Count(SanganBase, SanganPre)} original, {col.Count(SanganBase, 0)} current)",
                    !got.Kept && !wouldKeep && col.Count(SanganBase, SanganPre) == 1 && col.Count(SanganBase, 0) == 2 && col.CopiesOf(SanganBase).Any(c => c.Rarity == Rarity.UltraRare && c.Text == SanganPre));
            }
            // E: a card with no timeline row (every copy text 0): the rule changes nothing, the same copy goes with it on or off.
            string Play(bool keep)
            {
                Collection.KeepLoneText = keep;
                var col = Fresh(0xD0).Collection;
                var log = new List<string>();
                for (int i = 0; i < 12; i++)
                {
                    var c = col.NewCopy(darkMagician, i % 3 == 0 ? Rarity.UltraRare : Rarity.Common, CopySource.Pack);
                    c.Paid = 2 * Collection.SellValue(c.Rarity);
                    col.Stamp(c, "LOB-005", "2002-03-08");
                    var got = col.Take(c);
                    log.Add($"{got.Kept}/{got.Dp}/{string.Join(",", col.CopiesOf(darkMagician).Select(x => x.Id))}");
                }
                return string.Join(";", log);
            }
            Check("a card with no timeline row keeps and sells exactly the same copies with KeepLoneText on or off (12 pulls)", Play(true) == Play(false));
        }
        finally { Collection.KeepLoneText = saved; }

        // ---- Assign per text, Count(base, text) ------------------------------------------------------------------------
        Collection Mixed(params (Rarity rarity, uint text)[] copies) => Owning(copies.Select(c => (c.rarity, c.text, 0)).ToArray());
        string Texts(IEnumerable<CardCopy?> assigned) => string.Join(",", assigned.Select(c => c == null ? "-" : c.Text == 0 ? "cur" : c.Text == SanganPre ? "orig" : c.Text.ToString()));
        var oneOrig = Mixed((Rarity.Common, SanganPre), (Rarity.UltraRare, 0), (Rarity.SuperRare, 0));
        Check($"Count(base, text): {oneOrig.Count(SanganBase, SanganPre)} original, {oneOrig.Count(SanganBase, 0)} current, {oneOrig.Count(SanganBase)} in all; a copy at the desk is not counted; a dup row counts as its canonical",
            oneOrig.Count(SanganBase, SanganPre) == 1 && oneOrig.Count(SanganBase, 0) == 2 && oneOrig.Count(SanganBase) == 3 && oneOrig.Count(SanganBase, 511003012) == 0
            && DeskAndDup(cards, texts, printings));
        var d3Pre = new Deck { Main = { SanganPre, SanganPre, SanganPre } };
        var d1 = oneOrig.Assign(d3Pre, cards);
        var dMixed = new Deck { Main = { SanganBase, SanganBase, SanganPre } };
        var dMixed2 = oneOrig.Assign(dMixed, cards);
        var dCur = new Deck { Main = { SanganBase, SanganBase, SanganBase } };
        var dCur2 = oneOrig.Assign(dCur, cards);
        Check($"Assign per text: 3 original slots with 1 original copy get [{Texts(d1)}]; [current, current, original] gets [{Texts(dMixed2)}] (the original copy goes to the original slot); "
              + $"3 current slots get [{Texts(dCur2)}] (current copies first, the original last)",
            Texts(d1) == "orig,cur,cur" && Texts(dMixed2) == "cur,cur,orig" && Texts(dCur2) == "cur,cur,orig"
            && d1.Select(c => c!.Id).Distinct().Count() == 3 && dMixed2.Select(c => c!.Id).Distinct().Count() == 3);
        var twoOrig = Mixed((Rarity.Common, SanganPre), (Rarity.UltraRare, SanganPre), (Rarity.SuperRare, 0));
        var dTwo = twoOrig.Assign(new Deck { Main = { SanganPre, SanganBase, SanganBase } }, cards);
        Check($"2 originals and 1 current: [original, current, current] gets [{Texts(dTwo)}], the best original (Ultra) in the original slot ({dTwo[0]?.Rarity}); the current slot takes the current copy before the spare original",
            Texts(dTwo) == "orig,cur,orig" && dTwo[0]!.Rarity == Rarity.UltraRare && dTwo[1]!.Text == 0);
        var dGoat = oneOrig.Assign(new Deck { Main = { SanganGoat, SanganBase, SanganBase } }, cards);
        Check($"a GOAT entry of a card with a timeline asks for the earliest original ([{Texts(dGoat)}])", Texts(dGoat) == "orig,cur,cur");
        var noOrig = Mixed((Rarity.UltraRare, 0), (Rarity.SuperRare, 0), (Rarity.Common, 0));
        Check($"3 original slots with no original copy: every slot still gets a copy, a current one ([{Texts(noOrig.Assign(d3Pre, cards))}]); with 2 copies the third is null ([{Texts(Mixed((Rarity.Rare, 0), (Rarity.Rare, SanganPre)).Assign(d3Pre, cards))}])",
            Texts(noOrig.Assign(d3Pre, cards)) == "cur,cur,cur" && Texts(Mixed((Rarity.Rare, 0), (Rarity.Rare, SanganPre)).Assign(d3Pre, cards)) == "orig,cur,-");

        // GOAT slots: Pre-Errata slots take their own text first, then a GOAT slot takes any collectible original left (TextShortfalls fills the same way).
        var goatDeck = new Deck { Main = { SanganGoat, SanganPre } };
        var goatOne = oneOrig.Assign(goatDeck, cards);
        var goatTwo = twoOrig.Assign(goatDeck, cards);
        var goatNotes = CollectionRules.TextShortfalls(goatDeck, oneOrig, texts, cards);
        Check($"[GOAT entry, Pre-Errata entry] with 1 original copy: the Pre-Errata slot gets it ([{Texts(goatOne)}]), the GOAT slot a current one, and one note says so (\"{goatNotes.FirstOrDefault()?.Message}\"); "
              + $"with 2 originals both get one ([{Texts(goatTwo)}], no note: {CollectionRules.TextShortfalls(goatDeck, twoOrig, texts, cards).Count})",
            Texts(goatOne) == "cur,orig" && Texts(goatTwo) == "orig,orig" && goatNotes.Count == 1 && goatNotes[0].Message.Contains("2 slots") && goatNotes[0].Message.Contains("1 original copy")
            && CollectionRules.TextShortfalls(goatDeck, twoOrig, texts, cards).Count == 0);

        // A stored text whose row is gone, or is a never/cpu row, counts as current text.
        var toonWorld = cards.All.First(c => c.Name == "Toon World" && c.Alias == 0).Code;
        var oddOnes = new Profile().Collection;
        oddOnes.Texts = texts;
        oddOnes.Insert(new CardCopy { Code = toonWorld, Rarity = Rarity.Common, Grade = 8, Id = 1, Text = 511004007 });  // a `never` row
        oddOnes.Insert(new CardCopy { Code = toonWorld, Rarity = Rarity.Common, Grade = 8, Id = 2, Text = 999999999 });  // no such row
        oddOnes.Insert(new CardCopy { Code = toonWorld, Rarity = Rarity.Common, Grade = 8, Id = 3 });
        var oddAssign = oddOnes.Assign(new Deck { Main = { toonWorld, toonWorld, toonWorld } }, cards);
        Check($"a copy whose stored text is a `never` row or a row that no longer exists counts as current: Count(Toon World, 0) = {oddOnes.Count(toonWorld, 0)} of 3, Count(never row) = {oddOnes.Count(toonWorld, 511004007)}, "
              + $"Assign fills all {oddAssign.Count(c => c != null)} current slots",
            oddOnes.Count(toonWorld, 0) == 3 && oddOnes.Count(toonWorld, 511004007) == 3 && oddAssign.All(c => c != null));

        // A reprint's dates (campaign/printing-dates.txt): the IOC and DCR EN rows are the 2023 reprint; IOC-000 is a 2004 original.
        var ced = printings.Of(82301904, 0);
        var iocReprint = ced.Where(x => x.SetCode == "IOC-EN000").Select(x => (Printing?)x).FirstOrDefault();
        var iocFirst = ced.Where(x => x.SetCode == "IOC-000").Select(x => (Printing?)x).FirstOrDefault();
        var ccd = Fresh(6).Collection;
        var cedNew = ccd.NewCopy(82301904, Rarity.UltraRare, CopySource.Shop);
        var cedOld = ccd.NewCopy(82301904, Rarity.UltraRare, CopySource.Shop);
        if (iocReprint is Printing rr1) ccd.Stamp(cedNew, rr1, printings);
        if (iocFirst is Printing rr2) ccd.Stamp(cedOld, rr2, printings);
        var dmoc = printings.Of(Banlist.BaseCode(511001039, cards), 0).Where(x => x.SetCode == "IOC-EN065").Select(x => (Printing?)x).FirstOrDefault();
        var dmocCopy = ccd.NewCopy(Banlist.BaseCode(511001039, cards), Rarity.UltraRare, CopySource.Shop);
        if (dmoc is Printing rr3) ccd.Stamp(dmocCopy, rr3, printings);
        Check($"Chaos Emperor Dragon IOC-EN000 dates as the reprint ({cedNew.Printed}, text {cedNew.Text}: current), IOC-000 as 2004 ({cedOld.Printed}, text {cedOld.Text}: original); Dark Magician of Chaos IOC-EN065 stamps current ({dmocCopy.Printed}, text {dmocCopy.Text})",
            iocReprint != null && iocFirst != null && cedNew.Printed == "2023-04" && cedNew.Text == 0 && cedOld.Printed.StartsWith("2004") && cedOld.Text == CedPre
            && dmoc != null && dmocCopy.Text == 0 && dmocCopy.Printed == "2023-04");

        // Without a text timeline a copy is left unstamped (so none is frozen as current).
        var bare = new Collection();
        var bareCopy = bare.NewCopy(SanganBase, Rarity.Common, CopySource.Pack);
        bare.Stamp(bareCopy, "MRD-069", "2002-06-26");
        Check($"a collection with no Texts leaves a copy unstamped ({bareCopy.Set.Length} set, text {bareCopy.Text}); the harness sets Texts wherever it expects originals",
            bareCopy.Set == "" && bareCopy.Printed == "" && bareCopy.Text == 0);

        Console.WriteLine($"\n{(failures == 0 ? "All card version copy checks passed." : $"{failures} card version copy checks failed.")}");
        return failures;
    }

    /// <summary>Copies at the desk aren't counted by text; a dup text counts as its canonical (Crush Card Virus 511000823 is 511000822).</summary>
    private static bool DeskAndDup(ICardDataProvider cards, CardTexts texts, PrintingIndex printings)
    {
        const uint CcvA = 511000822, CcvB = 511000823;
        var baseCode = Banlist.BaseCode(CcvA, cards);
        var col = new Profile().Collection;
        col.Texts = texts;
        var a = new CardCopy { Code = baseCode, Rarity = Rarity.Common, Grade = 8, Id = 1, Text = CcvA };
        var b = new CardCopy { Code = baseCode, Rarity = Rarity.Common, Grade = 8, Id = 2, Text = CcvA, ReadyAt = 5 };
        col.Insert(a); col.Insert(b);
        return col.Count(baseCode, CcvA) == 1 && col.Count(baseCode, CcvB) == 1 && col.Count(baseCode, 0) == 0 && col.Owned(baseCode) == 2;
    }
}
