using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;
using Vibra.App;
using Vibra.Core;

namespace Vibra.Platform
{
    /// <summary>
    /// Finds installed games by reading launcher library files on disk (Steam, Epic, GOG, Xbox) and
    /// the Windows installed-apps list for well-known titles. Read-only; games are never started or
    /// touched. Runs on a background thread.
    /// </summary>
    internal static class LibraryScanner
    {
        private const int MaxFolderDepth = 4;
        private const int MaxFoldersPerGame = 3000;
        private const int MaxExesPerGame = 24;

        public static List<DetectedGame> Scan()
        {
            var games = new List<DetectedGame>();
            Run("Steam", ScanSteam, games);
            Run("Epic", ScanEpic, games);
            Run("GOG", ScanGog, games);
            Run("Xbox", ScanXbox, games);
            Run("Installed apps", ScanKnownInstalled, games);
            Log.Info($"Library scan found {games.Count} games");
            return games;
        }

        private static void Run(string source, Func<IEnumerable<DetectedGame>> scan, List<DetectedGame> into)
        {
            try
            {
                into.AddRange(scan());
            }
            catch (Exception ex)
            {
                Log.Error($"{source} library scan failed", ex);
            }
        }

        // ------------------------------------------------------------------ Steam

        private static IEnumerable<DetectedGame> ScanSteam()
        {
            string steamPath = ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath")
                ?? ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
            if (string.IsNullOrEmpty(steamPath))
                yield break;
            steamPath = steamPath.Replace('/', '\\');

            var libraries = new List<string> { steamPath };
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                libraries.AddRange(SteamFormats.ParseLibraryFolders(File.ReadAllText(vdf)));

            foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string steamapps = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamapps))
                    continue;

                foreach (string manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    DetectedGame game = null;
                    try
                    {
                        var values = SteamFormats.ParseAppManifest(File.ReadAllText(manifest));
                        values.TryGetValue("name", out string name);
                        values.TryGetValue("installdir", out string installDir);
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir) || IsSteamTool(name))
                            continue;
                        string folder = Path.Combine(steamapps, "common", installDir);
                        game = FromFolder(name, "Steam", folder);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Could not read {manifest}", ex);
                    }
                    if (game != null)
                        yield return game;
                }
            }
        }

        private static bool IsSteamTool(string name) =>
            name.IndexOf("Redistributable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("SteamVR", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Wallpaper Engine", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("Dedicated Server", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf(" SDK", StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ Epic

        [DataContract]
        private sealed class EpicManifest
        {
            [DataMember] public string DisplayName { get; set; }
            [DataMember] public string InstallLocation { get; set; }
            [DataMember] public string LaunchExecutable { get; set; }
            [DataMember] public List<string> AppCategories { get; set; }
        }

        private static IEnumerable<DetectedGame> ScanEpic()
        {
            string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (!Directory.Exists(manifests))
                yield break;

            var serializer = new DataContractJsonSerializer(typeof(EpicManifest));
            foreach (string file in Directory.EnumerateFiles(manifests, "*.item"))
            {
                DetectedGame game = null;
                try
                {
                    EpicManifest manifest;
                    using (var stream = File.OpenRead(file))
                        manifest = (EpicManifest)serializer.ReadObject(stream);
                    if (manifest == null || string.IsNullOrEmpty(manifest.DisplayName) || string.IsNullOrEmpty(manifest.InstallLocation))
                        continue;
                    if (manifest.AppCategories != null && !manifest.AppCategories.Contains("games", StringComparer.OrdinalIgnoreCase))
                        continue;

                    var found = FromFolder(manifest.DisplayName, "Epic", manifest.InstallLocation);
                    string launch = string.IsNullOrEmpty(manifest.LaunchExecutable) ? null : Path.GetFileName(manifest.LaunchExecutable);
                    var exes = new List<string>();
                    string icon = found?.IconPath;
                    if (launch != null && !ExeFilter.IsHelper(launch))
                    {
                        exes.Add(launch);
                        icon = Path.Combine(manifest.InstallLocation, manifest.LaunchExecutable.Replace('/', '\\'));
                    }
                    if (found != null)
                        exes.AddRange(found.Exes);
                    if (exes.Count > 0)
                        game = new DetectedGame(manifest.DisplayName, "Epic", exes, icon);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not read {file}", ex);
                }
                if (game != null)
                    yield return game;
            }
        }

        // ------------------------------------------------------------------ GOG

        private static IEnumerable<DetectedGame> ScanGog()
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var games = hklm.OpenSubKey(@"SOFTWARE\GOG.com\Games"))
            {
                if (games == null)
                    yield break;
                foreach (string id in games.GetSubKeyNames())
                {
                    using (var key = games.OpenSubKey(id))
                    {
                        string name = key?.GetValue("gameName") as string;
                        string exe = key?.GetValue("exe") as string;
                        string path = key?.GetValue("path") as string;
                        if (string.IsNullOrEmpty(name))
                            continue;
                        var exes = new List<string>();
                        string icon = null;
                        if (!string.IsNullOrEmpty(exe) && !ExeFilter.IsHelper(Path.GetFileName(exe)))
                        {
                            exes.Add(Path.GetFileName(exe));
                            icon = exe;
                        }
                        var found = string.IsNullOrEmpty(path) ? null : FromFolder(name, "GOG", path);
                        if (found != null)
                            exes.AddRange(found.Exes);
                        if (exes.Count > 0)
                            yield return new DetectedGame(name, "GOG", exes, icon ?? found?.IconPath);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Xbox / Game Pass

        private static IEnumerable<DetectedGame> ScanXbox()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    continue;
                string root = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
                if (!Directory.Exists(root))
                    continue;
                foreach (string folder in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(folder);
                    string content = Path.Combine(folder, "Content");
                    var game = FromFolder(name, "Xbox", Directory.Exists(content) ? content : folder);
                    if (game != null)
                        yield return game;
                }
            }
        }

        // ------------------------------------------------------------------ Well-known titles (Riot, Battle.net, EA, Ubisoft...)

        private static IEnumerable<DetectedGame> ScanKnownInstalled()
        {
            var installed = new List<(string Name, string Icon)>();
            foreach (var (hive, view) in new[]
                     {
                         (RegistryHive.LocalMachine, RegistryView.Registry64),
                         (RegistryHive.LocalMachine, RegistryView.Registry32),
                         (RegistryHive.CurrentUser, RegistryView.Default),
                     })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null)
                            continue;
                        foreach (string sub in uninstall.GetSubKeyNames())
                        {
                            using (var key = uninstall.OpenSubKey(sub))
                            {
                                if (key?.GetValue("DisplayName") is string displayName && !string.IsNullOrWhiteSpace(displayName))
                                    installed.Add((displayName.Trim(), CleanIconPath(key.GetValue("DisplayIcon") as string)));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Could not read installed apps", ex);
                }
            }

            foreach (var known in KnownGames.All)
            {
                var match = installed.FirstOrDefault(entry =>
                    string.Equals(entry.Name, known.Name, StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.StartsWith(known.Name + " ", StringComparison.OrdinalIgnoreCase) ||
                    entry.Name.StartsWith(known.Name + ":", StringComparison.OrdinalIgnoreCase));
                if (match.Name != null)
                    yield return new DetectedGame(known.Name, "Installed", known.Exes, match.Icon);
            }
        }

        /// <summary>Uninstall entries store icons as "C:\path\game.exe,0" (sometimes quoted).</summary>
        private static string CleanIconPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string path = value.Trim();
            int comma = path.LastIndexOf(',');
            if (comma > 2 && int.TryParse(path.Substring(comma + 1).Trim(), out _))
                path = path.Substring(0, comma);
            path = path.Trim().Trim('"');
            string ext = Path.GetExtension(path);
            bool usable = string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase);
            return usable && File.Exists(path) ? path : null;
        }

        // ------------------------------------------------------------------ Helpers

        /// <summary>Collects the plausible game executables inside an install folder.</summary>
        private static DetectedGame FromFolder(string name, string source, string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return null;

            var exes = new List<(string Name, long Size)>();
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((folder, 0));
            int visited = 0;
            while (pending.Count > 0 && visited < MaxFoldersPerGame && exes.Count < MaxExesPerGame * 4)
            {
                var (path, depth) = pending.Pop();
                visited++;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*.exe"))
                    {
                        string exeName = Path.GetFileName(file);
                        exes.Add((exeName, new FileInfo(file).Length));
                        if (!paths.ContainsKey(exeName))
                            paths[exeName] = file;
                    }

                    if (depth >= MaxFolderDepth)
                        continue;
                    foreach (string dir in Directory.EnumerateDirectories(path))
                    {
                        if (!ExeFilter.IsSkippedFolder(Path.GetFileName(dir)))
                            pending.Push((dir, depth + 1));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            var ranked = ExeFilter.RankExes(exes, name).Take(MaxExesPerGame).ToList();
            return ranked.Count == 0 ? null : new DetectedGame(name, source, ranked, paths[ranked[0]]);
        }

        private static string ReadRegistryString(RegistryHive hive, RegistryView view, string path, string value)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(path))
                    return key?.GetValue(value) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
