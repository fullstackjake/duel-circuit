"""The Borrowed Road generator study (Level Designer, 2026-11-25). Read-only over repo data.
python3 road.py [--seeds N] [--ranks 0-10] [--plays K] [--concept]"""
import sys, random, math, argparse, statistics as st
from itertools import product
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from data import ratings, deck_files, read_ydk, profiles
from tags import T, ERA_FALLBACK, EXCLUDED, CHAMPIONS

R = ratings(); F = deck_files(); P = profiles()
def era_of(n):
    m, _, _ = read_ydk(F[n]); e = m.get("era")
    return int(e) if e and e[:4].isdigit() else ERA_FALLBACK[n]
def bucket(y): return 0 if y <= 2010 else 1 if y <= 2016 else 2 if y <= 2021 else 3
D = {}
for n, r in R.items():
    if n in EXCLUDED: continue
    s, mech, easy = T[n]
    D[n] = dict(elo=r["elo"], turns=r["turns"], f=r["first"], s=r["second"], sig=r["sig"], era=era_of(n),
                b=bucket(era_of(n)), style=s, mech=mech, easy=easy, opp=n in P)
OPP = sorted(n for n in D if D[n]["opp"]); WALL = sorted(n for n in D if D[n]["turns"] <= 14.0)   # gadgets 14.7 out
G = st.mean(r["second"] - r["first"] for r in R.values()) / 100   # engine-wide going-second edge

def pbase(ep, ec): return 1 / (1 + 10 ** ((ec - ep) / 400))
def p_duel(pl, cpu, toss):
    """toss: 'rand' | 'cpu' | 'player'.
    'cpu': a CPU that calls the toss picks the side worse for the player, judged on BOTH decks' first/second splits.
    'player': the player picks (Call the Toss boon). Precedence: when the player holds Call the Toss and the CPU would
    also call (champion from RR2, every seat from RR5), the player's call wins (see play())."""
    b = pbase(D[pl]["elo"], D[cpu]["elo"])
    dP = (D[pl]["s"] - D[pl]["f"]) / 200; dC = (D[cpu]["s"] - D[cpu]["f"]) / 200
    pf = min(.99, max(.01, b - dP - dC + G / 2)); ps = min(.99, max(.01, b + dP + dC - G / 2))
    if toss == "player": return max(pf, ps)
    if toss == "cpu": return min(pf, ps)
    return (pf + ps) / 2

# ---------------------------------------------------------------- rules
BASE = dict(
    start=(1350, 100, 40),        # S1 centre, offset, half width
    centres=[1350, 1420, None, 1560, 1620],   # S1, S2, (S3 champion), S4, S5
    fork_half=40,                 # low road c-h..c, high road c..c+h
    champ=(1500, 50), kit=(1660, 1740),
    wall_n=2, wall_off=100, wall_half=40, strikes=3, boons=3,
    champ_toss=False, all_toss=False, no_wall_late=False, kit_two=None,
    pmin=0.40, floor=0.50, turn_cap=75,
)
TUNED = dict(BASE, start=(1400, 60, 40), centres=[1390, 1460, None, 1580, 1630], champ=(1520, 55), fork_half=60,
             wall_off=40, tire_max=3, once=True, span=(3, 3), reverse=True, turn_cap=130,
             wall_targets=[1460, 1520, 1580, 1630, 1700])
RANK_RULES = {   # rank: (label, edit) ; cumulative
    1: ("wall offers 1 deck", lambda r: r.update(wall_n=1)),
    2: ("champion +40 and calls the toss", lambda r: r.update(champ=(r["champ"][0] + 40, r["champ"][1]), champ_toss=True)),
    3: ("boons show 2", lambda r: r.update(boons=2)),
    4: ("wall decks -20", lambda r: r.update(wall_off=r["wall_off"] - 20)),
    5: ("every CPU calls the toss; gate p 0.33", lambda r: r.update(all_toss=True, pmin=0.33)),
    6: ("Kit 1760-1840; the S5 wall shows a Kit-window deck (refuse it and Kit plays it); fork centres +20",
        lambda r: r.update(kit=(1760, 1840), kit_wall=True, centres=[c + 20 if c else None for c in r["centres"]])),
    7: ("2 strikes (Spare Tire repairs to 3)", lambda r: r.update(strikes=2, tire_max=3)),
    8: ("boons show 1", lambda r: r.update(boons=1)),
    9: ("no Spare Tire; fork centres +15", lambda r: r.update(banned=("tire",), centres=[c + 15 if c else None for c in r["centres"]])),
    10: ("Kit two decks 1740-1950 in one sitting (each p >= 0.33); a loss replays the deck that beat you",
         lambda r: r.update(kit_two=(1740, 1950), kit_pmin=0.33, kit_replay_one=True)),
}
def rules_for(rank, tuned=True, base=None, rules=None):
    r = dict(base or (TUNED if tuned else BASE))
    for k in range(1, rank + 1): (rules or RANK_RULES)[k][1](r)
    return r
FLOORS = [50, 44, 38, 33, 28, 23, 18, 14, 10, 7, 4]

def pick(rng, pool, lo, hi, used, ok=lambda n: True, prefer=None):
    c = [n for n in pool if lo <= D[n]["elo"] <= hi and n not in used and ok(n)]
    if prefer and c and rng.random() < PREFER:
        best = max(prefer(n) for n in c); c = [n for n in c if prefer(n) == best]
    return rng.choice(c) if c else None
PREFER = 0.75
DBG = {}
PAIRS = []
KITSTAT = [0, 0]   # Kit sittings, sittings where Kit plays a deck the player passed up
DRAWS = [0]
INNER = {}
GDBG = {}
SLOW = {}

ROAD_SALT = "road"
def generate(salt, run, rank, R_, attempt, snapshot=None):
    """Called ONCE at run start; the route it returns is stored in the save and never regenerated.
    Seed = Hash.Mix(collectionSalt, RoadSalt, runNumber, rank, attempt): the rank is mixed in, so a route is tied to the
    rank it was started at. Everything else Generate reads is frozen into `snapshot` at run start: the Pilot's Log x2
    start-offer weights ("logged"), the bus stage's unlocked champions ("champions") and the poster (in R_["poster"])."""
    snapshot = snapshot or {}
    rng = random.Random(f"{salt}|{ROAD_SALT}|{run}|{rank}|{attempt}")
    CH = snapshot.get("champions", CHAMPIONS); logged = snapshot.get("logged", set())
    used = set(); route = {}
    # start offer: 3 from the wall pool, different buckets and styles, one easy
    c0, off, h = R_["start"]; lo, hi = c0 + off - h, c0 + off + h
    PO = R_.get("poster") or {}
    lo += PO.get("start_shift", 0); hi += PO.get("start_shift", 0)
    cand = [n for n in WALL if lo <= D[n]["elo"] <= hi and PO.get("offer", lambda n: True)(n)]
    offer = None
    for _ in range(80):
        if len(cand) < 3: break
        o = []
        if PO.get("offer_bin"):   # Bargain Bin poster: one offer slot from the bottom of the shop wall
            blo, bhi = PO["offer_bin"]; bin_ = [n for n in WALL if blo <= D[n]["elo"] <= bhi]
            if bin_: o.append(rng.choice(bin_))
        while len(o) < 3:   # weighted draw without replacement: unlogged decks x2
            pool = [n for n in cand if n not in o]; wts = [1 if n in logged else 2 for n in pool]
            o.append(rng.choices(pool, wts)[0])
        if len({D[n]["b"] for n in o}) >= PO.get("offer_buckets", 3) and len({D[n]["style"] for n in o}) >= PO.get("offer_styles", 3) and sum(D[n]["easy"] for n in o) >= PO.get("offer_easy", 1) and any(PO.get("offer_need", lambda n: True)(n) for n in o):
            offer = o; break
    if not offer: return None, "start"
    route["offer"] = offer; used |= set(offer)
    # walls after S1..S5: decks at the next stop's centre + offset
    nxt = R_.get("wall_targets") or [R_["centres"][1], R_["champ"][0], R_["centres"][3], R_["centres"][4], sum(R_["kit"]) / 2]
    walls = []; best = max(offer, key=lambda n: D[n]["elo"])
    cc, cw = R_["champ"]; champs = [x for x in CH if x in D and cc - cw <= D[x]["elo"] <= cc + cw]
    ctoss = "cpu" if R_["all_toss"] or R_["champ_toss"] else "rand"
    for k, c in enumerate(nxt):
        if k >= R_.get("no_wall_from", 9) or (R_["no_wall_late"] and k >= 2): walls.append([]); continue
        w = []
        if k == 4 and R_.get("kit_wall"):   # RR6+: the wall after S5 shows one deck from Kit's window; refuse it and Kit plays it
            klo, khi = R_["kit_two"] or R_["kit"]
            d = pick(rng, WALL, klo, khi, used, lambda d: p_duel(best, d, "cpu" if R_["all_toss"] else "rand") >= R_.get("kit_pmin", R_["pmin"]))
            d = d or pick(rng, WALL, klo, khi, used)
            if d is None: return None, "wall"
            w.append(d)
        # the wall before the house champion leaves at least 2 champions fair (p >= pmin)
        fair = (lambda d: sum(p_duel(max(best, d, key=lambda n: D[n]["elo"]), x, ctoss) >= R_["pmin"] + R_.get("margin", 0) for x in champs) >= 2) if k == 1 else (lambda d: True)
        for _ in range(R_["wall_n"] - len(w)):
            d = pick(rng, WALL, c + R_["wall_off"] - R_["wall_half"], c + R_["wall_off"] + R_["wall_half"], used | set(w), fair if not w else (lambda d: True))
            if d is None: return None, "wall"
            w.append(d)
        walls.append(w); used |= set(w); best = max([best] + w, key=lambda n: D[n]["elo"])
    route["walls"] = walls
    # the reference deck at each stop: the best start offer, then the best wall deck so far (beaten decks ignored)
    refdeck = []; me = max(offer, key=lambda n: D[n]["elo"])
    for k in range(len(R_["centres"])):
        refdeck.append(me)
        if walls[k]: me = max([me] + walls[k], key=lambda n: D[n]["elo"])
    def toss_at(k): return "cpu" if R_["all_toss"] or (k == 2 and R_["champ_toss"]) else "rand"
    def fill_seats():
        used0 = set(used)
        seats = [None] * len(R_["centres"]); paths = [(frozenset(), frozenset())]   # (buckets, styles) of every partial path
        nstops = len(R_["centres"]); NB, NS = R_.get("span", (3, 4))
        order = list(range(nstops))[::-1] if R_.get("reverse", True) else list(range(nstops))
        fs = PO.get("feature", {})
        ch_i = R_["centres"].index(None)   # the house champion first, then featured stops, then the rest
        order = [ch_i] + [i for i in order if i in fs and i != ch_i] + [i for i in order if i not in fs and i != ch_i]
        for step, i in enumerate(order):
            c = R_["centres"][i]
            prev = [s for j in (i - 1, i + 1) if 0 <= j < nstops and seats[j] for s in seats[j]]; left = nstops - step
            def okc(n, prev=prev, left=left):
                if any(D[n]["b"] == D[s]["b"] and D[n]["style"] == D[s]["style"] for s in prev): return False
                for bs, ss in paths:   # this stop must add what a tight path still needs
                    if NB - len(bs) >= left and D[n]["b"] in bs: return False
                    if NS - len(ss) >= left and D[n]["style"] in ss: return False
                return True
            def novel(n):   # how many partial paths still short of the span this seat helps (bucket counts double)
                return sum(2 * (len(bs) < NB and D[n]["b"] not in bs) + (len(ss) < NS and D[n]["style"] not in ss) for bs, ss in paths)
            if c is None:
                cc, w = R_["champ"]
                ch = pick(rng, [x for x in CH if x in D], cc - w, cc + w, used0,
                          lambda n, i=i: okc(n) and PO.get("champ", lambda n: True)(n) and p_duel(refdeck[i], n, toss_at(i)) >= R_["pmin"] + R_.get("margin", 0), novel)
                if not ch: return None, "champ"
                stop = [ch]; used0.add(ch)
            else:
                h = R_["fork_half"]
                pok = lambda n, i=i, m=0.0: (p_duel(refdeck[i], n, toss_at(i)) >= R_["pmin"] + R_.get("margin", 0) - m
                                             and (D[refdeck[i]]["turns"] + D[n]["turns"]) / 2 <= R_.get("seat_turns", 13.5) and abs(D[refdeck[i]]["era"] - D[n]["era"]) <= R_.get("era_cap", 99))
                feat = PO.get("feature", {}).get(i)
                lo_f = hi_f = (lambda n: True)
                if feat:   # put the featured seat on whichever road can carry it (seeded coin when both can)
                    Lc = [n for n in OPP if (PO["s1_low"][0] if (i == 0 and PO.get("s1_low")) else c - h) <= D[n]["elo"] < c and n not in used0 and okc(n) and pok(n)]
                    Hc = [n for n in OPP if c <= D[n]["elo"] <= c + h and n not in used0 and okc(n) and pok(n, m=0.10)]
                    opts = []
                    if any(feat(n) for n in Lc) and Hc: opts.append("lo")
                    if any(feat(n) for n in Hc) and Lc: opts.append("hi")
                    if not opts: return None, f"S{i+1}"
                    side = rng.choice(opts)
                    if side == "lo": lo_f = feat
                    else: hi_f = feat
                llo, lhi = PO["s1_low"] if (i == 0 and PO.get("s1_low")) else (c - h, c - 1)
                lo_s = pick(rng, OPP, llo, lhi, used0, lambda n, i=i: okc(n) and pok(n) and lo_f(n), novel)
                if lo_s: used0.add(lo_s)
                hi_s = pick(rng, OPP, c, c + h, used0, lambda n, i=i: okc(n) and pok(n, m=0.10) and hi_f(n) and (lo_s is None or D[n]["style"] != D[lo_s]["style"] or D[n]["b"] != D[lo_s]["b"]), novel)
                if not lo_s or not hi_s:
                    nb = min(len(x[0]) for x in paths); ns = min(len(x[1]) for x in paths)
                    DBG[(i+1, "lo" if not lo_s else "hi", nb, ns)] = DBG.get((i+1, "lo" if not lo_s else "hi", nb, ns), 0) + 1
                    return None, f"S{i+1}"
                used0.add(hi_s); stop = [lo_s, hi_s]
            seats[i] = stop
            paths = list({(bs | {D[n]["b"]}, ss | {D[n]["style"]}) for bs, ss in paths for n in stop})
        # span rule on every path through the forks (Kit excluded: chosen at run time)
        for path in product(*seats):
            if len({D[n]["b"] for n in path}) < NB or len({D[n]["style"] for n in path}) < NS: return None, "span"
        return seats, used0
    tries = 0
    while True:
        res = fill_seats()
        if res[0] is not None: break
        tries += 1; INNER[res[1]] = INNER.get(res[1], 0) + 1
        if tries >= R_.get("inner", 8): return None, res[1]
    seats, used = res
    route["inner"] = tries
    route["seats"] = seats
    route["used"] = used; route["rng"] = rng.random()
    return route, None

def kit_decks(route, R_, passed, rng, me, toss):
    """Kit, at run time: the strongest passed-up decks in Kit's window that leave you p >= pmin, else seeded picks from
    the window that do, else the window's weakest. Two decks at RR10."""
    met = {s for ss in route["seats"] for s in ss} | set(route["offer"]) | {me}
    lo, hi = R_["kit_two"] or R_["kit"]; n = 2 if R_["kit_two"] else 1
    ok = lambda d: p_duel(me, d, toss) >= R_.get("kit_pmin", R_["pmin"])
    c = [d for d in sorted(passed, key=lambda d: -D[d]["elo"]) if lo <= D[d]["elo"] <= hi and ok(d) and d != me][:n]
    rest = sorted(d for d in WALL if lo <= D[d]["elo"] <= hi and d not in met and d not in c and ok(d))
    while len(c) < n and rest: c.append(rest.pop(rng.randrange(len(rest))))
    weak = sorted((d for d in WALL if lo <= D[d]["elo"] <= hi and d not in met and d not in c), key=lambda d: D[d]["elo"])
    while len(c) < n and weak: c.append(weak.pop(0))
    return c

def reference(route, R_):
    """Gate reference: the best start offer, the low road, greedy swaps, no boons.
    Returns per stop (p on the road taken, p on the other seat, matchup turns) and Kit's decks."""
    me = max(route["offer"], key=lambda n: D[n]["elo"]) if R_.get("ref_best", True) else min(route["offer"], key=lambda n: D[n]["elo"])
    out = []; passed = set()
    for k, ss in enumerate(route["seats"]):
        toss = "cpu" if R_["all_toss"] or (k == 2 and R_["champ_toss"]) else "rand"
        opp = ss[0]; passed |= set(ss[1:])
        p_lo = p_duel(me, opp, toss); p_hi = min(p_duel(me, s, toss) for s in ss)
        mt = max((D[me]["turns"] + D[s]["turns"]) / 2 for s in ss)
        if mt > 13: SLOW[(me, max(ss, key=lambda s: D[s]["turns"]))] = SLOW.get((me, max(ss, key=lambda s: D[s]["turns"])), 0) + 1
        out.append(("S%d" % (k + 1), p_lo, p_hi, mt, [p_lo]))
        cands = [me, opp] + route["walls"][k]
        if k >= R_.get("no_wall_from", 9) or (R_["no_wall_late"] and k >= 2): cands = [me, opp]
        best = max(cands, key=lambda n: D[n]["elo"]); passed |= set(w for w in route["walls"][k] if w != best)
        me = best
    toss = "cpu" if R_["all_toss"] else "rand"
    kit = kit_decks(route, R_, passed, random.Random(route["rng"]), me, toss)
    if not kit: return None
    pks = [p_duel(me, kd, toss) for kd in kit]
    out.append(("Kit", min(pks), min(pks), max((D[me]["turns"] + D[kd]["turns"]) / 2 for kd in kit), pks))
    return out, kit

def clear_prob(plist, strikes):
    # P(total losses <= strikes-1), losses at each stop geometric(p)
    dist = [1.0] + [0.0] * (strikes - 1)
    for p in plist:
        nd = [0.0] * strikes
        for a in range(strikes):
            for k in range(strikes - a): nd[a + k] += dist[a] * ((1 - p) ** k) * p
        dist = nd
    return sum(dist)

BOONS = ["toss", "tire", "scout", "detour", "second", "rest", "postcard"]
def play(route, R_, rng):
    """Bot pilot: best start offer, low road, greedy swaps, boon priority tire > rest > toss."""
    me = max(route["offer"], key=lambda n: D[n]["elo"]); strikes = R_["strikes"]; rest = False; mytoss = False
    passed = set(); faced = []; duels = 0; taken = set()
    stops = list(route["seats"]) + [None]
    for k, ss in enumerate(stops):
        if ss is None:
            toss = "cpu" if R_["all_toss"] else "rand"
            opps = kit_decks(route, R_, passed, random.Random(route["rng"]), me, toss)
            KITSTAT[0] += 1; KITSTAT[1] += any(o_ in passed for o_ in opps)
        else:
            opps = [ss[0]]; passed |= set(ss[1:])
            toss = "cpu" if R_["all_toss"] or (k == 2 and R_["champ_toss"]) else "rand"
        faced += opps
        for o_ in opps: PAIRS.append((me, o_))
        if mytoss: toss = "player"   # precedence: the player's Call the Toss beats any CPU call
        i = 0
        while i < len(opps):
            duels += 1
            if rng.random() < R_.get("draw", 0.0): DRAWS[0] += 1; continue   # a draw replays the seat, no strike
            p = min(.99, max(.01, p_duel(me, opps[i], toss) + R_.get("p_shift", 0.0)))   # human-pilot sensitivity
            if rng.random() < p: i += 1; continue
            if rest: rest = False
            else: strikes -= 1
            if strikes <= 0: return False, faced, duels
            if not R_.get("kit_replay_one"): i = 0
        mytoss = False
        if k == 5: return True, faced, duels
        cands = [me, opps[0]] + route["walls"][k]
        if k == 4 and R_.get("kit_wall") and R_.get("kit_wall_policy") == "refuse":
            cands = [me, opps[0]] + route["walls"][k][1:]
        best = max(cands, key=lambda n: D[n]["elo"])
        passed |= set(w for w in route["walls"][k] if w != best); me = best
        pool = [b for b in BOONS if not (R_.get("once") and b in taken) and b not in R_.get("banned", ())]
        offer = rng.sample(pool, min(R_["boons"], len(pool)))
        for b in R_.get("useful", ("tire", "rest", "toss")):
            if b in offer and not (b == "tire" and strikes >= R_.get("tire_max", 4)) and not (b == "rest" and rest):
                taken.add(b)
                if b == "tire": strikes += 1
                elif b == "rest": rest = True
                else: mytoss = True
                break
    return True, faced, duels

def study(rank, seeds, plays, R_=None, salt="s", verbose=True):
    R_ = R_ or rules_for(rank)
    floor = 2 * R_.get("gate_floor", 0)
    bot_faced = []; trip = []; rej = 0; fb = 0; reasons = {}; preds = []; minp = []; clears = 0; n = 0; turns = []; faced_runs = []; kits = []
    gate_reasons = {}
    for run in range(1, seeds + 1):
        route = None
        for attempt in range(32):
            rt, why = generate(salt, run, rank, R_, attempt)
            if rt is None: reasons[why] = reasons.get(why, 0) + 1; rej += 1; continue
            ref = reference(rt, R_)
            if ref is None: reasons["kit"] = reasons.get("kit", 0) + 1; rej += 1; continue
            stops, kit = ref
            ps = [math.prod(x[4]) for x in stops]
            pc = clear_prob(ps, R_["strikes"])
            tt = sum(x[3] / x[1] * len(x[4]) for x in stops)
            bad = any((x[1] < R_["pmin"] or x[2] < R_["pmin"] - 0.10) if x[0] != "Kit" else min(x[4]) < R_.get("kit_pmin", R_["pmin"]) for x in stops)
            slow = any(x[3] > R_.get("seat_turns", 13.5) for x in stops)
            if bad:
                gate_reasons["p<min"] = gate_reasons.get("p<min", 0) + 1
                for x in stops:
                    if x[1] < R_["pmin"] or x[2] < R_["pmin"] - 0.10 or min(x[4]) < R_["pmin"]:
                        key = ("p", x[0], "lo" if x[1] < R_["pmin"] else "hi"); GDBG[key] = GDBG.get(key, 0) + 1
            if pc < floor / 2: gate_reasons["clear<floor/2"] = gate_reasons.get("clear<floor/2", 0) + 1
            if slow:
                gate_reasons["slow seat"] = gate_reasons.get("slow seat", 0) + 1
                for x in stops:
                    if x[3] > R_.get("seat_turns", 13.5): GDBG[("t", x[0])] = GDBG.get(("t", x[0]), 0) + 1
            if bad or pc < floor / 2 or slow: rej += 1; continue
            route = rt; break
        if route is None: fb += 1; continue
        preds.append(pc); minp.append(min(x[1] for x in stops)); turns.append(tt); kits.append(kit[0])
        rng = random.Random(f"{salt}|play|{run}")
        for t in range(plays):
            ok, faced, _ = play(route, R_, rng); clears += ok; n += 1
            if t == 0: bot_faced.append(set(faced)); trip.append((route["seats"][2][0], kit[0]))
        faced_runs.append(set(s for ss in route["seats"] for s in ss) | set(kit))
    att = seeds + rej
    out = dict(rank=rank, reject_rate=rej / att, fallback=fb, reasons=reasons, gate=gate_reasons,
               pred=st.mean(preds) if preds else 0, pred_q=st.quantiles(preds, n=10) if len(preds) > 10 else [],
               sim=clears / max(1, n), minp=st.mean(minp) if minp else 0, turns=st.mean(turns) if turns else 0,
               faced=faced_runs, kits=kits, bot_faced=bot_faced, trip=trip, R=R_)
    return out

def reachable(R_):
    lo = R_["centres"][0] - R_["fork_half"]; hi = max(c for c in R_["centres"] if c) + R_["fork_half"]
    k = R_["kit_two"] or R_["kit"]; cc, cw = R_["champ"]
    return {n for n in OPP if lo <= D[n]["elo"] <= hi or k[0] <= D[n]["elo"] <= k[1]} | {n for n in CHAMPIONS if cc - cw <= D[n]["elo"] <= cc + cw}

def oatmeal(faced, R_=None):
    js, cov = [], []
    reach = reachable(R_) if R_ else set(OPP)
    for s in range(0, len(faced) - 49, 50):
        blk = faced[s:s + 50]
        for i in range(50):
            for j in range(i + 1, 50):
                a, b = blk[i], blk[j]; js.append(len(a & b) / len(a | b))
        cov.append(len(set().union(*blk) & reach) / len(reach))
    return (st.mean(js) if js else 0), (min(cov) if cov else 0), (st.mean(cov) if cov else 0)

def triple_repeats(trip, posters):
    rep = 0
    for i in range(len(trip)):
        t = (posters[i],) + trip[i]
        if any((posters[j],) + trip[j] == t for j in range(max(0, i - 9), i)): rep += 1
    return rep

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("--seeds", type=int, default=10000); ap.add_argument("--plays", type=int, default=4)
    ap.add_argument("--ranks", default="0-10"); a = ap.parse_args()
    lo, _, hi = a.ranks.partition("-"); hi = hi or lo
    print(f"pool: {len(OPP)} seat decks, {len(WALL)} wall decks; engine going-second edge g = {G*100:.1f} points")
    prev = None
    for rk in range(int(lo), int(hi) + 1):
        o = study(rk, a.seeds, a.plays)
        j, cmin, cmean = oatmeal(o["faced"], o["R"]); jb, _, _ = oatmeal(o["bot_faced"], o["R"])
        drop = "" if prev is None else f" drop {100*(prev-o['sim']):4.1f}"
        q = o["pred_q"] or [0] * 9
        print(f"RR{rk:<2} reject {100*o['reject_rate']:4.1f}% fallback {o['fallback']}  pred clear {100*o['pred']:4.1f}% (p10 {100*q[0]:.0f} p50 {100*q[4]:.0f} p90 {100*q[8]:.0f})"
              f"  bot sim {100*o['sim']:4.1f}%{drop}  min stop p {o['minp']:.2f}  E[turns] {o['turns']:.0f}  jaccard seats {j:.3f} faced {jb:.3f} reach {len(reachable(o["R"]))} coverage min {100*cmin:.0f}% mean {100*cmean:.0f}%"
              f"  gen-fail {o['reasons']} gate {o['gate']}")
        prev = o["sim"]
