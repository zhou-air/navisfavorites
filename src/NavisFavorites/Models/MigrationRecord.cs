using System;

namespace NavisFavorites.Models
{
    public sealed class MigrationRecord
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SourceScopeId { get; set; }

        public string SourceDisplayName { get; set; } = string.Empty;

        public string SourceContentHash { get; set; } = string.Empty;

        public DateTime AppliedUtc { get; set; } = DateTime.UtcNow;

        public int MatchedCount { get; set; }

        public int SkippedCount { get; set; }

        public int AmbiguousCount { get; set; }
    }
}
