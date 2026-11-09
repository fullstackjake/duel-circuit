#!/usr/bin/env python3
"""
Fetches the TCG Forbidden/Limited list of each historic deck's format from Format Library's public API
(formatlibrary.com) and writes them to lflists/history.lflist.conf in EDOPro's format, one "!TCG <month year>" list
per format, so decks can be checked against the list of their era, and tags each deck with a "#banlist TCG <month year>"
line (the deck editor selects that list when the deck is opened). One request every 1.5 s. The October 2026 decks use the September 2026 list (in effect at YCS Houston).
Run from the repo root:  python3 tools/fetch_banlists.py

`--list "<Month Year>"` (repeatable) fetches just those lists and writes each into the existing file in date order,
replacing a list of the same name and leaving every other list (and every deck) alone: how lists no history deck uses get
in (No Limits' TCG January 2024 ... May 2026, docs/dev/NO-LIMITS-SPEC-2026-11-02.md P1), and how a list is corrected.
Format Library's lists omit two cards Konami's lists carry (FL_MISSING); both modes add them. The full rewrite drops
lists added with --list and the hand-added October 2026 list, so re-add them after one.
  python3 tools/fetch_banlists.py --list "September 2025" --list "October 2025"
"""
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UA = "DuelCircuit/0.1 (personal non-commercial fan project; fetches a few ban lists)"
API = "https://www.formatlibrary.com/api/"
HOUSTON = "September 2026"  # the list in effect at YCS Houston 2026 (October 17-18), for the October 2026 decks
LIMITS = (("forbidden", 0), ("limited", 1), ("semiLimited", 2))
# Cards Format Library's lists leave out, by the first list Konami's carries them on (checked against yugiohcardguide.com's
# full tables and Konami's 21 Sep 2026 page): (code, copies, name), added from that list on when the fetched list lacks them.
FL_MISSING = (
    ("September 2024", (82732705, 1, "Skill Drain")),
    ("February 2026", (10963799, 0, "Barrier Statue of the Torrent")),
)
PATH = os.path.join(ROOT, "lflists", "history.lflist.conf")


def get(path):
    time.sleep(1.5)
    req = urllib.request.Request(API + path, headers={"User-Agent": UA, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def when(name):
    month, year = name.split()[-2:]
    return int(year), time.strptime(month, "%B").tm_mon


def fetch_list(name):
    """One list's lines ("!TCG <name>", then "<code> <copies> --<card>"), Forbidden, Limited, Semi-Limited, with FL_MISSING added."""
    data = get("banlists/" + urllib.parse.quote(name))
    groups = {copies: [] for _, copies in LIMITS}
    for key, copies in LIMITS:
        for row in data.get(key) or []:
            code = (row.get("card") or {}).get("artworkId")
            if code and code.isdigit():
                groups[copies].append((int(code), row.get("cardName", "")))
    for since, (code, copies, card) in FL_MISSING:
        if when(name) >= when(since) and all(code != c for rows in groups.values() for c, _ in rows):
            groups[copies].append((code, card))
    if not any(groups.values()):
        raise SystemExit(f"Format Library has no cards for {name}")
    return [f"!TCG {name}"] + [f"{code} {copies} --{card}" for copies, rows in groups.items() for code, card in rows]


def insert_lists(names):
    """--list: each list fetched and written into the file in date order; the file's other lists are kept as they are."""
    blocks, header, current = [], [], None
    for line in open(PATH, encoding="utf-8").read().rstrip("\n").split("\n"):
        if line.startswith("!"):
            current = [line]
            blocks.append(current)
        elif current is None:
            header.append(line)
        else:
            current.append(line)
    for name in names:
        lines = fetch_list(name)
        blocks = [b for b in blocks if b[0] != lines[0]]
        at = next((i for i, b in enumerate(blocks) if b[0].startswith("!TCG ") and when(b[0][1:]) > when(name)), len(blocks))
        blocks.insert(at, lines)
        print(f"TCG {name}: {len(lines) - 1} cards")
    with open(PATH, "w", encoding="utf-8") as f:
        f.write("\n".join(header + [l for b in blocks for l in b]) + "\n")


def main():
    if "--list" in sys.argv:
        insert_lists([sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == "--list"])
        return
    catalog = json.load(open(os.path.join(ROOT, "campaign", "history.json"), encoding="utf-8"))
    formats = {f["name"]: f.get("banlist") for f in get("formats/")}
    deck_list, wanted = {}, []
    for deck in catalog:
        # The format the deck's list was taken from (its #source line), else its first format.
        fmt = deck["formats"][0]
        ydk = os.path.join(ROOT, "decks", "history", deck["id"] + ".ydk")
        if os.path.exists(ydk):
            m = re.search(r"\(([^()]+) format, ", open(ydk, encoding="utf-8").read())
            fmt = m.group(1) if m else fmt
        banlist = formats.get(fmt)
        if fmt.startswith("houston:"):
            banlist = HOUSTON
        if not banlist:
            continue
        deck_list[deck["id"]] = "TCG " + banlist
        if banlist not in wanted:
            wanted.append(banlist)

    out = ["#[Duel Circuit historic TCG lists, from Format Library (formatlibrary.com)]"]
    for name in sorted(wanted, key=when):
        lines = fetch_list(name)
        out += lines
        print(f"TCG {name}: {len(lines) - 1} cards")
    os.makedirs(os.path.join(ROOT, "lflists"), exist_ok=True)
    with open(PATH, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    for deck_id, name in deck_list.items():
        path = os.path.join(ROOT, "decks", "history", deck_id + ".ydk")
        lines = [l for l in open(path, encoding="utf-8").read().split("\n") if not l.startswith("#banlist ")]
        lines.insert(next(i for i, l in enumerate(lines) if l.strip() == "#main"), f"#banlist {name}")
        open(path, "w", encoding="utf-8").write("\n".join(lines))


if __name__ == "__main__":
    main()
