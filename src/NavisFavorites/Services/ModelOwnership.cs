using System;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace NavisFavorites.Services
{
    /// <summary>
    /// 部分 Selection Tree 父节点的 Model 属性为空；以 CreatePathId.ModelIndex 为权威归属。
    /// </summary>
    public static class ModelOwnership
    {
        public static Model GetModel(Document document, ModelItem item)
        {
            if (document == null || item == null)
            {
                return null;
            }

            try
            {
                var pathId = document.Models.CreatePathId(item);
                if (pathId != null && pathId.ModelIndex >= 0 && pathId.ModelIndex < document.Models.Count)
                {
                    return document.Models[pathId.ModelIndex];
                }
            }
            catch
            {
                // 继续使用公开的 Model/根节点关系回退。
            }

            if (item.HasModel && item.Model != null)
            {
                return item.Model;
            }

            var root = GetRoot(item);
            return document.Models.FirstOrDefault(model => model.RootItem.Equals(root));
        }

        public static ModelItem GetRoot(ModelItem item)
        {
            var current = item;
            while (current?.Parent != null)
            {
                current = current.Parent;
            }

            return current;
        }

        public static bool BelongsTo(ModelItem item, Model model)
        {
            if (item == null || model == null)
            {
                return false;
            }

            if (item.HasModel && item.Model != null)
            {
                return item.Model.Equals(model);
            }

            return model.RootItem.Equals(GetRoot(item));
        }
    }
}
