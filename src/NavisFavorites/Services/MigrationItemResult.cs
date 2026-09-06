using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class MigrationItemResult
    {
        public FavoriteFolder SourceFolder { get; set; }

        public FavoriteItem SourceItem { get; set; }

        public ModelItem TargetItem { get; set; }

        public string Method { get; set; } = string.Empty;

        public string FailureReason { get; set; } = string.Empty;

        public bool IsAmbiguous { get; set; }

        public bool IsMatched => TargetItem != null;
    }
}
