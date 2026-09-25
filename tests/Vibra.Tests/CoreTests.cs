using System.Collections.Generic;
using System.Linq;
using Vibra.Core;
using Xunit;

namespace Vibra.Tests
{
    public class VibranceScaleTests
    {
        [Fact]
        public void Every_percentage_survives_a_round_trip_through_nvidia_levels()
        {
            for (int percent = VibranceScale.MinPercent; percent <= VibranceScale.MaxPercent; percent++)
            {
                int level = VibranceScale.ToLevel(percent, 0, 63);
                Assert.Equal(percent, VibranceScale.ToPercent(level, 0, 63));
            }
        }

        [Theory]
        [InlineData(50, 0)]
        [InlineData(100, 63)]
        [InlineData(20, 0)]
        [InlineData(150, 63)]
        public void Percent_maps_onto_the_driver_range(int percent, int level)
        {
            Assert.Equal(level, VibranceScale.ToLevel(percent, 0, 63));
        }

        [Fact]
        public void Nvidia_control_panel_70_percent_reads_back_as_70()
        {
            Assert.Equal(70, VibranceScale.ToPercent(VibranceScale.ToLevel(70, 0, 63), 0, 63));
            Assert.Equal(70, VibranceScale.ToPercent(25, 0, 63));
        }
    }

    public class GameMatcherTests
    {
        [Theory]
        [InlineData("cs2.exe", "CS2.EXE", true)]
        [InlineData("VALORANT.exe", "VALORANT-Win64-Shipping.exe", true)]
        [InlineData("cs2.exe", "cs2launcher.exe", false)]
        [InlineData("game.exe", "othergame.exe", false)]
        [InlineData(null, "cs2.exe", false)]
        public void Matches(string saved, string running, bool expected)
        {
            Assert.Equal(expected, GameMatcher.Matches(saved, running));
        }

        [Fact]
        public void Display_name_strips_unreal_suffix()
        {
            Assert.Equal("Marvel", GameMatcher.DisplayNameFor("Marvel-Win64-Shipping.exe"));
            Assert.Equal("cs2", GameMatcher.DisplayNameFor("cs2.exe"));
        }
    }

    public class GameCatalogTests
    {
        [Fact]
        public void Recognises_well_known_competitive_games()
        {
            var catalog = GameCatalog.Empty;
            Assert.Equal("VALORANT", catalog.Identify("VALORANT-Win64-Shipping.exe")?.Name);
            Assert.Equal("Counter-Strike 2", catalog.Identify("cs2.exe")?.Name);
            Assert.Equal("Apex Legends", catalog.Identify("r5apex_dx12.exe")?.Name);
        }

        [Fact]
        public void Recognises_unknown_unreal_games_but_not_ordinary_apps()
        {
            var catalog = GameCatalog.Empty;
            Assert.Equal("SomeIndie", catalog.Identify("SomeIndie-Win64-Shipping.exe")?.Name);
            Assert.Null(catalog.Identify("chrome.exe"));
            Assert.Null(catalog.Identify("Discord.exe"));
            Assert.Null(catalog.Identify("CrashReportClient-Win64-Shipping.exe"));
        }

        [Fact]
        public void Installed_library_games_are_recognised_by_any_of_their_exes()
        {
            var catalog = new GameCatalog(new[] { new DetectedGame("Hades II", "Steam", new[] { "Hades2.exe", "Hades2_Vulkan.exe" }) });
            Assert.Equal("Hades II", catalog.Identify("Hades2_Vulkan.exe")?.Name);
            Assert.Single(catalog.Installed);
        }

        [Fact]
        public void Ranking_drops_helpers_and_prefers_the_game_exe()
        {
            var exes = new List<(string, long)>
            {
                ("UnityCrashHandler64.exe", 1_000_000),
                ("unins000.exe", 3_000_000),
                ("EasyAntiCheat_EOS_Setup.exe", 5_000_000),
                ("Launcher.exe", 9_000_000),
                ("tool.exe", 50_000_000),
                ("HollowKnight.exe", 600_000),
            };
            var ranked = ExeFilter.RankExes(exes, "Hollow Knight");
            Assert.Equal(new[] { "HollowKnight.exe", "tool.exe" }, ranked);
        }
    }

    public class SteamFormatTests
    {
        [Fact]
        public void Parses_current_libraryfolders_format()
        {
            const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n";
            Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, SteamFormats.ParseLibraryFolders(vdf));
        }

        [Fact]
        public void Parses_old_libraryfolders_format()
        {
            const string vdf = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\t\"1700000000\"\n\t\"ContentStatsID\"\t\t\"-123\"\n\t\"1\"\t\t\"E:\\\\Games\\\\Steam\"\n}\n";
            Assert.Equal(new[] { @"E:\Games\Steam" }, SteamFormats.ParseLibraryFolders(vdf));
        }

        [Fact]
        public void Parses_app_manifest()
        {
            const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"name\"\t\t\"Counter-Strike 2\"\n\t\"installdir\"\t\t\"Counter-Strike Global Offensive\"\n}\n";
            var values = SteamFormats.ParseAppManifest(acf);
            Assert.Equal("Counter-Strike 2", values["name"]);
            Assert.Equal("Counter-Strike Global Offensive", values["installdir"]);
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void Round_trips_through_json()
        {
            var settings = new AppSettings();
            settings.AddGame(new GameProfile { Exe = "cs2.exe", Name = "Counter-Strike 2", Vibrance = 90 });
            settings.Displays.Add(new DisplayProfile { Id = @"\\?\DISPLAY#GSM5B7F#1", Name = "LG ULTRAGEAR", Vibrance = 70 });

            var copy = SettingsSerializer.FromJson(SettingsSerializer.ToJson(settings));

            Assert.Equal(90, copy.FindGame("CS2.exe").Vibrance);
            Assert.Equal(70, copy.FindDisplay(@"\\?\DISPLAY#GSM5B7F#1").Vibrance);
        }

        [Fact]
        public void Missing_fields_get_sensible_defaults()
        {
            var settings = SettingsSerializer.FromJson("{\"Games\":[{\"Exe\":\"C:\\\\Games\\\\cs2.exe\"}]}");

            Assert.NotNull(settings.Displays);
            Assert.NotNull(settings.IgnoredExes);
            Assert.Equal(5, settings.HotkeyStep);
            Assert.Equal(VibranceScale.DefaultGamePercent, settings.NewGameVibrance);
            var game = settings.Games.Single();
            Assert.Equal("cs2.exe", game.Exe);
            Assert.Equal("cs2", game.Name);
            Assert.Equal(VibranceScale.DefaultGamePercent, game.Vibrance);
        }

        [Fact]
        public void Removed_games_are_not_auto_added_again_until_added_by_hand()
        {
            var settings = new AppSettings();
            var game = new GameProfile { Exe = "r5apex.exe", OtherExes = new List<string> { "r5apex_dx12.exe" }, Name = "Apex", Vibrance = 80 };
            settings.AddGame(game);

            settings.RemoveGame(game);
            Assert.True(settings.IsIgnored("r5apex_dx12.exe"));

            settings.AddGame(new GameProfile { Exe = "r5apex.exe", Name = "Apex", Vibrance = 80 });
            Assert.False(settings.IsIgnored("r5apex.exe"));
        }

        [Fact]
        public void Games_match_any_of_their_executables()
        {
            var settings = new AppSettings();
            settings.AddGame(new GameProfile { Exe = "RainbowSix.exe", OtherExes = new List<string> { "RainbowSix_Vulkan.exe" }, Name = "Siege", Vibrance = 85 });
            Assert.Equal("Siege", settings.FindGame("rainbowsix_vulkan.exe")?.Name);
        }
    }
}
