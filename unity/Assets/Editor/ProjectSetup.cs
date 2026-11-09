using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using YgoCampaign.Game;

/// <summary>
/// Menu items (and batch-mode entry points) that create the main scene and build the macOS app and the Windows game.
/// Batch mode: Unity -batchmode -quit -projectPath unity -executeMethod ProjectSetup.BuildMac (or BuildWindows)
/// </summary>
public static class ProjectSetup
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    [MenuItem("YgoCampaign/Create Main Scene")]
    public static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);

        new GameObject("Duel").AddComponent<DuelController>();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"Created {ScenePath}");
    }

    private const string NativePluginPath = "Assets/Plugins/macOS/libocgcore.dylib";

    /// <summary>Marks the rules engine dylib as usable in the Editor and in macOS builds (Apple silicon).</summary>
    [MenuItem("YgoCampaign/Configure Native Plugin")]
    public static void ConfigureNativePlugin()
    {
        var importer = (PluginImporter)AssetImporter.GetAtPath(NativePluginPath);
        importer.SetCompatibleWithAnyPlatform(false);
        importer.SetCompatibleWithEditor(true);
        importer.SetEditorData("OS", "OSX");
        importer.SetEditorData("CPU", "ARM64");
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
        importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "ARM64");
        importer.SaveAndReimport();
        Debug.Log($"Configured {NativePluginPath}");
    }

    private const string WindowsPluginPath = "Assets/Plugins/Windows/x86_64/ocgcore.dll";

    /// <summary>
    /// Marks the Windows rules engine (built on the Mac with `tools/build_core.sh --windows`) as usable in 64-bit Windows
    /// builds only: the editor runs on the Mac and loads the dylib.
    /// </summary>
    [MenuItem("YgoCampaign/Configure Windows Plugin")]
    public static void ConfigureWindowsPlugin()
    {
        var importer = (PluginImporter)AssetImporter.GetAtPath(WindowsPluginPath);
        importer.SetCompatibleWithAnyPlatform(false);
        importer.SetCompatibleWithEditor(false);
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
        importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
        importer.SaveAndReimport();
        Debug.Log($"Configured {WindowsPluginPath}");
    }

    /// <summary>Batch-mode check that the editor can load the native engine.</summary>
    public static void VerifyEngineInEditor()
    {
        ConfigureNativePlugin();
        Debug.Log($"ocgcore loaded in editor: version {Ygo.Engine.Duel.CoreVersion}");
    }

    private const string AppIconPath = "Assets/Art/AppIcon.png";

    /// <summary>The game's bundle id (its preferences domain), and a check build's: checks never write the player's settings.</summary>
    public const string BundleId = "com.ygocampaign.game", QaBundleId = "com.ygocampaign.game.qa";

    [MenuItem("YgoCampaign/Build macOS App")]
    public static void BuildMac()
    {
        ConfigureNativePlugin();
        // libocgcore.dylib is built for Apple silicon only.
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
        // The log goes to ~/Library/Logs/YgoCampaign/Duel Circuit/Player.log. Saves and mods stay in ~/Library/Application
        // Support/YgoCampaign/YgoCampaign/mods whatever the name (DuelController pins that folder).
        var output = Build(BuildTarget.StandaloneOSX, Path.Combine("build", "unity", "Duel Circuit.app"));
        if (output == null) return;
        CopyData(Path.Combine(output, "Contents", "Resources", "Data", "StreamingAssets"));
        Debug.Log($"Built {output}");
    }

    /// <summary>
    /// The 64-bit Windows game (build/windows/Duel Circuit.exe and its Duel Circuit_Data folder), built on the Mac. Zip the
    /// whole folder to copy it to a PC. Saves and mods go to %USERPROFILE%\AppData\LocalLow\YgoCampaign\Duel Circuit\mods
    /// (Unity's persistentDataPath); the log is Player.log in that folder's parent. Batch mode:
    /// Unity -batchmode -quit -projectPath unity -executeMethod ProjectSetup.BuildWindows
    /// </summary>
    [MenuItem("YgoCampaign/Build Windows Game")]
    public static void BuildWindows()
    {
        ConfigureWindowsPlugin();
        var output = Build(BuildTarget.StandaloneWindows64, Path.Combine("build", "windows", "Duel Circuit.exe"));
        if (output == null) return;
        CopyData(Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "_Data", "StreamingAssets"));
        Debug.Log($"Built {output}");
    }

    /// <summary>
    /// Builds the player for <paramref name="target"/> at <paramref name="defaultOutput"/> (from the repo root, unless
    /// `-buildout` says where); the full output path, or null when the build failed.
    /// </summary>
    private static string? Build(BuildTarget target, string defaultOutput)
    {
        // The generated card data isn't committed: without it the game builds but can't start a duel (docs/BUILDING.md).
        foreach (var data in new[] { "cards.bin", "printings.bin" })
            if (!File.Exists(Path.Combine(Application.streamingAssetsPath, data)))
            {
                Debug.LogError($"Build stopped: Assets/StreamingAssets/{data} is missing. Export it with the harness first (docs/BUILDING.md).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return null;
            }
        if (!File.Exists(ScenePath)) CreateMainScene();

        // The product name is the app's name and window title.
        PlayerSettings.companyName = "YgoCampaign";
        PlayerSettings.productName = "Duel Circuit";
        PlayerSettings.bundleVersion = "0.1.1";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, BundleId);
        // A window you can drag to any size (every canvas scales with Expand, keeping the whole 1920x1080 layout in view).
        PlayerSettings.resizableWindow = true;

        // The app icon: the logo on a square transparent canvas (Assets/Art/AppIcon.png, made from art/logo/logo.png).
        AssetDatabase.ImportAsset(AppIconPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath) is Texture2D icon)
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

        // `-buildout <path>` (from the repo root): build somewhere else, a copy to check while the usual app is in use.
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, "-buildout");
        var output = Path.GetFullPath(Path.Combine(RepoRoot, at >= 0 && at + 1 < args.Length ? args[at + 1] : defaultOutput));
        // A check build (`-buildout`, or `-qa`) gets its own bundle id, so its runs keep their settings (window, volumes, chain
        // prompts...) in their own preferences domain, never the player's. The shipping build keeps the game's, and the
        // project's setting is put back after (so ProjectSettings.asset doesn't change).
        bool qa = at >= 0 || System.Array.IndexOf(args, "-qa") >= 0;
        if (qa) PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, QaBundleId);
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            });
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, BundleId);
            AssetDatabase.SaveAssets();
        }
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build failed: {report.summary.result}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return null;
        }
        return output;
    }

    /// <summary>Card scripts, decks, and characters ship next to the game instead of being imported as Unity assets.</summary>
    private static void CopyData(string streaming)
    {
        CopyLicenses(Path.Combine(streaming, "licenses"));
        CopyFiles(Path.Combine(RepoRoot, "vendor", "CardScripts"), Path.Combine(streaming, "CardScripts"), "*.lua");
        CopyFiles(Path.Combine(RepoRoot, "decks"), Path.Combine(streaming, "decks"), "*.ydk");
        CopyFiles(Path.Combine(RepoRoot, "personalities"), Path.Combine(streaming, "personalities"), "*.profile");
        CopyFiles(Path.Combine(RepoRoot, "campaign"), Path.Combine(streaming, "campaign"), "*.txt");
        CopyFiles(Path.Combine(RepoRoot, "vendor", "LFLists"), Path.Combine(streaming, "lflists"), "*.conf");
        CopyFiles(Path.Combine(RepoRoot, "lflists"), Path.Combine(streaming, "lflists"), "*.conf");  // historic era lists
        CopyFiles(Path.Combine(RepoRoot, "art", "portraits"), Path.Combine(streaming, "portraits"), "*.png");  // character portraits
        if (Directory.Exists(Path.Combine(RepoRoot, "art", "map")))  // circuit map regions
            foreach (var pattern in new[] { "*.png", "*.jpg" }) CopyFiles(Path.Combine(RepoRoot, "art", "map"), Path.Combine(streaming, "map"), pattern);
        if (Directory.Exists(Path.Combine(RepoRoot, "art", "mats")))  // hand-made duel mats
            foreach (var pattern in new[] { "*.png", "*.jpg" }) CopyFiles(Path.Combine(RepoRoot, "art", "mats"), Path.Combine(streaming, "mats"), pattern);
        if (Directory.Exists(Path.Combine(RepoRoot, "art", "logo")))  // the game's logo (title screen and menu)
            CopyFiles(Path.Combine(RepoRoot, "art", "logo"), Path.Combine(streaming, "logo"), "*.png");
        if (Directory.Exists(Path.Combine(RepoRoot, "art", "backdrops")))  // story scenes' backdrops, one per place (else painted in code)
            CopyFiles(Path.Combine(RepoRoot, "art", "backdrops"), Path.Combine(streaming, "backdrops"), "*.png");
        if (Directory.Exists(Path.Combine(RepoRoot, "art", "coin")))  // the coin toss's two faces
            CopyFiles(Path.Combine(RepoRoot, "art", "coin"), Path.Combine(streaming, "coin"), "*.png");
    }

    /// <summary>
    /// The license texts and notices every build carries (AGPL: the license and where the source is), shown by the game's
    /// About screen: LICENSE.txt, LICENSING.md, THIRD_PARTY_NOTICES.md, the two LICENSES/ files, and SOURCE.txt, which
    /// names the commit the build was made from.
    /// </summary>
    private static void CopyLicenses(string to)
    {
        Directory.CreateDirectory(to);
        File.Copy(Path.Combine(RepoRoot, "LICENSE"), Path.Combine(to, "LICENSE.txt"), true);
        foreach (var name in new[] { "LICENSING.md", "THIRD_PARTY_NOTICES.md" })
            File.Copy(Path.Combine(RepoRoot, name), Path.Combine(to, name), true);
        foreach (var name in new[] { "CC-BY-SA-4.0.txt", "UNITY-PERMISSION.md" })
            File.Copy(Path.Combine(RepoRoot, "LICENSES", name), Path.Combine(to, name), true);
        var commit = Git("rev-parse HEAD");
        bool changed = Git("status --porcelain --untracked-files=no --ignore-submodules=dirty").Length > 0;
        File.WriteAllText(Path.Combine(to, "SOURCE.txt"),
            $"Duel Circuit {PlayerSettings.bundleVersion}\n" +
            $"Built from commit {(commit.Length > 0 ? commit : "unknown")}{(changed ? " with local changes (not a release build)" : "")}\n\n" +
            "Source code: https://github.com/fullstackjake/duel-circuit\n" +
            "The rules engine in this build is ygopro-core with Duel Circuit's patches (patches/ygopro-core/). Each release\n" +
            "on GitHub has its full source attached as ygopro-core-<commit>-patched.tar.gz; the repository at the release's\n" +
            "tag, with its submodules, holds everything else. If you can't get it there, open an issue and we'll send it.\n" +
            "See THIRD_PARTY_NOTICES.md.\n");
    }

    /// <summary>Runs git in the repo and returns its trimmed output ("" when git isn't there).</summary>
    private static string Git(string arguments)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = RepoRoot, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
            });
            var output = p!.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output.Trim() : "";
        }
        catch (System.Exception)
        {
            return "";
        }
    }

    private static void CopyFiles(string from, string to, string pattern)
    {
        foreach (var file in Directory.GetFiles(from, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith(".git")) continue;
            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
