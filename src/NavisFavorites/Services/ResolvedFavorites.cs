using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisFavorites.Services
{
    public sealed class ResolvedFavorites
    {
        public List<ModelItem> Items { get; } = new List<ModelItem>();

        public int InvalidCount { get; set; }
    }
}
