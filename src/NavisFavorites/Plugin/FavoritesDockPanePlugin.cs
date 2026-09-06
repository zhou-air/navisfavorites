using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisFavorites.UI;

namespace NavisFavorites.Plugin
{
    [Plugin(PluginConstants.DockPanePluginName, PluginConstants.DeveloperId,
        DisplayName = "★ 收藏夹", ToolTip = "Navisworks 常用节点收藏夹")]
    [DockPanePlugin(380, 620, MinimumWidth = 280, MinimumHeight = 320, AutoScroll = false, FixedSize = false)]
    public sealed class FavoritesDockPanePlugin : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            return new FavoritesPaneControl();
        }

        public override void DestroyControlPane(Control pane)
        {
            pane?.Dispose();
        }
    }
}
