using System.Collections.Generic;
using System.Linq;
using Vibra.Core;
using Xunit;

namespace Vibra.Tests
{
    public class HotkeyTests
    {
        [Theory]
        [InlineData("Ctrl+Alt+PgUp", Hotkey.Control | Hotkey.Alt, 0x21)]
        [InlineData("ctrl + alt + pageup", Hotkey.Control | Hotkey.Alt, 0x21)]
        [InlineData("Shift+F9", Hotkey.Shift, 0x78)]
        [InlineData("F10", 0, 0x79)]
        [InlineData("Ctrl+=", Hotkey.Control, 0xBB)]
        [InlineData("Alt+NumPlus", Hotkey.Alt, 0x6B)]
        public void Parses(string text, int modifiers, int key)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
            Assert.Equal(modifiers, hotkey.Modifiers);
            Assert.Equal(key, hotkey.Key);
        }

        [Theory]
        [InlineData("Ctrl+Alt+PgUp")]
        [InlineData("Shift+F9")]
        [InlineData("Ctrl+Shift+K")]
        [InlineData("Alt+NumMinus")]
        public void Formats_back_to_the_same_text(string text)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
            Assert.Equal(text, hotkey.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("None")]
        public void Empty_means_no_shortcut(string text)
        {
            Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
            Assert.True(hotkey.IsNone);
            Assert.Equal("None", hotkey.ToString());
        }

        [Theory]
        [InlineData("Ctrl+")]
        [InlineData("Hyper+K")]
        [InlineData("Ctrl+Banana")]
        public void Rejects_garbage(string text)
        {
            Assert.False(Hotkey.TryParse(text, out _));
        }

        [Fact]
        public void Plain_keys_need_a_modifier_except_function_keys()
        {
            Assert.False(new Hotkey(0, 'K').IsAllowed);
            Assert.True(new Hotkey(0, 0x78).IsAllowed);
            Assert.True(new Hotkey(Hotkey.Alt, 'K').IsAllowed);
        }

        [Fact]
        public void Settings_fall_back_to_defaults_for_missing_or_broken_shortcuts()
        {
            var settings = SettingsSerializer.FromJson("{\"HotkeyIncrease\":\"Ctrl+Banana\",\"HotkeyPause\":\"K\"}");
            Assert.Equal(AppSettings.DefaultHotkeyIncrease, settings.HotkeyIncrease);
            Assert.Equal(AppSettings.DefaultHotkeyDecrease, settings.HotkeyDecrease);
            Assert.Equal("None", settings.HotkeyPause);
        }
    }

    public class KnownGameGroupTests
    {
        [Fact]
        public void League_client_is_not_the_game()
        {
            var settings = new AppSettings();
            settings.AddGame(new GameProfile { Exe = "League of Legends.exe", Name = "League of Legends", Vibrance = 85 });

            Assert.Equal("League of Legends", settings.FindGame("League of Legends.exe")?.Name);
            Assert.Null(settings.FindGame("LeagueClientUx.exe"));
            Assert.Null(GameCatalog.Empty.Identify("LeagueClientUx.exe"));
            Assert.Equal("League of Legends", GameCatalog.Empty.Identify("League of Legends.exe")?.Name);
        }

        [Fact]
        public void Entry_saved_for_the_client_moves_to_the_match_and_keeps_its_level()
        {
            var settings = SettingsSerializer.FromJson("{\"Games\":[{\"Exe\":\"LeagueClientUx.exe\",\"Name\":\"League of Legends\",\"Vibrance\":90}]}");

            var game = settings.Games.Single();
            Assert.Equal("League of Legends.exe", game.Exe);
            Assert.Equal(90, game.Vibrance);
            Assert.Null(settings.FindGame("LeagueClientUx.exe"));
        }

        [Fact]
        public void Client_and_match_entries_collapse_into_one()
        {
            var settings = SettingsSerializer.FromJson(
                "{\"Games\":[{\"Exe\":\"League of Legends.exe\",\"Vibrance\":95},{\"Exe\":\"LeagueClientUx.exe\",\"Vibrance\":70}]}");

            var game = settings.Games.Single();
            Assert.Equal(95, game.Vibrance);
        }

        [Fact]
        public void Adding_the_client_by_hand_adds_the_game_instead()
        {
            var settings = new AppSettings();
            Assert.True(settings.AddGame(new GameProfile { Exe = "LeagueClientUx.exe", Name = "League of Legends", Vibrance = 80 }));
            Assert.Equal("League of Legends.exe", settings.Games.Single().Exe);
            Assert.False(settings.AddGame(new GameProfile { Exe = "League of Legends.exe", Name = "League", Vibrance = 80 }));
        }

        [Fact]
        public void Installed_games_remember_where_their_exe_is()
        {
            var catalog = new GameCatalog(new[] { new DetectedGame("Hades II", "Steam", new List<string> { "Hades2.exe" }, @"D:\Steam\Hades II\Hades2.exe") });
            Assert.Equal(@"D:\Steam\Hades II\Hades2.exe", catalog.InstalledPathFor("hades2.exe"));
            Assert.Equal(@"D:\Steam\Hades II\Hades2.exe", catalog.Identify("Hades2.exe").ToProfile(80).IconPath);
        }
    }
}
