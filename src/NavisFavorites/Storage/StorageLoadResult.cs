namespace NavisFavorites.Storage
{
    public sealed class StorageLoadResult<T> where T : new()
    {
        public T Value { get; set; } = new T();

        public string Warning { get; set; } = string.Empty;
    }
}
