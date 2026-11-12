namespace YgoCampaign.Game
{
    /// <summary>
    /// The "Import .ydk" button's file dialog (docs/dev/YDK-IMPORT-2026-11-07.md): <see cref="FilePicker"/> asked for one
    /// .ydk file, starting in Downloads. Windows and macOS use the system's dialog; the Unity editor uses its own file panel.
    /// <para>
    /// It never opens in an automated check run, in batch mode, on another platform, or while it's already open:
    /// <see cref="Pick"/> then returns null with the reason, and checks import from a path instead
    /// (DeckEditor.ImportYdkFile, RecipeBookScreen.ImportYdk).
    /// </para>
    /// </summary>
    public static class YdkFilePicker
    {
        /// <summary>For checks: what the last <see cref="Pick"/> refused with ("" when it opened).</summary>
        public static string LastRefusal => FilePicker.LastRefusal;

        /// <summary>
        /// Shows the dialog and returns the chosen file's path; null when the player cancelled or the dialog can't open
        /// (<paramref name="refused"/> says why; null for a plain cancel).
        /// </summary>
        public static string? Pick(out string? refused)
        {
            var files = FilePicker.Pick(new FilePicker.Request
            {
                Title = "Import a .ydk deck list",
                Message = "Choose a .ydk file (deck sites and EDOPro export them).",
                Prompt = "Import",
                FilterName = ".ydk deck lists",
                Extensions = new[] { "ydk" },
            }, out refused);
            return files != null && files.Length > 0 ? files[0] : null;
        }
    }
}
