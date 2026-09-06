using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;
using NavisFavorites.Storage;

namespace NavisFavorites.Services
{
    public sealed class FavoritesService
    {
        private readonly FavoritesStorage _storage;
        private readonly ModelItemResolver _resolver;
        private readonly ModelFamilyMatcher _familyMatcher;

        public FavoritesService(FavoritesStorage storage, ModelItemResolver resolver, ModelFamilyMatcher familyMatcher = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _familyMatcher = familyMatcher ?? new ModelFamilyMatcher();

            var favoritesResult = _storage.LoadFavorites();
            Store = favoritesResult.Value;
            var settingsResult = _storage.LoadSettings();
            Settings = settingsResult.Value;
            LastStorageWarning = string.Join(" ", new[] { favoritesResult.Warning, settingsResult.Warning }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            var upgraded = NormalizeLoadedData();
            if (upgraded)
            {
                var backupPath = _storage.BackupFavoritesForSchemaUpgrade(1);
                _storage.SaveFavorites(Store);
                _storage.SaveSettings(Settings);
                var upgradeMessage = string.IsNullOrWhiteSpace(backupPath)
                    ? "收藏数据已升级到 schemaVersion 2。"
                    : "收藏数据已升级到 schemaVersion 2，旧版备份：" + backupPath;
                LastStorageWarning = string.Join(" ", new[] { LastStorageWarning, upgradeMessage }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }

        public event EventHandler Changed;

        public FavoritesStore Store { get; }

        public FavoritesSettings Settings { get; }

        public string LastStorageWarning { get; private set; }

        public IReadOnlyList<LiveModelContext> GetLiveModels(Document document)
        {
            if (document == null || document.IsClear)
            {
                return new List<LiveModelContext>();
            }

            return document.Models.Select(model => new LiveModelContext
            {
                Model = model,
                Scope = FindScope(model),
                DisplayName = GetModelDisplayName(model)
            }).ToList();
        }

        public ModelFavoriteScope FindScope(Model model)
        {
            if (model == null)
            {
                return null;
            }

            var modelFileMatches = Store.Models.Where(scope => PathUtilities.EqualsPath(scope.ModelFileName, model.FileName)).ToList();
            if (modelFileMatches.Count == 1)
            {
                return modelFileMatches[0];
            }

            // V2 中不同文件版本必须拥有独立引用快照。仅对没有模型文件路径的旧数据使用 Guid/源文件回退。
            var legacyScopes = Store.Models.Where(scope => string.IsNullOrWhiteSpace(scope.ModelFileName)).ToList();
            if (model.SourceGuid != Guid.Empty && legacyScopes.Count > 0)
            {
                var guidMatches = legacyScopes.Where(scope => scope.SourceGuid == model.SourceGuid).ToList();
                if (guidMatches.Count == 1)
                {
                    return guidMatches[0];
                }
            }

            var sourceMatches = legacyScopes.Where(scope => PathUtilities.EqualsPath(scope.SourceFileName, model.SourceFileName)).ToList();
            return sourceMatches.Count == 1 ? sourceMatches[0] : null;
        }

        public ModelFavoriteScope GetOrCreateScope(Model model)
        {
            var scope = FindScope(model);
            if (scope == null)
            {
                scope = new ModelFavoriteScope();
                Store.Models.Add(scope);
            }

            scope.DisplayName = GetModelDisplayName(model);
            scope.ModelFileName = PathUtilities.Normalize(model.FileName);
            scope.SourceFileName = PathUtilities.Normalize(model.SourceFileName);
            scope.SourceGuid = model.SourceGuid;
            _familyMatcher.PopulateScopeMetadata(scope, model);
            return scope;
        }

        public bool RefreshLiveScopeMetadata(Model model)
        {
            var scope = FindScope(model);
            if (scope == null)
            {
                return false;
            }

            var familyKey = ModelFamilyName.GetFamilyKey(model.FileName);
            var revision = ModelFamilyName.GetRevisionToken(model.FileName);
            var fingerprint = _familyMatcher.CaptureFingerprint(model);
            var changed = scope.FamilyId == Guid.Empty
                          || !string.Equals(scope.FamilyKey, familyKey, StringComparison.Ordinal)
                          || !string.Equals(scope.RevisionToken, revision, StringComparison.Ordinal)
                          || scope.StructureFingerprint == null
                          || !scope.StructureFingerprint.SequenceEqual(fingerprint);
            if (!changed)
            {
                return false;
            }

            _familyMatcher.PopulateScopeMetadata(scope, model);
            SaveFavorites();
            return true;
        }

        public FavoriteFolder EnsureDefaultFolder(ModelFavoriteScope scope)
        {
            var existing = scope.Folders.FirstOrDefault();
            if (existing != null)
            {
                return existing;
            }

            var folder = new FavoriteFolder { Name = "当前工作" };
            scope.Folders.Add(folder);
            SaveFavorites();
            return folder;
        }

        public FavoriteFolder CreateFolder(ModelFavoriteScope scope, string name)
        {
            if (scope == null)
            {
                throw new InvalidOperationException("当前没有源模型。");
            }

            var normalizedName = NormalizeFolderName(name);
            if (scope.Folders.Any(folder => string.Equals(folder.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("同一源模型下已存在同名收藏夹。");
            }

            var created = new FavoriteFolder { Name = normalizedName };
            scope.Folders.Add(created);
            Settings.LastModelScopeId = scope.ScopeId;
            Settings.LastFolderId = created.Id;
            SaveAll();
            return created;
        }

        public void RenameFolder(ModelFavoriteScope scope, FavoriteFolder folder, string newName)
        {
            if (scope == null || folder == null || !scope.Folders.Contains(folder))
            {
                throw new InvalidOperationException("收藏夹已不存在。");
            }

            var normalizedName = NormalizeFolderName(newName);
            if (scope.Folders.Any(candidate => candidate.Id != folder.Id
                                               && string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("同一源模型下已存在同名收藏夹。");
            }

            folder.Name = normalizedName;
            SaveFavorites();
        }

        public void DeleteFolder(ModelFavoriteScope scope, FavoriteFolder folder)
        {
            if (scope == null || folder == null || !scope.Folders.Remove(folder))
            {
                return;
            }

            if (Settings.LastFolderId == folder.Id)
            {
                Settings.LastFolderId = null;
            }

            SaveAll();
        }

        public int DeleteItems(FavoriteFolder folder, IEnumerable<Guid> itemIds)
        {
            if (folder == null)
            {
                return 0;
            }

            var ids = new HashSet<Guid>(itemIds ?? Enumerable.Empty<Guid>());
            var removed = folder.Items.RemoveAll(item => ids.Contains(item.Id));
            if (removed > 0)
            {
                SaveFavorites();
            }

            return removed;
        }

        public OperationResult TryGetCurrentSelectionContext(Document document, out SelectionContext context)
        {
            context = null;
            if (document == null || document.IsClear)
            {
                return OperationResult.Fail("当前没有打开模型。");
            }

            var selected = document.CurrentSelection.SelectedItems.Where(item => item != null).ToList();
            if (selected.Count == 0)
            {
                return OperationResult.Fail("Navisworks 当前选择为空。");
            }

            var models = selected.Select(item => ModelOwnership.GetModel(document, item)).ToList();
            if (models.Any(model => model == null))
            {
                return OperationResult.Fail("当前选择中存在无法确定源模型的节点。");
            }

            models = models.Distinct().ToList();
            if (models.Count != 1)
            {
                return OperationResult.Fail("当前选择横跨多个源模型，请先只选择一个源模型中的节点。");
            }

            var model = models[0];
            context = new SelectionContext
            {
                Document = document,
                Model = model,
                Scope = GetOrCreateScope(model),
                Items = selected
            };
            return OperationResult.Ok($"已读取 {selected.Count} 个当前选择。", selected.Count);
        }

        public AddSelectionResult AddCurrentSelection(SelectionContext context, FavoriteFolder folder)
        {
            if (context == null || folder == null || !context.Scope.Folders.Contains(folder))
            {
                return new AddSelectionResult { Success = false, Message = "目标收藏夹无效。" };
            }

            var added = 0;
            var duplicates = 0;
            foreach (var item in context.Items)
            {
                var favorite = Capture(context.Document, context.Model, item);
                if (folder.Items.Any(existing => IsSameFavorite(existing, favorite)))
                {
                    duplicates++;
                    continue;
                }

                folder.Items.Add(favorite);
                added++;
            }

            Settings.LastModelScopeId = context.Scope.ScopeId;
            Settings.LastFolderId = folder.Id;
            SaveAll();

            return new AddSelectionResult
            {
                Success = true,
                ScopeId = context.Scope.ScopeId,
                FolderId = folder.Id,
                AddedCount = added,
                DuplicateCount = duplicates,
                Message = $"已添加 {added} 项，跳过重复 {duplicates} 项。"
            };
        }

        public FavoriteItem Capture(Document document, Model model, ModelItem item)
        {
            var favorite = new FavoriteItem
            {
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? "（未命名节点）" : item.DisplayName,
                ModelIndexAtSave = document.Models.IndexOf(model),
                HierarchyPath = ModelItemResolver.BuildHierarchy(item)
            };

            try
            {
                var pathId = document.Models.CreatePathId(item);
                favorite.PathId = pathId?.PathId ?? string.Empty;
                if (pathId != null)
                {
                    favorite.ModelIndexAtSave = pathId.ModelIndex;
                }
            }
            catch
            {
                favorite.PathId = string.Empty;
            }

            try
            {
                favorite.IndexPath = document.Models.CreateIndexPath(item).ToList();
            }
            catch
            {
                favorite.IndexPath = new List<int>();
            }

            return favorite;
        }

        public void SaveSettings(Guid? scopeId, Guid? folderId)
        {
            Settings.LastModelScopeId = scopeId;
            Settings.LastFolderId = folderId;
            _storage.SaveSettings(Settings);
        }

        public void AcknowledgeMigration(Guid migrationId)
        {
            Settings.LastAcknowledgedMigrationId = migrationId;
            _storage.SaveSettings(Settings);
        }

        public void SaveFavorites()
        {
            _storage.SaveFavorites(Store);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SaveAll()
        {
            _storage.SaveFavorites(Store);
            _storage.SaveSettings(Settings);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public static bool IsSameFavorite(FavoriteItem left, FavoriteItem right)
        {
            if (!string.IsNullOrWhiteSpace(left.PathId) && !string.IsNullOrWhiteSpace(right.PathId))
            {
                return string.Equals(left.PathId, right.PathId, StringComparison.Ordinal);
            }

            if (left.IndexPath != null && right.IndexPath != null
                && left.IndexPath.Count > 0 && left.IndexPath.SequenceEqual(right.IndexPath))
            {
                return true;
            }

            return left.HierarchyPath != null
                   && right.HierarchyPath != null
                   && left.HierarchyPath.Count == right.HierarchyPath.Count
                   && left.HierarchyPath.Zip(right.HierarchyPath, (a, b) =>
                       string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
                       && string.Equals(a.ClassName, b.ClassName, StringComparison.Ordinal)
                       && string.Equals(a.ClassDisplayName, b.ClassDisplayName, StringComparison.Ordinal)).All(value => value);
        }

        private static string NormalizeFolderName(string name)
        {
            var normalized = (name ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                throw new InvalidOperationException("收藏夹名称不能为空。");
            }

            if (normalized.Length > 80)
            {
                throw new InvalidOperationException("收藏夹名称不能超过 80 个字符。");
            }

            return normalized;
        }

        public static string GetModelDisplayName(Model model)
        {
            if (!string.IsNullOrWhiteSpace(model.RootItem?.DisplayName))
            {
                return model.RootItem.DisplayName;
            }

            var fileName = model.FileName;
            return string.IsNullOrWhiteSpace(fileName) ? "（未命名源模型）" : Path.GetFileName(fileName);
        }

        private bool NormalizeLoadedData()
        {
            var sourceVersion = Store.SchemaVersion <= 0 ? 1 : Store.SchemaVersion;
            if (sourceVersion > 2)
            {
                throw new InvalidOperationException($"不支持的收藏数据版本：{Store.SchemaVersion}。未覆盖原文件。");
            }

            Store.Models = Store.Models ?? new List<ModelFavoriteScope>();
            var familyIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var scope in Store.Models)
            {
                scope.Folders = scope.Folders ?? new List<FavoriteFolder>();
                scope.MigrationHistory = scope.MigrationHistory ?? new List<MigrationRecord>();
                scope.StructureFingerprint = scope.StructureFingerprint ?? new List<string>();
                scope.FamilyKey = string.IsNullOrWhiteSpace(scope.FamilyKey)
                    ? ModelFamilyName.GetFamilyKey(string.IsNullOrWhiteSpace(scope.ModelFileName)
                        ? scope.SourceFileName
                        : scope.ModelFileName)
                    : scope.FamilyKey;
                scope.RevisionToken = string.IsNullOrWhiteSpace(scope.RevisionToken)
                    ? ModelFamilyName.GetRevisionToken(scope.ModelFileName)
                    : scope.RevisionToken;
                if (!familyIds.TryGetValue(scope.FamilyKey ?? string.Empty, out var familyId))
                {
                    familyId = scope.FamilyId == Guid.Empty ? Guid.NewGuid() : scope.FamilyId;
                    familyIds[scope.FamilyKey ?? string.Empty] = familyId;
                }

                if (scope.FamilyId == Guid.Empty)
                {
                    scope.FamilyId = familyId;
                }

                foreach (var folder in scope.Folders)
                {
                    folder.Items = folder.Items ?? new List<FavoriteItem>();
                    foreach (var item in folder.Items)
                    {
                        item.IndexPath = item.IndexPath ?? new List<int>();
                        item.HierarchyPath = item.HierarchyPath ?? new List<HierarchySegment>();
                    }
                }

                if (sourceVersion < 2 || scope.FirstSeenUtc == default(DateTime))
                {
                    var created = scope.Folders.SelectMany(folder => folder.Items)
                        .Select(item => item.CreatedUtc)
                        .Where(value => value != default(DateTime))
                        .DefaultIfEmpty(DateTime.UtcNow)
                        .Min();
                    scope.FirstSeenUtc = created;
                }
            }

            Store.SchemaVersion = 2;
            Settings.SchemaVersion = 2;
            return sourceVersion < 2;
        }
    }
}
