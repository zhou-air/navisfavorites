using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    /// <summary>
    /// 只使用官方稳定标识和精确层级匹配，不进行模糊搜索。
    /// </summary>
    public sealed class ModelItemResolver
    {
        public Model FindLiveModel(Document document, ModelFavoriteScope scope)
        {
            if (document == null || document.IsClear || scope == null)
            {
                return null;
            }

            var models = document.Models.ToList();
            var byModelFile = models.Where(model => PathUtilities.EqualsPath(model.FileName, scope.ModelFileName)).ToList();
            if (byModelFile.Count == 1)
            {
                return byModelFile[0];
            }

            if (scope.SourceGuid != Guid.Empty)
            {
                var bySourceGuid = models.Where(model => model.SourceGuid == scope.SourceGuid).ToList();
                if (bySourceGuid.Count == 1)
                {
                    return bySourceGuid[0];
                }
            }

            var bySourceFile = models.Where(model => PathUtilities.EqualsPath(model.SourceFileName, scope.SourceFileName)).ToList();
            return bySourceFile.Count == 1 ? bySourceFile[0] : null;
        }

        public ModelItem Resolve(Document document, ModelFavoriteScope scope, FavoriteItem favorite)
        {
            if (document == null || favorite == null)
            {
                return null;
            }

            var model = FindLiveModel(document, scope);
            if (model == null)
            {
                return null;
            }

            var modelIndex = document.Models.IndexOf(model);
            if (!string.IsNullOrWhiteSpace(favorite.PathId))
            {
                try
                {
                    var pathId = new ModelItemPathId
                    {
                        ModelIndex = modelIndex,
                        PathId = favorite.PathId
                    };
                    var candidate = document.Models.ResolvePathId(pathId);
                    if (IsExpected(candidate, model, favorite))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // 继续尝试 IndexPath 和精确层级路径。
                }
            }

            if (favorite.IndexPath != null && favorite.IndexPath.Count > 0)
            {
                var paths = new List<List<int>> { new List<int>(favorite.IndexPath) };
                var adjusted = new List<int>(favorite.IndexPath);
                adjusted[0] = modelIndex;
                if (!adjusted.SequenceEqual(favorite.IndexPath))
                {
                    paths.Insert(0, adjusted);
                }

                foreach (var path in paths)
                {
                    try
                    {
                        var candidate = document.Models.ResolveIndexPath(path);
                        if (IsExpected(candidate, model, favorite))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // 单个回退失败不阻断后续解析。
                    }
                }
            }

            return ResolveExactHierarchy(model, favorite);
        }

        public bool IsValid(Document document, ModelFavoriteScope scope, FavoriteItem favorite)
        {
            return Resolve(document, scope, favorite) != null;
        }

        private static ModelItem ResolveExactHierarchy(Model model, FavoriteItem favorite)
        {
            if (favorite.HierarchyPath == null || favorite.HierarchyPath.Count == 0)
            {
                return null;
            }

            var current = model.RootItem;
            var first = favorite.HierarchyPath[0];
            if (!MatchesSegment(current, first))
            {
                return null;
            }

            for (var index = 1; index < favorite.HierarchyPath.Count; index++)
            {
                var segment = favorite.HierarchyPath[index];
                var matches = current.Children.Where(child => MatchesSegment(child, segment)).Take(2).ToList();
                if (matches.Count != 1)
                {
                    return null;
                }

                current = matches[0];
            }

            return IsExpected(current, model, favorite) ? current : null;
        }

        private static bool IsExpected(ModelItem candidate, Model expectedModel, FavoriteItem favorite)
        {
            if (candidate == null || !ModelOwnership.BelongsTo(candidate, expectedModel))
            {
                return false;
            }

            if (!string.Equals(candidate.DisplayName ?? string.Empty, favorite.DisplayName ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }

            var actualPath = BuildHierarchy(candidate);
            if (favorite.HierarchyPath == null || favorite.HierarchyPath.Count == 0)
            {
                return true;
            }

            if (actualPath.Count != favorite.HierarchyPath.Count)
            {
                return false;
            }

            for (var index = 0; index < actualPath.Count; index++)
            {
                if (!MatchesSegment(actualPath[index], favorite.HierarchyPath[index]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static List<ModelItem> BuildItemHierarchy(ModelItem item)
        {
            var path = new List<ModelItem>();
            var current = item;
            while (current != null)
            {
                path.Add(current);
                current = current.Parent;
            }

            path.Reverse();
            return path;
        }

        internal static List<HierarchySegment> BuildHierarchy(ModelItem item)
        {
            return BuildItemHierarchy(item).Select(ToSegment).ToList();
        }

        internal static HierarchySegment ToSegment(ModelItem item)
        {
            return new HierarchySegment
            {
                DisplayName = item.DisplayName ?? string.Empty,
                ClassName = item.ClassName ?? string.Empty,
                ClassDisplayName = item.ClassDisplayName ?? string.Empty
            };
        }

        private static bool MatchesSegment(ModelItem item, HierarchySegment segment)
        {
            return item != null
                   && segment != null
                   && string.Equals(item.DisplayName ?? string.Empty, segment.DisplayName ?? string.Empty, StringComparison.Ordinal)
                   && string.Equals(item.ClassName ?? string.Empty, segment.ClassName ?? string.Empty, StringComparison.Ordinal)
                   && string.Equals(item.ClassDisplayName ?? string.Empty, segment.ClassDisplayName ?? string.Empty, StringComparison.Ordinal);
        }

        private static bool MatchesSegment(HierarchySegment actual, HierarchySegment expected)
        {
            return actual != null
                   && expected != null
                   && string.Equals(actual.DisplayName ?? string.Empty, expected.DisplayName ?? string.Empty, StringComparison.Ordinal)
                   && string.Equals(actual.ClassName ?? string.Empty, expected.ClassName ?? string.Empty, StringComparison.Ordinal)
                   && string.Equals(actual.ClassDisplayName ?? string.Empty, expected.ClassDisplayName ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
