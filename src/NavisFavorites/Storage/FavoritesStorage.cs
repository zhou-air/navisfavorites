using System;
using System.IO;
using NavisFavorites.Models;

namespace NavisFavorites.Storage
{
    public sealed class FavoritesStorage
    {
        private readonly JsonFileStore<FavoritesStore> _favorites;
        private readonly JsonFileStore<FavoritesSettings> _settings;

        public FavoritesStorage(string rootDirectory = null)
        {
            RootDirectory = rootDirectory ?? System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NavisFavorites");

            _favorites = new JsonFileStore<FavoritesStore>(System.IO.Path.Combine(RootDirectory, "favorites.json"));
            _settings = new JsonFileStore<FavoritesSettings>(System.IO.Path.Combine(RootDirectory, "settings.json"));
        }

        public string RootDirectory { get; }

        public string FavoritesPath => _favorites.Path;

        public string SettingsPath => _settings.Path;

        public StorageLoadResult<FavoritesStore> LoadFavorites() => _favorites.Load();

        public StorageLoadResult<FavoritesSettings> LoadSettings() => _settings.Load();

        public void SaveFavorites(FavoritesStore value) => _favorites.Save(value);

        public void SaveSettings(FavoritesSettings value) => _settings.Save(value);

        public string BackupFavoritesForSchemaUpgrade(int sourceVersion)
        {
            if (!File.Exists(FavoritesPath))
            {
                return string.Empty;
            }

            Directory.CreateDirectory(RootDirectory);
            var backupPath = FavoritesPath + $".schema{sourceVersion}-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            File.Copy(FavoritesPath, backupPath, false);
            return backupPath;
        }
    }
}
