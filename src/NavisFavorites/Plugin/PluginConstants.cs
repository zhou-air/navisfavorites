namespace NavisFavorites.Plugin
{
    internal static class PluginConstants
    {
        public const string DeveloperId = "2E3F445B-6C6E-4A98-9A9C-203680A746B8";
        public const string CommandPluginName = "NavisFavorites.Commands";
        public const string DockPanePluginName = "NavisFavorites.DockPane";
        public const string ContextMenuPluginName = "NavisFavorites.AddCurrentSelection";
        public const string OpenAddInPluginName = "NavisFavorites.OpenAddIn";
        public const string AddSelectionAddInPluginName = "NavisFavorites.AddSelectionAddIn";
        public const string MigrationAddInPluginName = "NavisFavorites.MigrationAddIn";
        public const string DiagnosticsPluginName = "NavisFavorites.Diagnostics";

        public const string OpenFavoritesCommand = "NavisFavorites.OpenPane";
        public const string AddCurrentSelectionCommand = "NavisFavorites.AddSelection";
        public const string MatchRevisionCommand = "NavisFavorites.MatchRevision";
        public const string RibbonTabId = "NavisFavorites.Tab";

        public static string DockPanePluginId => DockPanePluginName + "." + DeveloperId;
    }
}
