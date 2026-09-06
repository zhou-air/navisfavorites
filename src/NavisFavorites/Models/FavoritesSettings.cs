using System;

namespace NavisFavorites.Models
{
    public sealed class FavoritesSettings
    {
        public int SchemaVersion { get; set; } = 2;

        public Guid? LastModelScopeId { get; set; }

        public Guid? LastFolderId { get; set; }

        public Guid? LastAcknowledgedMigrationId { get; set; }
    }
}
