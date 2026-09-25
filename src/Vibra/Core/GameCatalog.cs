using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vibra.Core
{
    /// <summary>A game Vibra found installed or recognised.</summary>
    internal sealed class DetectedGame
    {
        public DetectedGame(string name, string source, IEnumerable<string> exes, string iconPath = null)
        {
            Name = name;
            Source = source;
            if (!string.IsNullOrEmpty(iconPath))
                IconCandidates.Add(iconPath);
            Exes = exes.Where(e => !string.IsNullOrWhiteSpace(e))
                       .Select(GameMatcher.FileName)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        public string Name { get; }
        /// <summary>Where it was found: "Steam", "Epic", "GOG", "Xbox", "Installed" or "Recognised".</summary>
        public string Source { get; }
        /// <summary>Executables belonging to the game, most likely main executable first.</summary>
        public List<string> Exes { get; }
        public string PrimaryExe => Exes.Count > 0 ? Exes[0] : null;
        /// <summary>Full path of the main exe (or an icon file) on disk, if known.</summary>
        public string IconPath => IconCandidates.Count > 0 ? IconCandidates[0] : null;

        /// <summary>Files to take the icon from, best first: exes, .ico files, launcher artwork.</summary>
        public List<string> IconCandidates { get; } = new List<string>();

        public DetectedGame WithIconCandidates(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path) && !IconCandidates.Contains(path, StringComparer.OrdinalIgnoreCase))
                    IconCandidates.Add(path);
            }
            return this;
        }

        public GameProfile ToProfile(int vibrance) => new GameProfile
        {
            Exe = PrimaryExe,
            OtherExes = Exes.Count > 1 ? Exes.Skip(1).ToList() : null,
            Name = Name,
            Vibrance = VibranceScale.Clamp(vibrance),
            IconPath = IconPath,
        };
    }

    /// <summary>Popular games whose executable names are well known, whatever launcher installed them.</summary>
    internal static class KnownGames
    {
        public sealed class Entry
        {
            public Entry(string name, params string[] exes)
            {
                Name = name;
                Exes = exes;
            }

            public string Name { get; }
            public string[] Exes { get; }

            /// <summary>
            /// The game's own launcher/client processes (e.g. League's lobby). They are not the game:
            /// never boosted, auto-added or offered in the Add menu.
            /// </summary>
            public string[] Launchers { get; set; } = new string[0];
        }

        public static readonly Entry[] All =
        {
            new Entry("VALORANT", "VALORANT-Win64-Shipping.exe"),
            new Entry("Counter-Strike 2", "cs2.exe"),
            new Entry("Fortnite", "FortniteClient-Win64-Shipping.exe"),
            new Entry("Apex Legends", "r5apex.exe", "r5apex_dx12.exe"),
            new Entry("Overwatch", "Overwatch.exe"),
            new Entry("League of Legends", "League of Legends.exe")
            {
                Launchers = new[] { "LeagueClientUx.exe", "LeagueClient.exe", "LeagueClientUxRender.exe" },
            },
            new Entry("Rainbow Six Siege", "RainbowSix.exe", "RainbowSix_Vulkan.exe", "RainbowSix_DX11.exe"),
            new Entry("Rocket League", "RocketLeague.exe"),
            new Entry("Call of Duty", "cod.exe", "ModernWarfare.exe", "BlackOpsColdWar.exe"),
            new Entry("PUBG: BATTLEGROUNDS", "TslGame.exe"),
            new Entry("Dota 2", "dota2.exe"),
            new Entry("Marvel Rivals", "Marvel-Win64-Shipping.exe"),
            new Entry("THE FINALS", "Discovery.exe"),
            new Entry("Deadlock", "deadlock.exe", "project8.exe"),
            new Entry("Rust", "RustClient.exe"),
            new Entry("Escape from Tarkov", "EscapeFromTarkov.exe"),
            new Entry("Destiny 2", "destiny2.exe"),
            new Entry("Grand Theft Auto V", "GTA5.exe", "GTA5_Enhanced.exe"),
            new Entry("Minecraft", "Minecraft.Windows.exe"),
            new Entry("Battlefield 2042", "BF2042.exe"),
            new Entry("Halo Infinite", "HaloInfinite.exe"),
            new Entry("Dead by Daylight", "DeadByDaylight-Win64-Shipping.exe"),
            new Entry("Team Fortress 2", "tf_win64.exe"),
            new Entry("Genshin Impact", "GenshinImpact.exe"),
            new Entry("Warframe", "Warframe.x64.exe"),
            new Entry("Hunt: Showdown", "HuntGame.exe"),
            new Entry("ELDEN RING", "eldenring.exe"),
            new Entry("Cyberpunk 2077", "Cyberpunk2077.exe"),
            new Entry("Roblox", "RobloxPlayerBeta.exe"),
            new Entry("osu!", "osu!.exe"),
            new Entry("World of Warcraft", "Wow.exe"),
            new Entry("Diablo IV", "Diablo IV.exe"),
            new Entry("NARAKA: BLADEPOINT", "NarakaBladepoint.exe"),
        };

        public static Entry Find(string exe)
        {
            if (string.IsNullOrEmpty(exe))
                return null;
            return All.FirstOrDefault(g => g.Exes.Any(e => GameMatcher.IsExactMatch(e, exe)))
                ?? All.FirstOrDefault(g => g.Exes.Any(e => GameMatcher.Matches(e, exe)));
        }

        /// <summary>The known game whose launcher/client this executable is, or null.</summary>
        public static Entry FindByLauncher(string exe)
        {
            if (string.IsNullOrEmpty(exe))
                return null;
            return All.FirstOrDefault(g => g.Launchers.Any(l => GameMatcher.IsExactMatch(l, exe)));
        }

        public static bool IsLauncher(string exe) => FindByLauncher(exe) != null;

        /// <summary>Unreal Engine games ship their game process as "Name-Win64-Shipping.exe".</summary>
        public static bool IsUnrealShippingExe(string exe)
        {
            if (string.IsNullOrEmpty(exe))
                return false;
            string stem = Path.GetFileNameWithoutExtension(exe);
            return stem.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase) ||
                   stem.EndsWith("-WinGDK-Shipping", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Answers "is this executable a game, and what is it called?".</summary>
    internal sealed class GameCatalog
    {
        private readonly Dictionary<string, DetectedGame> byExe = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

        public GameCatalog(IEnumerable<DetectedGame> installed)
        {
            Installed = installed
                .Where(g => g.Exes.Count > 0)
                .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var game in Installed)
            {
                foreach (string exe in game.Exes)
                {
                    if (!byExe.ContainsKey(exe))
                        byExe[exe] = game;
                }
            }
        }

        public static GameCatalog Empty { get; } = new GameCatalog(Enumerable.Empty<DetectedGame>());

        /// <summary>Installed games found in launcher libraries, sorted by name.</summary>
        public IReadOnlyList<DetectedGame> Installed { get; }

        /// <summary>Full path of an installed game's exe (for its icon), or null.</summary>
        public string InstalledPathFor(string exe) =>
            exe != null && byExe.TryGetValue(exe, out var game) ? game.IconPath : null;

        /// <summary>Every file an installed game's icon could come from, best first.</summary>
        public IReadOnlyList<string> IconCandidatesFor(string exe) =>
            exe != null && byExe.TryGetValue(exe, out var game) ? (IReadOnlyList<string>)game.IconCandidates : new string[0];

        /// <summary>Identifies a running executable as a game, or returns null if it does not look like one.</summary>
        public DetectedGame Identify(string exe)
        {
            if (string.IsNullOrEmpty(exe) || KnownGames.IsLauncher(exe))
                return null;
            if (byExe.TryGetValue(exe, out var installed))
                return installed;

            var known = KnownGames.Find(exe);
            if (known != null)
                return new DetectedGame(known.Name, "Recognised", new[] { exe }.Concat(known.Exes));

            if (!ExeFilter.IsHelper(exe) && KnownGames.IsUnrealShippingExe(exe))
                return new DetectedGame(GameMatcher.DisplayNameFor(exe), "Recognised", new[] { exe });

            return null;
        }
    }

    /// <summary>Filters out installers, crash reporters, anti-cheat services and launchers found in game folders.</summary>
    internal static class ExeFilter
    {
        private static readonly string[] HelperFragments =
        {
            "unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "directx",
            "crash", "reporter", "easyanticheat", "eac_", "battleye", "beservice", "be_service", "prereq",
            "helper", "updater", "uploader", "patcher", "launcher", "cefprocess", "webhelper", "overlay",
            "dotnet", "physx", "oalinst", "vcrun", "touchup", "cleanup", "diagnostic", "notification",
            "errorreporter", "crashpad", "7za", "python", "node.exe", "config", "settings", "editor",
            "server.exe", "dedicated", "benchmark", "register", "repair", "activation", "uplay", "service",
        };

        private static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_CommonRedist", "CommonRedist", "Redist", "Redistributables", "redist", "DirectX", "Support",
            "Installers", "Installer", "__Installer", "EasyAntiCheat", "BattlEye", "Prerequisites", "vcredist",
            "dotNetFx", "ThirdParty", "Extras", "Tools", "Docs", "Manual", "Soundtrack", "OST", "Mods",
            "Screenshots", "Logs", "Saved", "Cache", "shadercache", "MonoBleedingEdge",
        };

        public static bool IsHelper(string exe)
        {
            if (string.IsNullOrEmpty(exe))
                return true;
            string name = exe.ToLowerInvariant();
            return HelperFragments.Any(name.Contains);
        }

        public static bool IsSkippedFolder(string folderName) => SkippedFolders.Contains(folderName);

        /// <summary>
        /// Orders a game's executables so the most likely game process comes first:
        /// Unreal shipping builds, then names resembling the game's name, then larger files.
        /// </summary>
        public static List<string> RankExes(IEnumerable<(string Name, long Size)> exes, string gameName)
        {
            string key = Normalize(gameName);
            return exes
                .Where(e => !IsHelper(e.Name))
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(e => e.Size).First())
                .OrderByDescending(e => KnownGames.IsUnrealShippingExe(e.Name))
                .ThenByDescending(e => Resembles(Normalize(Path.GetFileNameWithoutExtension(e.Name)), key))
                .ThenByDescending(e => e.Size)
                .Select(e => e.Name)
                .ToList();
        }

        private static bool Resembles(string exeKey, string nameKey) =>
            exeKey.Length >= 3 && nameKey.Length >= 3 && (nameKey.Contains(exeKey) || exeKey.Contains(nameKey));

        private static string Normalize(string text) =>
            new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    /// <summary>Parses Steam's libraryfolders.vdf and appmanifest_*.acf text files.</summary>
    internal static class SteamFormats
    {
        private static readonly Regex KeyValue = new Regex("^\\s*\"(?<key>[^\"]*)\"\\s+\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Multiline);

        public static List<string> ParseLibraryFolders(string vdf)
        {
            var paths = new List<string>();
            foreach (Match match in KeyValue.Matches(vdf ?? string.Empty))
            {
                string key = match.Groups["key"].Value;
                string value = Unescape(match.Groups["value"].Value);
                // Current format: "path" "D:\\SteamLibrary". Old format: "1" "D:\\SteamLibrary".
                bool isPath = string.Equals(key, "path", StringComparison.OrdinalIgnoreCase) ||
                              (key.Length > 0 && key.All(char.IsDigit) && value.Contains(":\\"));
                if (isPath && !paths.Contains(value, StringComparer.OrdinalIgnoreCase))
                    paths.Add(value);
            }
            return paths;
        }

        public static Dictionary<string, string> ParseAppManifest(string acf)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in KeyValue.Matches(acf ?? string.Empty))
            {
                string key = match.Groups["key"].Value;
                if (!values.ContainsKey(key))
                    values[key] = Unescape(match.Groups["value"].Value);
            }
            return values;
        }

        private static string Unescape(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }
}
