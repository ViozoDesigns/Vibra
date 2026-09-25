using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Vibra.Core;

namespace Vibra.App
{
    /// <summary>
    /// Keeps Vibra up to date from the repository's GitHub releases: checks in the background,
    /// downloads a newer Vibra.exe, and swaps it in only while no game is on screen (so there is
    /// never a color blink mid-game), then restarts itself.
    /// </summary>
    internal sealed class Updater : IDisposable
    {
        public const string Repository = "ViozoDesigns/Vibra";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";

        private const int FirstCheckDelayMs = 60 * 1000;
        private const int CheckIntervalMs = 6 * 60 * 60 * 1000;
        private const int InstallRetryMs = 30 * 1000;
        private const string ExeAsset = "Vibra.exe";
        private const string ConfigAsset = "Vibra.exe.config";

        private readonly SettingsStore store;
        private readonly Func<bool> canInstallNow;
        private readonly Action restart;
        private readonly SynchronizationContext ui;
        private readonly System.Windows.Forms.Timer checkTimer = new System.Windows.Forms.Timer { Interval = FirstCheckDelayMs };
        private readonly System.Windows.Forms.Timer installTimer = new System.Windows.Forms.Timer { Interval = InstallRetryMs };
        private bool checking;
        private bool installFailed;
        private Version pendingVersion;
        private string pendingFolder;

        public Updater(SettingsStore store, Func<bool> canInstallNow, Action restart)
        {
            this.store = store;
            this.canInstallNow = canInstallNow;
            this.restart = restart;
            ui = SynchronizationContext.Current;
            checkTimer.Tick += (s, e) =>
            {
                checkTimer.Interval = CheckIntervalMs;
                if (!store.Settings.DisableAutoUpdate)
                    Check();
            };
            installTimer.Tick += (s, e) => TryInstall();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static Version CurrentVersion => ReleaseInfo.Normalize(Assembly.GetExecutingAssembly().GetName().Version);

        /// <summary>"0.1.12", as shown to users.</summary>
        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public string Status { get; private set; } = "Not checked yet";

        public event Action StatusChanged;

        public void Start()
        {
            CleanUpAfterPreviousUpdate();
            checkTimer.Start();
        }

        public void CheckNow() => Check();

        private void Check()
        {
            if (checking)
                return;
            if (pendingVersion != null)
            {
                TryInstall();
                return;
            }

            checking = true;
            SetStatus("Checking for updates…");
            var thread = new Thread(() =>
            {
                var result = FetchAndDownload();
                ui.Post(_ => OnChecked(result), null);
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "Vibra update check",
            };
            thread.Start();
        }

        private sealed class CheckResult
        {
            public string Status;
            public Version Version;
            public string Folder;
        }

        private static CheckResult FetchAndDownload()
        {
            try
            {
                string json = GetString($"https://api.github.com/repos/{Repository}/releases/latest");
                var release = ReleaseInfo.Parse(json);
                if (!release.IsNewerThan(CurrentVersion))
                    return new CheckResult { Status = "Up to date" };

                var exe = release.FindAsset(ExeAsset);
                if (exe == null)
                    return new CheckResult { Status = $"Version {release.TagName} has no {ExeAsset} to download" };

                string folder = Path.Combine(AppPaths.DataDirectory, "update", release.Version.ToString());
                Directory.CreateDirectory(folder);
                string exePath = Path.Combine(folder, ExeAsset);
                Download(exe, exePath);

                var config = release.FindAsset(ConfigAsset);
                if (config != null)
                    Download(config, Path.Combine(folder, ConfigAsset));

                Log.Info($"Downloaded update {release.TagName}");
                return new CheckResult { Version = release.Version, Folder = folder };
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return new CheckResult { Status = "No published version found (is the repository public?)" };
            }
            catch (Exception ex)
            {
                Log.Error("Update check failed", ex);
                return new CheckResult { Status = "Couldn't check for updates: " + ex.Message };
            }
        }

        private void OnChecked(CheckResult result)
        {
            checking = false;
            if (result.Version == null)
            {
                SetStatus(result.Status);
                return;
            }

            pendingVersion = result.Version;
            pendingFolder = result.Folder;
            SetStatus($"Version {Short(pendingVersion)} downloaded · installs when no game is on screen");
            installTimer.Start();
            TryInstall();
        }

        /// <summary>Installs a downloaded update if nothing is on screen that a restart would disturb.</summary>
        public void TryInstall()
        {
            if (pendingVersion == null || installFailed || !canInstallNow())
                return;

            string exe = Application.ExecutablePath;
            string old = exe + ".old";
            try
            {
                if (File.Exists(old))
                    File.Delete(old);
                // A running exe can't be overwritten, but it can be renamed.
                File.Move(exe, old);
                try
                {
                    File.Copy(Path.Combine(pendingFolder, ExeAsset), exe);
                }
                catch
                {
                    File.Move(old, exe);
                    throw;
                }
                string newConfig = Path.Combine(pendingFolder, ConfigAsset);
                if (File.Exists(newConfig))
                    File.Copy(newConfig, exe + ".config", true);
            }
            catch (Exception ex)
            {
                installFailed = true;
                installTimer.Stop();
                Log.Error("Could not install update", ex);
                SetStatus($"Couldn't install {Short(pendingVersion)} ({ex.Message}). Download it from the releases page.");
                return;
            }

            installTimer.Stop();
            Log.Info($"Installed update {pendingVersion}, restarting");
            restart();
        }

        private static void CleanUpAfterPreviousUpdate()
        {
            // The old exe may still be locked for a moment while the previous process exits.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(10000);
                try
                {
                    string old = Application.ExecutablePath + ".old";
                    if (File.Exists(old))
                        File.Delete(old);
                    string updates = Path.Combine(AppPaths.DataDirectory, "update");
                    if (Directory.Exists(updates))
                        Directory.Delete(updates, true);
                }
                catch (Exception ex)
                {
                    Log.Error("Could not clean up after update", ex);
                }
            });
        }

        private static string GetString(string url)
        {
            var request = CreateRequest(url, "application/vnd.github+json", 20000);
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return reader.ReadToEnd();
        }

        private static void Download(ReleaseAsset asset, string path)
        {
            var request = CreateRequest(asset.DownloadUrl, "application/octet-stream", 120000);
            using (var response = request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = File.Create(path))
                input.CopyTo(output);

            var info = new FileInfo(path);
            if (asset.Size > 0 && info.Length != asset.Size)
                throw new InvalidDataException($"{asset.Name} download is incomplete");
            if (asset.Sha256 != null && !string.Equals(Sha256Of(path), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{asset.Name} failed its checksum");
            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !StartsWithMz(path))
                throw new InvalidDataException($"{asset.Name} is not a Windows program");
        }

        private static HttpWebRequest CreateRequest(string url, string accept, int timeoutMs)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Vibra/" + CurrentVersionText;
            request.Accept = accept;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            return request;
        }

        private static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool StartsWithMz(string path)
        {
            using (var stream = File.OpenRead(path))
                return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }

        private static string Short(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

        private void SetStatus(string status)
        {
            Status = status;
            StatusChanged?.Invoke();
        }

        public void OpenReleasesPage()
        {
            try
            {
                Process.Start(ReleasesPage);
            }
            catch (Exception ex)
            {
                Log.Error("Could not open the releases page", ex);
            }
        }

        public void Dispose()
        {
            checkTimer.Dispose();
            installTimer.Dispose();
        }
    }
}
