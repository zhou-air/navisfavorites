using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisFavorites.Services;
using NavisFavorites.Storage;
using NavisFavorites.UI;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.Plugin
{
    internal static class PluginRuntime
    {
        private static readonly Lazy<ModelItemResolver> ResolverValue = new Lazy<ModelItemResolver>(() => new ModelItemResolver());
        private static readonly Lazy<ModelFamilyMatcher> FamilyMatcherValue = new Lazy<ModelFamilyMatcher>(() => new ModelFamilyMatcher());
        private static readonly Lazy<RevisionItemMatcher> RevisionMatcherValue = new Lazy<RevisionItemMatcher>(() => new RevisionItemMatcher());
        private static readonly Lazy<FavoritesStorage> StorageValue = new Lazy<FavoritesStorage>(() => new FavoritesStorage());
        private static readonly Lazy<FavoritesService> FavoritesValue = new Lazy<FavoritesService>(() =>
            new FavoritesService(StorageValue.Value, ResolverValue.Value, FamilyMatcherValue.Value));
        private static readonly Lazy<SelectionService> SelectionValue = new Lazy<SelectionService>(() => new SelectionService(ResolverValue.Value));
        private static readonly Lazy<VisibilityService> VisibilityValue = new Lazy<VisibilityService>(() => new VisibilityService(SelectionValue.Value));
        private static readonly Lazy<FavoritesMigrationService> MigrationValue = new Lazy<FavoritesMigrationService>(() =>
            new FavoritesMigrationService(FavoritesValue.Value, FamilyMatcherValue.Value, RevisionMatcherValue.Value));

        public static event EventHandler<string> StatusChanged;

        public static ModelItemResolver Resolver => ResolverValue.Value;

        public static FavoritesService Favorites => FavoritesValue.Value;

        public static SelectionService Selection => SelectionValue.Value;

        public static VisibilityService Visibility => VisibilityValue.Value;

        public static FavoritesMigrationService Migration => MigrationValue.Value;

        public static string LastStatus { get; private set; } = "就绪";

        public static void PublishStatus(string message)
        {
            LastStatus = string.IsNullOrWhiteSpace(message) ? "就绪" : message;
            StatusChanged?.Invoke(null, LastStatus);
        }

        public static OperationResult AddCurrentSelectionWithDialog(IWin32Window owner = null)
        {
            var document = NwApplication.ActiveDocument;
            var check = Favorites.TryGetCurrentSelectionContext(document, out var context);
            if (!check.Success)
            {
                PublishStatus(check.Message);
                MessageBox.Show(owner, check.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return check;
            }

            Favorites.EnsureDefaultFolder(context.Scope);
            using (var dialog = new FolderPickerDialog(Favorites, context.Scope))
            {
                var dialogResult = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (dialogResult != DialogResult.OK)
                {
                    return OperationResult.Fail("已取消添加。");
                }

                var result = Favorites.AddCurrentSelection(context, dialog.SelectedFolder);
                PublishStatus(result.Message);
                if (result.Success)
                {
                    ShowDockPane();
                }

                return result;
            }
        }

        public static OperationResult ShowDockPane()
        {
            try
            {
                var record = NwApplication.Plugins.FindPlugin(PluginConstants.DockPanePluginId) as DockPanePluginRecord;
                if (record == null)
                {
                    return OperationResult.Fail("未找到收藏夹 DockPane 插件记录。");
                }

                var pane = record.TryLoadPlugin();
                if (pane == null)
                {
                    return OperationResult.Fail("收藏夹 DockPane 加载失败。");
                }

                pane.Visible = true;
                pane.ActivatePane();
                PublishStatus("已打开收藏夹。");
                return OperationResult.Ok("已打开收藏夹。");
            }
            catch (Exception ex)
            {
                var result = OperationResult.Fail("打开收藏夹失败：" + ex.Message);
                PublishStatus(result.Message);
                MessageBox.Show(result.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return result;
            }
        }

        public static MigrationReport CheckAutomaticInheritance()
        {
            var document = NwApplication.ActiveDocument;
            if (document == null || document.IsClear)
            {
                return null;
            }

            MigrationReport last = null;
            foreach (var model in document.Models)
            {
                var report = Migration.TryAutoInherit(document, model);
                if (report != null)
                {
                    last = report;
                }
            }

            if (last != null && !string.IsNullOrWhiteSpace(last.Message))
            {
                PublishStatus(last.Message);
            }

            return last;
        }

        public static OperationResult ShowMigrationDialog(IWin32Window owner = null, Autodesk.Navisworks.Api.Model preferredModel = null)
        {
            var document = NwApplication.ActiveDocument;
            if (document == null || document.IsClear || document.Models.Count == 0)
            {
                var failure = OperationResult.Fail("当前没有可迁移的目标模型。");
                PublishStatus(failure.Message);
                MessageBox.Show(owner, failure.Message, "NavisFavorites", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return failure;
            }

            using (var dialog = new MigrationDialog(Migration, document, preferredModel))
            {
                var result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result != DialogResult.OK || dialog.AppliedReport == null)
                {
                    return OperationResult.Fail("已取消版本迁移。");
                }

                PublishStatus(dialog.AppliedReport.Message);
                ShowDockPane();
                return OperationResult.Ok(dialog.AppliedReport.Message,
                    dialog.AppliedReport.MatchedCount, dialog.AppliedReport.SkippedCount);
            }
        }
    }
}
