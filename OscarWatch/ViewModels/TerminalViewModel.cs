using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;
using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Uhm;
using OscarWatch.Localization;
using OscarWatch.Terminal;
using Serilog;

namespace OscarWatch.ViewModels;

/// <summary>OscarWatch Terminal window: macros, send box, statistics, message log, QSO and heard station lists.</summary>
public sealed partial class TerminalViewModel : ViewModelBase, IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<TerminalViewModel>();

    private readonly ISettingsService _settings;
    private readonly ILiveTrackerSnapshotProvider _tracker;
    private readonly FrequencyOverlayViewModel _frequencyOverlay;
    private readonly ILocalizationService _l;
    private readonly TerminalService _service;
    private readonly IQsoLogbookRepository _logbook;
    private readonly DispatcherTimer _uiTimer;
    private readonly List<TerminalMessageRowViewModel> _allRows = [];
    private IReadOnlySet<string> _workedCalls = new HashSet<string>(StringComparer.Ordinal);
    private IReadOnlySet<string> _workedGridFields = new HashSet<string>(StringComparer.Ordinal);
    private bool _windowOpen;
    private bool _disposed;
    private int _changedQueued;

    public TerminalViewModel(
        ISettingsService settings,
        ILiveTrackerSnapshotProvider tracker,
        FrequencyOverlayViewModel frequencyOverlay,
        ILocalizationService localization,
        TerminalService service,
        IQsoLogbookRepository logbook)
    {
        _settings = settings;
        _tracker = tracker;
        _frequencyOverlay = frequencyOverlay;
        _l = localization;
        _service = service;
        _logbook = logbook;

        var terminal = _settings.Current.Terminal;
        _scrollToLastLine = terminal.ScrollToLastLine;
        _showCallers = terminal.ShowCallers;
        _txDelaySeconds = terminal.TxDelaySeconds;
        _fontSize = terminal.FontSize;
        _qsoFontSize = terminal.QsoFontSize;
        _heardFontSize = terminal.HeardFontSize;
        RefreshColumnLayouts();

        for (var i = 0; i < TerminalSettings.MacroCount; i++)
            Macros.Add(new TerminalMacroButtonViewModel(i, SendMacroCommand));
        ReloadMacros();

        _service.MessageAdded += OnMessageAdded;
        _service.Changed += OnServiceChanged;
        _service.Logged += OnServiceLogged;
        _logbook.QsosChanged += OnLogbookQsosChanged;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) =>
        {
            try
            {
                RefreshTick();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Terminal refresh failed");
            }
        };
        StatusLine = _l.Get("Terminal.Status.Ready");
    }

    public ObservableCollection<TerminalMessageRowViewModel> Rows { get; } = [];
    public ObservableCollection<TerminalStationRowViewModel> QsoStations { get; } = [];
    public ObservableCollection<TerminalStationRowViewModel> HeardStations { get; } = [];
    public ObservableCollection<TerminalMacroButtonViewModel> Macros { get; } = [];

    /// <summary>Raised after a row is added and the log should scroll to it.</summary>
    public event Action<TerminalMessageRowViewModel>? ScrollRequested;

    [ObservableProperty] private string _toCall = TerminalMessageCodec.CqTo;
    [ObservableProperty] private string _messageText = "";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private TerminalMessageRowViewModel? _selectedRow;
    [ObservableProperty] private TerminalStationRowViewModel? _selectedStation;

    [ObservableProperty] private string _uniqueCallsignsText = "0";
    [ObservableProperty] private string _myTxText = "0";
    [ObservableProperty] private string _myRxText = "0";
    [ObservableProperty] private string _successText = "-";
    [ObservableProperty] private string _digipeatedText = "0";
    [ObservableProperty] private string _medianDelayText = "-";
    [ObservableProperty] private string _uhmUsersText = "-";

    [ObservableProperty] private string _myCallText = "-";
    [ObservableProperty] private string _myGridText = "-";
    [ObservableProperty] private string _satelliteText = "-";
    [ObservableProperty] private string _losText = "-";
    [ObservableProperty] private string _horizonLabel = "";
    [ObservableProperty] private bool _isBelowHorizon;
    [ObservableProperty] private string _horizonWarningText = "";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private bool _hasQsoStations;
    [ObservableProperty] private bool _hasHeardStations;
    [ObservableProperty] private IBrush _modemDot = DotOff;
    [ObservableProperty] private IBrush _aprsIsDot = DotOff;
    [ObservableProperty] private IBrush _uhmDot = DotOff;

    private static readonly IBrush DotOk = new SolidColorBrush(Color.Parse("#43A047"));
    private static readonly IBrush DotBusy = new SolidColorBrush(Color.Parse("#FFB300"));
    private static readonly IBrush DotError = new SolidColorBrush(Color.Parse("#E53935"));
    private static readonly IBrush DotOff = new SolidColorBrush(Color.Parse("#808080"));
    [ObservableProperty] private string _azimuthText = "-";
    [ObservableProperty] private string _elevationText = "-";
    [ObservableProperty] private string _rangeText = "-";
    [ObservableProperty] private double _elevationBarValue;
    [ObservableProperty] private double _elevationOpacity = 0.5;
    [ObservableProperty] private string _profileText = "";
    [ObservableProperty] private string _modemHintText = "";

    [ObservableProperty] private string _modemStatusText = "";
    [ObservableProperty] private string _aprsIsStatusText = "";
    [ObservableProperty] private string _uhmStatusText = "";
    [ObservableProperty] private string _uhmServerText = "";
    [ObservableProperty] private string _utcClockText = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlainFormat))]
    private bool _isAprsFormat = true;

    public bool IsPlainFormat
    {
        get => !IsAprsFormat;
        set => IsAprsFormat = !value;
    }

    [ObservableProperty] private bool _scrollToLastLine;
    [ObservableProperty] private bool _showCallers;
    [ObservableProperty] private bool _showOwnOnly;
    [ObservableProperty] private int _txDelaySeconds;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LogZoom))]
    private int _fontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QsoZoom))]
    private int _qsoFontSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeardZoom))]
    private int _heardFontSize;

    /// <summary>Scale factors for the log, QSO and heard tables, relative to the sizes their layouts were drawn at.</summary>
    public double LogZoom => FontSize / (double)TerminalSettings.DefaultLogFontSize;
    public double QsoZoom => QsoFontSize / (double)TerminalSettings.DefaultStationFontSize;
    public double HeardZoom => HeardFontSize / (double)TerminalSettings.DefaultStationFontSize;

    public const string LogPanel = "log";
    public const string QsoPanel = "qso";
    public const string HeardPanel = "heard";

    /// <summary>Grid column definitions for each table, shared by its header and rows.</summary>
    [ObservableProperty] private string _logColumns = "";
    [ObservableProperty] private string _qsoColumns = "";
    [ObservableProperty] private string _heardColumns = "";

    public double CallersWidth => _settings.Current.Terminal.CallersWidth;
    public double QsoShare => _settings.Current.Terminal.QsoShare;

    public string LegendToMeBrushHex => _settings.Current.Terminal.ToMeColour;
    public string LegendCqBrushHex => _settings.Current.Terminal.CqColour;
    public string LegendMyTxBrushHex => _settings.Current.Terminal.MyTxColour;
    public string LegendMyDigipeatedBrushHex => _settings.Current.Terminal.MyDigipeatedColour;

    public TerminalService Service => _service;

    /// <summary>Fills the status line before the window's first layout, so the strip is not measured empty.</summary>
    public void PrepareForShow() => RefreshTick();

    public void OnWindowOpened()
    {
        _windowOpen = true;
        _uiTimer.Start();
        try
        {
            ResolveSatelliteContext();
            if (_settings.Current.Terminal.Agwpe.AutoConnect)
                _service.Start();
            _ = RefreshWorkedSetsAsync();
            RefreshTick();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminal window open refresh failed");
        }
    }

    public void OnWindowClosed()
    {
        _windowOpen = false;
        _uiTimer.Stop();
        _ = StopServiceAsync();
    }

    /// <summary>Called after the settings window saves: macros, colours and connection may all have changed.</summary>
    public async Task ApplySettingsAsync()
    {
        var terminal = _settings.Current.Terminal;
        terminal.Normalise();
        _settings.RequestSave();
        ReloadMacros();
        TxDelaySeconds = terminal.TxDelaySeconds;
        FontSize = terminal.FontSize;
        QsoFontSize = terminal.QsoFontSize;
        HeardFontSize = terminal.HeardFontSize;
        OnPropertyChanged(nameof(LegendToMeBrushHex));
        OnPropertyChanged(nameof(LegendCqBrushHex));
        OnPropertyChanged(nameof(LegendMyTxBrushHex));
        OnPropertyChanged(nameof(LegendMyDigipeatedBrushHex));
        RebuildRows();
        ResolveSatelliteContext();
        if (_windowOpen)
            await _service.RestartAsync().ConfigureAwait(true);
        RefreshTick();
    }

    /// <summary>Widens or narrows one table column by <paramref name="delta"/> (in unscaled pixels).</summary>
    public void ResizeColumn(string? panel, int index, double delta)
    {
        if (ColumnWidthsFor(panel) is not { } widths || index < 0 || index >= widths.Count || widths[index] == 0)
            return;

        var resized = Math.Round(Math.Clamp(widths[index] + delta, TerminalTableLayout.MinColumnWidth, TerminalTableLayout.MaxColumnWidth));
        if (resized == widths[index])
            return;
        widths[index] = resized;
        RefreshColumnLayouts();
        _settings.RequestSave();
    }

    /// <summary>Puts one column back to its default width.</summary>
    public void ResetColumn(string? panel, int index)
    {
        if (ColumnWidthsFor(panel) is not { } widths || DefaultColumnsFor(panel) is not { } defaults
            || index < 0 || index >= widths.Count)
            return;

        widths[index] = defaults[index];
        RefreshColumnLayouts();
        _settings.RequestSave();
    }

    /// <summary>Saves the splitter positions after the user drags them.</summary>
    public void SaveCallersLayout(double? callersWidth, double? qsoShare)
    {
        var t = _settings.Current.Terminal;
        if (callersWidth is { } w)
            t.CallersWidth = TerminalTableLayout.ClampCallersWidth(w);
        if (qsoShare is { } s)
            t.QsoShare = TerminalTableLayout.ClampQsoShare(s);
        _settings.RequestSave();
    }

    private List<double>? ColumnWidthsFor(string? panel)
    {
        var t = _settings.Current.Terminal;
        return panel switch
        {
            LogPanel => t.LogColumnWidths,
            QsoPanel => t.QsoColumnWidths,
            HeardPanel => t.HeardColumnWidths,
            _ => null,
        };
    }

    private static IReadOnlyList<double>? DefaultColumnsFor(string? panel) => panel switch
    {
        LogPanel => TerminalTableLayout.DefaultLogColumns,
        QsoPanel => TerminalTableLayout.DefaultQsoColumns,
        HeardPanel => TerminalTableLayout.DefaultHeardColumns,
        _ => null,
    };

    private void RefreshColumnLayouts()
    {
        var t = _settings.Current.Terminal;
        t.LogColumnWidths = TerminalTableLayout.Normalise(t.LogColumnWidths, TerminalTableLayout.DefaultLogColumns);
        t.QsoColumnWidths = TerminalTableLayout.Normalise(t.QsoColumnWidths, TerminalTableLayout.DefaultQsoColumns);
        t.HeardColumnWidths = TerminalTableLayout.Normalise(
            TerminalTableLayout.MigrateHeardColumns(t.HeardColumnWidths), TerminalTableLayout.DefaultHeardColumns);
        LogColumns = TerminalTableLayout.ToColumnDefinitions(t.LogColumnWidths);
        QsoColumns = TerminalTableLayout.ToColumnDefinitions(t.QsoColumnWidths);
        HeardColumns = TerminalTableLayout.ToColumnDefinitions(t.HeardColumnWidths);
    }

    partial void OnToCallChanged(string value) => _service.CurrentToCall = NormaliseTo(value);

    partial void OnScrollToLastLineChanged(bool value)
    {
        _settings.Current.Terminal.ScrollToLastLine = value;
        _settings.RequestSave();
    }

    partial void OnShowCallersChanged(bool value)
    {
        _settings.Current.Terminal.ShowCallers = value;
        _settings.RequestSave();
    }

    partial void OnShowOwnOnlyChanged(bool value) => RebuildRows();

    [RelayCommand]
    private void TextLarger(string? panel) => ChangeTextSize(panel, +1);

    [RelayCommand]
    private void TextSmaller(string? panel) => ChangeTextSize(panel, -1);

    public void ChangeTextSize(string? panel, int delta)
    {
        var t = _settings.Current.Terminal;
        switch (panel)
        {
            case LogPanel:
                FontSize = t.FontSize = TerminalSettings.ClampFontSize(FontSize + delta, TerminalSettings.DefaultLogFontSize);
                break;
            case QsoPanel:
                QsoFontSize = t.QsoFontSize = TerminalSettings.ClampFontSize(QsoFontSize + delta, TerminalSettings.DefaultStationFontSize);
                break;
            case HeardPanel:
                HeardFontSize = t.HeardFontSize = TerminalSettings.ClampFontSize(HeardFontSize + delta, TerminalSettings.DefaultStationFontSize);
                break;
            default:
                return;
        }

        _settings.RequestSave();
    }

    partial void OnTxDelaySecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 60);
        if (clamped != value)
        {
            TxDelaySeconds = clamped;
            return;
        }

        _settings.Current.Terminal.TxDelaySeconds = clamped;
        _settings.RequestSave();
    }

    partial void OnIsAprsFormatChanged(bool value)
    {
        var profile = _service.Profile;
        var format = value ? TerminalMessageFormat.Aprs : TerminalMessageFormat.PlainText;
        if (profile is null || profile.Format == format)
            return;

        var overrides = _settings.Current.Terminal.SatelliteProfiles;
        var entry = overrides.FirstOrDefault(o => string.Equals(o.NoradId, profile.NoradId, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new TerminalSatelliteProfileSettings
            {
                NoradId = profile.NoradId,
                Name = profile.Name,
                Path = profile.Path,
                UhmName = profile.UhmName,
                ModemHint = profile.ModemHint,
            };
            overrides.Add(entry);
        }

        entry.Format = format;
        _settings.RequestSave();
        ResolveSatelliteContext();
    }

    partial void OnSelectedStationChanged(TerminalStationRowViewModel? value)
    {
        if (value is not null)
            ToCall = value.Call;
    }

    [RelayCommand]
    private Task SendAsync() => SendTextAsync(ToCall, MessageText, clearMessage: true);

    /// <summary>Minimum gap between position sends, so busy satellite digipeaters are not flooded.</summary>
    public static readonly TimeSpan PositionCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Macro text that sends our position instead of a message.</summary>
    public const string PositionMacroToken = "{POSITION}";

    private DateTime _positionCooldownUntilUtc = DateTime.MinValue;

    [ObservableProperty] private bool _canSendPosition;
    [ObservableProperty] private string _sendPositionTip = "";

    [RelayCommand]
    private async Task SendPositionAsync()
    {
        var now = DateTime.UtcNow;
        if (now < _positionCooldownUntilUtc)
        {
            StatusLine = _l.Get("Terminal.SendPosition.Cooldown", Math.Ceiling((_positionCooldownUntilUtc - now).TotalSeconds));
            return;
        }

        var (result, fix) = await _service.SendPositionAsync(DescribePosition).ConfigureAwait(true);
        if (result == TerminalSendResult.Sent && fix is not null)
        {
            _positionCooldownUntilUtc = DateTime.UtcNow + PositionCooldown;
            StatusLine = _l.Get("Terminal.Status.PositionSent." + fix.Origin);
        }
        else
        {
            StatusLine = result switch
            {
                TerminalSendResult.NotConnected => _l.Get("Terminal.Status.NotConnected"),
                TerminalSendResult.NoCallsign => _l.Get("Terminal.Status.NoCallsign"),
                TerminalSendResult.NotAprs => _l.Get("Terminal.Status.PositionNeedsAprs"),
                TerminalSendResult.NoPosition => _l.Get("Terminal.Status.PositionUnknown"),
                _ => _l.Get("Terminal.Status.SendFailed"),
            };
        }

        RefreshSendPosition(DateTime.UtcNow);
    }

    private string DescribePosition(TerminalPositionFix fix)
    {
        var precision = _settings.Current.Terminal.PositionPrecision;
        var grid = precision == TerminalPositionPrecision.Exact ? fix.Grid6 : fix.Grid6[..4];
        return _l.Get("Terminal.Log.Position", grid, _l.Get("Terminal.PositionOrigin." + fix.Origin));
    }

    private void RefreshSendPosition(DateTime now)
    {
        var cooling = now < _positionCooldownUntilUtc;
        CanSendPosition = IsConnected && IsAprsFormat && HasProfile && !cooling;
        if (cooling)
        {
            SendPositionTip = _l.Get("Terminal.SendPosition.Cooldown", Math.Ceiling((_positionCooldownUntilUtc - now).TotalSeconds));
            return;
        }

        if (!IsAprsFormat || !HasProfile)
        {
            SendPositionTip = _l.Get("Terminal.Status.PositionNeedsAprs");
            return;
        }

        var fix = _service.ResolvePosition();
        SendPositionTip = fix is null
            ? _l.Get("Terminal.Status.PositionUnknown")
            : _l.Get("Terminal.SendPosition.Tip",
                fix.Latitude.ToString("0.0000", CultureInfo.InvariantCulture),
                fix.Longitude.ToString("0.0000", CultureInfo.InvariantCulture),
                fix.Grid6,
                _l.Get("Terminal.PositionOrigin." + fix.Origin),
                _l.Get("Terminal.Precision." + _settings.Current.Terminal.PositionPrecision));
    }

    [RelayCommand]
    private async Task SendMacroAsync(string? parameter)
    {
        if (!int.TryParse(parameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            || index < 0 || index >= _settings.Current.Terminal.Macros.Count)
            return;

        var macro = _settings.Current.Terminal.Macros[index];
        if (string.IsNullOrWhiteSpace(macro.Text))
        {
            StatusLine = _l.Get("Terminal.Status.MacroEmpty", $"F{index + 1}");
            return;
        }

        if (string.Equals(macro.Text.Trim(), PositionMacroToken, StringComparison.OrdinalIgnoreCase))
        {
            await SendPositionAsync().ConfigureAwait(true);
            return;
        }

        var to = macro.SendsToCq ? TerminalMessageCodec.CqTo : ToCall;
        if (macro.SendsToCq)
            ToCall = TerminalMessageCodec.CqTo;
        var theirCall = IsBroadcast(to) ? null : NormaliseTo(to);
        var text = TerminalMessageCodec.ExpandMacro(macro.Text, _service.MyCall, _service.MyGrid, theirCall);
        await SendTextAsync(to, text, clearMessage: false).ConfigureAwait(true);
    }

    /// <summary>F12: save the call in the To box (or the selected station) to the logbook now.</summary>
    [RelayCommand]
    private async Task LogCallAsync()
    {
        var call = !IsBroadcast(ToCall) ? NormaliseTo(ToCall) : SelectedStation?.Call;
        if (string.IsNullOrWhiteSpace(call) || !TerminalText.LooksLikeCallsign(call))
        {
            StatusLine = _l.Get("Terminal.Status.LogNeedsCall");
            return;
        }

        var record = await _service.LogCallAsync(call).ConfigureAwait(true);
        if (record is null)
            StatusLine = _l.Get("Terminal.Status.LogFailed", call);
    }

    [RelayCommand]
    private void Connect()
    {
        if (_service.IsRunning)
            return;
        _service.Start();
        RefreshTick();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _service.StopAsync().ConfigureAwait(true);
        RefreshTick();
    }

    [RelayCommand]
    private void ClearLog()
    {
        _allRows.Clear();
        Rows.Clear();
        HasRows = false;
    }

    [RelayCommand]
    private void ClearQso()
    {
        _service.ClearQsoStations();
        RefreshTick();
    }

    [RelayCommand]
    private void ClearHeard()
    {
        SelectedStation = null;
        _service.ClearHeardStations();
        QsoStations.Clear();
        HeardStations.Clear();
        HasQsoStations = false;
        HasHeardStations = false;
        RefreshTick();
    }

    [RelayCommand]
    private void ResetStatistics()
    {
        _service.ResetStatistics();
        QsoStations.Clear();
        HeardStations.Clear();
        RefreshTick();
    }

    /// <summary>Pass map marker click: select the station in the heard list and put it in the To box.</summary>
    public void SelectStationByCall(string call)
    {
        var row = HeardStations.FirstOrDefault(r => string.Equals(r.Call, call, StringComparison.Ordinal));
        if (row is not null)
            SelectedStation = row;
        ToCall = call;
    }

    [RelayCommand]
    private void ReplyToSelected()
    {
        if (SelectedRow is not { } row)
            return;
        var from = TerminalText.BaseCallsign(row.From);
        if (TerminalText.LooksLikeCallsign(from) && !TerminalText.SameStation(from, _service.MyCall))
            ToCall = from;
    }

    private async Task SendTextAsync(string? to, string? text, bool clearMessage)
    {
        var body = (text ?? "").Trim();
        var result = await _service.SendAsync(NormaliseTo(to), body).ConfigureAwait(true);
        StatusLine = result switch
        {
            TerminalSendResult.Sent => _l.Get("Terminal.Status.Sent", body),
            TerminalSendResult.NotConnected => _l.Get("Terminal.Status.NotConnected"),
            TerminalSendResult.NoCallsign => _l.Get("Terminal.Status.NoCallsign"),
            TerminalSendResult.InvalidDestination => _l.Get("Terminal.Status.InvalidDestination"),
            TerminalSendResult.EmptyMessage => _l.Get("Terminal.Status.EmptyMessage"),
            _ => _l.Get("Terminal.Status.SendFailed"),
        };
        if (result == TerminalSendResult.Sent && clearMessage)
            MessageText = "";
    }

    private static bool IsBroadcast(string? to)
    {
        var t = (to ?? "").Trim();
        return t.Length == 0
               || string.Equals(t, TerminalMessageCodec.CqTo, StringComparison.OrdinalIgnoreCase)
               || string.Equals(t, TerminalMessageCodec.AllTo, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormaliseTo(string? to) => (to ?? "").Trim().ToUpperInvariant();

    private void ReloadMacros()
    {
        var macros = _settings.Current.Terminal.Macros;
        for (var i = 0; i < Macros.Count; i++)
        {
            var m = i < macros.Count ? macros[i] : new TerminalMacro();
            Macros[i].Label = m.Label;
            Macros[i].Text = m.Text;
        }
    }

    private void ResolveSatelliteContext()
    {
        var norad = _tracker.FocusedNoradId;
        var snapshot = _tracker.GetCurrent();
        var name = string.IsNullOrWhiteSpace(snapshot.SatelliteName) ? _frequencyOverlay.SatelliteName : snapshot.SatelliteName;
        var modeTypes = _frequencyOverlay.AvailableModes.Select(m => m.Type ?? "").Append(snapshot.ModeType).ToList();
        var profile = TerminalSatelliteProfiles.Resolve(norad, name, modeTypes, _settings.Current.Terminal.SatelliteProfiles);
        _service.SetSatelliteContext(profile);

        HasProfile = profile is not null;
        if (profile is null)
        {
            SatelliteText = string.IsNullOrWhiteSpace(name) || name == "-" ? "-" : name.Trim();
            ProfileText = _l.Get("Terminal.Profile.None");
            ModemHintText = "";
            return;
        }

        SatelliteText = profile.Name;
        var formatText = profile.Format == TerminalMessageFormat.Aprs
            ? _l.Get("Terminal.Format.Aprs")
            : _l.Get("Terminal.Format.Plain");
        var pathText = string.IsNullOrWhiteSpace(profile.Path) ? _l.Get("Terminal.Path.None") : profile.Path;
        ProfileText = _l.Get("Terminal.Profile.Summary", formatText, pathText);
        ModemHintText = _l.Get("Terminal.Profile.Modem", profile.ModemHint);
        IsAprsFormat = profile.Format == TerminalMessageFormat.Aprs;
    }

    private void OnMessageAdded(TerminalLogEntry entry) =>
        Dispatcher.UIThread.Post(() => AddRow(entry));

    private void AddRow(TerminalLogEntry entry)
    {
        if (_disposed)
            return;
        var row = CreateRow(entry);
        _allRows.Add(row);
        if (_allRows.Count > TerminalService.MaxLogEntries)
        {
            var removed = _allRows[0];
            _allRows.RemoveAt(0);
            Rows.Remove(removed);
        }

        if (ShowOwnOnly && !row.InvolvesMe)
            return;
        Rows.Add(row);
        HasRows = true;
        if (ScrollToLastLine)
            ScrollRequested?.Invoke(row);
    }

    private void RebuildRows()
    {
        var rebuilt = _allRows.Select(r => CreateRow(r.Entry)).ToList();
        _allRows.Clear();
        _allRows.AddRange(rebuilt);
        Rows.Clear();
        foreach (var row in _allRows.Where(r => !ShowOwnOnly || r.InvolvesMe))
            Rows.Add(row);
        HasRows = Rows.Count > 0;
        if (ScrollToLastLine && Rows.Count > 0)
            ScrollRequested?.Invoke(Rows[^1]);
    }

    private TerminalMessageRowViewModel CreateRow(TerminalLogEntry entry)
    {
        var t = _settings.Current.Terminal;
        var (tagKey, hex) = entry.Kind switch
        {
            TerminalMessageKind.ToMe => ("Terminal.Tag.ToMe", t.ToMeColour),
            TerminalMessageKind.Cq => ("Terminal.Tag.Cq", t.CqColour),
            TerminalMessageKind.MyTx => ("Terminal.Tag.MyTx", t.MyTxColour),
            TerminalMessageKind.MyDigipeated => ("Terminal.Tag.MyDigipeated", t.MyDigipeatedColour),
            TerminalMessageKind.Telemetry => ("Terminal.Tag.Telemetry", null),
            TerminalMessageKind.Beacon => ("Terminal.Tag.Beacon", null),
            _ => ("Terminal.Tag.Normal", null),
        };
        Color? colour = !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) && c.A > 0 ? c : null;
        var micE = entry.MicEStatus is { } status and not AprsMicEStatus.OffDuty
            ? _l.Get("Terminal.MicE." + status)
            : null;
        return new TerminalMessageRowViewModel(entry, _l.Get(tagKey), colour, micE);
    }

    private void OnServiceChanged()
    {
        if (Interlocked.Exchange(ref _changedQueued, 1) == 1)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _changedQueued, 0);
            if (!_disposed)
                RefreshTick();
        });
    }

    private void OnServiceLogged(QsoRecord record, bool manual) =>
        Dispatcher.UIThread.Post(() =>
        {
            StatusLine = _l.Get(manual ? "Terminal.Status.LoggedManual" : "Terminal.Status.LoggedAuto", record.Call);
        });

    private void OnLogbookQsosChanged(long logbookId) => _ = RefreshWorkedSetsAsync();

    private async Task RefreshWorkedSetsAsync()
    {
        try
        {
            var (calls, grids) = await _logbook.LoadWorkedCallAndGridFieldsAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _workedCalls = calls;
                _workedGridFields = grids;
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminal worked-call refresh failed");
        }
    }

    private async Task StopServiceAsync()
    {
        try
        {
            await _service.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Terminal stop failed");
        }
    }

    private void RefreshTick()
    {
        if (_disposed)
            return;
        var now = DateTime.UtcNow;
        UtcClockText = now.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        ResolveSatelliteContext();
        RefreshStationInfo(now);
        RefreshStatistics();
        RefreshStatus();
        RefreshStations(now);
        RefreshSendPosition(now);
    }

    private void RefreshStationInfo(DateTime now)
    {
        var call = _service.MyCall;
        var ssid = _settings.Current.Terminal.Ssid;
        MyCallText = string.IsNullOrWhiteSpace(call) ? "-" : ssid > 0 ? $"{call}-{ssid}" : call;
        MyGridText = string.IsNullOrWhiteSpace(_service.MyGrid) ? "-" : _service.MyGrid;

        var angles = _service.CurrentLookAngles();
        if (angles is null)
        {
            AzimuthText = ElevationText = RangeText = "-";
            ElevationBarValue = 0;
            ElevationOpacity = 0.5;
        }
        else
        {
            ElevationBarValue = Math.Clamp(angles.ElevationDeg, 0, 90);
            ElevationOpacity = angles.ElevationDeg > 0 ? 1.0 : 0.5;
            AzimuthText = $"{angles.AzimuthDeg:0}°";
            ElevationText = angles.ElevationDeg < 0
                ? $"−{Math.Abs(angles.ElevationDeg):0.0}°"
                : $"{angles.ElevationDeg:0.0}°";
            RangeText = $"{angles.RangeKm:0} km";
        }

        var crossing = _service.EstimateHorizonCrossing();
        HorizonLabel = _l.Get(crossing?.IsAos == true ? "Terminal.Station.Aos" : "Terminal.Station.Los");
        LosText = crossing is { } c
            ? $"{c.Utc.ToString("HH:mm", CultureInfo.InvariantCulture)} ({FormatSpan(c.Utc - now)})"
            : "-";

        IsBelowHorizon = HasProfile && angles is { ElevationDeg: <= 0 };
        HorizonWarningText = !IsBelowHorizon
            ? ""
            : crossing is { IsAos: true } aos
                ? _l.Get("Terminal.Warning.BelowHorizonAos", SatelliteText, FormatSpan(aos.Utc - now))
                : _l.Get("Terminal.Warning.BelowHorizon", SatelliteText);
    }

    private void RefreshStatistics()
    {
        var s = _service.GetStatistics();
        UniqueCallsignsText = s.UniqueCallsigns.ToString(CultureInfo.CurrentCulture);
        MyTxText = s.MyTx.ToString(CultureInfo.CurrentCulture);
        MyRxText = s.MyRx.ToString(CultureInfo.CurrentCulture);
        SuccessText = s.MyDigipeatSuccessPercent is { } p ? $"{p:0}%" : "-";
        DigipeatedText = s.Digipeated.ToString(CultureInfo.CurrentCulture);
        MedianDelayText = s.MedianDelaySeconds is { } d ? $"{d:0.0} s" : "-";
        UhmUsersText = _service.UhmStatus == TerminalUhmStatus.Active
            ? s.UhmUsers.ToString(CultureInfo.CurrentCulture)
            : "-";
    }

    private void RefreshStatus()
    {
        IsRunning = _service.IsRunning;
        IsConnected = _service.TransportState == TerminalTransportState.Connected;
        var agw = _settings.Current.Terminal.Agwpe;
        var endpoint = $"{agw.Host}:{agw.Port}";
        ModemStatusText = _service.TransportState switch
        {
            TerminalTransportState.Connected => _l.Get("Terminal.Modem.Connected", endpoint,
                string.IsNullOrWhiteSpace(_service.ModemDescription) ? "AGWPE" : _service.ModemDescription),
            TerminalTransportState.Connecting => _l.Get("Terminal.Modem.Connecting", endpoint),
            TerminalTransportState.Error => _l.Get("Terminal.Modem.Error", endpoint, _service.LastConnectionError ?? ""),
            _ when _service.IsRunning && !string.IsNullOrWhiteSpace(_service.LastConnectionError) =>
                _l.Get("Terminal.Modem.Error", endpoint, _service.LastConnectionError),
            _ when _service.IsRunning => _l.Get("Terminal.Modem.Connecting", endpoint),
            _ => _l.Get("Terminal.Modem.Disconnected"),
        };
        ModemDot = _service.TransportState switch
        {
            TerminalTransportState.Connected => DotOk,
            TerminalTransportState.Error => DotError,
            _ when _service.IsRunning && !string.IsNullOrWhiteSpace(_service.LastConnectionError) => DotError,
            _ when _service.IsRunning => DotBusy,
            _ => DotOff,
        };

        AprsIsStatusText = _service.AprsIsState switch
        {
            AprsIsState.Connected => _l.Get("Terminal.AprsIs.Connected"),
            AprsIsState.Connecting => _l.Get("Terminal.AprsIs.Connecting"),
            AprsIsState.Unverified => _l.Get("Terminal.AprsIs.Unverified"),
            AprsIsState.Error => _l.Get("Terminal.AprsIs.Error"),
            _ => _l.Get("Terminal.AprsIs.Off"),
        };
        AprsIsDot = _service.AprsIsState switch
        {
            AprsIsState.Connected => DotOk,
            AprsIsState.Connecting => DotBusy,
            AprsIsState.Unverified or AprsIsState.Error => DotError,
            _ => DotOff,
        };
        UhmDot = _service.UhmStatus switch
        {
            TerminalUhmStatus.Active => DotOk,
            TerminalUhmStatus.Off => DotOff,
            TerminalUhmStatus.NoSatellite or TerminalUhmStatus.RateLimited => DotBusy,
            _ => DotError,
        };

        UhmStatusText = _service.UhmStatus switch
        {
            TerminalUhmStatus.Active => _l.Get("Terminal.Uhm.Status.Active"),
            TerminalUhmStatus.NoToken => _l.Get("Terminal.Uhm.Status.NoToken"),
            TerminalUhmStatus.NoSatellite => _l.Get("Terminal.Uhm.Status.NoSatellite"),
            TerminalUhmStatus.RateLimited => _l.Get("Terminal.Uhm.Status.RateLimited"),
            TerminalUhmStatus.NotEnabled => _l.Get("Terminal.Uhm.Status.NotEnabled"),
            TerminalUhmStatus.Unauthorised => _l.Get("Terminal.Uhm.Status.Unauthorised"),
            TerminalUhmStatus.Forbidden => _l.Get("Terminal.Uhm.Status.Forbidden"),
            TerminalUhmStatus.Error => _l.Get("Terminal.Uhm.Status.Error"),
            _ => _l.Get("Terminal.Uhm.Status.Off"),
        };
        UhmServerText = _service.UhmServerMessage ?? "";
    }

    private void RefreshStations(DateTime now)
    {
        var stations = _service.GetStations();
        var qso = stations
            .Where(s => s.InQso || s.Logged || s.RogerSent || s.RogerReceived
                        || s.State is not (TerminalStationState.Heard or TerminalStationState.Cq))
            .OrderByDescending(s => s.LastActivityUtc)
            .ToList();
        var heard = stations.OrderByDescending(s => s.LastHeardUtc).ToList();
        SyncStations(QsoStations, qso, now);
        SyncStations(HeardStations, heard, now);
        HasQsoStations = QsoStations.Count > 0;
        HasHeardStations = HeardStations.Count > 0;
    }

    private void SyncStations(
        ObservableCollection<TerminalStationRowViewModel> target,
        IReadOnlyList<TerminalStationSnapshot> source,
        DateTime now)
    {
        var sameOrder = target.Count == source.Count
                        && target.Select(r => r.Call).SequenceEqual(source.Select(s => s.Call), StringComparer.Ordinal);
        if (!sameOrder)
        {
            var existing = target.ToDictionary(r => r.Call, StringComparer.Ordinal);
            var selected = target == HeardStations ? SelectedStation : null;
            target.Clear();
            foreach (var s in source)
                target.Add(existing.TryGetValue(s.Call, out var row) ? row : new TerminalStationRowViewModel(s.Call));
            if (selected is not null && target.Contains(selected))
                SelectedStation = selected;
        }

        for (var i = 0; i < source.Count; i++)
            UpdateStationRow(target[i], source[i], now);
    }

    private void UpdateStationRow(TerminalStationRowViewModel row, TerminalStationSnapshot s, DateTime now)
    {
        row.Grid = s.Grid ?? "";
        row.SinceText = FormatSpan(now - s.LastHeardUtc);
        row.CqTxText = $"{s.CqCount}/{s.TxCount}";
        var el = _service.ElevationFromGrid(s.Grid);
        row.ElevationText = el is { } e ? (e < 0 ? $"−{Math.Abs(e):0}°" : $"{e:0}°") : "-";
        var home = _settings.Current.GroundStation;
        if (TerminalStationGeo.TryLocate(s.Position, s.Grid, out var lat, out var lon))
        {
            var (km, bearing) = TerminalStationGeo.DistanceBearing(home.LatitudeDeg, home.LongitudeDeg, lat, lon);
            row.DistanceText = TerminalStationGeo.FormatDistanceBearing(km, bearing);
        }
        else
        {
            row.DistanceText = "-";
        }
        row.StateText = _l.Get("Terminal.State." + s.State);
        row.Uhm = s.Uhm;
        row.UhmText = s.Uhm switch
        {
            UhmMatchState.Waiting => _l.Get("Terminal.Uhm.Waiting"),
            UhmMatchState.HeardMe => _l.Get("Terminal.Uhm.HeardMe"),
            UhmMatchState.Mutual => _l.Get("Terminal.Uhm.Mutual"),
            UhmMatchState.Expired => _l.Get("Terminal.Uhm.Expired"),
            _ when s.IsUhmUser => _l.Get("Terminal.Uhm.User"),
            _ => "-",
        };
        row.LoggedText = s.Logged ? _l.Get("Terminal.Logged.Yes") : "";
        row.MarkerText = MarkerFor(s);
    }

    private string MarkerFor(TerminalStationSnapshot s)
    {
        if (!_workedCalls.Contains(s.Call))
            return _l.Get("Terminal.Marker.NewCall");
        var field = s.Grid is { Length: >= 4 } g ? g[..4].ToUpperInvariant() : null;
        if (field is not null && !_workedGridFields.Contains(field))
            return _l.Get("Terminal.Marker.NewGrid");
        return _l.Get("Terminal.Marker.Worked");
    }

    private static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _uiTimer.Stop();
        _service.MessageAdded -= OnMessageAdded;
        _service.Changed -= OnServiceChanged;
        _service.Logged -= OnServiceLogged;
        _logbook.QsosChanged -= OnLogbookQsosChanged;
    }
}
