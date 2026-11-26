"""Read-only loader for the Borrowed Road study: ratings, decks, characters, usage, legality."""
import glob, json, os, re, sqlite3
from collections import Counter, defaultdict

ROOT = __import__("os").path.abspath(__import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", ".."))

def ratings():
    out = {}
    pat = re.compile(r"^\s*\d+\.\s+(\S+)\s+Elo\s+(\d+)\s+won\s+(\d+)% of (\d+)\s+avg\s+([\d.]+) turns\s+first\s+(\d+)% / second\s+(\d+)%")
    for line in open(f"{ROOT}/campaign/deck-ratings.txt"):
        m = pat.match(line)
        if m:
            n, e, w, g, t, f, s = m.groups()
            out[n] = dict(elo=int(e), won=int(w), games=int(g), turns=float(t), first=int(f), second=int(s))
    sig = set()
    for line in open(f"{ROOT}/campaign/deck-ratings.txt"):
        m = re.match(r"^\s{2}(\S+)\s+-?\d+\s+\(first .*\)\s*\*\s*$", line)
        if m: sig.add(m.group(1))
    for n in out: out[n]["sig"] = n in sig
    return out

def deck_files():
    fs = {}
    for f in glob.glob(f"{ROOT}/decks/**/*.ydk", recursive=True):
        fs.setdefault(os.path.splitext(os.path.basename(f))[0], f)
    return fs

def read_ydk(path):
    meta, main, extra, sec = {}, [], [], None
    for raw in open(path, encoding="utf-8"):
        l = raw.strip()
        if l == "#main": sec = main; continue
        if l == "#extra": sec = extra; continue
        if l.startswith("!side"): sec = None; continue
        if l.startswith("#"):
            k, _, v = l[1:].partition(" ")
            meta.setdefault(k, v)
            continue
        if l.isdigit() and sec is not None: sec.append(int(l))
    return meta, main, extra

_db = None
def cards():
    global _db
    if _db is None:
        _db = {}
        for f in sorted(glob.glob(f"{ROOT}/vendor/BabelCDB/*.cdb")):
            b = os.path.basename(f)
            if "rush" in b or "skill" in b: continue
            con = sqlite3.connect(f)
            for cid, name, alias, typ in con.execute("select t.id,t.name,d.alias,d.type from texts t join datas d on d.id=t.id"):
                _db.setdefault(cid, (name, alias, typ))
    return _db

def canon(cid):
    db = cards()
    seen = 0
    while cid in db and db[cid][1] and seen < 3:
        cid = db[cid][1]; seen += 1
    return cid

def lflists():
    lists, cur = {}, None
    for raw in open(f"{ROOT}/lflists/history.lflist.conf", encoding="utf-8"):
        l = raw.strip()
        if l.startswith("!"): cur = lists.setdefault(l[1:], {}); continue
        m = re.match(r"^(\d+)\s+(-?\d+)", l)
        if m and cur is not None: cur[int(m.group(1))] = int(m.group(2))
    return lists

def profiles():
    out = {}
    for f in glob.glob(f"{ROOT}/personalities/*.profile"):
        pid = os.path.basename(f)[:-8]
        d = {}
        for l in open(f, encoding="utf-8"):
            m = re.match(r"^(\w+):\s*(.*?)\s*(#.*)?$", l)
            if m and m.group(1) not in d: d[m.group(1)] = m.group(2)
        out[pid] = d
    return out

def usage():
    """deck -> list of places it is played (circuit match, library match/local, ladder rung)."""
    use = defaultdict(list)
    cur = None
    for l in open(f"{ROOT}/campaign/circuits.txt", encoding="utf-8"):
        if l.startswith("circuit:"): cur = l.split(":", 1)[1].strip()
        m = re.match(r"^match:\s*(\S+)\s+(\S+)", l)
        if m: use[m.group(2)].append(f"circuit:{cur}")
    cur = None
    for l in open(f"{ROOT}/campaign/library.txt", encoding="utf-8"):
        if l.startswith("circuit:"): cur = l.split(":", 1)[1].strip()
        m = re.match(r"^(match|local):\s*(\S+)\s+(\S+)", l)
        if m: use[m.group(3)].append(f"library-{m.group(1)}:{cur}")
    for l in open(f"{ROOT}/campaign/ladder.txt", encoding="utf-8"):
        m = re.match(r"^(rung|visitor):\s*(\S+)\s+([^#]+)", l)
        if m:
            for d in m.group(3).split(","): use[d.strip()].append(f"ladder-{m.group(1)}")
    return use

def usage_all():
    use = usage()
    for l in open(f"{ROOT}/campaign/guests.txt", encoding="utf-8"):
        m = re.match(r"^guest:\s*(.*)$", l)
        if m:
            for part in m.group(1).split("+"):
                toks = part.split()
                for d in " ".join(toks[1:]).split(","):
                    if d.strip(): use[d.strip()].append("guest")
    ev = None
    for l in open(f"{ROOT}/campaign/alley-events.txt", encoding="utf-8"):
        if l.startswith("event:"): ev = l.split(":", 1)[1].strip()
        m = re.match(r"^bases?:\s*([^#]+)", l)
        if m:
            for d in m.group(1).split(","):
                if d.strip(): use[d.strip()].append(f"alley:{ev}")
    return use
