namespace Ygo.Harness;

/// <summary>
/// Path guards for the replay analyzer and --keep-replays: is a folder inside another (the replays being read, the game's
/// save), with symlinks resolved and, on case-insensitive systems (macOS, Windows), case ignored. Also scrubs the home
/// folder and the project root out of text that goes into a page.
/// </summary>
public static class SafePaths
{
    /// <summary>The game's save folder (the app's mods live under it): never written to. Tests point it at a stand-in.</summary>
    public static string SaveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "YgoCampaign");

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static bool CaseInsensitive => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();
    private static StringComparison Comparison => CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The path with every symlink in its deepest existing ancestor resolved (/tmp becomes /private/tmp on macOS), the
    /// rest appended as given; on a case-insensitive system the existing part takes the disk's own spelling where it can.
    /// </summary>
    public static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "/";
        var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        int i = 0;
        for (; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo? info = Directory.Exists(next) ? new DirectoryInfo(next) : File.Exists(next) ? new FileInfo(next) : null;
            if (info == null) break;
            // The entry's own spelling on disk (a case-insensitive lookup can be given any case).
            if (CaseInsensitive && Directory.Exists(current))
            {
                var actual = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(e => string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
                if (actual != null) next = actual;
                info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            }
            if (info.LinkTarget != null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                // A link's target may itself sit under links (or be relative): canonicalise it in turn.
                next = target != null ? Canonical(target.FullName) : next;
            }
            current = next;
        }
        for (; i < parts.Length; i++) current = Path.Combine(current, parts[i]);
        return current.Length > root.Length ? current.TrimEnd(Path.DirectorySeparatorChar) : current;
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsInside(string path, string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        var p = Canonical(path) + Path.DirectorySeparatorChar;
        var f = Canonical(folder) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, Comparison);
    }

    /// <summary>Text for a page: the project root and the home folder replaced by neutral names.</summary>
    public static string Scrub(string text, string projectRoot)
    {
        foreach (var (prefix, name) in new[] { (projectRoot, "<project>"), (Canonical(projectRoot), "<project>"), (Home, "~"), (Canonical(Home), "~") })
            if (prefix.Length > 1) text = text.Replace(prefix, name, Comparison);
        return text;
    }
}
