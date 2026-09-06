using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class MigrationReport
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public bool IsAutomatic { get; set; }

        public bool Applied { get; set; }

        public string Message { get; set; } = string.Empty;

        public ModelFavoriteScope SourceScope { get; set; }

        public ModelFavoriteScope TargetScope { get; set; }

        public Model TargetModel { get; set; }

        public Document Document { get; set; }

        public double StructureSimilarity { get; set; }

        public string SourceContentHash { get; set; } = string.Empty;

        public List<MigrationItemResult> Items { get; set; } = new List<MigrationItemResult>();

        public int MatchedCount { get; set; }

        public int SkippedCount { get; set; }

        public int AmbiguousCount { get; set; }
    }
}
