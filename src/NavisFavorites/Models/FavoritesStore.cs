using System.Collections.Generic;

namespace NavisFavorites.Models
{
    public sealed class FavoritesStore
    {
        public int SchemaVersion { get; set; } = 2;

        public List<ModelFavoriteScope> Models { get; set; } = new List<ModelFavoriteScope>();
    }
}
