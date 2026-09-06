using System;
using System.IO;

namespace NavisFavorites.Services
{
    internal static class PathUtilities
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        public static bool EqualsPath(string left, string right)
        {
            var normalizedLeft = Normalize(left);
            var normalizedRight = Normalize(right);
            return normalizedLeft.Length > 0
                   && normalizedRight.Length > 0
                   && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
    }
}
