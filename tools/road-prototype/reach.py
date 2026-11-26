"""Reachability: for every pool deck, can it be met as an opponent, offered as a rental, both, or neither, at any rank?
Empirical over generated routes (all ranks, posters rotating), plus Kit's window decks under both wall policies."""
import sys, os, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import road, posters, final
from road import D
from tags import CHAMPIONS, CHAMPIONS_LATER
N = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
opp, rent, rent_offer_wall = set(), set(), set()
for rk in range(11):
    for later in (False, True):
        snap = {"champions": CHAMPIONS + (CHAMPIONS_LATER if later else [])}
        for run in range(1, N + 1):
            R_ = road.rules_for(rk); R_["poster"] = posters.POSTERS[final.poster_for("r", run)]
            for att in range(32):
                rt, why = road.generate(f"r{later}", run, rk, R_, att, snap)
                if rt: break
            if not rt: continue
            seats = {s for ss in rt["seats"] for s in ss}
            opp |= seats; rent |= seats   # a beaten seat can be taken
            ow = set(rt["offer"]) | {w for ws in rt["walls"] for w in ws}
            rent |= ow; rent_offer_wall |= ow
            for pol in ("greedy", "refuse"):
                R2 = dict(R_, kit_wall_policy=pol)
                ok, faced, _ = road.play(rt, R2, random.Random(run))
                opp |= set(faced)   # Kit's decks (only reached if the run gets there; play() picks Kit at S6)
            # Kit candidates regardless of reaching S6: every window deck Kit could field
            lo, hi = R_["kit_two"] or R_["kit"]
            opp |= {d for d in road.WALL if lo <= D[d]["elo"] <= hi}
pool = sorted(D, key=lambda n: -D[n]["elo"])
cat = {"both": [], "opponent only": [], "rental only": [], "neither": []}
for n in pool:
    o, r = n in opp, n in rent
    cat["both" if o and r else "opponent only" if o else "rental only" if r else "neither"].append(f"{n} {D[n]['elo']}")
for k, v in cat.items(): print(f"{k} ({len(v)}): " + ", ".join(v))
rentable = [n for n in pool if n in rent and n in road.WALL or n in rent and n in road.OPP]
print(f"rentable (pins under option a): {len([n for n in pool if n in rent])}; of these via offer/wall: {len(rent_offer_wall)}; only by beating a seat: {len([n for n in pool if n in rent and n not in rent_offer_wall])}")
print("pool", len(pool), "rental pool (WALL)", len(road.WALL), "seat pool (OPP)", len(road.OPP))
