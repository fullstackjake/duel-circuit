using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.AI;

namespace YgoCampaign.Game
{
    /// <summary>Who plays a tag duel: two teams of a lead and a partner, each a character (or you) with a deck.</summary>
    public sealed class TagLineup
    {
        /// <summary>By slot: 0 your team's lead (-1 = you), 1 your partner, 2 the other team's lead, 3 their partner. Roster indices.</summary>
        public int[] Characters = { -1, 0, 0, 0 };
        public string[] Decks = { "", "", "", "" };
        /// <summary>Your partner's play when it isn't the roster's (a Tag Cup bond's sharper play); null = the roster's.</summary>
        public Ygo.Engine.AI.Personality? PartnerStyle;
        public bool YouPlay => Characters[0] < 0;
    }

    /// <summary>
    /// Free play's Tag Duel setup: you (or a character) and a partner against two duelists, Tag Force style: one shared
    /// field and LP per team, and turns going your lead, theirs, your partner, theirs.
    /// </summary>
    public sealed class TagSetupScreen : MonoBehaviour
    {
        private static readonly string[] SlotTitles = { "YOU", "YOUR PARTNER", "OPPONENT", "THEIR PARTNER" };

        private RectTransform _root = null!, _slots = null!;
        private Text _status = null!;
        private List<Personality> _roster = new List<Personality>();
        private Func<int, bool> _isFree = _ => true;
        private Func<int, List<string>> _decksFor = _ => new List<string>();
        private readonly TagLineup _lineup = new TagLineup();
        private readonly System.Random _rng = new System.Random();

        public event Action<TagLineup>? Start;
        public event Action? Back;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <param name="decksFor">Deck choices for a roster index (-1 = you: every deck).</param>
        public void Init(List<Personality> roster, Func<int, bool> isFree, Func<int, List<string>> decksFor)
        {
            _roster = roster;
            _isFree = isFree;
            _decksFor = decksFor;
            _root = Ui.Overlay(transform, "TagSetup", 34);
            Ui.Image(_root, Ui.Hex("#0B0E15"));
            Ui.Text(Ui.Box(_root, 110, 50, 1200, 90), "TAG DUEL", 60, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_root, 110, 138, 1700, 60),
                "Two against two. Each team shares one field, one Graveyard and 8,000 LP; each duelist has their own Deck and hand. "
                + "Turns go: your team's lead, theirs, your partner, theirs, and around again.", 18, new Color(0.75f, 0.78f, 0.86f), TextAnchor.UpperLeft);
            _slots = Ui.Box(_root, 110, 220, 1700, 640);
            Ui.Text(Ui.Box(_root, 900, 470, 120, 80), "VS", 56, Color.white).fontStyle = FontStyle.Bold;
            Ui.BoxButton(_root, 110, 920, 240, 64, "Back", () => { Close(); Back?.Invoke(); }, 22);
            Ui.BoxButton(_root, 1010, 920, 340, 64, "Random opponents", () => { Randomize(2); Randomize(3); Show(); }, 22);
            var start = Ui.BoxButton(_root, 1370, 920, 440, 64, "Start Tag Duel", () => { Close(); Start?.Invoke(_lineup); }, 26);
            start.targetGraphic.color = Ui.Hex("#B8432F");
            _status = Ui.Text(Ui.Box(_root, 110, 1000, 1700, 40), "", 16, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (_lineup.Decks[0].Length == 0)
            {
                _lineup.Decks[0] = _decksFor(-1)[0];
                for (int s = 1; s < 4; s++) Randomize(s);
            }
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Show();
        }

        public void Close() => _root.gameObject.SetActive(false);

        private void Randomize(int slot)
        {
            var free = new List<int>();
            for (int i = 0; i < _roster.Count; i++)
                if (_isFree(i) && Array.IndexOf(_lineup.Characters, i) < 0) free.Add(i);
            if (free.Count == 0) return;
            _lineup.Characters[slot] = free[_rng.Next(free.Count)];
            _lineup.Decks[slot] = _decksFor(_lineup.Characters[slot])[0];
        }

        private void Cycle(int slot, int delta)
        {
            int n = _roster.Count + 1, pick = _lineup.Characters[slot];
            for (int tries = 0; tries < n; tries++)
            {
                pick = (pick + 1 + delta + n) % n - 1;
                // Only the first slot can be you; every other slot is a duelist open in free play.
                if ((pick < 0 && slot == 0) || (pick >= 0 && _isFree(pick))) break;
            }
            _lineup.Characters[slot] = pick;
            _lineup.Decks[slot] = _decksFor(pick)[0];
            Show();
        }

        private void CycleDeck(int slot, int delta)
        {
            var decks = _decksFor(_lineup.Characters[slot]);
            int i = Math.Max(0, decks.IndexOf(_lineup.Decks[slot]));
            _lineup.Decks[slot] = decks[(i + delta + decks.Count) % decks.Count];
            Show();
        }

        private void Show()
        {
            for (int i = _slots.childCount - 1; i >= 0; i--) Destroy(_slots.GetChild(i).gameObject);
            for (int slot = 0; slot < 4; slot++)
            {
                int s = slot;
                // Your team on the left, theirs on the right; lead above partner.
                float x = slot < 2 ? 0 : 920, y = slot % 2 == 0 ? 0 : 330;
                var panel = Ui.Box(_slots, x, y, 780, 300);
                Ui.Image(panel, Ui.Hex("#161B27"));
                Ui.Image(Ui.Box(panel, 0, 0, 780, 6), slot < 2 ? Ui.Hex("#4C8DFF") : Ui.Hex("#FF5A5A"));
                int c = _lineup.Characters[slot];
                // A portrait column on the left; the seat's controls to its right.
                if (c >= 0) Portraits.Place(Ui.Box(panel, 14, 14, 210, 280), _roster[c].Id, mirror: slot >= 2);
                else Ui.Text(Ui.Box(panel, 14, 14, 210, 280), "YOU", 44, new Color(1, 1, 1, 0.18f)).fontStyle = FontStyle.Bold;
                var who = c < 0 ? "You" : _roster[c].DisplayName;
                var style = c < 0 ? "You play this seat; your partner plays theirs." : _roster[c].StyleSummary;
                const float L = 236, W = 530;
                Ui.Text(Ui.Box(panel, L, 20, W, 30), SlotTitles[slot], 16, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
                Ui.BoxButton(panel, L, 58, 44, 56, "<", () => Cycle(s, -1), 24);
                var whoText = Ui.Text(Ui.Box(panel, L + 50, 58, W - 100, 56), who, who.Length > 30 ? 20 : 25, Color.white);
                whoText.fontStyle = FontStyle.Bold;
                Ui.BoxButton(panel, L + W - 44, 58, 44, 56, ">", () => Cycle(s, +1), 24);
                Ui.Text(Ui.Box(panel, L, 120, W, 28), style, 15, new Color(0.75f, 0.78f, 0.86f));
                Ui.BoxButton(panel, L, 160, 44, 52, "<", () => CycleDeck(s, -1), 24);
                Ui.Text(Ui.Box(panel, L + 50, 160, W - 100, 52), $"Deck: {CampaignState.SlugTitle(_lineup.Decks[slot])}", 21, Color.white);
                Ui.BoxButton(panel, L + W - 44, 160, 44, 52, ">", () => CycleDeck(s, +1), 24);
                if (c >= 0 && _roster[c].Intro.Length > 0)
                    Ui.Text(Ui.Box(panel, L, 226, W, 60), $"“{_roster[c].Intro}”", 15, Ui.Hex("#F2E6B8")).fontStyle = FontStyle.Italic;
            }
            _status.text = _lineup.YouPlay ? "You lead your team. When it's your partner's turn, they play it." : "Watching four duelists.";
        }

        /// <summary>For automated checks: the lineup and starting it.</summary>
        public TagLineup DebugLineup => _lineup;
        public void DebugStart() { Close(); Start?.Invoke(_lineup); }
    }
}
