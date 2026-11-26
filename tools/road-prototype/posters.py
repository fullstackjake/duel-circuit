import sys
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
import road
from road import D
from data import usage_all
U = usage_all()
# stop indices: 0 = S1, 1 = S2, 3 = S4, 4 = S5 (forks); a featured stop shows at least one seat meeting the test
def feat(stops, test): return {"feature": {i: test for i in stops}}
POSTERS = {
 "none": {},
 "Throwback Weekend":  feat((0, 1, 3), lambda n: D[n]["era"] <= 2016),
 "Fresh Off the Truck": feat((3, 4), lambda n: D[n]["era"] >= 2024),
 "Extra Deck Expo":    {"feature": {0: lambda n: D[n]["mech"] == "synchro", 1: lambda n: D[n]["mech"] == "xyz",
                                    3: lambda n: D[n]["mech"] == "fusion"}},
 "Speed Week":         feat((1, 3, 4), lambda n: D[n]["turns"] <= 10.0),
 "The Long Game":      feat((0, 1), lambda n: D[n]["turns"] >= 12.0),
 "Fusion Festival":    feat((3, 4), lambda n: D[n]["mech"] == "fusion"),
 "Graveyard Shift":    {"offer_need": lambda n: D[n]["style"] == "graveyard", **feat((0,), lambda n: D[n]["style"] == "graveyard")},
 "Trap Night":         feat((0, 1), lambda n: D[n]["style"] in ("control", "stun")),
 "Beatdown Boulevard": feat((1, 4), lambda n: D[n]["style"] == "beatdown"),
 "Rookie Road":        {"offer_easy": 2, "offer_styles": 2},
 "Underdog Day":       {"start_shift": -60},
 "Bargain Bin":        {"s1_low": (1170, 1330), "offer_bin": (1230, 1330)},
 "Back Roads":         {"champ": lambda n: len(U.get(n, [])) <= 1, **feat((0, 1, 3), lambda n: len(U.get(n, [])) <= 2)},
}
if __name__ == "__main__":
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 2000
    only = sys.argv[2].split(",") if len(sys.argv) > 2 else None
    print(f"{'poster':20}" + "".join(f" RR{r:<2}rej/sim" for r in range(11)))
    for name, po in POSTERS.items():
        if only and name not in only: continue
        row = f"{name:20}"
        for rk in range(11):
            R_ = road.rules_for(rk); R_["poster"] = po
            o = road.study(rk, seeds, 2, R_=R_)
            row += f" {100*o['reject_rate']:4.1f}/{100*o['sim']:4.1f}" + ("!" if o["fallback"] else " ")
        print(row, flush=True)
