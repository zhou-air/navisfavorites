using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class SelectionService
    {
        private readonly ModelItemResolver _resolver;

        public SelectionService(ModelItemResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public ResolvedFavorites Resolve(Document document, ModelFavoriteScope scope, IEnumerable<FavoriteItem> favorites)
        {
            var result = new ResolvedFavorites();
            var seen = new HashSet<ModelItem>();
            foreach (var favorite in favorites ?? Enumerable.Empty<FavoriteItem>())
            {
                var item = _resolver.Resolve(document, scope, favorite);
                if (item == null)
                {
                    result.InvalidCount++;
                    continue;
                }

                if (seen.Add(item))
                {
                    result.Items.Add(item);
                }
            }

            return result;
        }

        public OperationResult Select(Document document, ModelFavoriteScope scope, IEnumerable<FavoriteItem> favorites)
        {
            if (document == null || document.IsClear)
            {
                return OperationResult.Fail("当前没有打开模型。");
            }

            var resolved = Resolve(document, scope, favorites);
            if (resolved.Items.Count == 0)
            {
                return OperationResult.Fail(resolved.InvalidCount > 0
                    ? $"所选收藏均已失效（{resolved.InvalidCount} 项）。"
                    : "请先在收藏树中选择项目。");
            }

            try
            {
                document.CurrentSelection.CopyFrom(resolved.Items);
                return OperationResult.Ok(BuildMessage("已选择", resolved), resolved.Items.Count, resolved.InvalidCount);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("更新 Navisworks 当前选择失败：" + ex.Message);
            }
        }

        internal static string BuildMessage(string action, ResolvedFavorites resolved)
        {
            return resolved.InvalidCount == 0
                ? $"{action} {resolved.Items.Count} 项。"
                : $"{action} {resolved.Items.Count} 项，忽略失效收藏 {resolved.InvalidCount} 项。";
        }
    }
}
