#!/usr/bin/env python3
"""
Fetches a real tournament list for every deck in campaign/history.json and writes it to decks/history/<id>.ydk.

Sources (one request every 1.5 s, identified by user agent):
  - Format Library's public API (formatlibrary.com) for 2004 through early 2026: for each deck, its formats and deck
    type names are tried in order; the best-placed list wins, preferring original Konami events over modern retro events.
  - YGOPRODeck's YCS Houston 2026 Top 32 page for the October 2026 decks (best-placed list of the archetype).

Decks with no list found are left for a representative build (written by hand). Run from the repo root:
  python3 tools/fetch_history_decks.py [--only id,id] [--force]
"""
import html
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "campaign", "history.json")
OUT = os.path.join(ROOT, "decks", "history")
SOURCES = os.path.join(OUT, "SOURCES.md")
UA = "DuelCircuit/0.1 (personal non-commercial fan project; fetches a few tournament decklists)"
HOUSTON = "https://ygoprodeck.com/tournament/ycs-houston-5185/"
DELAY = 1.5
_last = [0.0]


def get(url):
    wait = _last[0] + DELAY - time.time()
    if wait > 0:
        time.sleep(wait)
    _last[0] = time.time()
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json, text/html"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read().decode("utf-8", "replace")


def formatlibrary(deck):
    """Best list for the deck's formats and types: an exact deck type in any of its formats first, then a close one."""
    return _formatlibrary(deck, exact_only=True) or _formatlibrary(deck, exact_only=False)


def _formatlibrary(deck, exact_only):
    for fmt in deck["formats"]:
        candidates = []
        for kind in deck["types"]:
            q = urllib.parse.quote(f"type:inc:{kind},format:eq:{fmt}", safe=":,")
            rows = []
            # Best placements, and the oldest lists (classic formats also have modern retro events).
            for sort in ("placement:asc", "publishDate:asc"):
                try:
                    rows += json.loads(get(f"https://www.formatlibrary.com/api/decks?page=1&limit=50&sort={sort}&filter={q}"))
                except Exception as ex:  # noqa: BLE001 - report and keep going
                    print(f"    {fmt} / {kind}: {ex}")
            exact = [r for r in rows if (r.get("deckTypeName") or "").lower() == kind.lower()]
            candidates += exact if exact_only else (exact or rows)
            if exact:
                break
        if not candidates:
            continue
        def rank(r):
            # From the deck's own era (published by the year after), then original Konami events, then placement.
            year = int((r.get("publishDate") or "9999")[:4])
            era = year <= deck["year"] + 1
            konami = (r.get("communityName") or "") == "Konami"
            placement = r.get("placement") or 999
            return (not era, not konami, placement, -(r.get("views") or 0))
        best = sorted(candidates, key=rank)[0]
        full = json.loads(get(f"https://www.formatlibrary.com/api/decks/{best['id']}"))
        event = full.get("event") or {}
        source = (f"Format Library deck {full['id']} (https://www.formatlibrary.com/decks/{full['id']}): "
                  f"{full.get('deckTypeName')}, {ordinal(full.get('placement'))} by {full.get('builderName')} at "
                  f"{event.get('name') or full.get('eventAbbreviation') or 'an event'} ({fmt} format, "
                  f"{(full.get('publishDate') or '')[:10]})")
        return full["ydk"], source
    return None


def houston(deck, page):
    """The best-placed YCS Houston 2026 list whose deck name contains the keyword."""
    keyword = deck["formats"][0].split(":", 1)[1]
    rows = re.findall(r'<a class="tournament_table_row[^"]*"[^>]*href="(/deck/[^"]+)"[^>]*>(.*?)</a>', page, re.S)
    for href, cells in rows:
        if keyword not in href:
            continue
        place = re.search(r"<b>([^<]+)</b>", cells)
        who = re.search(r'class="player-name">([^<]+)<', cells)
        deckpage = get("https://ygoprodeck.com" + href)
        sections = {}
        for name, start in (("main", 'id="main_deck"'), ("extra", 'id="extra_deck"'), ("side", 'id="side_deck"')):
            i = deckpage.find(start)
            if i < 0:
                sections[name] = []
                continue
            seg = deckpage[i:i + 60000]
            ends = [seg.find(x) for x in ('id="extra_deck"', 'id="side_deck"', "Deck Statistics", "deck_stats") if seg.find(x) > 50]
            seg = seg[:min(ends)] if ends else seg
            sections[name] = re.findall(r'href="/card/\?search=(\d+)', seg)
        if not sections["main"]:
            continue
        ydk = "#main\n" + "\n".join(sections["main"]) + "\n#extra\n" + "\n".join(sections["extra"]) + "\n!side\n" + "\n".join(sections["side"]) + "\n"
        placement = html.unescape(place.group(1)).strip() if place else "Top 32"
        player = html.unescape(who.group(1)).strip() if who else "a top-cut player"
        source = f"YGOPRODeck (https://ygoprodeck.com{href}): {placement} by {player} at YCS Houston 2026 (October 17-18, 2026)"
        return ydk, source
    return None


def ordinal(n):
    if not n:
        return "top cut"
    return f"{n}{'th' if 10 <= n % 100 <= 20 else {1: 'st', 2: 'nd', 3: 'rd'}.get(n % 10, 'th')}"


def main():
    only = None
    if "--only" in sys.argv:
        only = set(sys.argv[sys.argv.index("--only") + 1].split(","))
    force = "--force" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    catalog = json.load(open(CATALOG, encoding="utf-8"))
    sources = {}
    if os.path.exists(SOURCES):
        for line in open(SOURCES, encoding="utf-8"):
            m = re.match(r"^- `(\S+)`: (.*)$", line.strip())
            if m:
                sources[m.group(1)] = m.group(2)
    page = None
    missing = []
    for deck in catalog:
        if only and deck["id"] not in only:
            continue
        path = os.path.join(OUT, deck["id"] + ".ydk")
        if os.path.exists(path) and not force:
            continue
        print(f"{deck['year']} {deck['name']}")
        if deck["formats"][0].startswith("houston:"):
            page = page or get(HOUSTON)
            found = houston(deck, page)
        else:
            found = formatlibrary(deck)
        if not found:
            print("    no list found")
            missing.append(deck["id"])
            continue
        ydk, source = found
        body = "\n".join(l for l in ydk.replace("\r", "").split("\n") if not l.startswith("#created"))
        # The game shows the #source line, and real players' names never appear in the game: the builder's name is
        # credited in SOURCES.md only.
        shown = re.sub(r" by .+? at ", " at ", source)
        with open(path, "w", encoding="utf-8") as f:
            f.write(f"#created by Duel Circuit - {deck['name']} ({deck['year']})\n#source {shown}\n{body.strip()}\n")
        sources[deck["id"]] = source
        print("    " + source)
    with open(SOURCES, "w", encoding="utf-8") as f:
        f.write("# Sources of the historic deck lists\n\nReal tournament lists, credited to the players who built them.\n\n")
        for deck in catalog:
            if deck["id"] in sources:
                f.write(f"- `{deck['id']}`: {sources[deck['id']]}\n")
    print(f"\nmissing: {', '.join(missing) if missing else 'none'}")


if __name__ == "__main__":
    main()
