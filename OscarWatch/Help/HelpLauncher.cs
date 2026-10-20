using System.Diagnostics;
using System.Text.RegularExpressions;

namespace OscarWatch.Help;

/// <summary>
/// Opens the operator guide on oscarwatch.org in the default browser.
/// The guide is maintained in the website repo (docs/*.md), not in this repo.
/// </summary>
public static partial class HelpLauncher
{
    public const string DocsBaseUrl = "https://oscarwatch.org/docs/";
    public const string DefaultPage = "tracker";

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex AnchorPattern();

    /// <summary>
    /// Builds the docs URL for a help page. Accepts a slug ("ft4"), an old file name
    /// ("satellite-link.html") and an optional anchor ("settings#gps").
    /// Unknown or unsafe input falls back to the guide's introduction.
    /// </summary>
    public static string BuildUrl(string? page = null)
    {
        if (string.IsNullOrWhiteSpace(page))
            return DocsBaseUrl + DefaultPage;

        var value = page.Trim();
        string? anchor = null;
        var hash = value.IndexOf('#');
        if (hash >= 0)
        {
            anchor = value[(hash + 1)..];
            value = value[..hash];
        }

        if (value.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            value = value[..^5];

        var slug = value.ToLowerInvariant();
        if (slug is "" or "index")
            slug = DefaultPage;

        if (!SlugPattern().IsMatch(slug))
            return DocsBaseUrl + DefaultPage;

        var url = DocsBaseUrl + slug;
        if (!string.IsNullOrEmpty(anchor) && AnchorPattern().IsMatch(anchor))
            url += "#" + anchor;

        return url;
    }

    public static bool TryOpenHelp(string? page = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = BuildUrl(page),
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
