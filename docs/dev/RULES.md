# Official rules: where to check

A local copy of Konami's rulebook lives in `rules/` (git-ignored: it's Konami's document, not ours to redistribute):
- `rules/Rulebook_v9_en.pdf`, the Yu-Gi-Oh! TCG Rulebook v9, from
  https://www.yugioh-card.com/eu/wp-content/uploads/2022/07/Rulebook_v9_en.pdf
- `rules/Rulebook_v9_en.txt`, its text, for grep (`grep -n -i shuffl rules/Rulebook_v9_en.txt`)
- `rules/2021-rules-update.html`, the 2021 rules update page (Link Monster Zones and the Pendulum Zone changes the v9
  book predates), from https://www.yugioh-card.com/eu/play/2021-rules-update/

To refresh it on another machine: download the two URLs above into `rules/`; the text is extracted with PDFKit
(`swift` script: PDFDocument(url:).page(at:).string for each page).

Card text overrides the rulebook. Rulings for specific cards come from Konami's card database FAQ
(db.yugioh-card.com) or judges; community wikis are secondary sources.

Rules checked so far: deck shuffling ([SHUFFLE-2026-11-07.md](SHUFFLE-2026-11-07.md)).
