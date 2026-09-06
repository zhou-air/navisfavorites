using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class SelectionContext
    {
        public Document Document { get; set; }

        public Model Model { get; set; }

        public ModelFavoriteScope Scope { get; set; }

        public List<ModelItem> Items { get; set; } = new List<ModelItem>();
    }
}
