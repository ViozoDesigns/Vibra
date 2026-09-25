using System;
using System.Collections.Generic;
using Vibra.Core;
using Xunit;

namespace Vibra.Tests
{
    public class ScreenDeciderTests
    {
        private static readonly MonitorArea Left = new MonitorArea("DISPLAY1", new ScreenRect(0, 0, 2560, 1440));
        private static readonly MonitorArea Right = new MonitorArea("DISPLAY2", new ScreenRect(2560, 0, 4480, 1080));
        private static readonly List<MonitorArea> Monitors = new List<MonitorArea> { Left, Right };

        private static int nextHandle = 1;

        private static WindowInfo Window(string exe, ScreenRect bounds, bool foreground = false) => new WindowInfo
        {
            Handle = new IntPtr(nextHandle++),
            ExeName = exe,
            Bounds = bounds,
            IsForeground = foreground,
        };

        [Fact]
        public void Clicking_the_other_monitor_keeps_the_game_on_its_monitor()
        {
            var browser = Window("chrome.exe", new ScreenRect(2560, 0, 4480, 1040), foreground: true);
            var game = Window("cs2.exe", Left.Bounds);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { browser, game });

            Assert.Same(game, owners["DISPLAY1"]);
            Assert.Same(browser, owners["DISPLAY2"]);
        }

        [Fact]
        public void Focused_window_on_the_game_monitor_takes_it_over()
        {
            var discord = Window("Discord.exe", new ScreenRect(200, 200, 1200, 900), foreground: true);
            var game = Window("cs2.exe", Left.Bounds);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { discord, game });

            Assert.Same(discord, owners["DISPLAY1"]);
        }

        [Fact]
        public void Small_unfocused_window_over_the_game_does_not_take_it_over()
        {
            var popup = Window("Spotify.exe", new ScreenRect(200, 200, 900, 700));
            var game = Window("cs2.exe", Left.Bounds, foreground: true);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { popup, game });

            Assert.Same(game, owners["DISPLAY1"]);
        }

        [Fact]
        public void Large_unfocused_window_over_the_game_takes_it_over()
        {
            var browser = Window("chrome.exe", new ScreenRect(0, 0, 2000, 1400));
            var game = Window("cs2.exe", Left.Bounds);
            var other = Window("notepad.exe", new ScreenRect(2600, 50, 3000, 400), foreground: true);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { other, browser, game });

            Assert.Same(browser, owners["DISPLAY1"]);
        }

        [Fact]
        public void Maximized_window_with_invisible_borders_still_covers_its_monitor()
        {
            var game = Window("game.exe", new ScreenRect(-8, -8, 2568, 1448));

            var owners = ScreenDecider.FindOwners(Monitors, new[] { game });

            Assert.Same(game, owners["DISPLAY1"]);
            Assert.False(owners.ContainsKey("DISPLAY2"));
        }

        [Fact]
        public void Empty_desktop_has_no_owner()
        {
            var owners = ScreenDecider.FindOwners(Monitors, Array.Empty<WindowInfo>());
            Assert.Empty(owners);
        }

        [Fact]
        public void Focused_window_only_claims_the_monitor_it_mostly_sits_on()
        {
            // Mostly on the right monitor, peeking 100px into the left one.
            var chat = Window("Discord.exe", new ScreenRect(2460, 100, 3400, 900), foreground: true);
            var game = Window("cs2.exe", Left.Bounds);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { chat, game });

            Assert.Same(game, owners["DISPLAY1"]);
            Assert.Same(chat, owners["DISPLAY2"]);
        }

        private static bool IsGame(WindowInfo w) => w.ExeName == "Balatro.exe" || w.ExeName == "cs2.exe";

        [Fact]
        public void Windowed_game_keeps_its_monitor_while_you_use_the_other_one()
        {
            // A windowed game (1280x720) on the right monitor; focus is on the left one.
            var right = new MonitorArea("DISPLAY2", new ScreenRect(2560, 0, 5120, 1440));
            var browser = Window("chrome.exe", new ScreenRect(0, 0, 2560, 1400), foreground: true);
            var client = Window("Balatro.exe", new ScreenRect(3200, 300, 4480, 1020));

            var owners = ScreenDecider.FindOwners(new List<MonitorArea> { Left, right }, new[] { browser, client }, IsGame);

            Assert.Same(browser, owners["DISPLAY1"]);
            Assert.Same(client, owners["DISPLAY2"]);
        }

        [Fact]
        public void Windowed_game_keeps_its_monitor_while_vibra_has_focus()
        {
            // Vibra's own window is never passed in, so nothing is focused from the decider's view.
            var client = Window("Balatro.exe", new ScreenRect(600, 300, 1880, 1020));

            var owners = ScreenDecider.FindOwners(Monitors, new[] { client }, IsGame);

            Assert.Same(client, owners["DISPLAY1"]);
        }

        [Fact]
        public void Windowed_game_mostly_covered_by_another_window_does_not_own_its_monitor()
        {
            var explorer = Window("explorer.exe", new ScreenRect(500, 250, 1700, 1000));
            var client = Window("Balatro.exe", new ScreenRect(600, 300, 1880, 1020));

            var owners = ScreenDecider.FindOwners(Monitors, new[] { explorer, client }, IsGame);

            Assert.False(owners.ContainsKey("DISPLAY1"));
        }

        [Fact]
        public void Focused_app_beside_a_windowed_game_takes_the_monitor()
        {
            var notes = Window("notepad.exe", new ScreenRect(1900, 100, 2500, 700), foreground: true);
            var client = Window("Balatro.exe", new ScreenRect(100, 100, 1380, 820));

            var owners = ScreenDecider.FindOwners(Monitors, new[] { notes, client }, IsGame);

            Assert.Same(notes, owners["DISPLAY1"]);
        }

        [Fact]
        public void Borderless_game_spanning_both_monitors_owns_both()
        {
            var game = Window("game.exe", new ScreenRect(0, 0, 4480, 1440), foreground: true);

            var owners = ScreenDecider.FindOwners(Monitors, new[] { game });

            Assert.Same(game, owners["DISPLAY1"]);
            Assert.Same(game, owners["DISPLAY2"]);
        }
    }
}
