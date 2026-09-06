using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace NavisFavorites.Storage
{
    /// <summary>
    /// 使用同目录临时文件和 Replace 写入，避免中途退出留下半个 JSON。
    /// </summary>
    public sealed class JsonFileStore<T> where T : new()
    {
        private readonly string _path;

        public JsonFileStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path => _path;

        public StorageLoadResult<T> Load()
        {
            var result = new StorageLoadResult<T>();
            if (!File.Exists(_path))
            {
                return result;
            }

            try
            {
                var json = File.ReadAllText(_path, Encoding.UTF8);
                result.Value = JsonConvert.DeserializeObject<T>(json) ?? new T();
                return result;
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                var preservedPath = PreserveCorruptFile();
                result.Warning = string.IsNullOrEmpty(preservedPath)
                    ? $"数据文件无法读取：{ex.Message}"
                    : $"数据文件损坏，已保留副本：{preservedPath}";
                return result;
            }
        }

        public void Save(T value)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("存储路径缺少目录。");
            }

            Directory.CreateDirectory(directory);
            var tempPath = _path + ".tmp";
            var backupPath = _path + ".bak";
            var json = JsonConvert.SerializeObject(value, Formatting.Indented);

            try
            {
                File.WriteAllText(tempPath, json + Environment.NewLine, new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tempPath, _path, backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceWithFallback(tempPath, backupPath);
                    }
                    catch (IOException)
                    {
                        ReplaceWithFallback(tempPath, backupPath);
                    }
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private void ReplaceWithFallback(string tempPath, string backupPath)
        {
            File.Copy(_path, backupPath, true);
            File.Copy(tempPath, _path, true);
            File.Delete(tempPath);
        }

        private string PreserveCorruptFile()
        {
            try
            {
                var suffix = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var preservedPath = _path + ".corrupt-" + suffix;
                File.Copy(_path, preservedPath, false);
                return preservedPath;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
