# Authored by the Level Designer (2026-11-25): style, summon mechanic, easy-to-pilot, era year for era-less decks.
# OPP = may sit in a seat (history deck with a same-id character). WALL = shop wall / start offer only (no character).
T = {
 # slug: (style, mechanic, easy)
 "tearlaments": ("graveyard", "fusion", 0), "chaos-ritual": ("combo", "ritual", 0),
 "ishizu-tearlaments": ("graveyard", "fusion", 0), "ryzeal": ("combo", "xyz", 0),
 "dark-magician-chaos-ritual": ("midrange", "ritual", 0), "branded-despia": ("midrange", "fusion", 0),
 "kashtira": ("stun", "xyz", 0), "angelechy": ("control", "synchro", 0),
 "light-and-darkness": ("midrange", "ritual", 0), "eldlich": ("control", "main", 1),
 "mitsurugi": ("midrange", "ritual", 0), "thunder-dragon": ("midrange", "fusion", 0),
 "spright": ("combo", "link", 0), "bystial-dragon-link": ("combo", "link", 0),
 "tenpai-dragon": ("beatdown", "synchro", 1), "radiant-typhoon": ("control", "main", 0),
 "k9-vanquish-soul": ("midrange", "xyz", 0), "sacred-beast": ("beatdown", "main", 1),
 "fire-king-snake-eye": ("combo", "link", 0), "yubel": ("burn", "fusion", 0),
 "magistus-fairy-tail": ("midrange", "fusion", 0), "blitzclique": ("beatdown", "fusion", 1),
 "danger-dark-world": ("graveyard", "link", 0), "artmage": ("combo", "fusion", 0),
 "kozmo": ("beatdown", "main", 1), "snake-eye": ("combo", "link", 0),
 "unchained": ("combo", "link", 0), "shaddoll": ("control", "fusion", 0),
 "abc": ("midrange", "fusion", 0), "mermail-atlantean": ("midrange", "xyz", 0),
 "drytron": ("combo", "ritual", 0), "lunalight": ("beatdown", "fusion", 0),
 "elfnote": ("combo", "synchro", 0), "dracotail": ("midrange", "fusion", 0),
 "power-patron": ("combo", "pendulum", 0), "exosister": ("stun", "xyz", 0),
 "wind-up": ("stun", "xyz", 0), "adamancipator": ("combo", "synchro", 0),
 "dragon-ruler": ("midrange", "xyz", 1), "six-samurai": ("beatdown", "synchro", 1),
 "nekroz": ("combo", "ritual", 0), "tri-brigade": ("graveyard", "link", 0),
 "hat": ("stun", "xyz", 0), "orcust": ("graveyard", "link", 0),
 "spyral": ("combo", "link", 0), "teledad": ("midrange", "synchro", 1),
 "dino-rabbit": ("beatdown", "xyz", 1), "true-draco": ("stun", "tribute", 0),
 "blackwing": ("beatdown", "synchro", 1), "plant-synchro": ("graveyard", "synchro", 0),
 "inzektor": ("beatdown", "xyz", 1), "chaos-yata": ("stun", "main", 0),
 "performapal": ("combo", "pendulum", 0), "goat-control": ("control", "fusion", 0),
 "sky-striker": ("control", "link", 0), "maliss": ("combo", "link", 0),
 "floowandereeze": ("stun", "tribute", 0), "satellarknight": ("midrange", "xyz", 1),
 "salamangreat": ("midrange", "link", 0), "pendulum-magician": ("combo", "pendulum", 0),
 "lightsworn": ("graveyard", "synchro", 0), "infernity": ("combo", "synchro", 0),
 "burning-abyss": ("graveyard", "xyz", 0), "zoodiac": ("combo", "xyz", 0),
 "gouki": ("combo", "link", 0), "altergeist": ("control", "link", 0),
 "qliphort": ("stun", "pendulum", 0), "runick-spright": ("control", "link", 0),
 "domain-monarch": ("stun", "tribute", 0), "yummy": ("control", "synchro", 0),
 "spellbook": ("control", "main", 0), "purrely": ("midrange", "xyz", 0),
 # wall-only (no same-id character)
 "mekk-knight-invoked": ("midrange", "fusion", 0), "synchron": ("combo", "synchro", 0),
 "mekk-knight": ("midrange", "link", 0), "gadgets": ("midrange", "main", 1),
 "machine-re-volt": ("beatdown", "fusion", 1), "dragunity-legion": ("combo", "synchro", 0),
 "modern": ("midrange", "link", 0), "warriors": ("beatdown", "main", 1),
 "spellcasters-command": ("midrange", "main", 1), "zombies": ("graveyard", "main", 1),
 "synchron-extreme": ("combo", "synchro", 0), "classic": ("beatdown", "main", 1),
 "fury-from-the-deep": ("beatdown", "main", 1), "dinosaurs-rage": ("beatdown", "main", 1),
 "lost-sanctuary": ("control", "main", 0),
}
# Era-less root decks: the year of the circuit that first uses them (circuits.txt era lines) or their card pool.
ERA_FALLBACK = {"classic": 2005, "warriors": 2005, "zombies": 2005, "gadgets": 2005, "modern": 2019, "synchron": 2009}
EXCLUDED = {"frog-ftk": "Elo 1054, 285 under the next deck; a turn-1 burn loop, not a duel",
            "geargia": "16.9-turn average, 0 uses, trap stall",
            "stall-burn": "18.6-turn average; the Alley already skips it (Goat Control mirror past turn 40); no character"}
CHAMPIONS = ["spyral", "tri-brigade", "nekroz", "power-patron", "dracotail", "elfnote", "drytron", "mermail-atlantean"]
CHAMPIONS_LATER = ["lunalight", "artmage"]
