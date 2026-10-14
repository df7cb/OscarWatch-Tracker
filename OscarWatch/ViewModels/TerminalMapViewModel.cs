using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;
using OscarWatch.Core.Terminal;
using OscarWatch.Localization;
using OscarWatch.Terminal;
using Serilog;

namespace OscarWatch.ViewModels;

/// <summary>Terminal pass map: the focused satellite's footprint and the heard stations with a known location.</summary>
public sealed partial class TerminalMapViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TerminalMapViewModel>();

    private readonly ISettingsService _settings;
    private readonly ILiveTrackingService _tracking;
    private readonly TerminalService _service;
    private readonly ILocalizationService _l;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public TerminalMapViewModel(
        ISettingsService settings,
        ILiveTrackingService tracking,
        TerminalService service,
        ILocalizationService localization)
    {
        _settings = settings;
        _tracking = tracking;
        _service = service;
        _l = localization;

        var terminal = settings.Current.Terminal;
        _thisPassOnly = terminal.MapThisPassOnly;
        _fadeMinutes = terminal.MapFadeMinutes;

        _service.Changed += OnServiceChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    [ObservableProperty] private IReadOnlyList<SatelliteTrackState>? _trackStates;
    [ObservableProperty] private GroundStation? _groundStation;
    [ObservableProperty] private string? _focusedNoradId;
    [ObservableProperty] private double _mapCentreLongitude;
    [ObservableProperty] private IReadOnlyList<MapStationMarker> _stationMarkers = [];
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _accessibleSummary = "";
    [ObservableProperty] private bool _hasMarkers;
    [ObservableProperty] private bool _thisPassOnly;
    [ObservableProperty] private int _fadeMinutes;

    public int MinFadeMinutes => TerminalSettings.MinMapFadeMinutes;
    public int MaxFadeMinutes => TerminalSettings.MaxMapFadeMinutes;

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    partial void OnThisPassOnlyChanged(bool value)
    {
        _settings.Current.Terminal.MapThisPassOnly = value;
        _settings.RequestSave();
        Refresh();
    }

    partial void OnFadeMinutesChanged(int value)
    {
        var clamped = Math.Clamp(value, TerminalSettings.MinMapFadeMinutes, TerminalSettings.MaxMapFadeMinutes);
        if (clamped != value)
        {
            FadeMinutes = clamped;
            return;
        }

        _settings.Current.Terminal.MapFadeMinutes = clamped;
        _settings.RequestSave();
        Refresh();
    }

    private void OnServiceChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _timer.IsEnabled)
                Refresh();
        });

    private void Refresh()
    {
        if (_disposed)
            return;
        try
        {
            var now = DateTime.UtcNow;
            var home = _settings.Current.GroundStation;
            GroundStation = home;
            MapCentreLongitude = home.LongitudeDeg;

            var norad = _tracking.FocusedNoradId;
            FocusedNoradId = norad;
            var focused = _tracking.GetLiveNowSnapshot()
                .FirstOrDefault(s => string.Equals(s.NoradId, norad, StringComparison.Ordinal));
            TrackStates = focused is null ? [] : [focused];

            var passStart = ThisPassOnly ? _service.LastPassStartUtc() ?? now : (DateTime?)null;
            var sources = _service.GetStations()
                .Select(s => new MapStationSource(s.Call, s.Position, s.Grid, s.LastHeardUtc));
            StationMarkers = MapStationMarkers.Build(
                sources, now, TimeSpan.FromMinutes(FadeMinutes), passStart, s => ToolTipFor(s, home));
            HasMarkers = StationMarkers.Count > 0;
            SummaryText = _l.Get("TerminalMap.Count", StationMarkers.Count);
            AccessibleSummary = _l.Get("TerminalMap.Accessible", StationMarkers.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminal map refresh failed");
        }
    }

    private string ToolTipFor(MapStationSource s, GroundStation home)
    {
        var grid = s.Grid ?? "-";
        var distance = "-";
        if (TerminalStationGeo.TryLocate(s.Position, s.Grid, out var lat, out var lon))
        {
            var (km, bearing) = TerminalStationGeo.DistanceBearing(home.LatitudeDeg, home.LongitudeDeg, lat, lon);
            distance = TerminalStationGeo.FormatDistanceBearing(km, bearing);
        }

        var heard = s.LastHeardUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var key = s.Position is null ? "TerminalMap.MarkerTip.Grid" : "TerminalMap.MarkerTip";
        return _l.Get(key, s.Call, grid, distance, heard);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer.Stop();
        _service.Changed -= OnServiceChanged;
    }
}
