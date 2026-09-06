using System;
using System.Collections.Generic;

namespace NavisFavorites.Models
{
    public sealed class ModelFavoriteScope
    {
        public Guid ScopeId { get; set; } = Guid.NewGuid();

        public Guid FamilyId { get; set; } = Guid.Empty;

        public string FamilyKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string ModelFileName { get; set; } = string.Empty;

        public string SourceFileName { get; set; } = string.Empty;

        public Guid SourceGuid { get; set; } = Guid.Empty;

        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ModelFileLastWriteUtc { get; set; }

        public string RevisionToken { get; set; } = string.Empty;

        public List<string> StructureFingerprint { get; set; } = new List<string>();

        public Guid? InheritedFromScopeId { get; set; }

        public List<MigrationRecord> MigrationHistory { get; set; } = new List<MigrationRecord>();

        public List<FavoriteFolder> Folders { get; set; } = new List<FavoriteFolder>();
    }
}
