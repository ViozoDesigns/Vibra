using System;
using System.Collections.Generic;
using Vibra.Core;
using Xunit;

namespace Vibra.Tests
{
    public class UndoHistoryTests
    {
        private DateTime now = new DateTime(2026, 1, 1, 12, 0, 0);

        private UndoHistory NewHistory() => new UndoHistory(() => now);

        [Fact]
        public void Undo_all_installed_games_removes_them_again_and_redo_brings_them_back()
        {
            var history = NewHistory();
            var settings = new AppSettings();
            settings.AddGame(new GameProfile { Exe = "cs2.exe", Name = "CS2", Vibrance = 90 });

            var added = new List<GameProfile>();
            foreach (var exe in new[] { "a.exe", "b.exe", "c.exe" })
            {
                var profile = new GameProfile { Exe = exe, Name = exe, Vibrance = 80 };
                settings.AddGame(profile);
                added.Add(profile);
            }
            history.Record("Added 3 games", () => added.ForEach(p => settings.Games.Remove(p)), () => added.ForEach(p => settings.AddGame(p)));

            Assert.True(history.Undo());
            Assert.Single(settings.Games);
            Assert.True(history.Redo());
            Assert.Equal(4, settings.Games.Count);
        }

        [Fact]
        public void A_slider_drag_is_one_step()
        {
            var history = NewHistory();
            int value = 70;
            for (int v = 71; v <= 90; v++)
            {
                int before = value, after = v;
                value = v;
                history.Record("level", () => value = before, () => value = after, "slider");
                now += TimeSpan.FromMilliseconds(100);
            }

            history.Undo();
            Assert.Equal(70, value);
            Assert.False(history.CanUndo);
            history.Redo();
            Assert.Equal(90, value);
        }

        [Fact]
        public void Changes_far_apart_are_separate_steps()
        {
            var history = NewHistory();
            int value = 50;
            history.Record("a", () => value = 50, () => value = 60, "slider");
            value = 60;
            now += TimeSpan.FromSeconds(5);
            history.Record("b", () => value = 60, () => value = 70, "slider");
            value = 70;

            history.Undo();
            Assert.Equal(60, value);
            history.Undo();
            Assert.Equal(50, value);
        }

        [Fact]
        public void New_action_clears_redo()
        {
            var history = NewHistory();
            history.Record("a", () => { }, () => { });
            history.Undo();
            Assert.True(history.CanRedo);
            history.Record("b", () => { }, () => { });
            Assert.False(history.CanRedo);
        }

        [Fact]
        public void Changes_made_while_undoing_are_not_recorded()
        {
            var history = NewHistory();
            history.Record("a", () => history.Record("echo", () => { }, () => { }), () => { });
            history.Undo();
            Assert.False(history.CanUndo);
            Assert.True(history.CanRedo);
        }

        [Fact]
        public void Undone_steps_never_absorb_new_changes()
        {
            var history = NewHistory();
            int value = 50;
            history.Record("a", () => value = 50, () => value = 60, "slider");
            value = 60;
            history.Undo();
            history.Redo();
            history.Record("b", () => value = 60, () => value = 65, "slider");
            value = 65;

            history.Undo();
            Assert.Equal(60, value);
        }

        [Fact]
        public void Keeps_at_most_capacity_steps()
        {
            var history = NewHistory();
            for (int i = 0; i < UndoHistory.Capacity + 20; i++)
                history.Record("x" + i, () => { }, () => { });
            int count = 0;
            while (history.Undo())
                count++;
            Assert.Equal(UndoHistory.Capacity, count);
        }

        [Fact]
        public void Nothing_to_undo_returns_false()
        {
            var history = NewHistory();
            Assert.False(history.Undo());
            Assert.False(history.Redo());
        }
    }
}
