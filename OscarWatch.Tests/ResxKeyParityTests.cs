using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OscarWatch.Tests;

/// <summary>
/// CI gate: every locale <c>Strings.*.resx</c> must have the same keys as British English
/// <c>Strings.resx</c>. Missing keys fall back to English and look half-translated.
/// </summary>
public sealed class ResxKeyParityTests
{
    private static readonly string[] LocaleFileNames =
    [
        "Strings.de.resx",
        "Strings.es.resx",
        "Strings.id.resx",
        "Strings.it.resx",
        "Strings.ja.resx",
        "Strings.pt-BR.resx",
        "Strings.ru.resx",
        "Strings.th.resx",
        "Strings.zh-CN.resx"
    ];

    private static readonly Regex FormatPlaceholder = new(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.Compiled);

    [Fact]
    public void Locale_resx_files_have_the_same_keys_as_british_english()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Resources");
        var englishPath = Path.Combine(directory, "Strings.resx");
        Assert.True(File.Exists(englishPath), $"Missing {englishPath}");

        var english = LoadEntries(englishPath);
        Assert.NotEmpty(english);

        foreach (var fileName in LocaleFileNames)
        {
            var path = Path.Combine(directory, fileName);
            Assert.True(File.Exists(path), $"Missing {path}");

            var locale = LoadEntries(path);
            var missing = english.Keys.Except(locale.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extra = locale.Keys.Except(english.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(
                missing.Count == 0 && extra.Count == 0,
                FormatMismatch(fileName, english.Count, locale.Count, missing, extra));

            var placeholderMismatches = english
                .Where(pair => locale.TryGetValue(pair.Key, out var translated)
                    && !PlaceholderIndexes(pair.Value).SetEquals(PlaceholderIndexes(translated)))
                .Select(pair => pair.Key)
                .Take(15)
                .ToList();

            Assert.True(
                placeholderMismatches.Count == 0,
                $"{fileName} format placeholders do not match Strings.resx for: {string.Join(", ", placeholderMismatches)}");
        }
    }

    private static Dictionary<string, string> LoadEntries(string path)
    {
        var doc = XDocument.Load(path);
        var names = new List<string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = (string?)data.Attribute("name");
            if (string.IsNullOrEmpty(name))
                continue;

            names.Add(name);
            map[name] = data.Element("value")?.Value ?? "";
        }

        var duplicates = names
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicates.Count == 0, $"{Path.GetFileName(path)} has duplicate keys: {string.Join(", ", duplicates)}");

        return map;
    }

    private static HashSet<int> PlaceholderIndexes(string value) =>
        FormatPlaceholder.Matches(value)
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToHashSet();

    private static string FormatMismatch(
        string fileName,
        int englishCount,
        int localeCount,
        IReadOnlyList<string> missing,
        IReadOnlyList<string> extra)
    {
        var lines = new List<string>
        {
            $"{fileName} keys ({localeCount}) do not match Strings.resx ({englishCount})."
        };
        if (missing.Count > 0)
            lines.Add("Missing: " + string.Join(", ", missing.Take(20)) + (missing.Count > 20 ? $" (+{missing.Count - 20} more)" : ""));
        if (extra.Count > 0)
            lines.Add("Extra: " + string.Join(", ", extra.Take(20)) + (extra.Count > 20 ? $" (+{extra.Count - 20} more)" : ""));
        return string.Join(Environment.NewLine, lines);
    }
}
