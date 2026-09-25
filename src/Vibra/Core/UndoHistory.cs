using System;
using System.Collections.Generic;

namespace Vibra.Core
{
    /// <summary>
    /// Undo/redo for user actions. Each entry knows how to undo and redo itself on the settings
    /// model; the UI refreshes from the model afterwards (see <see cref="Applied"/>).
    /// </summary>
    internal sealed class UndoHistory
    {
        public const int Capacity = 100;

        /// <summary>Changes to the same thing (e.g. one slider) this close together form one step.</summary>
        public static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(2);

        private sealed class Entry
        {
            public string Description;
            public Action Undo;
            public Action Redo;
            public string MergeKey;
            public DateTime LastChanged;
        }

        private readonly List<Entry> undo = new List<Entry>();
        private readonly List<Entry> redo = new List<Entry>();
        private readonly Func<DateTime> clock;

        public UndoHistory(Func<DateTime> clock = null)
        {
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>An entry was undone (true) or redone (false); the model has changed.</summary>
        public event Action<string, bool> Applied;

        /// <summary>True while an undo/redo runs; changes made then are not recorded.</summary>
        public bool IsApplying { get; private set; }

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public string UndoDescription => undo.Count > 0 ? undo[undo.Count - 1].Description : null;
        public string RedoDescription => redo.Count > 0 ? redo[redo.Count - 1].Description : null;

        /// <summary>
        /// Records an action that has already been done. With a <paramref name="mergeKey"/>, a
        /// follow-up change to the same thing within <see cref="MergeWindow"/> extends the last step
        /// instead of adding one (so a slider drag undoes in one go).
        /// </summary>
        public void Record(string description, Action undoAction, Action redoAction, string mergeKey = null)
        {
            if (IsApplying)
                return;

            DateTime now = clock();
            redo.Clear();
            var last = undo.Count > 0 ? undo[undo.Count - 1] : null;
            if (mergeKey != null && last != null && last.MergeKey == mergeKey && now - last.LastChanged <= MergeWindow)
            {
                last.Description = description;
                last.Redo = redoAction;
                last.LastChanged = now;
                return;
            }

            undo.Add(new Entry { Description = description, Undo = undoAction, Redo = redoAction, MergeKey = mergeKey, LastChanged = now });
            if (undo.Count > Capacity)
                undo.RemoveAt(0);
        }

        public bool Undo() => Move(undo, redo, e => e.Undo, true);

        public bool Redo() => Move(redo, undo, e => e.Redo, false);

        private bool Move(List<Entry> from, List<Entry> to, Func<Entry, Action> pick, bool isUndo)
        {
            if (from.Count == 0)
                return false;
            var entry = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);

            IsApplying = true;
            try
            {
                pick(entry)();
            }
            finally
            {
                IsApplying = false;
            }

            entry.LastChanged = DateTime.MinValue; // never merge into a step that was undone/redone
            to.Add(entry);
            Applied?.Invoke(entry.Description, isUndo);
            return true;
        }
    }
}
