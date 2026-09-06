using System;
using System.Collections.Generic;

namespace NavisFavorites.Models
{
    public sealed class FavoriteItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string DisplayName { get; set; } = string.Empty;

        public string PathId { get; set; } = string.Empty;

        public int ModelIndexAtSave { get; set; } = -1;

        public List<int> IndexPath { get; set; } = new List<int>();

        public List<HierarchySegment> HierarchyPath { get; set; } = new List<HierarchySegment>();

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public Guid? MigratedFromItemId { get; set; }

        public string MigrationMethod { get; set; } = string.Empty;
    }
}
