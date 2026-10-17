using CommunityToolkit.Mvvm.ComponentModel;
using OscarWatch.Core.Ft4;
using OscarWatch.Core.Geo;
using OscarWatch.Core.Services;
using OscarWatch.Localization;

namespace OscarWatch.ViewModels;

/// <summary>Callsign and locator needed before the FT4 waterfall can listen.</summary>
public partial class Ft4StationSetupViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _l;

    public Ft4StationSetupViewModel(
        ISettingsService settings,
        ILocalizationService localization,
        bool focusCallsign)
    {
        _settings = settings;
        _l = localization;
        FocusCallsign = focusCallsign;
        Callsign = settings.Current.GroundStation.Callsign ?? "";
        Locator = settings.Current.GroundStation.GridSquare ?? "";
    }

    public bool FocusCallsign { get; }

    [ObservableProperty] private string _callsign = "";
    [ObservableProperty] private string _locator = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = "";

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool TrySave()
    {
        if (!Ft4StationIdentity.TryNormalize(
                Callsign,
                Locator,
                out var call,
                out var grid,
                out var error,
                out var invalid))
        {
            ErrorText = error switch
            {
                Ft4StationIdentityError.CallsignRequired => _l.Get("Ft4.Station.CallsignRequired"),
                Ft4StationIdentityError.LocatorRequired => _l.Get("Ft4.Station.LocatorRequired"),
                Ft4StationIdentityError.BothRequired => _l.Get("Ft4.Station.NeedBoth"),
                Ft4StationIdentityError.TooManyLocators =>
                    _l.Get("Logbook.Error.GridTooMany", MaidenheadLocator.MaxGridCount),
                Ft4StationIdentityError.LocatorInvalid =>
                    _l.Get("Logbook.Error.GridInvalidSegment", invalid ?? ""),
                _ => _l.Get("Ft4.Station.LocatorRequired")
            };
            return false;
        }

        var station = _settings.Current.GroundStation;
        station.Callsign = call;
        station.GridSquare = grid;
        _settings.SyncActiveStationFromGroundStation();
        _settings.RequestSave();
        ErrorText = "";
        return true;
    }
}
