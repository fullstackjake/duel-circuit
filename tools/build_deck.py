#!/usr/bin/env python3
"""
Builds .ydk decks from readable recipes: a recipe file holds one or more decks, each starting with "deck: <path>", then
"#note" lines copied as they are (#era, #about, #why, #price...), then "main:" / "extra:" (and "side:") sections of "<copies> <card
name>" lines. Names are matched exactly (case-insensitive) against the engine's card databases, preferring the base
card (not an alternate artwork). Reports unknown names and Main Decks outside 40-60; writes nothing if anything is wrong.
Run from the repo root:  python3 tools/build_deck.py decks/recipes/starters.txt
"""
import glob
import os
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = ("rush", "skill", "unofficial", "goat")
# Fusion, Synchro, Xyz and Link Monsters: the Extra Deck's cards (ocgcore puts any other card listed there on top of
# the Main Deck, so the deck opens with it every duel).
EXTRA_TYPES = 0x40 | 0x2000 | 0x800000 | 0x4000000


def card_ids():
    """Card ids by lowercase name, and the names of Extra Deck monsters."""
    by_name = {}
    for f in sorted(glob.glob(os.path.join(ROOT, "vendor", "BabelCDB", "*.cdb"))):
        if any(s in os.path.basename(f) for s in SKIP):
            continue
        con = sqlite3.connect(f)
        for cid, name, alias, kind in con.execute("select t.id, t.name, d.alias, d.type from texts t join datas d on d.id = t.id"):
            key = name.lower()
            # Base cards win over alternate artworks; official ids over pre-release ones.
            if key not in by_name or (alias == 0 and by_name[key][1] != 0):
                by_name[key] = (cid, alias, kind)
    return {k: v[0] for k, v in by_name.items()}, {k for k, v in by_name.items() if v[2] & 1 and v[2] & EXTRA_TYPES}


def main():
    ids, extra_monsters = card_ids()
    decks, current, section = [], None, None
    for raw in open(sys.argv[1], encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("//"):
            continue
        if line.startswith("deck:"):
            current = {"path": line[5:].strip(), "notes": [], "main": [], "extra": [], "side": []}
            decks.append(current)
            section = None
        elif line.startswith("#"):
            current["notes"].append(line)
        elif line in ("main:", "extra:", "side:"):
            section = line[:-1]
        else:
            copies, name = line.split(" ", 1)
            current[section].append((int(copies), name.strip()))
    problems = []
    for d in decks:
        for section in ("main", "extra", "side"):
            for copies, name in d[section]:
                if name.lower() not in ids:
                    problems.append(f"{d['path']}: unknown card '{name}'")
                elif section == "main" and name.lower() in extra_monsters:
                    problems.append(f"{d['path']}: '{name}' is an Extra Deck monster, listed under main:")
                elif section == "extra" and name.lower() not in extra_monsters:
                    problems.append(f"{d['path']}: '{name}' isn't an Extra Deck monster, listed under extra:")
        size = sum(c for c, _ in d["main"])
        if not 40 <= size <= 60:
            problems.append(f"{d['path']}: Main Deck has {size} cards")
        if sum(c for c, _ in d["extra"]) > 15:
            problems.append(f"{d['path']}: Extra Deck over 15")
        if sum(c for c, _ in d["side"]) > 15:
            problems.append(f"{d['path']}: Side Deck over 15")
    if problems:
        print("\n".join(problems))
        sys.exit(1)
    for d in decks:
        # A recipe's own "#created by Duel Circuit - ..." line replaces the plain one.
        created = [n for n in d["notes"] if n.startswith("#created")] or ["#created by Duel Circuit"]
        out = created[:1] + [n for n in d["notes"] if not n.startswith("#created")] + ["#main"]
        out += [str(ids[n.lower()]) for c, n in d["main"] for _ in range(c)]
        out += ["#extra"] + [str(ids[n.lower()]) for c, n in d["extra"] for _ in range(c)]
        out += ["!side"] + [str(ids[n.lower()]) for c, n in d["side"] for _ in range(c)] + [""]
        path = os.path.join(ROOT, d["path"])
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, "w", encoding="utf-8").write("\n".join(out))
        print(f"wrote {d['path']} ({sum(c for c, _ in d['main'])} + {sum(c for c, _ in d['extra'])})")


if __name__ == "__main__":
    main()
