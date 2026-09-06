using System;

namespace NavisFavorites.Services
{
    public sealed class AddSelectionResult : OperationResult
    {
        public Guid ScopeId { get; set; }

        public Guid FolderId { get; set; }

        public int AddedCount { get; set; }

        public int DuplicateCount { get; set; }
    }
}
