using Autodesk.Navisworks.Api.Plugins;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.Plugin
{
    [Plugin(PluginConstants.CommandPluginName, PluginConstants.DeveloperId,
        DisplayName = "NavisFavorites", ToolTip = "打开收藏夹或收藏当前选择")]
    [RibbonLayout("NavisFavoritesRibbon.xaml")]
    [RibbonTab(PluginConstants.RibbonTabId, DisplayName = "收藏夹")]
    [Command(PluginConstants.OpenFavoritesCommand, DisplayName = "★ 收藏夹", ToolTip = "打开收藏夹窗格")]
    [Command(PluginConstants.AddCurrentSelectionCommand, DisplayName = "★ 收藏当前选择…", ToolTip = "把 Navisworks 当前选择加入收藏夹")]
    [Command(PluginConstants.MatchRevisionCommand, DisplayName = "版本迁移…", ToolTip = "把旧版模型收藏安全匹配到当前修改版")]
    public sealed class NavisFavoritesCommandPlugin : CommandHandlerPlugin
    {
        public override int ExecuteCommand(string name, params string[] parameters)
        {
            switch (name)
            {
                case PluginConstants.OpenFavoritesCommand:
                    PluginRuntime.ShowDockPane();
                    break;
                case PluginConstants.AddCurrentSelectionCommand:
                    PluginRuntime.AddCurrentSelectionWithDialog();
                    break;
                case PluginConstants.MatchRevisionCommand:
                    PluginRuntime.ShowMigrationDialog();
                    break;
            }

            return 0;
        }

        public override CommandState CanExecuteCommand(string name)
        {
            if (name == PluginConstants.OpenFavoritesCommand)
            {
                return new CommandState(true);
            }

            var document = NwApplication.ActiveDocument;
            if (name == PluginConstants.MatchRevisionCommand)
            {
                return new CommandState(document != null && !document.IsClear && document.Models.Count > 0);
            }

            return new CommandState(document != null && !document.IsClear && !document.CurrentSelection.IsEmpty);
        }

        public override bool CanExecuteRibbonTab(string ribbonTabId) => true;
    }
}
