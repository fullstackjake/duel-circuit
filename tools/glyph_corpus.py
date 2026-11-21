#!/usr/bin/env python3
"""Writes unity/Assets/Resources/glyph-corpus.txt: every non-ASCII character the game's own strings use.

Sources: the string and char literals of every .cs file under unity/Assets/Scripts/ (comments are skipped, \\u and \\U
escapes decoded), and every file under campaign/ and personalities/ (whole files: they are all text the game shows or
reads). One character per line, by code point, with the first file (in path order) it came from. Comment lines
"# faux <file>:<line>: ..." list the script lines whose literals hold rich-text <b> or <i> (a faux weight on Barlow;
-typecheck step 6 lists them).

The game's -typecheck asks each font face to draw every character listed here (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md,
section 3). The file's "chars-sha256" line is the SHA-256 of the characters themselves (in order, UTF-8, nothing
between them), so a change to the sources that adds or drops a character makes it stale: the harness's --texts-check
recomputes it from the same sources (src/Ygo.Harness/TextsCheck.cs, GlyphCorpus) and fails until this is rerun.

Usage (from anywhere): python3 tools/glyph_corpus.py [--check]
  --check  writes nothing; exits 1 when the corpus on disk is stale.
"""
import hashlib
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Resources", "glyph-corpus.txt")


def cs_literals(src):
    """The text inside a C# file's string and char literals (comments skipped; \\u, \\U escapes decoded)."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            j = src.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        verbatim = False
        if c in "@$" and i + 1 < n and (src[i + 1] == '"' or (src[i + 1] in "@$" and i + 2 < n and src[i + 2] == '"')):
            prefix = src[i:i + 3] if src[i + 1] in "@$" else src[i:i + 2]
            verbatim = "@" in prefix
            i += len(prefix) - 1  # now at the quote
            c = '"'
        if c == '"' or c == "'":
            quote = c
            i += 1
            while i < n:
                d = src[i]
                if verbatim:
                    if d == '"':
                        if i + 1 < n and src[i + 1] == '"':
                            i += 2
                            continue
                        i += 1
                        break
                    out.append(d)
                    i += 1
                    continue
                if d == "\\" and i + 1 < n:
                    e = src[i + 1]
                    if e == "u" and i + 6 <= n:
                        try:
                            out.append(chr(int(src[i + 2:i + 6], 16)))
                            i += 6
                            continue
                        except ValueError:
                            pass
                    if e == "U" and i + 10 <= n:
                        try:
                            out.append(chr(int(src[i + 2:i + 10], 16)))
                            i += 10
                            continue
                        except ValueError:
                            pass
                    i += 2
                    continue
                if d == quote or d == "\n":
                    i += 1
                    break
                out.append(d)
                i += 1
            continue
        i += 1
    # An escaped surrogate pair ("\\uD83D\\uDCCC") becomes one character, as C# strings hold it; a lone half stays as it is.
    return "".join(out).encode("utf-16-le", "surrogatepass").decode("utf-16-le", "surrogatepass")


def faux_tags():
    """Every line of a script whose string literals hold rich-text <b> or <i> (a faux weight on Barlow): (file, line, text)."""
    found = []
    scripts = os.path.join(ROOT, "unity", "Assets", "Scripts")
    for folder, _, files in os.walk(scripts):
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(folder, name)
            rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
            with open(path, "r", encoding="utf-8", errors="replace") as f:
                for number, line in enumerate(f, 1):
                    literal = cs_literals(line)
                    if "<b>" in literal or "<i>" in literal:
                        found.append((rel, number, line.strip()[:100]))
    return sorted(found)


def sources():
    """(path relative to the repo, text that counts) for every source, in path order."""
    found = []
    scripts = os.path.join(ROOT, "unity", "Assets", "Scripts")
    for folder, _, files in os.walk(scripts):
        for name in files:
            if name.endswith(".cs"):
                found.append((os.path.join(folder, name), True))
    for top in ("campaign", "personalities"):
        for folder, _, files in os.walk(os.path.join(ROOT, top)):
            for name in files:
                found.append((os.path.join(folder, name), False))
    result = []
    for path, code in sorted(found, key=lambda p: os.path.relpath(p[0], ROOT).replace(os.sep, "/")):
        rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            text = f.read()
        result.append((rel, cs_literals(text) if code else text))
    return result


def corpus():
    """{character: first file} for every character above U+007F (the byte-order mark aside)."""
    first = {}
    for rel, text in sources():
        for ch in text:
            if ord(ch) > 0x7F and ch != "﻿" and ch not in first:
                first[ch] = rel
    return first


def render(first):
    chars = sorted(first, key=ord)
    digest = hashlib.sha256("".join(chars).encode("utf-8")).hexdigest()
    lines = [
        "# Glyph corpus: every non-ASCII character in the game's own strings (unity/Assets/Scripts literals, campaign/,",
        "# personalities/), one per line with the first file it came from. Written by tools/glyph_corpus.py: rerun it when",
        "# strings change (--texts-check fails on a stale corpus). Read by the game's -typecheck. Don't edit by hand.",
        f"# chars-sha256: {digest}",
    ]
    lines += [f"# faux {rel}:{number}: {text}" for rel, number, text in faux_tags()]
    lines += [f"{ch}\t{first[ch]}" for ch in chars]
    return "\n".join(lines) + "\n"


def main():
    text = render(corpus())
    if "--check" in sys.argv[1:]:
        try:
            with open(OUT, "r", encoding="utf-8") as f:
                current = f.read()
        except OSError:
            current = ""
        if current != text:
            print("glyph corpus: stale (run python3 tools/glyph_corpus.py)")
            return 1
        print("glyph corpus: current")
        return 0
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(f"glyph corpus: {len([l for l in text.splitlines() if not l.startswith('#')])} characters -> {os.path.relpath(OUT, ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
