using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The license texts and notices the About screen shows (AGPL section 5(d): how to view a copy of the license).
    /// A built game carries them in StreamingAssets/licenses (ProjectSetup copies them: LICENSE.txt, LICENSING.md,
    /// THIRD_PARTY_NOTICES.md, CC-BY-SA-4.0.txt, UNITY-PERMISSION.md, SOURCE.txt); in the Unity editor they're read from the
    /// repo checkout (LICENSE, LICENSES/...). A file that isn't there reads as a line saying where to find it online.
    /// </summary>
    public static class LicenseFiles
    {
        public const string SourceUrl = "https://github.com/fullstackjake/duel-circuit";

        public enum Doc { License, UnityPermission, ThirdParty, Licensing, CcBySa, Source }

        /// <summary>For checks: read from this folder instead (null: the game's own).</summary>
        public static string? FolderOverride;

        private static string Streaming => Path.Combine(Application.streamingAssetsPath, "licenses");
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        /// <summary>The folder the files are read from (it may not exist: then every file reads as missing).</summary>
        public static string Folder
        {
            get
            {
                if (FolderOverride != null) return FolderOverride;
                if (Directory.Exists(Streaming)) return Streaming;
                // The editor has no StreamingAssets copy: the repo checkout one level above the Unity project.
                if (Application.isEditor && File.Exists(Path.Combine(RepoRoot, "LICENSE"))) return RepoRoot;
                return Streaming;
            }
        }

        /// <summary>The names a document goes by: the build's copy first, then the repo's.</summary>
        private static string[] Names(Doc doc) => doc switch
        {
            Doc.License => new[] { "LICENSE.txt", "LICENSE" },
            Doc.UnityPermission => new[] { "UNITY-PERMISSION.md", Path.Combine("LICENSES", "UNITY-PERMISSION.md") },
            Doc.ThirdParty => new[] { "THIRD_PARTY_NOTICES.md" },
            Doc.Licensing => new[] { "LICENSING.md" },
            Doc.CcBySa => new[] { "CC-BY-SA-4.0.txt", Path.Combine("LICENSES", "CC-BY-SA-4.0.txt") },
            _ => new[] { "SOURCE.txt" },
        };

        /// <summary>Where the document is online (the repo's main branch).</summary>
        public static string OnlineUrl(Doc doc) => doc switch
        {
            Doc.License => SourceUrl + "/blob/main/LICENSE",
            Doc.UnityPermission => SourceUrl + "/blob/main/LICENSES/UNITY-PERMISSION.md",
            Doc.ThirdParty => SourceUrl + "/blob/main/THIRD_PARTY_NOTICES.md",
            Doc.Licensing => SourceUrl + "/blob/main/LICENSING.md",
            Doc.CcBySa => SourceUrl + "/blob/main/LICENSES/CC-BY-SA-4.0.txt",
            _ => SourceUrl,
        };

        /// <summary>The document's file, or null when this copy of the game doesn't have it.</summary>
        public static string? Find(Doc doc)
        {
            var folder = Folder;
            foreach (var name in Names(doc))
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// The document as plain text to read on screen (Markdown made plain), with <paramref name="found"/> false and a
        /// line saying where to read it online when the file isn't there or can't be read.
        /// </summary>
        public static string Read(Doc doc, out bool found)
        {
            found = false;
            var path = Find(doc);
            string text;
            try { text = path != null ? File.ReadAllText(path) : ""; }
            catch (IOException) { path = null; text = ""; }
            catch (UnauthorizedAccessException) { path = null; text = ""; }
            if (path == null)
                return $"{Names(doc)[0]} isn't in this copy of the game. Read it online: {OnlineUrl(doc)}";
            found = true;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").Replace("\f", "");
            return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? MarkdownToPlain(text) : text.TrimEnd();
        }

        /// <summary>
        /// The build's "Built from commit ..." line (SOURCE.txt), with the commit cut to 7 characters; null without one.
        /// </summary>
        public static string? BuiltFrom()
        {
            var path = Find(Doc.Source);
            if (path == null) return null;
            try
            {
                foreach (var line in File.ReadAllLines(path))
                    if (line.StartsWith("Built from commit ", StringComparison.Ordinal))
                        return Regex.Replace(line.Trim(), @"\b([0-9a-f]{7})[0-9a-f]{33}\b", "$1");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        private static readonly Regex Image = new Regex(@"!\[([^\]]*)\]\([^)]*\)");
        private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\(([^)\s]+)\)");
        private static readonly Regex Bold = new Regex(@"(\*\*|__)(.+?)\1");
        private static readonly Regex Italic = new Regex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])");
        private static readonly Regex Code = new Regex(@"`([^`]*)`");
        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+");
        private static readonly Regex Numbered = new Regex(@"^\s*\d+\.\s+");
        private static readonly Regex TableRule = new Regex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$");

        /// <summary>
        /// Markdown as readable plain text, no engine needed: headings without their #s (the top two levels in capitals),
        /// bold, italics and code marks dropped, links as their words (with the address when it's a web one), bullets as •,
        /// a table's rows as their cells joined by " · " (its rule line dropped), quotes indented, code blocks as written,
        /// and a paragraph's hard-wrapped lines joined into one.
        /// </summary>
        public static string MarkdownToPlain(string markdown)
        {
            // Lines out; a paragraph's (or list item's, or quote's) hard-wrapped lines are joined into one, so the reader
            // wraps them to its own width. Their inline marks are made plain once joined (a link can span two lines).
            var lines = new List<(string text, bool marks, string indent)>();
            string? open = null; // "para" or "quote": the last line takes the next plain line onto its end
            void Add(string line, string? kind, bool marks = false, string indent = "") { lines.Add((line, marks, indent)); open = kind; }
            void Join(string more) { var last = lines[lines.Count - 1]; lines[lines.Count - 1] = (last.text + " " + more, last.marks, last.indent); }
            void Blank() { if (lines.Count > 0 && lines[lines.Count - 1].text.Length > 0) lines.Add(("", false, "")); open = null; }
            bool code = false;
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw;
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { code = !code; open = null; continue; }
                if (code) { Add(line, null); continue; }
                if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (line.Trim().Length == 0) { Blank(); continue; }
                if (TableRule.IsMatch(line) && line.Contains("-")) continue;
                var heading = Heading.Match(line);
                if (heading.Success)
                {
                    var words = Inline(heading.Groups[2].Value);
                    Blank();
                    Add(heading.Groups[1].Value.Length <= 2 ? words.ToUpperInvariant() : words, null);
                    Blank();
                    continue;
                }
                if (line.TrimStart().StartsWith("|", StringComparison.Ordinal))
                {
                    var cells = new List<string>();
                    foreach (var cell in SplitRow(line.Trim()))
                    {
                        var words = Inline(cell.Trim());
                        if (words.Length > 0 && words != "—" && words != "-") cells.Add(words);
                    }
                    Add(string.Join(" · ", cells), null);
                    continue;
                }
                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    while (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        line = line.TrimStart().Substring(1);
                        if (line.StartsWith(" ")) line = line.Substring(1);
                    }
                    if (line.Trim().Length == 0) { Blank(); continue; } // a ">" line on its own: between two quoted paragraphs
                    if (open == "quote") Join(line.Trim());
                    else Add(line.Trim(), "quote", marks: true, indent: "    ");
                    continue;
                }
                if (Bullet.IsMatch(line) || Numbered.IsMatch(line))
                {
                    Add(Bullet.Replace(line, m => m.Groups[1].Value + "• "), "para", marks: true);
                    continue;
                }
                if (open == "para") Join(line.Trim());
                else Add(line, "para", marks: true);
            }
            return string.Join("\n", lines.Select(l => l.indent + (l.marks ? Inline(l.text) : l.text))).Trim('\n');
        }

        /// <summary>A table row's cells (an escaped \| stays in its cell).</summary>
        private static IEnumerable<string> SplitRow(string row)
        {
            if (row.StartsWith("|")) row = row.Substring(1);
            if (row.EndsWith("|") && !row.EndsWith("\\|")) row = row.Substring(0, row.Length - 1);
            var cell = new StringBuilder();
            for (int i = 0; i < row.Length; i++)
            {
                if (row[i] == '\\' && i + 1 < row.Length && row[i + 1] == '|') { cell.Append('|'); i++; continue; }
                if (row[i] == '|') { yield return cell.ToString(); cell.Clear(); continue; }
                cell.Append(row[i]);
            }
            yield return cell.ToString();
        }

        /// <summary>One line's inline marks made plain.</summary>
        private static string Inline(string text)
        {
            text = Image.Replace(text, "$1");
            text = Link.Replace(text, m =>
            {
                var words = m.Groups[1].Value;
                var url = m.Groups[2].Value;
                bool web = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                // A web link keeps its address (unless the words are the address); a link inside the repo is just its words.
                return web && words.Trim('`') != url ? $"{words} ({url})" : words;
            });
            text = Code.Replace(text, "$1");
            text = Bold.Replace(text, "$2");
            text = Italic.Replace(text, "$1");
            // <https://...> autolinks, and backslash escapes.
            text = Regex.Replace(text, @"<(https?://[^>\s]+)>", "$1");
            text = Regex.Replace(text, @"\\([\\`*_{}\[\]()#+\-.!|>])", "$1");
            return text;
        }
    }
}
