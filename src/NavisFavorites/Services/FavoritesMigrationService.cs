using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class FavoritesMigrationService
    {
        private readonly FavoritesService _favorites;
        private readonly ModelFamilyMatcher _familyMatcher;
        private readonly RevisionItemMatcher _itemMatcher;
        private bool _automaticCheckInProgress;

        public FavoritesMigrationService(
            FavoritesService favorites,
            ModelFamilyMatcher familyMatcher,
            RevisionItemMatcher itemMatcher)
        {
            _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
            _familyMatcher = familyMatcher ?? throw new ArgumentNullException(nameof(familyMatcher));
            _itemMatcher = itemMatcher ?? throw new ArgumentNullException(nameof(itemMatcher));
        }

        public event EventHandler<MigrationReport> ReportAvailable;

        public MigrationReport LastReport { get; private set; }

        public IReadOnlyList<ModelFavoriteScope> GetManualSources(Model targetModel)
        {
            return _favorites.Store.Models
                .Where(scope => !PathUtilities.EqualsPath(scope.ModelFileName, targetModel?.FileName))
                .Where(scope => scope.Folders.Any(folder => folder.Items.Count > 0))
                .OrderByDescending(scope => scope.FirstSeenUtc)
                .ToList();
        }

        public MigrationReport TryAutoInherit(Document document, Model targetModel)
        {
            if (_automaticCheckInProgress || document == null || targetModel == null)
            {
                return null;
            }

            _automaticCheckInProgress = true;
            try
            {
                var existingTarget = _favorites.FindScope(targetModel);
                if (existingTarget != null)
                {
                    _favorites.RefreshLiveScopeMetadata(targetModel);
                }

                var familyKey = ModelFamilyName.GetFamilyKey(targetModel.FileName);
                var revision = ModelFamilyName.GetRevisionToken(targetModel.FileName);
                var fingerprint = _familyMatcher.CaptureFingerprint(targetModel);
                var candidates = _favorites.Store.Models
                    .Where(scope => !PathUtilities.EqualsPath(scope.ModelFileName, targetModel.FileName))
                    .ToList();
                var source = _familyMatcher.SelectAutomaticSource(candidates, familyKey, revision, fingerprint,
                    out var similarity, out var reason);
                if (source == null)
                {
                    if (candidates.Any(scope => string.Equals(scope.FamilyKey, familyKey,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        Publish(new MigrationReport
                        {
                            IsAutomatic = true,
                            TargetModel = targetModel,
                            TargetScope = existingTarget,
                            StructureSimilarity = similarity,
                            Message = reason
                        });
                    }

                    return LastReport;
                }

                var sourceHash = ComputeSourceContentHash(source);
                if (existingTarget?.MigrationHistory?.Any(record => record.SourceScopeId == source.ScopeId
                        && string.Equals(record.SourceContentHash, sourceHash, StringComparison.Ordinal)) == true)
                {
                    return null;
                }

                var report = Preview(document, targetModel, source, true);
                report.StructureSimilarity = similarity;
                if (report.MatchedCount == 0)
                {
                    report.Message = "检测到同系列模型，但没有收藏项目能够安全匹配。";
                    Publish(report);
                    return report;
                }

                Apply(report);
                return report;
            }
            finally
            {
                _automaticCheckInProgress = false;
            }
        }

        public MigrationReport Preview(Document document, Model targetModel, ModelFavoriteScope sourceScope,
            bool automatic)
        {
            if (document == null || document.IsClear || targetModel == null || sourceScope == null)
            {
                return new MigrationReport { Message = "迁移来源或目标模型无效。", IsAutomatic = automatic };
            }

            var targetScope = _favorites.FindScope(targetModel) ?? CreateDetachedScope(targetModel, sourceScope.FamilyId);
            var report = new MigrationReport
            {
                IsAutomatic = automatic,
                SourceScope = sourceScope,
                TargetScope = targetScope,
                TargetModel = targetModel,
                Document = document,
                SourceContentHash = ComputeSourceContentHash(sourceScope)
            };

            var allItems = targetModel.RootItem.DescendantsAndSelf.ToList();
            foreach (var folder in sourceScope.Folders)
            {
                foreach (var item in folder.Items)
                {
                    report.Items.Add(_itemMatcher.Match(document, targetModel, folder, item, allItems));
                }
            }

            report.MatchedCount = report.Items.Count(item => item.IsMatched);
            report.AmbiguousCount = report.Items.Count(item => !item.IsMatched && item.IsAmbiguous);
            report.SkippedCount = report.Items.Count - report.MatchedCount;
            report.Message = BuildSummary(report, false);
            return report;
        }

        public OperationResult Apply(MigrationReport report)
        {
            if (report == null || report.SourceScope == null || report.TargetModel == null || report.MatchedCount == 0)
            {
                return OperationResult.Fail("没有可迁移的收藏项目。");
            }

            var targetScope = _favorites.FindScope(report.TargetModel) ?? report.TargetScope;
            if (!_favorites.Store.Models.Contains(targetScope))
            {
                _favorites.Store.Models.Add(targetScope);
            }

            targetScope.FamilyId = report.SourceScope.FamilyId;
            targetScope.InheritedFromScopeId = report.SourceScope.ScopeId;
            _familyMatcher.PopulateScopeMetadata(targetScope, report.TargetModel);

            foreach (var itemResult in report.Items.Where(item => item.IsMatched))
            {
                var targetFolder = targetScope.Folders.FirstOrDefault(folder =>
                    string.Equals(folder.Name, itemResult.SourceFolder.Name, StringComparison.OrdinalIgnoreCase));
                if (targetFolder == null)
                {
                    targetFolder = new FavoriteFolder { Name = itemResult.SourceFolder.Name };
                    targetScope.Folders.Add(targetFolder);
                }

                var captured = _favorites.Capture(report.Document, report.TargetModel, itemResult.TargetItem);
                captured.MigratedFromItemId = itemResult.SourceItem.Id;
                captured.MigrationMethod = itemResult.Method;
                if (!targetFolder.Items.Any(existing => FavoritesService.IsSameFavorite(existing, captured)))
                {
                    targetFolder.Items.Add(captured);
                }
            }

            targetScope.MigrationHistory = targetScope.MigrationHistory ?? new List<MigrationRecord>();
            targetScope.MigrationHistory.Add(new MigrationRecord
            {
                Id = report.Id,
                SourceScopeId = report.SourceScope.ScopeId,
                SourceDisplayName = report.SourceScope.DisplayName,
                SourceContentHash = report.SourceContentHash,
                MatchedCount = report.MatchedCount,
                SkippedCount = report.SkippedCount,
                AmbiguousCount = report.AmbiguousCount
            });
            _favorites.SaveAll();
            report.TargetScope = targetScope;
            report.Applied = true;
            report.Message = BuildSummary(report, true);
            Publish(report);
            return OperationResult.Ok(report.Message, report.MatchedCount, report.SkippedCount);
        }

        public static string ComputeSourceContentHash(ModelFavoriteScope scope)
        {
            var builder = new StringBuilder();
            foreach (var folder in (scope?.Folders ?? new List<FavoriteFolder>()).OrderBy(folder => folder.Id))
            {
                builder.Append(folder.Id).Append('|').Append(folder.Name).AppendLine();
                foreach (var item in folder.Items.OrderBy(item => item.Id))
                {
                    builder.Append(item.Id).Append('|').Append(item.DisplayName).Append('|').Append(item.PathId)
                        .Append('|').Append(string.Join(",", item.IndexPath ?? new List<int>())).Append('|');
                    foreach (var segment in item.HierarchyPath ?? new List<HierarchySegment>())
                    {
                        builder.Append(segment.DisplayName).Append('\u001f').Append(segment.ClassName).Append('\u001e');
                    }

                    builder.AppendLine();
                }
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                    .Replace("-", string.Empty);
            }
        }

        private ModelFavoriteScope CreateDetachedScope(Model model, Guid familyId)
        {
            var scope = new ModelFavoriteScope
            {
                FamilyId = familyId == Guid.Empty ? Guid.NewGuid() : familyId,
                DisplayName = FavoritesService.GetModelDisplayName(model),
                ModelFileName = PathUtilities.Normalize(model.FileName),
                SourceFileName = PathUtilities.Normalize(model.SourceFileName),
                SourceGuid = model.SourceGuid
            };
            _familyMatcher.PopulateScopeMetadata(scope, model);
            return scope;
        }

        private void Publish(MigrationReport report)
        {
            LastReport = report;
            ReportAvailable?.Invoke(this, report);
        }

        private static string BuildSummary(MigrationReport report, bool applied)
        {
            var prefix = applied ? "已自动继承" : "迁移预览";
            if (!report.IsAutomatic)
            {
                prefix = applied ? "已迁移" : "迁移预览";
            }

            var source = report.SourceScope?.DisplayName ?? "旧版模型";
            return $"{prefix}“{source}”：成功 {report.MatchedCount}，跳过 {report.SkippedCount}，歧义 {report.AmbiguousCount}。";
        }
    }
}
