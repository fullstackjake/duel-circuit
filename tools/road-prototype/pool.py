import sys; sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from data import *

R = ratings(); F = deck_files(); P = profiles(); U = usage(); L = lflists(); db = cards()
FUS, RIT, SYN, XYZ, PEN, LNK = 0x40, 0x80, 0x2000, 0x800000, 0x1000000, 0x4000000
print(f"rated {len(R)}; significant splits {sum(r['sig'] for r in R.values())}")
illegal_total = 0
for n, r in sorted(R.items(), key=lambda kv: -kv[1]["elo"]):
    path = F.get(n, "?")
    meta, main, extra = read_ydk(path)
    era = meta.get("era", "-"); bl = meta.get("banlist", "-")
    ex = Counter()
    for c in extra:
        t = db.get(c, ("", 0, 0))[2]
        for k, b in (("F", FUS), ("S", SYN), ("X", XYZ), ("L", LNK)):
            if t & b: ex[k] += 1
    rit = sum(1 for c in main if db.get(c, ("", 0, 0))[2] & RIT and db.get(c, ("", 0, 0))[2] & 1)
    pen = sum(1 for c in main if db.get(c, ("", 0, 0))[2] & PEN)
    # legality under its own #banlist
    over = []
    if bl in L:
        lim = {canon(k): v for k, v in L[bl].items()}
        cnt = Counter(canon(c) for c in main + extra)
        for cid, k in cnt.items():
            if cid in lim and k > lim[cid]:
                over.append(f"{db.get(cid,('?',))[0]} {k}/{lim[cid]}")
    illegal_total += bool(over)
    ch = "self" if n in P else "-"
    where = os.path.relpath(os.path.dirname(path), ROOT)
    print(f"{n:27} {r['elo']:5} t{r['turns']:5.1f} f{r['first']:3}/s{r['second']:3}{'*' if r['sig'] else ' '} era {era:5} {where:16} list {bl:20} ch:{ch:5} ex:{dict(ex)} rit:{rit} pen:{pen} use:{len(U.get(n,[]))} {'OVER: '+'; '.join(over) if over else ''}")
print("decks over their own list:", illegal_total)
