#!/usr/bin/env python3
"""
Checks the fetched historic decks against the engine's card data and fixes card ids where it can:
  - a card id the engine doesn't know is mapped by name (YGOPRODeck's card data, data/ygoprodeck/cardinfo.json) to the
    id the engine uses, e.g. a real password to EDOPro's temporary pre-release id for the newest cards;
  - reports cards with no script, decks under 40 cards, and more than 3 copies of a card;
  - the `#originals` lint (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md S3): a deck under decks/ with `#originals` and `#banlist`
    uses an original version id (GOAT or Pre-Errata) for every card whose campaign/texts.txt row plays as printed at
    that list's texts date, except the ids it names on a `#current <id> ...` line.
Run from the repo root:  python3 tools/check_history_decks.py [--fix]
"""
import glob
import json
import os
import re
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SKIP = ("rush", "skill", "unofficial", "goat")


def engine_cards():
    by_id, by_name = {}, {}
    for f in sorted(glob.glob(os.path.join(ROOT, "vendor", "BabelCDB", "*.cdb"))):
        if any(s in os.path.basename(f) for s in SKIP):
            continue
        con = sqlite3.connect(f)
        for cid, name, alias, kind in con.execute("select t.id, t.name, d.alias, d.type from texts t join datas d on d.id = t.id"):
            by_id[cid] = (name, alias, kind)
            # Prefer a real (official) id over a temporary pre-release one for the same name.
            if name.lower() not in by_name or not os.path.basename(f).startswith("prerelease"):
                by_name.setdefault(name.lower(), cid)
    # Original card versions the engine also loads (the 2004-2005 decks use them): Goat-format entries, and the
    # pre-errata entries of the unofficial database. Ids only; names stay mapped to the current versions.
    for f, where in (("goat-entries.cdb", ""), ("cards-unofficial.cdb", " where t.name like '%(Pre-Errata)'")):
        path = os.path.join(ROOT, "vendor", "BabelCDB", f)
        if os.path.exists(path):
            con = sqlite3.connect(path)
            for cid, name, alias, kind in con.execute("select t.id, t.name, d.alias, d.type from texts t join datas d on d.id = t.id" + where):
                by_id.setdefault(cid, (name, alias, kind))
    return by_id, by_name


def scripts():
    found = set()
    for root, _, files in os.walk(os.path.join(ROOT, "vendor", "CardScripts")):
        for f in files:
            if f.startswith("c") and f.endswith(".lua"):
                found.add(f[1:-4])
    return found


def era_banlists():
    """List name -> {card id: copies}: the historic lists in lflists/history.lflist.conf."""
    lists, current = {}, None
    path = os.path.join(ROOT, "lflists", "history.lflist.conf")
    if not os.path.exists(path):
        return {}
    for line in open(path, encoding="utf-8"):
        line = line.strip()
        if line.startswith("!"):
            current = lists.setdefault(line[1:], {})
        elif current is not None and line[:1].isdigit():
            code, copies = line.split()[:2]
            current[int(code)] = int(copies)
    return lists


MONTHS = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"]
LATEST = "9999-12"
GOAT_FROM, GOAT_UNTIL = "2005-04", "2005-09"  # TextRules.GoatFrom, GoatUntil


def list_date(name):
    """A list's month "yyyy-mm" from its name, as Banlist.Date reads it ("TCG March 2011", "2005.4 GOAT"); "" if none."""
    lower = name.lower()
    dotted = re.search(r"\b(\d{4})\.(\d{1,2})\b", lower)
    if dotted:
        return f"{dotted.group(1)}-{int(dotted.group(2)):02d}"
    year = re.search(r"\b(19|20)\d{2}\b", lower)
    if not year:
        return ""
    month = next((i + 1 for i, m in enumerate(MONTHS) if m in lower), 12)
    return f"{year.group(0)}-{month:02d}"


def all_list_names():
    names = []
    for folder in ("lflists", os.path.join("vendor", "LFLists")):
        for path in sorted(glob.glob(os.path.join(ROOT, folder, "*.conf"))):
            names += [l.strip()[1:].strip() for l in open(path, encoding="utf-8") if l.startswith("!")]
    return names


def texts_date(name, names):
    """TextRules.ForList for a list: the GOAT list is 2005-09; else the month the next TCG list took effect (today for the newest),
    but no later than 2005-09 for a list dated in the Goat window (2005-04..2005-09: the history skips to TCG September 2008)."""
    if "GOAT" in name:
        return GOAT_UNTIL
    date = list_date(name)
    if not date:
        return LATEST
    later = sorted(d for d in (list_date(n) for n in names if "TCG" in n) if d and d > date)
    end = later[0] if later else LATEST
    if GOAT_FROM <= date <= GOAT_UNTIL:
        end = min(end, GOAT_UNTIL)
    return end


def text_rows():
    """campaign/texts.txt: base-less rows {version id: (until, collectible)}; None when the file isn't there."""
    path = os.path.join(ROOT, "campaign", "texts.txt")
    if not os.path.exists(path):
        return None
    rows = {}
    for raw in open(path, encoding="utf-8"):
        words = raw.split("#")[0].split()
        if not words or not words[0].isdigit():
            continue
        until = words[words.index("until") + 1] if "until" in words and words.index("until") + 1 < len(words) else ""
        collectible = bool(re.fullmatch(r"\d{4}-(0[1-9]|1[0-2])", until)) and not {"cpu", "never", "dup"} & set(words)
        rows[int(words[0])] = (until, collectible)
    return rows


def originals_lint(by_id):
    """Lint hits for every deck with `#originals` (S3); prints them and returns their number."""
    rows = text_rows()
    print("\n#originals lint:")
    if rows is None:
        print("  (no campaign/texts.txt yet: skipped)")
        return 0
    names = all_list_names()
    open_until = {}  # base id -> the latest `until` of its collectible rows
    for cid, (until, collectible) in rows.items():
        if collectible and cid in by_id:
            base = by_id[cid][1] or cid
            open_until[base] = max(open_until.get(base, ""), until)
    hits = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "decks", "**", "*.ydk"), recursive=True)):
        lines = [l.strip() for l in open(path, encoding="utf-8")]
        if not any(l.startswith("#originals") for l in lines):
            continue
        name = os.path.relpath(path, os.path.join(ROOT, "decks"))
        banlist = next((l[9:].strip() for l in lines if l.startswith("#banlist ")), None)
        if not banlist or banlist not in names:
            print(f"  {name}: #originals with no installed #banlist")
            hits += 1
            continue
        asof = texts_date(banlist, names)
        kept = {int(w) for l in lines if l.startswith("#current ") for w in l.split()[1:] if w.isdigit()}
        found = []
        for cid in dict.fromkeys(int(l) for l in lines if l.isdigit()):
            if cid not in by_id or by_id[cid][0].endswith(("(GOAT)", "(Pre-Errata)")):
                continue
            base = by_id[cid][1] or cid
            if cid in kept or base in kept or open_until.get(base, "") <= asof:
                continue
            found.append(f"{by_id[cid][0]} ({cid}) on its current id; its original plays under {banlist} (texts of {asof})")
        for f in found:
            print(f"  {name}: {f}")
        if not found:
            print(f"  {name}: ok ({banlist}, texts of {asof})")
        hits += len(found)
    return hits


def main():
    fix = "--fix" in sys.argv
    by_id, by_name = engine_cards()
    eras = era_banlists()
    have_script = scripts()
    info = json.load(open(os.path.join(ROOT, "data", "ygoprodeck", "cardinfo.json"), encoding="utf-8"))["data"]
    ygo_name = {}
    for card in info:
        for image in card.get("card_images", []):
            ygo_name[image["id"]] = card["name"]
    problems_total = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "decks", "history", "*.ydk"))):
        lines = open(path, encoding="utf-8").read().split("\n")
        out, problems, mapped, section, counts = [], [], 0, None, {}
        for line in lines:
            s = line.strip()
            if s.startswith("#main") or s.startswith("#extra") or s.startswith("!side"):
                section = s
            if s.isdigit():
                cid = int(s)
                if cid not in by_id:
                    name = ygo_name.get(cid)
                    new = by_name.get(name.lower()) if name else None
                    if new:
                        cid, mapped = new, mapped + 1
                    else:
                        problems.append(f"unknown card {s}" + (f" ({name})" if name else ""))
                if cid in by_id:
                    base = by_id[cid][1] or cid
                    counts[base] = counts.get(base, 0) + 1
                    script_id = cid if str(cid) in have_script else base
                    is_normal = (by_id[cid][2] & 0x10) and not (by_id[cid][2] & 0x20)  # Normal, non-Effect monster
                    if str(script_id) not in have_script and not is_normal:
                        # Normal monsters have no script by design; anything else does nothing in a duel.
                        problems.append(f"no script: {by_id[cid][0]} ({cid})")
                out.append(str(cid))
            else:
                out.append(line)
        text = "\n".join(out)
        main_count = sum(1 for l in text.split("#extra")[0].split("\n") if l.strip().isdigit())
        if main_count < 40:
            problems.append(f"Main Deck has {main_count} cards")
        for base, n in counts.items():
            if n > 3:
                problems.append(f"{n} copies of {by_id[base][0] if base in by_id else base}")
        name = os.path.basename(path)
        era = next((l[9:].strip() for l in lines if l.startswith("#banlist ")), None)
        if era and era not in eras:
            problems.append(f"unknown ban list {era}")
        elif era:
            for base, n in counts.items():
                allowed = eras[era].get(base, 3)
                if n > allowed:
                    problems.append(f"{n} copies of {by_id[base][0] if base in by_id else base} ({era} allows {allowed})")
        if fix and mapped:
            open(path, "w", encoding="utf-8").write(text)
        status = "ok" if not problems else "; ".join(sorted(set(problems)))
        print(f"{name:34} main {main_count:2}  mapped {mapped:2}  {status}")
        problems_total += len(set(problems))
    problems_total += originals_lint(by_id)
    print(f"\n{problems_total} problems")


if __name__ == "__main__":
    main()
