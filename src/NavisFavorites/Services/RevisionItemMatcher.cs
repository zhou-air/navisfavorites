using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class RevisionItemMatcher
    {
        public MigrationItemResult Match(
            Document document,
            Model targetModel,
            FavoriteFolder sourceFolder,
            FavoriteItem favorite,
            IReadOnlyList<ModelItem> allTargetItems)
        {
            var result = new MigrationItemResult
            {
                SourceFolder = sourceFolder,
                SourceItem = favorite
            };

            var modelIndex = document.Models.IndexOf(targetModel);
            if (!string.IsNullOrWhiteSpace(favorite.PathId))
            {
                try
                {
                    var candidate = document.Models.ResolvePathId(new ModelItemPathId
                    {
                        ModelIndex = modelIndex,
                        PathId = favorite.PathId
                    });
                    if (IsExpectedIgnoringRoot(candidate, targetModel, favorite))
                    {
                        result.TargetItem = candidate;
                        result.Method = "PathId";
                        return result;
                    }
                }
                catch
                {
                    // 继续尝试 IndexPath 和层级匹配。
                }
            }

            if (favorite.IndexPath != null && favorite.IndexPath.Count > 0)
            {
                var adjusted = new List<int>(favorite.IndexPath);
                adjusted[0] = modelIndex;
                foreach (var path in new[] { adjusted, favorite.IndexPath }.Distinct(new IntegerPathComparer()))
                {
                    try
                    {
                        var candidate = document.Models.ResolveIndexPath(path);
                        if (IsExpectedIgnoringRoot(candidate, targetModel, favorite))
                        {
                            result.TargetItem = candidate;
                            result.Method = "IndexPath";
                            return result;
                        }
                    }
                    catch
                    {
                        // 继续尝试层级匹配。
                    }
                }
            }

            var exact = ResolveExactHierarchyIgnoringRoot(targetModel, favorite, out var hierarchyAmbiguous);
            if (exact != null)
            {
                result.TargetItem = exact;
                result.Method = "ExactHierarchy";
                return result;
            }

            var leaf = favorite.HierarchyPath?.LastOrDefault();
            if (leaf == null)
            {
                result.FailureReason = "收藏缺少层级指纹。";
                return result;
            }

            var leafCandidates = (allTargetItems ?? new List<ModelItem>())
                .Where(item => string.Equals(item.DisplayName ?? string.Empty, favorite.DisplayName ?? string.Empty,
                                   StringComparison.Ordinal)
                               && string.Equals(item.ClassName ?? string.Empty, leaf.ClassName ?? string.Empty,
                                   StringComparison.Ordinal))
                .ToList();
            if (leafCandidates.Count == 1)
            {
                result.TargetItem = leafCandidates[0];
                result.Method = "UniqueLeaf";
                return result;
            }

            if (leafCandidates.Count > 1)
            {
                var scored = leafCandidates.Select(item => new
                    {
                        Item = item,
                        Score = MatchingAncestorSuffixLength(item, favorite)
                    })
                    .OrderByDescending(item => item.Score)
                    .ToList();
                if (scored[0].Score > 0 && (scored.Count == 1 || scored[0].Score > scored[1].Score))
                {
                    result.TargetItem = scored[0].Item;
                    result.Method = "UniqueAncestorSuffix";
                    return result;
                }

                result.IsAmbiguous = true;
                result.FailureReason = $"新版中存在 {leafCandidates.Count} 个同名同类型节点。";
                return result;
            }

            result.IsAmbiguous = hierarchyAmbiguous;
            result.FailureReason = hierarchyAmbiguous ? "精确层级中出现重名节点。" : "新版中未找到同名同类型节点。";
            return result;
        }

        private static ModelItem ResolveExactHierarchyIgnoringRoot(
            Model model,
            FavoriteItem favorite,
            out bool ambiguous)
        {
            ambiguous = false;
            if (model?.RootItem == null || favorite.HierarchyPath == null || favorite.HierarchyPath.Count < 2)
            {
                return null;
            }

            var current = model.RootItem;
            for (var index = 1; index < favorite.HierarchyPath.Count; index++)
            {
                var segment = favorite.HierarchyPath[index];
                var matches = current.Children.Where(child => Matches(child, segment)).Take(2).ToList();
                if (matches.Count != 1)
                {
                    ambiguous = matches.Count > 1;
                    return null;
                }

                current = matches[0];
            }

            return current;
        }

        private static bool IsExpectedIgnoringRoot(ModelItem candidate, Model model, FavoriteItem favorite)
        {
            if (candidate == null || !ModelOwnership.BelongsTo(candidate, model)
                                  || !string.Equals(candidate.DisplayName ?? string.Empty,
                                      favorite.DisplayName ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            if (favorite.HierarchyPath == null || favorite.HierarchyPath.Count == 0)
            {
                return true;
            }

            var actual = ModelItemResolver.BuildHierarchy(candidate);
            if (actual.Count != favorite.HierarchyPath.Count)
            {
                return false;
            }

            for (var index = 1; index < actual.Count; index++)
            {
                if (!Matches(actual[index], favorite.HierarchyPath[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int MatchingAncestorSuffixLength(ModelItem candidate, FavoriteItem favorite)
        {
            var actual = ModelItemResolver.BuildHierarchy(candidate);
            var expected = favorite.HierarchyPath ?? new List<HierarchySegment>();
            var actualIndex = actual.Count - 2;
            var expectedIndex = expected.Count - 2;
            var score = 0;
            while (actualIndex >= 1 && expectedIndex >= 1 && Matches(actual[actualIndex], expected[expectedIndex]))
            {
                score++;
                actualIndex--;
                expectedIndex--;
            }

            return score;
        }

        private static bool Matches(ModelItem item, HierarchySegment segment)
        {
            return item != null && segment != null
                   && string.Equals(item.DisplayName ?? string.Empty, segment.DisplayName ?? string.Empty,
                       StringComparison.Ordinal)
                   && string.Equals(item.ClassName ?? string.Empty, segment.ClassName ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private static bool Matches(HierarchySegment actual, HierarchySegment expected)
        {
            return actual != null && expected != null
                   && string.Equals(actual.DisplayName ?? string.Empty, expected.DisplayName ?? string.Empty,
                       StringComparison.Ordinal)
                   && string.Equals(actual.ClassName ?? string.Empty, expected.ClassName ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private sealed class IntegerPathComparer : IEqualityComparer<IList<int>>
        {
            public bool Equals(IList<int> left, IList<int> right)
            {
                return ReferenceEquals(left, right) || left != null && right != null && left.SequenceEqual(right);
            }

            public int GetHashCode(IList<int> value)
            {
                unchecked
                {
                    return (value ?? new int[0]).Aggregate(17, (current, item) => current * 31 + item);
                }
            }
        }
    }
}
