using OscarWatch.Localization;

namespace OscarWatch.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void Get_returns_english_by_default()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_File", text);
    }

    [Fact]
    public void Get_returns_portuguese_brazil_when_culture_is_pt_BR()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.PortugueseBrazilLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_Arquivo", text);
    }

    [Fact]
    public void Get_returns_chinese_when_culture_is_zh_CN()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.SimplifiedChineseLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_文件", text);
    }

    [Fact]
    public void Get_returns_japanese_when_culture_is_ja()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.JapaneseLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("ファイル(_F)", text);
    }

    [Fact]
    public void Get_returns_spanish_when_culture_is_es()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.SpanishLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_Archivo", text);
    }

    [Fact]
    public void Get_returns_thai_when_culture_is_th()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.ThaiLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("ไฟล์(_F)", text);
    }

    [Fact]
    public void Get_returns_indonesian_when_culture_is_id()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.IndonesianLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_Berkas", text);
    }

    [Fact]
    public void Get_returns_russian_when_culture_is_ru()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.RussianLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_Файл", text);
    }

    [Fact]
    public void Get_returns_german_when_culture_is_de()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.GermanLanguage);
        var text = LocalizationService.Instance.Get("Menu.File");
        Assert.Equal("_Datei", text);
    }

    [Fact]
    public void Get_returns_italian_when_culture_is_it()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.ItalianLanguage);
        var text = LocalizationService.Instance.Get("Menu.File.Close");
        Assert.Equal("_Chiudi", text);
    }

    [Fact]
    public void Get_returns_spanish_rotator_park_label()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.SpanishLanguage);
        var text = LocalizationService.Instance.Get("Main.Rotator.Park");
        Assert.Equal("Estacionar rotor", text);
    }

    [Fact]
    public void Get_keeps_universal_abbreviations_in_spanish()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.SpanishLanguage);
        var text = LocalizationService.Instance.Get("Pass.Rec");
        Assert.Equal("REC", text);
    }

    [Fact]
    public void Get_falls_back_to_english_for_untranslated_key()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.JapaneseLanguage);
        var text = LocalizationService.Instance.Get("Settings.Tab.Cloudlog");
        Assert.Equal("Cloudlog", text);
    }

    [Fact]
    public void Get_returns_japanese_status_satellite_count()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.JapaneseLanguage);
        var text = LocalizationService.Instance.Get("Status.SatellitesEnabled", "TLE 01:23 前 (cache)", 3);
        Assert.Equal("TLE 01:23 前 (cache) | 3 機の衛星を有効", text);
    }

    [Fact]
    public void Get_returns_japanese_pass_planner_status()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.JapaneseLanguage);
        var text = LocalizationService.Instance.Get("Pass.CountPasses", 3, 48);
        Assert.Equal("今後 48 時間で 3 件のパス", text);
    }

    [Fact]
    public void Get_formats_arguments_with_current_culture()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var text = LocalizationService.Instance.Get("Main.Pass.AosIn", "ISS", "00:12:34");
        Assert.Equal("ISS AOS in 00:12:34", text);
    }

    [Fact]
    public void Get_formats_los_countdown()
    {
        using var _ = TestUiCulture.Apply(LocalizationCulture.DefaultLanguage);
        var text = LocalizationService.Instance.Get("Main.Pass.LosIn", "AO-07", "0:04:12");
        Assert.Equal("AO-07 LOS in 0:04:12", text);
    }
}
