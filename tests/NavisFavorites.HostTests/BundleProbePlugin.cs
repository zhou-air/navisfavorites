using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api.Plugins;
using Newtonsoft.Json;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.HostTests
{
    [Plugin("NavisFavorites.BundleProbe4", "43E189EC-F8D3-4280-82D9-72B8F0EDAE7D",
        DisplayName = "NavisFavorites Bundle Probe 4")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class BundleProbePlugin : AddInPlugin
    {
        private const string NavisFavoritesDeveloperId = "2E3F445B-6C6E-4A98-9A9C-203680A746B8";

        public override int Execute(params string[] parameters)
        {
            var reportPath = parameters != null && parameters.Length > 0
                ? parameters[0]
                : Path.Combine(Path.GetTempPath(), "NavisFavorites-bundle-probe.json");
            var checks = new List<Dictionary<string, object>>();
            try
            {
                var command = NwApplication.Plugins.FindPlugin("NavisFavorites.Commands." + NavisFavoritesDeveloperId)
                              as CommandHandlerPluginRecord;
                Check(checks, "COMMAND_RECORD", command != null, command?.Id ?? "missing");
                if (command != null)
                {
                    var commandIds = command.CommandRecords.Select(record => record.Id).ToList();
                    Check(checks, "THREE_COMMANDS", commandIds.Count == 3
                        && commandIds.Any(id => id.Contains("NavisFavorites.OpenPane"))
                        && commandIds.Any(id => id.Contains("NavisFavorites.AddSelection"))
                        && commandIds.Any(id => id.Contains("NavisFavorites.MatchRevision")),
                        string.Join(" | ", commandIds));
                    Check(checks, "RIBBON_LAYOUT", command.RibbonLayoutRecords.Count > 0
                        && command.RibbonTabRecords.Count == 1
                        && command.RibbonTabRecords[0].DisplayName == "收藏夹",
                        $"Layouts={command.RibbonLayoutRecords.Count}; Tabs={command.RibbonTabRecords.Count}; "
                        + $"Title={command.RibbonTabRecords.FirstOrDefault()?.DisplayName}");
                    var loaded = command.TryLoadPlugin();
                    Check(checks, "ASSEMBLY_V0200", loaded != null
                        && loaded.GetType().Assembly.GetName().Version.ToString() == "0.2.0.0",
                        loaded?.GetType().Assembly.FullName ?? "not loaded");
                }

                CheckAddIn(checks, "OPEN_ADDIN", "NavisFavorites.OpenAddIn." + NavisFavoritesDeveloperId, AddInLocation.AddIn);
                CheckAddIn(checks, "ADD_SELECTION_ADDIN", "NavisFavorites.AddSelectionAddIn." + NavisFavoritesDeveloperId,
                    AddInLocation.AddIn);
                CheckAddIn(checks, "MIGRATION_ADDIN", "NavisFavorites.MigrationAddIn." + NavisFavoritesDeveloperId,
                    AddInLocation.AddIn);

                var success = checks.All(check => (bool)check["passed"]);
                Write(reportPath, new { timestampUtc = DateTime.UtcNow.ToString("O"), success, checks });
                return success ? 0 : 5;
            }
            catch (Exception ex)
            {
                Check(checks, "UNHANDLED", false, ex.ToString());
                Write(reportPath, new { timestampUtc = DateTime.UtcNow.ToString("O"), success = false, checks });
                return 1;
            }
        }

        private static void CheckAddIn(ICollection<Dictionary<string, object>> checks, string code, string id,
            AddInLocation expectedLocation)
        {
            var record = NwApplication.Plugins.FindPlugin(id) as AddInPluginRecord;
            Check(checks, code, record != null && record.Location == expectedLocation,
                record == null ? "missing" : record.Id + "; Location=" + record.Location);
        }

        private static void Check(ICollection<Dictionary<string, object>> checks, string code, bool passed,
            string detail)
        {
            checks.Add(new Dictionary<string, object>
            {
                ["code"] = code,
                ["passed"] = passed,
                ["detail"] = detail ?? string.Empty
            });
        }

        private static void Write(string path, object report)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }
}
