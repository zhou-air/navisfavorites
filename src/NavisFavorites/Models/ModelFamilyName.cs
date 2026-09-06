using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace NavisFavorites.Models
{
    public static class ModelFamilyName
    {
        private static readonly Regex DateSuffix = new Regex(
            @"(?:[-_. ]+)((?:19|20)\d{2})[-_.]?(\d{2})[-_.]?(\d{2})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex VersionSuffix = new Regex(
            @"(?:[-_. ]+)((?:V(?:ER(?:SION)?)?|REV|R)[-_. ]*[A-Z0-9.]+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex ChineseSuffix = new Regex(
            @"(?:[-_. ]+)((?:修改版|修订版|新版)[-_. A-Z0-9]*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static string GetFamilyKey(string pathOrName)
        {
            var stem = GetStem(pathOrName);
            if (stem.Length == 0)
            {
                return string.Empty;
            }

            var current = stem;
            while (true)
            {
                var trimmed = StripOneSuffix(current);
                if (string.Equals(trimmed, current, StringComparison.Ordinal))
                {
                    break;
                }

                current = trimmed;
            }

            current = current.Trim(' ', '-', '_', '.');
            return (current.Length == 0 ? stem : current).ToUpperInvariant();
        }

        public static string GetRevisionToken(string pathOrName)
        {
            var stem = GetStem(pathOrName);
            var match = DateSuffix.Match(stem);
            if (match.Success)
            {
                return $"DATE:{match.Groups[1].Value}-{match.Groups[2].Value}-{match.Groups[3].Value}";
            }

            match = VersionSuffix.Match(stem);
            if (match.Success)
            {
                return "VERSION:" + match.Groups[1].Value.ToUpperInvariant().Replace(" ", string.Empty);
            }

            match = ChineseSuffix.Match(stem);
            return match.Success ? "LABEL:" + match.Groups[1].Value.ToUpperInvariant().Trim() : string.Empty;
        }

        public static bool TryGetRevisionOrder(string token, out long order)
        {
            order = 0;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token.StartsWith("DATE:", StringComparison.Ordinal))
            {
                if (DateTime.TryParseExact(token.Substring(5), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    order = date.Year * 10000L + date.Month * 100L + date.Day;
                    return true;
                }

                return false;
            }

            if (token.StartsWith("VERSION:", StringComparison.Ordinal))
            {
                var digits = Regex.Match(token.Substring(8), @"\d+(?:\.\d+)*").Value;
                if (digits.Length == 0)
                {
                    return false;
                }

                var parts = digits.Split('.');
                long value = 0;
                foreach (var part in parts)
                {
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    {
                        return false;
                    }

                    value = checked(value * 10000L + Math.Min(number, 9999));
                }

                order = value;
                return true;
            }

            return false;
        }

        private static string GetStem(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileNameWithoutExtension(pathOrName.Trim()) ?? string.Empty;
            }
            catch
            {
                return pathOrName.Trim();
            }
        }

        private static string StripOneSuffix(string value)
        {
            foreach (var regex in new[] { DateSuffix, VersionSuffix, ChineseSuffix })
            {
                var match = regex.Match(value);
                if (match.Success)
                {
                    return value.Substring(0, match.Index).TrimEnd(' ', '-', '_', '.');
                }
            }

            return value;
        }
    }
}
