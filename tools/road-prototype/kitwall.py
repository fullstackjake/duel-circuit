"""RR6+: Kit's deck on the S5 wall. Kit-plays-a-passed-up-deck share and clear, greedy pilot vs one who refuses it."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import road
N = int(sys.argv[1]) if len(sys.argv) > 1 else 3000
for rk in range(5, 11):
    row = f"RR{rk}"
    for pol in ("greedy", "refuse"):
        R_ = road.rules_for(rk); R_["kit_wall_policy"] = pol; road.KITSTAT[:] = [0, 0]
        o = road.study(rk, N, 4, R_=R_)
        row += f" | {pol}: Kit passed-up {100*road.KITSTAT[1]/max(1, road.KITSTAT[0]):.0f}% clear {100*o['sim']:.1f}%"
    print(row, flush=True)
