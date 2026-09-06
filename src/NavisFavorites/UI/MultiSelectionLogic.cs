using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisFavorites.UI
{
    internal static class MultiSelectionLogic
    {
        public static void SelectSingle(ISet<Guid> selected, Guid target)
        {
            selected.Clear();
            selected.Add(target);
        }

        public static void Toggle(ISet<Guid> selected, Guid target)
        {
            if (!selected.Add(target))
            {
                selected.Remove(target);
            }
        }

        public static void SelectRange(
            ISet<Guid> selected,
            IReadOnlyList<Guid> orderedVisibleItems,
            Guid anchor,
            Guid target,
            bool preserveExisting)
        {
            var first = IndexOf(orderedVisibleItems, anchor);
            var last = IndexOf(orderedVisibleItems, target);
            if (!preserveExisting)
            {
                selected.Clear();
            }

            if (first < 0 || last < 0)
            {
                selected.Add(target);
                return;
            }

            var start = Math.Min(first, last);
            var end = Math.Max(first, last);
            for (var index = start; index <= end; index++)
            {
                selected.Add(orderedVisibleItems[index]);
            }
        }

        public static void SelectAll(ISet<Guid> selected, IEnumerable<Guid> itemIds)
        {
            selected.Clear();
            foreach (var id in itemIds ?? Enumerable.Empty<Guid>())
            {
                selected.Add(id);
            }
        }

        private static int IndexOf(IReadOnlyList<Guid> values, Guid target)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (values[index] == target)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
