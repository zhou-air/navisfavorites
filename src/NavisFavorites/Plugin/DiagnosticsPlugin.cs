using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisFavorites.Models;
using NavisFavorites.Services;
using Newtonsoft.Json;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.Plugin
{
    /// <summary>
    /// 隐藏的开发验收入口，可由 Roamer -ExecuteAddInPlugin 调用，不显示在 Ribbon。
    /// </summary>
    [Plugin(PluginConstants.DiagnosticsPluginName, PluginConstants.DeveloperId,
        DisplayName = "NavisFavorites Diagnostics")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class DiagnosticsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var outputPath = parameters != null && parameters.Length > 0 && !string.IsNullOrWhiteSpace(parameters[0])
                ? parameters[0]
                : Path.Combine(Path.GetTempPath(), "NavisFavorites-smoke.json");
            var report = new Dictionary<string, object>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                ["pluginVersion"] = typeof(DiagnosticsPlugin).Assembly.GetName().Version.ToString(),
                ["checks"] = new List<Dictionary<string, object>>()
            };

            var checks = (List<Dictionary<string, object>>)report["checks"];
            try
            {
                AddCheck(checks, "COMMAND_PLUGIN_RECORD",
                    NwApplication.Plugins.FindPlugin(PluginConstants.CommandPluginName + "." + PluginConstants.DeveloperId) != null,
                    "命令插件记录");
                AddCheck(checks, "DOCK_PLUGIN_RECORD",
                    NwApplication.Plugins.FindPlugin(PluginConstants.DockPanePluginId) != null,
                    "DockPane 插件记录");
                AddCheck(checks, "CONTEXT_PLUGIN_RECORD",
                    NwApplication.Plugins.FindPlugin(PluginConstants.ContextMenuPluginName + "." + PluginConstants.DeveloperId) != null,
                    "右键菜单插件记录");

                var paneResult = PluginRuntime.ShowDockPane();
                AddCheck(checks, "DOCKPANE_OPEN", paneResult.Success, paneResult.Message);

                var document = NwApplication.ActiveDocument;
                var documentReady = document != null && !document.IsClear && document.Models.Count > 0;
                AddCheck(checks, "ACTIVE_DOCUMENT", documentReady,
                    documentReady ? document.FileName : "当前没有已加载模型");
                if (!documentReady)
                {
                    report["success"] = false;
                    WriteReport(outputPath, report);
                    return 2;
                }

                report["documentFile"] = document.FileName;
                report["documentSha256"] = File.Exists(document.FileName) ? ComputeSha256(document.FileName) : string.Empty;
                report["modelCount"] = document.Models.Count;

                var candidates = FindCandidates(document).Take(3).ToList();
                AddCheck(checks, "MODEL_ITEMS_FOUND", candidates.Count > 0,
                    candidates.Count == 0 ? "未找到可测试节点" : string.Join(" | ", candidates.Select(item => item.DisplayName)));
                if (candidates.Count == 0)
                {
                    report["success"] = false;
                    WriteReport(outputPath, report);
                    return 3;
                }

                var model = ModelOwnership.GetModel(document, candidates[0]);
                if (model == null)
                {
                    throw new InvalidOperationException("无法确定测试节点所属源模型。");
                }

                candidates = candidates.Where(item => ModelOwnership.GetModel(document, item)?.Equals(model) == true).ToList();
                var scope = CreateScope(model);
                var favorites = candidates.Select(item => PluginRuntime.Favorites.Capture(document, model, item)).ToList();
                var roundTrip = favorites.Count(item => PluginRuntime.Resolver.Resolve(document, scope, item) != null);
                AddCheck(checks, "REFERENCE_ROUNDTRIP", roundTrip == favorites.Count,
                    $"成功解析 {roundTrip}/{favorites.Count} 个 PathId/IndexPath 收藏引用");

                var selectResult = PluginRuntime.Selection.Select(document, scope, favorites);
                AddCheck(checks, "SELECTION", selectResult.Success && document.CurrentSelection.SelectedItems.Count == candidates.Count,
                    selectResult.Message);

                var hadHiddenItems = document.Models.RootItemDescendantsAndSelf.Any(item => item.IsHidden);
                if (hadHiddenItems)
                {
                    AddCheck(checks, "VISIBILITY", true, "检测到原有隐藏状态，为避免改变用户会话而跳过显隐自检");
                }
                else
                {
                    var single = new[] { favorites[0] };
                    var hideResult = PluginRuntime.Visibility.Hide(document, scope, single);
                    var hiddenObserved = candidates[0].IsHidden;
                    var showResult = PluginRuntime.Visibility.Show(document, scope, single);
                    var shownObserved = !candidates[0].IsHidden;
                    var isolateResult = PluginRuntime.Visibility.HideUnselected(document, scope, single);
                    var resetResult = PluginRuntime.Visibility.ShowAll(document);
                    AddCheck(checks, "VISIBILITY",
                        hideResult.Success && hiddenObserved && showResult.Success && shownObserved
                        && isolateResult.Success && resetResult.Success,
                        $"Hide={hideResult.Success}/{hiddenObserved}; Show={showResult.Success}/{shownObserved}; "
                        + $"HideUnselected={isolateResult.Success}; Reset={resetResult.Success}");
                }

                report["success"] = checks.All(check => check.TryGetValue("passed", out var value) && value is bool passed && passed);
                WriteReport(outputPath, report);
                return (bool)report["success"] ? 0 : 4;
            }
            catch (Exception ex)
            {
                AddCheck(checks, "UNHANDLED", false, ex.ToString());
                report["success"] = false;
                try
                {
                    WriteReport(outputPath, report);
                }
                catch
                {
                    // 保留原始异常返回码，避免诊断写文件再次掩盖根因。
                }

                return 1;
            }
        }

        private static IEnumerable<ModelItem> FindCandidates(Document document)
        {
            var all = document.Models.RootItemDescendantsAndSelf.ToList();
            var targetNames = new HashSet<string>(new[] { "/T1081", "T1081", "/V104", "V104", "/C106", "C106" },
                StringComparer.OrdinalIgnoreCase);
            var named = all.Where(item => targetNames.Contains(item.DisplayName ?? string.Empty)).ToList();
            if (named.Count > 0)
            {
                return named;
            }

            return all.Where(item => item.Parent != null
                                     && !string.IsNullOrWhiteSpace(item.DisplayName)
                                     && item.Children.Any());
        }

        private static ModelFavoriteScope CreateScope(Model model)
        {
            return new ModelFavoriteScope
            {
                DisplayName = model.RootItem?.DisplayName ?? string.Empty,
                ModelFileName = PathUtilities.Normalize(model.FileName),
                SourceFileName = PathUtilities.Normalize(model.SourceFileName),
                SourceGuid = model.SourceGuid
            };
        }

        private static void AddCheck(List<Dictionary<string, object>> checks, string code, bool passed, string detail)
        {
            checks.Add(new Dictionary<string, object>
            {
                ["code"] = code,
                ["passed"] = passed,
                ["detail"] = detail ?? string.Empty
            });
        }

        private static void WriteReport(string outputPath, object report)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(outputPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
