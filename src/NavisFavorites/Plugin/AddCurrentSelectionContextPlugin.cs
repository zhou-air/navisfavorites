using Autodesk.Navisworks.Api.Plugins;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.Plugin
{
    [Plugin(PluginConstants.ContextMenuPluginName, PluginConstants.DeveloperId,
        DisplayName = "★ 添加到收藏夹…", ToolTip = "把当前选择加入 NavisFavorites")]
    [AddInPlugin(AddInLocation.CurrentSelectionContextMenu)]
    public sealed class AddCurrentSelectionContextPlugin : AddInPlugin
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
}
