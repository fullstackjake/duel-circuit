using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// All decks the game knows: the built-in ones (read-only) and the player's own in mods/decks (editable).
    /// A player deck with the same name as a built-in one replaces it, so editing a built-in deck saves a copy.
    /// </summary>
    public sealed class DeckLibrary
    {
        private readonly string _builtIn;
        private readonly string _own;
        private readonly Dictionary<string, string> _paths = new Dictionary<string, string>();
        private readonly HashSet<string> _ownNames = new HashSet<string>();
        private readonly HashSet<string> _campaignNames = new HashSet<string>();

        public DeckLibrary(string builtInFolder, string ownFolder)
        {
            _builtIn = builtInFolder;
            _own = ownFolder;
            Directory.CreateDirectory(_own);
            Refresh();
        }

        public string OwnFolder => _own;

        public IReadOnlyList<string> Names => _paths.Keys.OrderBy(n => n).ToList();

        public void Refresh()
        {
            _paths.Clear();
            _ownNames.Clear();
            _campaignNames.Clear();
            foreach (var folder in new[] { _builtIn, _own }) // own decks last, so they win on a name clash
            {
                if (!Directory.Exists(folder)) continue;
                // Built-in decks may sit in subfolders (decks/history); a deck's name is its file name.
                foreach (var file in Directory.GetFiles(folder, "*.ydk", folder == _builtIn ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    _paths[name] = file;
                    if (folder == _own)
                    {
                        _ownNames.Add(name);
                        if (IsCampaignFile(file)) _campaignNames.Add(name);
                    }
                }
            }
        }

        /// <summary>The structure decks sold in the campaign's Card Shop (built-in decks/structure).</summary>
        public IReadOnlyList<string> StructureNames =>
            _paths.Where(p => Path.GetFileName(Path.GetDirectoryName(p.Value)) == "structure" && !_ownNames.Contains(p.Key))
                .Select(p => p.Key).OrderBy(n => n).ToList();

        public bool Contains(string name) => _paths.ContainsKey(name);

        /// <summary>True if the deck is the player's own (saved in mods/decks), so it can be deleted.</summary>
        public bool IsOwn(string name) => _ownNames.Contains(name);

        /// <summary>A deck you built in the campaign (from your collection). Anyone can play it; only the campaign edits it.</summary>
        public bool IsCampaign(string name) => _campaignNames.Contains(name);

        public IReadOnlyList<string> CampaignNames => _campaignNames.OrderBy(n => n).ToList();

        private static bool IsCampaignFile(string path)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("#campaign")) return true;
                if (line.StartsWith("#main")) return false;
            }
            return false;
        }

        public Deck Load(string name) => Deck.LoadYdk(_paths[name]);

        /// <summary>Saves to mods/decks/&lt;name&gt;.ydk (a built-in deck of the same name is then replaced in the game).</summary>
        public void Save(Deck deck)
        {
            var path = Path.Combine(_own, SafeName(deck.Name) + ".ydk");
            File.WriteAllText(path, DeckCodes.ToYdk(deck));
            Refresh();
        }

        /// <summary>Deletes one of the player's own decks. Built-in decks can't be deleted.</summary>
        public bool Delete(string name)
        {
            if (!IsOwn(name)) return false;
            File.Delete(_paths[name]);
            Refresh();
            return true;
        }

        /// <summary>A deck name that is safe as a file name.</summary>
        public static string SafeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '-' : c).ToArray());
            return clean.Length == 0 ? "new deck" : clean;
        }
    }
}
