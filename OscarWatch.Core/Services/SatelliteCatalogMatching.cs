using OscarWatch.Core.Models;
using OscarWatch.Core.Tle;

namespace OscarWatch.Core.Services;

public static class SatelliteCatalogMatching
{
    /// <summary>
    /// Pre-computed index: maps parenthesised aliases extracted from enabled names to true.
    /// E.g. if enabled contains "RADFXSAT (FOX-1B)", index contains "FOX-1B" → true.
    /// </summary>
    private static Dictionary<string, bool>? _parenthesisIndex;

    /// <summary>
    /// Reference to the enabled set used to build <see cref="_parenthesisIndex"/>.
    /// When the enabled set instance changes, the index is rebuilt.
    /// </summary>
    private static IReadOnlySet<string>? _lastEnabledSet;

    private static readonly object ParenthesisIndexLock = new();

    /// <summary>
    /// True when the satellite is enabled by normalised catalogue ID, or else by name alias rules.
    /// </summary>
    public static bool IsEnabled(
        SatelliteCatalogEntry satellite,
        IReadOnlySet<string> enabledNoradIds,
        IReadOnlySet<string> enabledNames)
    {
        if (MatchesNoradId(satellite.NoradId, enabledNoradIds))
            return true;

        return IsEnabledByName(satellite, enabledNames);
    }

    /// <summary>Name-only enablement (legacy / migrate source). Prefer the ID+name overload.</summary>
    public static bool IsEnabled(SatelliteCatalogEntry satellite, IReadOnlySet<string> enabledNames) =>
        IsEnabledByName(satellite, enabledNames);

    private static bool IsEnabledByName(SatelliteCatalogEntry satellite, IReadOnlySet<string> enabled)
    {
        // O(1) exact match via case-insensitive HashSet
        if (enabled.Contains(satellite.Name))
            return true;

        // O(1) check: is the satellite name itself a parenthesised alias from an enabled name?
        var index = GetOrBuildParenthesisIndex(enabled);
        if (index.Count > 0 && index.ContainsKey(satellite.Name))
            return true;

        // O(M) token-aware substring fallback — aliases like SO-50 ↔ SAUDISAT 1C (SO-50),
        // without treating "ISAT" as a match for "OrigamiSat 2" (mid-token "iSat").
        foreach (var name in enabled)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (ContainsToken(satellite.Name, name) || ContainsToken(name, satellite.Name))
                return true;

            if (satellite.Name.Contains($"({name})", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a case-insensitive set of normalised catalogue IDs (Alpha-5 / D5).
    /// </summary>
    public static HashSet<string> CreateNoradIdSet(IEnumerable<string>? ids)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ids is null)
            return set;

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            set.Add(NormalizeNoradId(id));
        }

        return set;
    }

    public static bool MatchesNoradId(string? noradId, IReadOnlySet<string> enabledNoradIds)
    {
        if (string.IsNullOrWhiteSpace(noradId) || enabledNoradIds.Count == 0)
            return false;

        return enabledNoradIds.Contains(NormalizeNoradId(noradId));
    }

    public static string NormalizeNoradId(string noradId)
    {
        var trimmed = noradId.Trim();
        return Alpha5CatalogId.Normalize(trimmed) ?? trimmed;
    }

    /// <summary>
    /// Appends normalised catalogue IDs for name-matched satellites that are missing from
    /// <see cref="AppSettings.EnabledSatelliteNoradIds"/>. Does not remove names.
    /// Also normalises any existing ID spellings (e.g. 100000 → A0000).
    /// </summary>
    public static bool TryMigrateEnabledSatelliteIds(
        AppSettings settings,
        IReadOnlyList<SatelliteCatalogEntry> catalog)
    {
        settings.EnabledSatelliteNames ??= [];
        settings.EnabledSatelliteNoradIds ??= [];

        var changed = false;
        var normalisedIds = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in settings.EnabledSatelliteNoradIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                changed = true;
                continue;
            }

            var normalised = NormalizeNoradId(id);
            if (!seenIds.Add(normalised))
            {
                changed = true;
                continue;
            }

            if (!string.Equals(id.Trim(), normalised, StringComparison.Ordinal))
                changed = true;

            normalisedIds.Add(normalised);
        }

        if (normalisedIds.Count != settings.EnabledSatelliteNoradIds.Count)
            changed = true;

        var nameSet = new HashSet<string>(settings.EnabledSatelliteNames, StringComparer.OrdinalIgnoreCase);

        foreach (var sat in catalog)
        {
            if (!IsEnabledByName(sat, nameSet))
                continue;

            if (string.IsNullOrWhiteSpace(sat.NoradId))
                continue;

            var normalised = NormalizeNoradId(sat.NoradId);
            if (!seenIds.Add(normalised))
                continue;

            normalisedIds.Add(normalised);
            changed = true;

            if (!nameSet.Contains(sat.Name))
            {
                settings.EnabledSatelliteNames.Add(sat.Name);
                nameSet.Add(sat.Name);
                changed = true;
            }
        }

        if (changed)
            settings.EnabledSatelliteNoradIds = normalisedIds;

        return changed;
    }

    /// <summary>
    /// True when <paramref name="needle"/> appears in <paramref name="haystack"/> as a whole
    /// token bounded by non-letter/non-digit characters (or string ends).
    /// </summary>
    internal static bool ContainsToken(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            return false;

        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var beforeOk = index == 0 || !IsNameTokenChar(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex == haystack.Length || !IsNameTokenChar(haystack[afterIndex]);
            if (beforeOk && afterOk)
                return true;

            start = index + 1;
        }

        return false;
    }

    private static bool IsNameTokenChar(char c) => char.IsLetterOrDigit(c);

    /// <summary>
    /// Returns the parenthesis alias index, rebuilding it only if the enabled set instance changes.
    /// </summary>
    private static Dictionary<string, bool> GetOrBuildParenthesisIndex(IReadOnlySet<string> enabled)
    {
        lock (ParenthesisIndexLock)
        {
            if (ReferenceEquals(_lastEnabledSet, enabled) && _parenthesisIndex is not null)
                return _parenthesisIndex;

            var index = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in enabled)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var openParen = name.IndexOf('(');
                var closeParen = name.IndexOf(')');
                if (openParen >= 0 && closeParen > openParen + 1)
                {
                    var alias = name[(openParen + 1)..closeParen].Trim();
                    if (!string.IsNullOrWhiteSpace(alias))
                        index[alias] = true;
                }
            }

            _parenthesisIndex = index;
            _lastEnabledSet = enabled;
            return _parenthesisIndex;
        }
    }

    /// <summary>
    /// Clears the cached index. Used by tests to ensure isolation.
    /// </summary>
    internal static void ResetCache()
    {
        lock (ParenthesisIndexLock)
        {
            _parenthesisIndex = null;
            _lastEnabledSet = null;
        }
    }
}
