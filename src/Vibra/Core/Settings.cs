using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Vibra.Core
{
    // Note: DataContract deserialization does not run constructors or initializers, so every
    // field is designed to be valid at its default value, and Normalize() repairs the rest.

    [DataContract]
    internal sealed class GameProfile
    {
        /// <summary>Executable file name, e.g. "VALORANT-Win64-Shipping.exe".</summary>
        [DataMember(Order = 1)] public string Exe { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        /// <summary>Vibrance in NVIDIA Control Panel percent (50-100).</summary>
        [DataMember(Order = 3)] public int Vibrance { get; set; }
        /// <summary>Other executables of the same game (e.g. DX11/DX12/Vulkan variants). May be null.</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public List<string> OtherExes { get; set; }

        public bool Matches(string exe, bool exactOnly)
        {
            if (exactOnly)
                return GameMatcher.IsExactMatch(Exe, exe) || (OtherExes?.Any(e => GameMatcher.IsExactMatch(e, exe)) ?? false);
            return GameMatcher.Matches(Exe, exe) || (OtherExes?.Any(e => GameMatcher.Matches(e, exe)) ?? false);
        }
    }

    [DataContract]
    internal sealed class DisplayProfile
    {
        /// <summary>Stable monitor identifier (device path), survives reboots and re-plugging.</summary>
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        /// <summary>Vibrance used whenever no game is on this monitor.</summary>
        [DataMember(Order = 3)] public int Vibrance { get; set; }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember(Order = 1)] public List<GameProfile> Games { get; set; } = new List<GameProfile>();
        [DataMember(Order = 2)] public List<DisplayProfile> Displays { get; set; } = new List<DisplayProfile>();
        [DataMember(Order = 3)] public bool Paused { get; set; }
        [DataMember(Order = 4)] public bool DisableHotkeys { get; set; }
        [DataMember(Order = 5)] public int HotkeyStep { get; set; } = 5;
        [DataMember(Order = 6)] public bool TrayHintShown { get; set; }
        /// <summary>Vibrance given to games that are added automatically.</summary>
        [DataMember(Order = 7)] public int NewGameVibrance { get; set; } = VibranceScale.DefaultGamePercent;
        [DataMember(Order = 8)] public bool DisableAutoAdd { get; set; }
        /// <summary>Executables the user removed; they are never auto-added again.</summary>
        [DataMember(Order = 9)] public List<string> IgnoredExes { get; set; } = new List<string>();

        public GameProfile FindGame(string exe)
        {
            if (string.IsNullOrEmpty(exe))
                return null;
            return Games.FirstOrDefault(g => g.Matches(exe, exactOnly: true))
                ?? Games.FirstOrDefault(g => g.Matches(exe, exactOnly: false));
        }

        public bool IsIgnored(string exe) =>
            !string.IsNullOrEmpty(exe) && IgnoredExes.Any(e => string.Equals(e, exe, StringComparison.OrdinalIgnoreCase));

        public void RemoveGame(GameProfile game)
        {
            if (!Games.Remove(game))
                return;
            foreach (string exe in new[] { game.Exe }.Concat(game.OtherExes ?? Enumerable.Empty<string>()))
            {
                if (!IsIgnored(exe))
                    IgnoredExes.Add(exe);
            }
        }

        /// <summary>Adding a game by hand lifts any earlier "never auto-add" for its executables.</summary>
        public void AddGame(GameProfile game)
        {
            Games.Add(game);
            var exes = new HashSet<string>(new[] { game.Exe }.Concat(game.OtherExes ?? Enumerable.Empty<string>()), StringComparer.OrdinalIgnoreCase);
            IgnoredExes.RemoveAll(exes.Contains);
        }

        public DisplayProfile FindDisplay(string id) =>
            string.IsNullOrEmpty(id)
                ? null
                : Displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        public void Normalize()
        {
            Games = (Games ?? new List<GameProfile>())
                .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Exe))
                .ToList();
            foreach (var game in Games)
            {
                game.Exe = GameMatcher.FileName(game.Exe);
                if (string.IsNullOrWhiteSpace(game.Name))
                    game.Name = GameMatcher.DisplayNameFor(game.Exe);
                game.Vibrance = game.Vibrance == 0 ? VibranceScale.DefaultGamePercent : VibranceScale.Clamp(game.Vibrance);
                game.OtherExes = game.OtherExes?
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(GameMatcher.FileName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (game.OtherExes != null && game.OtherExes.Count == 0)
                    game.OtherExes = null;
            }

            NewGameVibrance = NewGameVibrance == 0 ? VibranceScale.DefaultGamePercent : VibranceScale.Clamp(NewGameVibrance);
            IgnoredExes = (IgnoredExes ?? new List<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();

            Displays = (Displays ?? new List<DisplayProfile>())
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Id))
                .ToList();
            foreach (var display in Displays)
                display.Vibrance = VibranceScale.Clamp(display.Vibrance);

            if (HotkeyStep < 1 || HotkeyStep > 25)
                HotkeyStep = 5;
        }
    }

    internal static class SettingsSerializer
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(AppSettings));

        public static string ToJson(AppSettings settings)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    Serializer.WriteObject(writer, settings);
                    writer.Flush();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static AppSettings FromJson(string json)
        {
            AppSettings settings;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                settings = (AppSettings)Serializer.ReadObject(stream) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
    }
}
