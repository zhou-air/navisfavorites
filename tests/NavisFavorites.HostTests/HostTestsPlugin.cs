using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisFavorites.Models;
using NavisFavorites.Services;
using NavisFavorites.Storage;
using Newtonsoft.Json;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisFavorites.HostTests
{
    [Plugin("NavisFavorites.HostTests4", "43E189EC-F8D3-4280-82D9-72B8F0EDAE7D", DisplayName = "NavisFavorites Host Tests 4")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class HostTestsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            var reportPath = parameters != null && parameters.Length > 0
                ? parameters[0]
                : Path.Combine(Path.GetTempPath(), "NavisFavorites-host-tests.json");
            var persistencePath = parameters != null && parameters.Length > 1
                ? parameters[1]
                : string.Empty;
            var persistenceMode = parameters != null && parameters.Length > 2
                ? parameters[2]
                : string.Empty;
            var checks = new List<object>();
            var report = new Dictionary<string, object>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                ["hostTestsAssembly"] = typeof(HostTestsPlugin).Assembly.FullName,
                ["testedAssembly"] = typeof(FavoritesService).Assembly.FullName,
                ["checks"] = checks
            };

            try
            {
                var document = NwApplication.ActiveDocument;
                Check(checks, "ACTIVE_DOCUMENT", document != null && !document.IsClear, document?.FileName ?? string.Empty);
                if (document == null || document.IsClear)
                {
                    return Finish(reportPath, report, false, 2);
                }

                var targetNames = new HashSet<string>(new[] { "/T1081", "T1081", "/V104", "V104", "/C106", "C106" },
                    StringComparer.OrdinalIgnoreCase);
                var candidates = document.Models.RootItemDescendantsAndSelf
                    .Where(item => targetNames.Contains(item.DisplayName ?? string.Empty))
                    .Take(3)
                    .ToList();
                if (candidates.Count == 0)
                {
                    candidates = document.Models.RootItemDescendantsAndSelf
                        .Where(item => item.Parent != null && item.Children.Any() && !string.IsNullOrWhiteSpace(item.DisplayName))
                        .Take(3)
                        .ToList();
                }

                Check(checks, "TARGETS", candidates.Count > 0,
                    string.Join(" | ", candidates.Select(item => item.DisplayName)));
                if (candidates.Count == 0)
                {
                    return Finish(reportPath, report, false, 3);
                }

                var model = ModelOwnership.GetModel(document, candidates[0]);
                Check(checks, "PARENT_MODEL_OWNERSHIP", model != null,
                    $"HasModel={candidates[0].HasModel}; ModelResolved={model != null}");
                if (model == null)
                {
                    return Finish(reportPath, report, false, 4);
                }

                candidates = candidates.Where(item => ModelOwnership.GetModel(document, item)?.Equals(model) == true).ToList();
                var testStorage = new FavoritesStorage(Path.Combine(
                    Path.GetTempPath(), "NavisFavorites-HostTests-" + Guid.NewGuid().ToString("N")));
                var resolver = new ModelItemResolver();
                var favoritesService = new FavoritesService(testStorage, resolver);
                var selectionService = new SelectionService(resolver);
                var visibilityService = new VisibilityService(selectionService);
                var scope = favoritesService.GetOrCreateScope(model);
                var favorites = candidates.Select(item => favoritesService.Capture(document, model, item)).ToList();

                var resolved = favorites.Select(item => resolver.Resolve(document, scope, item)).ToList();
                Check(checks, "REFERENCE_ROUNDTRIP", resolved.All(item => item != null),
                    $"Resolved={resolved.Count(item => item != null)}/{resolved.Count}");

                if (string.Equals(persistenceMode, "capture", StringComparison.OrdinalIgnoreCase))
                {
                    var persistedStore = new FavoritesStore();
                    scope.Folders.Add(new FavoriteFolder
                    {
                        Name = "重启验证",
                        Items = favorites
                    });
                    persistedStore.Models.Add(scope);
                    new JsonFileStore<FavoritesStore>(persistencePath).Save(persistedStore);
                    Check(checks, "PERSISTENCE_CAPTURE", File.Exists(persistencePath),
                        "Saved stable references for a separate Navisworks process.");
                }
                else if (string.Equals(persistenceMode, "resolve", StringComparison.OrdinalIgnoreCase))
                {
                    var persisted = new JsonFileStore<FavoritesStore>(persistencePath).Load();
                    var persistedScope = persisted.Value.Models.Single();
                    var persistedItems = persistedScope.Folders.Single().Items;
                    var restored = persistedItems.Select(item => resolver.Resolve(document, persistedScope, item)).ToList();
                    Check(checks, "PERSISTENCE_REOPEN", string.IsNullOrWhiteSpace(persisted.Warning)
                        && restored.Count == persistedItems.Count
                        && restored.All(item => item != null),
                        $"Resolved after reopen={restored.Count(item => item != null)}/{persistedItems.Count}");
                }
                else if (string.Equals(persistenceMode, "migrate", StringComparison.OrdinalIgnoreCase))
                {
                    TestRevisionMigration(document, model, persistencePath, checks);
                }
                else if (string.Equals(persistenceMode, "upgrade-user-copy", StringComparison.OrdinalIgnoreCase))
                {
                    TestUserDataUpgradeCopy(persistencePath, checks);
                }

                TestSchemaUpgrade(checks);
                TestMigrationFailureModes(document, model, checks);

                var selectionResult = selectionService.Select(document, scope, favorites);
                Check(checks, "SELECTION", selectionResult.Success
                    && document.CurrentSelection.SelectedItems.Count == candidates.Count, selectionResult.Message);

                var hadHiddenItems = document.Models.RootItemDescendantsAndSelf.Any(item => item.IsHidden);
                if (hadHiddenItems)
                {
                    Check(checks, "VISIBILITY", true, "Skipped because the user session already has hidden items.");
                }
                else
                {
                    var single = new[] { favorites[0] };
                    var hide = visibilityService.Hide(document, scope, single);
                    var hiddenObserved = candidates[0].IsHidden;
                    var show = visibilityService.Show(document, scope, single);
                    var shownObserved = !candidates[0].IsHidden;
                    var isolate = visibilityService.HideUnselected(document, scope, single);
                    var reset = visibilityService.ShowAll(document);
                    Check(checks, "VISIBILITY", hide.Success && hiddenObserved && show.Success && shownObserved
                        && isolate.Success && reset.Success,
                        $"Hide={hide.Success}/{hiddenObserved}; Show={show.Success}/{shownObserved}; "
                        + $"HideUnselected={isolate.Success}; Reset={reset.Success}");
                }

                var success = checks.Cast<Dictionary<string, object>>().All(item => (bool)item["passed"]);
                return Finish(reportPath, report, success, success ? 0 : 5);
            }
            catch (Exception ex)
            {
                Check(checks, "UNHANDLED", false, ex.ToString());
                return Finish(reportPath, report, false, 1);
            }
        }

        private static void Check(ICollection<object> checks, string code, bool passed, string detail)
        {
            checks.Add(new Dictionary<string, object>
            {
                ["code"] = code,
                ["passed"] = passed,
                ["detail"] = detail ?? string.Empty
            });
        }

        private static void TestRevisionMigration(
            Document document,
            Model targetModel,
            string persistencePath,
            ICollection<object> checks)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NavisFavorites-MigrationHost-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                File.Copy(persistencePath, Path.Combine(tempRoot, "favorites.json"), true);
                var familyMatcher = new ModelFamilyMatcher();
                var resolver = new ModelItemResolver();
                var service = new FavoritesService(new FavoritesStorage(tempRoot), resolver, familyMatcher);
                var source = service.Store.Models.Single();
                var oldItemCount = source.Folders.Sum(folder => folder.Items.Count);
                var migration = new FavoritesMigrationService(service, familyMatcher, new RevisionItemMatcher());
                var report = migration.TryAutoInherit(document, targetModel);
                var target = service.FindScope(targetModel);
                var migratedItems = target?.Folders.SelectMany(folder => folder.Items).ToList()
                                    ?? new List<FavoriteItem>();
                Check(checks, "REVISION_AUTO_INHERIT", report != null && report.Applied
                    && report.MatchedCount == oldItemCount && report.SkippedCount == 0,
                    report?.Message ?? "No migration report");
                Check(checks, "REVISION_NEW_REFERENCES", migratedItems.Count == oldItemCount
                    && migratedItems.All(item => item.MigratedFromItemId.HasValue)
                    && !PathUtilities.EqualsPath(source.ModelFileName, target.ModelFileName),
                    $"Old={source.ModelFileName}; New={target?.ModelFileName}; Items={migratedItems.Count}");
                Check(checks, "REVISION_SOURCE_UNCHANGED",
                    source.Folders.Sum(folder => folder.Items.Count) == oldItemCount,
                    $"Source items={oldItemCount}");

                var second = migration.TryAutoInherit(document, targetModel);
                Check(checks, "REVISION_NO_DUPLICATE", second == null
                    && target.Folders.Sum(folder => folder.Items.Count) == oldItemCount,
                    "Repeated automatic check did not duplicate migrated favorites.");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void TestSchemaUpgrade(ICollection<object> checks)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NavisFavorites-SchemaUpgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var folderId = Guid.NewGuid();
                var itemId = Guid.NewGuid();
                var v1 = new FavoritesStore
                {
                    SchemaVersion = 1,
                    Models = new List<ModelFavoriteScope>
                    {
                        new ModelFavoriteScope
                        {
                            DisplayName = "QICHUANG-SITE-2026-08-21.nwc",
                            ModelFileName = "C:\\Models\\QICHUANG-SITE-2026-08-21.nwc",
                            Folders = new List<FavoriteFolder>
                            {
                                new FavoriteFolder
                                {
                                    Id = folderId,
                                    Name = "当前工作",
                                    Items = new List<FavoriteItem>
                                    {
                                        new FavoriteItem { Id = itemId, DisplayName = "/T1081", PathId = "0/1" }
                                    }
                                }
                            }
                        }
                    }
                };
                File.WriteAllText(Path.Combine(tempRoot, "favorites.json"),
                    JsonConvert.SerializeObject(v1, Formatting.Indented));
                var service = new FavoritesService(new FavoritesStorage(tempRoot), new ModelItemResolver(),
                    new ModelFamilyMatcher());
                var upgraded = service.Store.Models.Single();
                var backups = Directory.GetFiles(tempRoot, "favorites.json.schema1-backup-*");
                Check(checks, "SCHEMA_V1_TO_V2", service.Store.SchemaVersion == 2
                    && upgraded.Folders.Single().Id == folderId
                    && upgraded.Folders.Single().Items.Single().Id == itemId
                    && upgraded.FamilyId != Guid.Empty
                    && upgraded.FamilyKey == "QICHUANG-SITE"
                    && backups.Length == 1,
                    $"Schema={service.Store.SchemaVersion}; backups={backups.Length}; family={upgraded.FamilyKey}");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void TestMigrationFailureModes(Document document, Model model, ICollection<object> checks)
        {
            var all = model.RootItem.DescendantsAndSelf.ToList();
            var sample = all.FirstOrDefault(item => item.Parent?.Parent != null
                                                    && !string.IsNullOrWhiteSpace(item.DisplayName));
            if (sample == null)
            {
                Check(checks, "MIGRATION_AMBIGUITY", false, "The test model had no deep node.");
                return;
            }

            var matcher = new RevisionItemMatcher();
            var folder = new FavoriteFolder { Name = "歧义验证" };
            var ambiguousFavorite = new FavoriteItem
            {
                DisplayName = sample.DisplayName,
                HierarchyPath = new List<HierarchySegment>
                {
                    new HierarchySegment { DisplayName = "OLD-ROOT", ClassName = "ROOT" },
                    new HierarchySegment { DisplayName = sample.DisplayName, ClassName = sample.ClassName }
                }
            };
            var duplicatedCandidates = all.Concat(new[] { sample }).ToList();
            var ambiguous = matcher.Match(document, model, folder, ambiguousFavorite, duplicatedCandidates);
            Check(checks, "MIGRATION_AMBIGUITY", !ambiguous.IsMatched && ambiguous.IsAmbiguous,
                ambiguous.FailureReason);

            var missingFavorite = new FavoriteItem
            {
                DisplayName = "/NAVISFAVORITES-NOT-FOUND-7B4C",
                HierarchyPath = new List<HierarchySegment>
                {
                    new HierarchySegment { DisplayName = "OLD-ROOT", ClassName = "ROOT" },
                    new HierarchySegment { DisplayName = "/NAVISFAVORITES-NOT-FOUND-7B4C", ClassName = "NONE" }
                }
            };
            var missing = matcher.Match(document, model, folder, missingFavorite, all);
            Check(checks, "MIGRATION_MISSING_SKIPPED", !missing.IsMatched && !missing.IsAmbiguous,
                missing.FailureReason);

            var familyMatcher = new ModelFamilyMatcher();
            var similarityOk = Math.Abs(familyMatcher.Similarity(new[] { "A", "B" }, new[] { "A", "B" }) - 1d) < 0.0001
                               && Math.Abs(familyMatcher.Similarity(new[] { "A" }, new[] { "B" })) < 0.0001;
            Check(checks, "FINGERPRINT_SIMILARITY", similarityOk,
                "Identical=1.0; disjoint=0.0");

            var familyId = Guid.NewGuid();
            var nearest = new ModelFavoriteScope
            {
                FamilyId = familyId,
                FamilyKey = "QICHUANG-SITE",
                RevisionToken = "DATE:2026-08-21",
                FirstSeenUtc = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
                StructureFingerprint = new List<string> { "A", "B" },
                Folders = new List<FavoriteFolder>
                {
                    new FavoriteFolder { Items = new List<FavoriteItem> { new FavoriteItem() } }
                }
            };
            var older = new ModelFavoriteScope
            {
                FamilyId = familyId,
                FamilyKey = "QICHUANG-SITE",
                RevisionToken = "DATE:2026-08-20",
                FirstSeenUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
                StructureFingerprint = new List<string> { "A", "B" },
                Folders = new List<FavoriteFolder>
                {
                    new FavoriteFolder { Items = new List<FavoriteItem> { new FavoriteItem() } }
                }
            };
            var selected = familyMatcher.SelectAutomaticSource(new[] { older, nearest }, "QICHUANG-SITE",
                "DATE:2026-08-27", new[] { "A", "B" }, out var selectedSimilarity, out var selectionReason);
            Check(checks, "AUTO_SOURCE_NEAREST_EARLIER", ReferenceEquals(selected, nearest)
                && Math.Abs(selectedSimilarity - 1d) < 0.0001, selectionReason);

            var tied = new ModelFavoriteScope
            {
                FamilyId = familyId,
                FamilyKey = nearest.FamilyKey,
                RevisionToken = nearest.RevisionToken,
                FirstSeenUtc = nearest.FirstSeenUtc.AddMinutes(1),
                StructureFingerprint = new List<string> { "A", "B" },
                Folders = new List<FavoriteFolder>
                {
                    new FavoriteFolder { Items = new List<FavoriteItem> { new FavoriteItem() } }
                }
            };
            selected = familyMatcher.SelectAutomaticSource(new[] { nearest, tied }, "QICHUANG-SITE",
                "DATE:2026-08-27", new[] { "A", "B" }, out selectedSimilarity, out selectionReason);
            Check(checks, "AUTO_SOURCE_TIE_REJECTED", selected == null
                && selectionReason.Contains("多个同样接近"), selectionReason);

            selected = familyMatcher.SelectAutomaticSource(new[] { nearest }, "QICHUANG-SITE",
                "DATE:2026-08-20", new[] { "A", "B" }, out selectedSimilarity, out selectionReason);
            Check(checks, "AUTO_SOURCE_FUTURE_REJECTED", selected == null
                && selectionReason.Contains("早于当前模型"), selectionReason);

            nearest.StructureFingerprint = new List<string> { "A", "X" };
            selected = familyMatcher.SelectAutomaticSource(new[] { nearest, older }, "QICHUANG-SITE",
                "DATE:2026-08-27", new[] { "A", "B" }, out selectedSimilarity, out selectionReason);
            Check(checks, "AUTO_SOURCE_DOES_NOT_FALL_BACK", selected == null
                && Math.Abs(selectedSimilarity - (1d / 3d)) < 0.0001
                && selectionReason.Contains("低于"), selectionReason);
        }

        private static void TestUserDataUpgradeCopy(string sourcePath, ICollection<object> checks)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NavisFavorites-UserUpgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var targetPath = Path.Combine(tempRoot, "favorites.json");
                File.Copy(sourcePath, targetPath, true);
                var before = JsonConvert.DeserializeObject<FavoritesStore>(File.ReadAllText(targetPath));
                var beforeFolderIds = before.Models.SelectMany(scope => scope.Folders).Select(folder => folder.Id)
                    .OrderBy(id => id).ToList();
                var beforeItemIds = before.Models.SelectMany(scope => scope.Folders).SelectMany(folder => folder.Items)
                    .Select(item => item.Id).OrderBy(id => id).ToList();
                var service = new FavoritesService(new FavoritesStorage(tempRoot), new ModelItemResolver(),
                    new ModelFamilyMatcher());
                var afterFolderIds = service.Store.Models.SelectMany(scope => scope.Folders).Select(folder => folder.Id)
                    .OrderBy(id => id).ToList();
                var afterItemIds = service.Store.Models.SelectMany(scope => scope.Folders)
                    .SelectMany(folder => folder.Items).Select(item => item.Id).OrderBy(id => id).ToList();
                var backups = Directory.GetFiles(tempRoot, "favorites.json.schema1-backup-*");
                Check(checks, "USER_V1_COPY_UPGRADE", before.SchemaVersion == 1
                    && service.Store.SchemaVersion == 2
                    && beforeFolderIds.SequenceEqual(afterFolderIds)
                    && beforeItemIds.SequenceEqual(afterItemIds)
                    && backups.Length == 1,
                    $"Models={service.Store.Models.Count}; Folders={afterFolderIds.Count}; "
                    + $"Items={afterItemIds.Count}; Backups={backups.Length}");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static int Finish(string reportPath, IDictionary<string, object> report, bool success, int code)
        {
            report["success"] = success;
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            return code;
        }
    }
}
