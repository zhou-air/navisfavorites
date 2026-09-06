using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class VisibilityService
    {
        private readonly SelectionService _selectionService;

        public VisibilityService(SelectionService selectionService)
        {
            _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        }

        public OperationResult Hide(Document document, ModelFavoriteScope scope, IEnumerable<FavoriteItem> favorites)
        {
            return Apply(document, scope, favorites, (doc, resolved) =>
            {
                var targets = ExpandDescendants(resolved.Items);
                doc.Models.SetHidden(targets, true);
                return SelectionService.BuildMessage("已隐藏", resolved);
            });
        }

        public OperationResult Show(Document document, ModelFavoriteScope scope, IEnumerable<FavoriteItem> favorites)
        {
            return Apply(document, scope, favorites, (doc, resolved) =>
            {
                var targets = ExpandDescendants(resolved.Items);
                foreach (var item in resolved.Items)
                {
                    foreach (var ancestor in item.AncestorsAndSelf)
                    {
                        targets.Add(ancestor);
                    }
                }

                doc.Models.SetHidden(targets, false);
                return SelectionService.BuildMessage("已显示", resolved);
            });
        }

        public OperationResult HideUnselected(Document document, ModelFavoriteScope scope, IEnumerable<FavoriteItem> favorites)
        {
            return Apply(document, scope, favorites, (doc, resolved) =>
            {
                var keepVisible = ExpandDescendants(resolved.Items);
                foreach (var item in resolved.Items)
                {
                    foreach (var ancestor in item.AncestorsAndSelf)
                    {
                        keepVisible.Add(ancestor);
                    }
                }

                // 先确保保留集合可见，再隐藏补集；不采用“隐藏全部后再显示子节点”。
                doc.Models.SetHidden(keepVisible, false);
                var hide = doc.Models.RootItemDescendantsAndSelf.Where(item => !keepVisible.Contains(item)).ToList();
                if (hide.Count > 0)
                {
                    doc.Models.SetHidden(hide, true);
                }

                return resolved.InvalidCount == 0
                    ? $"已仅显示 {resolved.Items.Count} 个收藏节点及其层级。"
                    : $"已仅显示 {resolved.Items.Count} 个有效节点，忽略失效收藏 {resolved.InvalidCount} 项。";
            });
        }

        public OperationResult ShowAll(Document document)
        {
            if (document == null || document.IsClear)
            {
                return OperationResult.Fail("当前没有打开模型。");
            }

            try
            {
                document.Models.ResetAllHidden();
                return OperationResult.Ok("已全部显示。");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("全部显示失败：" + ex.Message);
            }
        }

        private OperationResult Apply(
            Document document,
            ModelFavoriteScope scope,
            IEnumerable<FavoriteItem> favorites,
            Func<Document, ResolvedFavorites, string> action)
        {
            if (document == null || document.IsClear)
            {
                return OperationResult.Fail("当前没有打开模型。");
            }

            var resolved = _selectionService.Resolve(document, scope, favorites);
            if (resolved.Items.Count == 0)
            {
                return OperationResult.Fail(resolved.InvalidCount > 0
                    ? $"所选收藏均已失效（{resolved.InvalidCount} 项）。"
                    : "请先在收藏树中选择项目。");
            }

            try
            {
                var message = action(document, resolved);
                return OperationResult.Ok(message, resolved.Items.Count, resolved.InvalidCount);
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("可见性操作失败：" + ex.Message);
            }
        }

        private static HashSet<ModelItem> ExpandDescendants(IEnumerable<ModelItem> roots)
        {
            var result = new HashSet<ModelItem>();
            foreach (var root in roots)
            {
                foreach (var item in root.DescendantsAndSelf)
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }
}
