using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ygo.Engine.Data
{
    /// <summary>Finds Lua scripts in a CardScripts checkout.</summary>
    public sealed class FileScriptProvider : IScriptProvider
    {
        private readonly string[] _searchDirs;
        private readonly Dictionary<string, byte[]?> _cache = new Dictionary<string, byte[]?>();

        public FileScriptProvider(string cardScriptsRoot)
        {
            // Root holds constant.lua / utility.lua / proc_*.lua; card scripts live in the subfolders.
            _searchDirs = new[] { "", "official", "pre-release", "pre-errata", "goat", "unofficial" }
                .Select(d => Path.Combine(cardScriptsRoot, d))
                .Where(Directory.Exists)
                .ToArray();
        }

        public byte[]? Load(string name)
        {
            // Duels may run on several threads (e.g. the deck tester), so the cache is guarded.
            lock (_cache)
                return LoadLocked(name);
        }

        private byte[]? LoadLocked(string name)
        {
            if (_cache.TryGetValue(name, out var cached)) return cached;
            byte[]? bytes = null;
            foreach (var dir in _searchDirs)
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                {
                    bytes = File.ReadAllBytes(path);
                    break;
                }
            }
            _cache[name] = bytes;
            return bytes;
        }
    }
}
