using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisFavorites.Models;
using NavisFavorites.Storage;
using NavisFavorites.UI;
using Newtonsoft.Json;

namespace NavisFavorites.CoreTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var reportPath = args.Length > 0
                ? args[0]
                : Path.Combine(Environment.CurrentDirectory, "core-tests.json");
            var tempRoot = Path.Combine(Path.GetTempPath(), "NavisFavorites-CoreTests-" + Guid.NewGuid().ToString("N"));
            var checks = new List<Dictionary<string, object>>();
            try
            {
                Directory.CreateDirectory(tempRoot);
                TestJsonStorage(tempRoot, checks);
                TestMultiSelection(checks);
                TestModelFamilyNames(checks);
            }
            catch (Exception ex)
            {
                AddCheck(checks, "UNHANDLED", false, ex.ToString());
            }
            finally
            {
                if (Directory.Exists(tempRoot)
                    && Path.GetFullPath(tempRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(tempRoot, true);
                }
            }

            var success = checks.All(check => (bool)check["passed"]);
            var report = new Dictionary<string, object>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                ["success"] = success,
                ["checks"] = checks
            };
            var reportDirectory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(reportDirectory))
            {
                Directory.CreateDirectory(reportDirectory);
            }

            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine(success ? "CORE_TESTS_OK" : "CORE_TESTS_FAILED");
            return success ? 0 : 1;
        }

        private static void TestJsonStorage(string tempRoot, ICollection<Dictionary<string, object>> checks)
        {
            var path = Path.Combine(tempRoot, "favorites.json");
            var store = new JsonFileStore<FavoritesStore>(path);
            var source = new FavoritesStore
            {
                Models = new List<ModelFavoriteScope>
                {
                    new ModelFavoriteScope
                    {
                        DisplayName = "启创模型",
                        Folders = new List<FavoriteFolder>
                        {
                            new FavoriteFolder
                            {
                                Name = "当前工作",
                                Items = new List<FavoriteItem> { new FavoriteItem { DisplayName = "/T1081", PathId = "1-2-3" } }
                            }
                        }
                    }
                }
            };

            store.Save(source);
            var raw = File.ReadAllText(path);
            AddCheck(checks, "JSON_HUMAN_READABLE", raw.Contains(Environment.NewLine) && raw.Contains("当前工作") && raw.Contains("/T1081"),
                "Indented UTF-8 JSON contains Chinese names and favorite nodes.");

            var loaded = store.Load();
            AddCheck(checks, "JSON_ROUNDTRIP",
                loaded.Value.Models.Single().Folders.Single().Items.Single().DisplayName == "/T1081",
                loaded.Warning);

            source.Models[0].Folders[0].Name = "设备检查";
            store.Save(source);
            AddCheck(checks, "ATOMIC_BACKUP", File.Exists(path + ".bak")
                && File.ReadAllText(path + ".bak").Contains("当前工作"), "Previous valid JSON is retained as .bak.");

            File.WriteAllText(path, "{ damaged json");
            var damaged = store.Load();
            var corruptCopies = Directory.GetFiles(tempRoot, "favorites.json.corrupt-*");
            AddCheck(checks, "CORRUPT_PRESERVED", damaged.Value.Models.Count == 0
                && !string.IsNullOrWhiteSpace(damaged.Warning)
                && corruptCopies.Length == 1
                && File.ReadAllText(path).Contains("damaged"),
                damaged.Warning);
        }

        private static void TestMultiSelection(ICollection<Dictionary<string, object>> checks)
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var c = Guid.NewGuid();
            var d = Guid.NewGuid();
            var order = new[] { a, b, c, d };
            var selected = new HashSet<Guid>();

            MultiSelectionLogic.SelectSingle(selected, b);
            var single = selected.SetEquals(new[] { b });
            MultiSelectionLogic.Toggle(selected, d);
            var ctrl = selected.SetEquals(new[] { b, d });
            MultiSelectionLogic.SelectRange(selected, order, b, c, false);
            var shift = selected.SetEquals(new[] { b, c });
            selected.Add(a);
            MultiSelectionLogic.SelectRange(selected, order, b, d, true);
            var ctrlShift = selected.SetEquals(new[] { a, b, c, d });
            MultiSelectionLogic.SelectAll(selected, new[] { a, c });
            var selectAll = selected.SetEquals(new[] { a, c });

            AddCheck(checks, "MULTI_SELECTION", single && ctrl && shift && ctrlShift && selectAll,
                $"single={single}; ctrl={ctrl}; shift={shift}; ctrlShift={ctrlShift}; selectAll={selectAll}");
        }

        private static void TestModelFamilyNames(ICollection<Dictionary<string, object>> checks)
        {
            var samples = new Dictionary<string, string>
            {
                ["QICHUANG-SITE-2026-08-21.nwc"] = "QICHUANG-SITE",
                ["QICHUANG-SITE-20260827.nwc"] = "QICHUANG-SITE",
                ["QICHUANG-SITE-REV03.nwc"] = "QICHUANG-SITE",
                ["QICHUANG-SITE-V2.1.nwc"] = "QICHUANG-SITE",
                ["QICHUANG-SITE-修改版2.nwc"] = "QICHUANG-SITE"
            };
            var normalized = samples.All(sample =>
                string.Equals(ModelFamilyName.GetFamilyKey(sample.Key), sample.Value, StringComparison.Ordinal));
            var dateToken = ModelFamilyName.GetRevisionToken("QICHUANG-SITE-2026-08-21.nwc");
            var versionToken = ModelFamilyName.GetRevisionToken("QICHUANG-SITE-V2.1.nwc");
            long dateOrder = 0;
            long versionOrder = 0;
            var orderOk = ModelFamilyName.TryGetRevisionOrder(dateToken, out dateOrder)
                          && dateOrder == 20260821
                          && ModelFamilyName.TryGetRevisionOrder(versionToken, out versionOrder)
                          && versionOrder == 20001;
            AddCheck(checks, "MODEL_FAMILY_NAMES", normalized && orderOk,
                $"normalized={normalized}; date={dateToken}/{dateOrder}; version={versionToken}/{versionOrder}");
        }

        private static void AddCheck(ICollection<Dictionary<string, object>> checks, string code, bool passed, string detail)
        {
            checks.Add(new Dictionary<string, object>
            {
                ["code"] = code,
                ["passed"] = passed,
                ["detail"] = detail ?? string.Empty
            });
        }
    }
}
