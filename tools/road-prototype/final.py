"""Final tables: every rank with rotating posters (10,000 runs), then each poster alone at every rank."""
import sys, random, statistics as st
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import road, posters
from road import D
NAMES = [n for n in posters.POSTERS if n != "none"]
def poster_for(salt, run, names=None):   # a fixed seeded lap: every poster once per lap, same order every lap
    names = names or NAMES; order = names[:]; random.Random(f"{salt}|posters").shuffle(order)
    return order[(run - 1) % len(names)]

SHIFT = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0   # human-pilot sensitivity: per-duel p change, e.g. -0.05
DRAW = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0    # per-duel draw chance (a draw replays the seat, no strike)
def mixed(rank, seeds, salt="s"):
    R_ = road.rules_for(rank); R_["p_shift"] = SHIFT; R_["draw"] = DRAW
    rej = fb = inner = 0; preds = []; clears = n = 0; seat_sets = []; faced = []; trip = []; posts = []; gate = {}
    road.PAIRS.clear(); road.KITSTAT[:] = [0, 0]
    for run in range(1, seeds + 1):
        po = poster_for(salt, run); RR = dict(R_); RR["poster"] = posters.POSTERS[po]
        o = road.study(rank, 1, 4, R_=RR, salt=f"{salt}{run}")   # one run, its own seed
        rej += o["reject_rate"] > 0 and round(o["reject_rate"] / (1 - o["reject_rate"])) or 0
        fb += o["fallback"]
        for k, v in o["gate"].items(): gate[k] = gate.get(k, 0) + v
        if o["fallback"]: continue
        preds.append(o["pred"]); clears += o["sim"] * 4; n += 4
        seat_sets += o["faced"]; faced += o["bot_faced"]; trip += o["trip"]; posts.append(po)
    return dict(rej=rej / (seeds + rej), fb=fb, pred=st.mean(preds), sim=clears / n, seats=seat_sets, faced=faced,
                trip=trip, posts=posts, gate=gate, R=R_, kitpu=road.KITSTAT[1] / max(1, road.KITSTAT[0]))

if __name__ == "__main__":
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 10000
    prev = None
    print(f"rank | rule | attempts discarded | fallback | gate rejects | predicted clear (no boons) | bot clear | drop | Jaccard seats/faced | coverage mean/min (reach) | (poster,champ,Kit) repeats in 10 | Kit plays a passed-up deck")
    for rk in range(11):
        o = mixed(rk, seeds)
        js, cmin, cm = road.oatmeal(o["seats"], o["R"]); jf, _, _ = road.oatmeal(o["faced"], o["R"])
        reps = road.triple_repeats(o["trip"], o["posts"])
        lab = "base" if rk == 0 else road.RANK_RULES[rk][0]
        drop = "" if prev is None else f"{100*(prev-o['sim']):.1f}"
        print(f"RR{rk} | {lab} | {100*o['rej']:.1f}% | {o['fb']} | {o['gate']} | {100*o['pred']:.1f}% | {100*o['sim']:.1f}% | {drop} | {js:.3f}/{jf:.3f} | {100*cm:.0f}%/{100*cmin:.0f}% ({len(road.reachable(o['R']))}) | {reps} | Kit passed-up {100*o['kitpu']:.0f}%", flush=True)
        prev = o["sim"]
        if rk in (0, 10):
            gaps = [abs(D[a]["elo"] - D[b]["elo"]) for a, b in road.PAIRS]; eras = [abs(D[a]["era"] - D[b]["era"]) for a, b in road.PAIRS]
            q = lambda xs: [round(x) for x in st.quantiles(xs, n=20)]
            print(f"   duels {len(gaps)}: |Elo gap| p50 {q(gaps)[9]} p95 {q(gaps)[18]} max {max(gaps)}; era gap p50 {q(eras)[9]} p95 {q(eras)[18]} max {max(eras)}; era gap >=15: {100*sum(e>=15 for e in eras)/len(eras):.1f}%, >=20: {100*sum(e>=20 for e in eras)/len(eras):.1f}%")
