using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class ModelFamilyMatcher
    {
        public const double AutomaticSimilarityThreshold = 0.75d;

        public List<string> CaptureFingerprint(Model model)
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            if (model?.RootItem == null)
            {
                return tokens.ToList();
            }

            foreach (var child in model.RootItem.Children)
            {
                var parentToken = SegmentToken(child);
                tokens.Add("1|" + parentToken);
                foreach (var grandchild in child.Children)
                {
                    tokens.Add("2|" + parentToken + "|" + SegmentToken(grandchild));
                }
            }

            return tokens.OrderBy(token => token, StringComparer.Ordinal).ToList();
        }

        public double Similarity(IEnumerable<string> left, IEnumerable<string> right)
        {
            var a = new HashSet<string>(left ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var b = new HashSet<string>(right ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (a.Count == 0 || b.Count == 0)
            {
                return 0d;
            }

            var intersection = a.Count(token => b.Contains(token));
            var union = a.Count + b.Count - intersection;
            return union == 0 ? 0d : (double)intersection / union;
        }

        public ModelFavoriteScope SelectAutomaticSource(
            IEnumerable<ModelFavoriteScope> scopes,
            string targetFamilyKey,
            string targetRevisionToken,
            IReadOnlyCollection<string> targetFingerprint,
            out double similarity,
            out string reason)
        {
            similarity = 0d;
            reason = string.Empty;
            var candidates = (scopes ?? Enumerable.Empty<ModelFavoriteScope>())
                .Where(scope => string.Equals(scope.FamilyKey, targetFamilyKey, StringComparison.OrdinalIgnoreCase))
                .Where(scope => scope.Folders.Any(folder => folder.Items.Count > 0))
                .ToList();
            if (candidates.Count == 0)
            {
                reason = "没有找到同系列的旧版收藏。";
                return null;
            }

            var families = candidates.Where(scope => scope.FamilyId != Guid.Empty)
                .Select(scope => scope.FamilyId)
                .Distinct()
                .ToList();
            if (families.Count != 1)
            {
                reason = "检测到多个同名模型系列，需要手动选择来源。";
                return null;
            }

            ModelFavoriteScope preferred;
            var comparable = new List<Tuple<ModelFavoriteScope, long>>();
            if (ModelFamilyName.TryGetRevisionOrder(targetRevisionToken, out var targetOrder))
            {
                foreach (var candidate in candidates)
                {
                    if (ModelFamilyName.TryGetRevisionOrder(candidate.RevisionToken, out var sourceOrder)
                        && sourceOrder < targetOrder)
                    {
                        comparable.Add(Tuple.Create(candidate, sourceOrder));
                    }
                }
            }

            if (comparable.Count > 0)
            {
                var nearestOrder = comparable.Max(item => item.Item2);
                var nearest = comparable.Where(item => item.Item2 == nearestOrder)
                    .Select(item => item.Item1)
                    .ToList();
                if (nearest.Count != 1)
                {
                    reason = "存在多个同样接近的旧版本，需要手动选择来源。";
                    return null;
                }

                preferred = nearest[0];
            }
            else
            {
                var hasComparableRevision = ModelFamilyName.TryGetRevisionOrder(targetRevisionToken, out _)
                                            && candidates.Any(candidate =>
                                                ModelFamilyName.TryGetRevisionOrder(candidate.RevisionToken, out _));
                if (hasComparableRevision)
                {
                    reason = "没有找到版本标记早于当前模型的收藏来源。";
                    return null;
                }

                var latestSeen = candidates.Max(scope => scope.FirstSeenUtc);
                var latest = candidates.Where(scope => scope.FirstSeenUtc == latestSeen).ToList();
                if (latest.Count != 1)
                {
                    reason = "多个收藏来源的创建时间相同，需要手动选择来源。";
                    return null;
                }

                preferred = latest[0];
            }

            similarity = Similarity(preferred.StructureFingerprint, targetFingerprint);
            if (similarity >= AutomaticSimilarityThreshold)
            {
                return preferred;
            }

            reason = $"模型结构相似度 {similarity:P0} 低于自动继承阈值 {AutomaticSimilarityThreshold:P0}。";
            return null;
        }

        public void PopulateScopeMetadata(ModelFavoriteScope scope, Model model)
        {
            if (scope == null || model == null)
            {
                return;
            }

            var modelPath = PathUtilities.Normalize(model.FileName);
            scope.FamilyKey = ModelFamilyName.GetFamilyKey(modelPath);
            scope.RevisionToken = ModelFamilyName.GetRevisionToken(modelPath);
            scope.StructureFingerprint = CaptureFingerprint(model);
            if (scope.FamilyId == Guid.Empty)
            {
                scope.FamilyId = Guid.NewGuid();
            }

            if (scope.FirstSeenUtc == default(DateTime))
            {
                scope.FirstSeenUtc = DateTime.UtcNow;
            }

            try
            {
                scope.ModelFileLastWriteUtc = File.Exists(modelPath) ? File.GetLastWriteTimeUtc(modelPath) : (DateTime?)null;
            }
            catch
            {
                scope.ModelFileLastWriteUtc = null;
            }
        }

        private static string SegmentToken(ModelItem item)
        {
            return (item?.DisplayName ?? string.Empty) + "\u001f" + (item?.ClassName ?? string.Empty);
        }
    }
}
