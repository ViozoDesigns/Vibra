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
