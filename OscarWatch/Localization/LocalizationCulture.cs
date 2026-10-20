using System.Globalization;
using System.Text.Json;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;

namespace OscarWatch.Localization;

/// <summary>Applies UI culture from settings before windows are created.</summary>
public static class LocalizationCulture
{
    /// <summary>Default UI language: British English (<c>en-GB</c>). Legacy <c>en</c> is accepted as an alias.</summary>
    public const string DefaultLanguage = "en-GB";
    public const string JapaneseLanguage = "ja";
    public const string PortugueseBrazilLanguage = "pt-BR";
    public const string SimplifiedChineseLanguage = "zh-CN";
    public const string SpanishLanguage = "es";
    public const string ThaiLanguage = "th";
    public const string IndonesianLanguage = "id";
    public const string RussianLanguage = "ru";
    public const string GermanLanguage = "de";
    public const string ItalianLanguage = "it";

    public static void ApplyFromSettings(ISettingsService settings) =>
        Apply(NormalizeLanguageCode(settings.Current.UiLanguage));

    public static void Apply(string? uiLanguage)
    {
        var uiCulture = ResolveCulture(uiLanguage);
        var formattingCulture = CultureInfo.GetCultureInfo(DefaultLanguage);
        CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        CultureInfo.DefaultThreadCurrentCulture = formattingCulture;
        CultureInfo.CurrentUICulture = uiCulture;
        CultureInfo.CurrentCulture = formattingCulture;
    }

    public static CultureInfo ResolveCulture(string? uiLanguage)
    {
        if (string.Equals(uiLanguage, JapaneseLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo("ja");

        if (string.Equals(uiLanguage, PortugueseBrazilLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(PortugueseBrazilLanguage);

        if (string.Equals(uiLanguage, SimplifiedChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiLanguage, "zh-Hans", StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(SimplifiedChineseLanguage);

        if (string.Equals(uiLanguage, SpanishLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(SpanishLanguage);

        if (string.Equals(uiLanguage, ThaiLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(ThaiLanguage);

        if (string.Equals(uiLanguage, IndonesianLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(IndonesianLanguage);

        if (string.Equals(uiLanguage, RussianLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(RussianLanguage);

        if (string.Equals(uiLanguage, GermanLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(GermanLanguage);

        if (string.Equals(uiLanguage, ItalianLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(ItalianLanguage);

        if (string.IsNullOrWhiteSpace(uiLanguage)
            || string.Equals(uiLanguage, "en", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiLanguage, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            return CultureInfo.GetCultureInfo(DefaultLanguage);

        return CultureInfo.GetCultureInfo(DefaultLanguage);
    }

    /// <summary>Maps legacy <c>en</c> and empty values to <see cref="DefaultLanguage"/>.</summary>
    public static string NormalizeLanguageCode(string? uiLanguage)
    {
        if (string.IsNullOrWhiteSpace(uiLanguage))
            return DefaultLanguage;

        var trimmed = uiLanguage.Trim();
        return string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase)
            ? DefaultLanguage
            : trimmed;
    }

    /// <summary>Reads <see cref="AppSettings.UiLanguage"/> before DI is available (fonts in Program).</summary>
    public static string ReadUiLanguageFromDisk() =>
        ReadUiLanguageFromFile(GetDefaultSettingsPath());

    internal static string ReadUiLanguageFromFile(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
                return DefaultLanguage;

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (TryReadUiLanguageProperty(doc.RootElement, out var value))
                return NormalizeLanguageCode(value);
        }
        catch
        {
            // use default
        }

        return DefaultLanguage;
    }

    private static string GetDefaultSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OscarWatch",
            "settings.json");

    private static bool TryReadUiLanguageProperty(JsonElement root, out string value)
    {
        value = "";
        if (root.TryGetProperty("uiLanguage", out var lang)
            || root.TryGetProperty(nameof(AppSettings.UiLanguage), out lang))
        {
            if (lang.ValueKind == JsonValueKind.String)
            {
                var text = lang.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    value = text;
                    return true;
                }
            }
        }

        return false;
    }
}
