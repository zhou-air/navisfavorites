using Autodesk.Navisworks.Api;
using NavisFavorites.Models;

namespace NavisFavorites.Services
{
    public sealed class LiveModelContext
    {
        public Model Model { get; set; }

        public ModelFavoriteScope Scope { get; set; }

        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }
}
