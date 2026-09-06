using System;
using System.Collections.Generic;

namespace NavisFavorites.Models
{
    public sealed class FavoriteFolder
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public List<FavoriteItem> Items { get; set; } = new List<FavoriteItem>();
    }
}
