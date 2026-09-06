using Autodesk.Navisworks.Api.Plugins;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.Plugin
{
    [Plugin(PluginConstants.OpenAddInPluginName, PluginConstants.DeveloperId,
        DisplayName = "★ 打开收藏夹", ToolTip = "打开 NavisFavorites 收藏夹窗格")]
    [AddInPlugin(AddInLocation.AddIn)]
    public sealed class OpenFavoritesAddInPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            PluginRuntime.ShowDockPane();
            return 0;
        }
    }

    [Plugin(PluginConstants.AddSelectionAddInPluginName, PluginConstants.DeveloperId,
        DisplayName = "★ 收藏当前选择…", ToolTip = "把当前选择加入 NavisFavorites")]
    [AddInPlugin(AddInLocation.AddIn)]
    public sealed class AddSelectionAddInPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            PluginRuntime.AddCurrentSelectionWithDialog();
            return 0;
        }

        public override CommandState CanExecute()
        {
            var document = NwApplication.ActiveDocument;
            return new CommandState(document != null && !document.IsClear && !document.CurrentSelection.IsEmpty);
        }
    }

    [Plugin(PluginConstants.MigrationAddInPluginName, PluginConstants.DeveloperId,
        DisplayName = "版本迁移…", ToolTip = "把旧版模型收藏迁移到当前修改版")]
    [AddInPlugin(AddInLocation.AddIn)]
    public sealed class MigrationAddInPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            PluginRuntime.ShowMigrationDialog();
            return 0;
        }

        public override CommandState CanExecute()
        {
            var document = NwApplication.ActiveDocument;
            return new CommandState(document != null && !document.IsClear && document.Models.Count > 0);
        }
    }
}
