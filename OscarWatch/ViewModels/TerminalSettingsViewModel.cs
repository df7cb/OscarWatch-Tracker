using System.Collections.ObjectModel;
using System.Diagnostics;
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

public sealed record TerminalTransportOption(TerminalTransportKind Kind, string Label, bool IsAvailable);

public sealed record TerminalFormatOption(TerminalMessageFormat Value, string Label);

public sealed record TerminalRadioPortOption(int Index, string Label);

public sealed record TerminalPrecisionOption(TerminalPositionPrecision Value, string Label);

/// <summary>APRS symbol preset: table character then symbol code.</summary>
public sealed record TerminalSymbolOption(string Symbol, string Label);

public sealed partial class TerminalMacroEditViewModel : ObservableObject
{
    public TerminalMacroEditViewModel(int index, TerminalMacro macro)
    {
        KeyLabel = $"F{index + 1}";
        _label = macro.Label;
        _text = macro.Text;
        _sendsToCq = macro.SendsToCq;
    }

    public string KeyLabel { get; }
    [ObservableProperty] private string _label;
    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _sendsToCq;

    public TerminalMacro ToMacro() => new() { Label = Label.Trim(), Text = Text.Trim(), SendsToCq = SendsToCq };
}

/// <summary>Link type choice for a profile; a null value works it out from the frequencies.</summary>
public sealed record TerminalLinkTypeOption(TerminalLinkType? Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class TerminalProfileEditViewModel : ObservableObject
{
    public TerminalProfileEditViewModel(
        TerminalSatelliteProfileSettings p,
        IReadOnlyList<TerminalFormatOption> formats,
        IReadOnlyList<TerminalLinkTypeOption> linkTypes)
    {
        Formats = formats;
        LinkTypes = linkTypes;
        _noradId = p.NoradId;
        _name = p.Name;
        _path = p.Path;
        _uhmName = p.UhmName;
        _modemHint = p.ModemHint;
        _selectedFormat = formats.FirstOrDefault(f => f.Value == p.Format) ?? formats[0];
        _selectedLinkType = linkTypes.FirstOrDefault(l => l.Value == p.LinkType) ?? linkTypes[0];
    }

    public IReadOnlyList<TerminalFormatOption> Formats { get; }
    public IReadOnlyList<TerminalLinkTypeOption> LinkTypes { get; }
    [ObservableProperty] private TerminalLinkTypeOption _selectedLinkType;
    [ObservableProperty] private string _noradId;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _uhmName;
    [ObservableProperty] private string _modemHint;
    [ObservableProperty] private TerminalFormatOption _selectedFormat;

    public TerminalSatelliteProfileSettings ToSettings() => new()
    {
        NoradId = NoradId.Trim(),
        Name = Name.Trim(),
        Format = SelectedFormat.Value,
        Path = Path.Trim().ToUpperInvariant(),
        UhmName = UhmName.Trim(),
        ModemHint = ModemHint.Trim(),
        LinkType = SelectedLinkType.Value,
    };
}

/// <summary>OscarWatch Terminal settings dialogue. Edits a working copy; <see cref="Save"/> writes it back.</summary>
public sealed partial class TerminalSettingsViewModel : ViewModelBase
{
    private const string AprsPasscodeUrl = "https://apps.magicbug.co.uk/passcode/";

    private static readonly ILogger Log = Serilog.Log.ForContext<TerminalSettingsViewModel>();

    private readonly ISettingsService _settings;
    private readonly ILocalizationService _l;
    private readonly TerminalService _service;
    private readonly UhmApiClient _uhm = new();

    public TerminalSettingsViewModel(
        ISettingsService settings,
        ILocalizationService localization,
        TerminalService service,
        IRigController? rig = null)
    {
        _settings = settings;
        _l = localization;
        _service = service;
        var t = settings.Current.Terminal;
        t.Normalise();

        TransportOptions =
        [
            new(TerminalTransportKind.Agwpe, _l.Get("Terminal.Settings.Transport.Agwpe"), true),
            new(TerminalTransportKind.BuiltIn, _l.Get("Terminal.Settings.Transport.BuiltIn"), true),
            new(TerminalTransportKind.KissTcp, _l.Get("Terminal.Settings.Transport.KissTcp"), false),
            new(TerminalTransportKind.KissSerial, _l.Get("Terminal.Settings.Transport.KissSerial"), false),
        ];
        FormatOptions =
        [
            new(TerminalMessageFormat.Aprs, _l.Get("Terminal.Format.Aprs")),
            new(TerminalMessageFormat.PlainText, _l.Get("Terminal.Format.Plain")),
        ];
        LinkTypeOptions =
        [
            new(null, _l.Get("Terminal.LinkType.Auto")),
            new(TerminalLinkType.Simplex, _l.Get("Terminal.LinkType.Simplex")),
            new(TerminalLinkType.SameBand, _l.Get("Terminal.LinkType.SameBand")),
            new(TerminalLinkType.Crossband, _l.Get("Terminal.LinkType.Crossband")),
        ];
        BuiltIn = new TerminalBuiltInModemViewModel(settings, localization, service, rig);

        _selectedTransport = TransportOptions.FirstOrDefault(o => o.Kind == t.Transport && o.IsAvailable)
                             ?? TransportOptions[0];
        _agwHost = t.Agwpe.Host;
        _agwPort = t.Agwpe.Port;
        _autoConnect = t.Agwpe.AutoConnect;
        _callsign = t.Callsign;
        _ssid = t.Ssid;
        RadioPorts.Add(new TerminalRadioPortOption(t.Agwpe.RadioPort, RadioPortLabel(t.Agwpe.RadioPort, null)));
        _selectedRadioPort = RadioPorts[0];

        for (var i = 0; i < t.Macros.Count; i++)
            Macros.Add(new TerminalMacroEditViewModel(i, t.Macros[i]));
        foreach (var p in t.SatelliteProfiles)
            Profiles.Add(new TerminalProfileEditViewModel(p, FormatOptions, LinkTypeOptions));

        _toMeColour = t.ToMeColour;
        _cqColour = t.CqColour;
        _myTxColour = t.MyTxColour;
        _myDigipeatedColour = t.MyDigipeatedColour;

        _aprsIsEnabled = t.AprsIs.Enabled;
        _aprsIsServer = t.AprsIs.Server;
        _aprsIsPort = t.AprsIs.Port;
        _aprsIsPasscode = t.AprsIs.Passcode;

        _uhmEnabled = t.Uhm.Enabled;
        _uhmFrameLevelMatch = t.Uhm.FrameLevelMatch;

        _autoLog = t.AutoLog;
        _aprsAutoAck = t.AprsAutoAck;
        _txDelaySeconds = t.TxDelaySeconds;
        _modemTxDelayMs = t.ModemTxDelayMs;
        _modemTxTailMs = t.ModemTxTailMs;
        var rigSettings = settings.Current.Rig;
        _isSingleVfoFt847 = rigSettings is { Type: RigType.YaesuFt847, DualRadioEnabled: false };
        _fontSize = t.FontSize;

        PrecisionOptions = Enum.GetValues<TerminalPositionPrecision>()
            .Select(p => new TerminalPrecisionOption(p, _l.Get("Terminal.Precision." + p)))
            .ToList();
        SymbolOptions =
        [
            new("/[", _l.Get("Terminal.Settings.Position.Symbol.Person")),
            new("/-", _l.Get("Terminal.Settings.Position.Symbol.House")),
            new("/>", _l.Get("Terminal.Settings.Position.Symbol.Car")),
            new("/`", _l.Get("Terminal.Settings.Position.Symbol.Dish")),
        ];
        _selectedPrecision = PrecisionOptions.First(o => o.Value == t.PositionPrecision);
        _positionSymbol = t.PositionSymbol;
        _selectedSymbol = SymbolOptions.FirstOrDefault(o => o.Symbol == t.PositionSymbol);
        _positionComment = t.PositionComment;
    }

    public IReadOnlyList<TerminalPrecisionOption> PrecisionOptions { get; }
    public IReadOnlyList<TerminalSymbolOption> SymbolOptions { get; }

    [ObservableProperty] private TerminalPrecisionOption _selectedPrecision;
    [ObservableProperty] private TerminalSymbolOption? _selectedSymbol;
    [ObservableProperty] private string _positionSymbol;
    [ObservableProperty] private string _positionComment;

    partial void OnSelectedSymbolChanged(TerminalSymbolOption? value)
    {
        if (value is not null && PositionSymbol != value.Symbol)
            PositionSymbol = value.Symbol;
    }

    partial void OnPositionSymbolChanged(string value)
    {
        var match = SymbolOptions.FirstOrDefault(o => o.Symbol == value);
        if (!Equals(SelectedSymbol, match))
            SelectedSymbol = match;
    }

    /// <summary>Raised with true after Save, false on Cancel.</summary>
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<TerminalTransportOption> TransportOptions { get; }
    public IReadOnlyList<TerminalFormatOption> FormatOptions { get; }
    public ObservableCollection<TerminalRadioPortOption> RadioPorts { get; } = [];
    public ObservableCollection<TerminalMacroEditViewModel> Macros { get; } = [];
    public ObservableCollection<TerminalProfileEditViewModel> Profiles { get; } = [];

    public string BuiltInProfilesText => string.Join(Environment.NewLine, TerminalSatelliteProfiles.BuiltIn.Select(p =>
        _l.Get("Terminal.Settings.Profiles.BuiltInLine", p.Name, p.NoradId,
            p.Format == TerminalMessageFormat.Aprs ? _l.Get("Terminal.Format.Aprs") : _l.Get("Terminal.Format.Plain"),
            string.IsNullOrWhiteSpace(p.Path) ? _l.Get("Terminal.Path.None") : p.Path,
            p.ModemHint)));

    public string GroundStationCallsign => string.IsNullOrWhiteSpace(_settings.Current.GroundStation.Callsign)
        ? "-"
        : _settings.Current.GroundStation.Callsign.Trim().ToUpperInvariant();

    public bool HasUhmToken => !string.IsNullOrWhiteSpace(_settings.Current.SatelliteStatus.ApiToken);
    public bool HasNoUhmToken => !HasUhmToken;

    [ObservableProperty] private TerminalTransportOption _selectedTransport;
    [ObservableProperty] private string _agwHost;
    [ObservableProperty] private int _agwPort;
    [ObservableProperty] private bool _autoConnect;
    [ObservableProperty] private TerminalRadioPortOption? _selectedRadioPort;
    [ObservableProperty] private string _modemTestText = "";
    [ObservableProperty] private bool _isTestingModem;
    [ObservableProperty] private string _callsign;
    [ObservableProperty] private int _ssid;
    [ObservableProperty] private TerminalProfileEditViewModel? _selectedProfile;

    [ObservableProperty] private string _toMeColour;
    [ObservableProperty] private string _cqColour;
    [ObservableProperty] private string _myTxColour;
    [ObservableProperty] private string _myDigipeatedColour;

    [ObservableProperty] private bool _aprsIsEnabled;
    [ObservableProperty] private string _aprsIsServer;
    [ObservableProperty] private int _aprsIsPort;
    [ObservableProperty] private string _aprsIsPasscode;

    [ObservableProperty] private bool _uhmEnabled;
    [ObservableProperty] private bool _uhmFrameLevelMatch;
    [ObservableProperty] private string _uhmTestText = "";
    [ObservableProperty] private string _uhmWarningText = "";
    [ObservableProperty] private bool _isTestingUhm;

    [ObservableProperty] private bool _autoLog;
    [ObservableProperty] private bool _aprsAutoAck;
    [ObservableProperty] private int _txDelaySeconds;
    [ObservableProperty] private int _modemTxDelayMs;
    [ObservableProperty] private int _modemTxTailMs;

    private readonly bool _isSingleVfoFt847;

    /// <summary>
    /// TXDELAY and TXTAIL: the built-in modem sends them, and the FT-847 (single radio) retunes around
    /// each transmit using them, so other setups do not need the fields.
    /// </summary>
    public bool ShowModemTiming => _isSingleVfoFt847 || IsBuiltInSelected;

    public string ModemTimingHelp => _l.Get(IsBuiltInSelected
        ? "Terminal.Settings.ModemTiming.BuiltInHelp"
        : "Terminal.Settings.ModemTiming.Help");

    [ObservableProperty] private int _fontSize;

    public TerminalBuiltInModemViewModel BuiltIn { get; }
    public IReadOnlyList<TerminalLinkTypeOption> LinkTypeOptions { get; }

    public bool IsAgwpeSelected => SelectedTransport.Kind == TerminalTransportKind.Agwpe;
    public bool IsBuiltInSelected => SelectedTransport.Kind == TerminalTransportKind.BuiltIn;

    /// <summary>A long TXTAIL delays hearing your own digipeat on a simplex satellite.</summary>
    public bool ShowLongTailWarning =>
        IsBuiltInSelected && ModemTxTailMs > TerminalBuiltInModemSettings.LongHalfDuplexTailMs;

    partial void OnModemTxTailMsChanged(int value) => OnPropertyChanged(nameof(ShowLongTailWarning));

    partial void OnSelectedTransportChanged(TerminalTransportOption value)
    {
        if (!value.IsAvailable)
        {
            ModemTestText = _l.Get("Terminal.Settings.Transport.Planned", value.Label);
            SelectedTransport = TransportOptions[0];
            return;
        }

        OnPropertyChanged(nameof(IsAgwpeSelected));
        OnPropertyChanged(nameof(IsBuiltInSelected));
        OnPropertyChanged(nameof(ShowModemTiming));
        OnPropertyChanged(nameof(ModemTimingHelp));
        OnPropertyChanged(nameof(ShowLongTailWarning));
        if (value.Kind == TerminalTransportKind.BuiltIn && BuiltIn.InputDeviceOptions.Count == 0)
            BuiltIn.Refresh();
    }

    /// <summary>The window calls this once shown, so listing soundcards does not hold up opening it.</summary>
    public void OnOpened()
    {
        if (IsBuiltInSelected)
            BuiltIn.Refresh();
    }

    public void OnClosed() => BuiltIn.Dispose();

    private string RadioPortLabel(int index, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? _l.Get("Terminal.Settings.RadioPort.Item", index + 1)
            : $"{index + 1}: {description.Trim()}";

    /// <summary>Connects to the AGWPE host just long enough to read its version and radio ports.</summary>
    [RelayCommand]
    private async Task TestModemAsync()
    {
        if (IsTestingModem)
            return;
        IsTestingModem = true;
        ModemTestText = _l.Get("Terminal.Settings.Modem.Testing");
        try
        {
            var host = string.IsNullOrWhiteSpace(AgwHost) ? "127.0.0.1" : AgwHost.Trim();
            var port = AgwPort is > 0 and <= 65535 ? AgwPort : TerminalAgwpeSettings.DefaultPort;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await using var client = new AgwpeClient(host, port, "");
            await client.ConnectAsync(cts.Token).ConfigureAwait(true);
            var ports = await client.GetRadioPortsAsync(cts.Token).ConfigureAwait(true);

            var keep = SelectedRadioPort?.Index ?? 0;
            RadioPorts.Clear();
            foreach (var p in ports)
                RadioPorts.Add(new TerminalRadioPortOption(p.Index, RadioPortLabel(p.Index, p.Description)));
            if (RadioPorts.Count == 0)
                RadioPorts.Add(new TerminalRadioPortOption(keep, RadioPortLabel(keep, null)));
            SelectedRadioPort = RadioPorts.FirstOrDefault(p => p.Index == keep) ?? RadioPorts[0];

            ModemTestText = _l.Get("Terminal.Settings.Modem.TestOk",
                string.IsNullOrWhiteSpace(client.ModemDescription) ? "AGWPE" : client.ModemDescription,
                ports.Count);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException
                                       or TimeoutException or InvalidOperationException or InvalidDataException)
        {
            Log.Information(ex, "Terminal AGWPE test failed");
            ModemTestText = _l.Get("Terminal.Settings.Modem.TestFailed", ex.Message);
        }
        finally
        {
            IsTestingModem = false;
        }
    }

    [RelayCommand]
    private void AddCurrentSatellite()
    {
        var current = _service.Profile;
        var source = current is null
            ? new TerminalSatelliteProfileSettings()
            : new TerminalSatelliteProfileSettings
            {
                NoradId = current.NoradId,
                Name = current.Name,
                Format = current.Format,
                Path = current.Path,
                UhmName = current.UhmName,
                ModemHint = current.ModemHint,
            };
        var existing = Profiles.FirstOrDefault(p => source.NoradId.Length > 0
                                                    && string.Equals(p.NoradId.Trim(), source.NoradId, StringComparison.Ordinal));
        if (existing is not null)
        {
            SelectedProfile = existing;
            return;
        }

        var row = new TerminalProfileEditViewModel(source, FormatOptions, LinkTypeOptions);
        Profiles.Add(row);
        SelectedProfile = row;
    }

    [RelayCommand]
    private void RemoveProfile()
    {
        if (SelectedProfile is { } p)
            Profiles.Remove(p);
    }

    [RelayCommand]
    private void ResetMacros()
    {
        var defaults = TerminalSettings.DefaultMacros();
        Macros.Clear();
        for (var i = 0; i < defaults.Count; i++)
            Macros.Add(new TerminalMacroEditViewModel(i, defaults[i]));
    }

    [RelayCommand]
    private void ResetColours()
    {
        var d = new TerminalSettings();
        ToMeColour = d.ToMeColour;
        CqColour = d.CqColour;
        MyTxColour = d.MyTxColour;
        MyDigipeatedColour = d.MyDigipeatedColour;
    }

    /// <summary>Checks the OscarWatch.org token with <c>/api/v1/me</c> and reads the UHM server status.</summary>
    [RelayCommand]
    private async Task TestUhmAsync()
    {
        if (IsTestingUhm)
            return;
        IsTestingUhm = true;
        UhmWarningText = "";
        UhmTestText = _l.Get("Terminal.Settings.Uhm.Testing");
        try
        {
            var statusSettings = _settings.Current.SatelliteStatus;
            var me = await _uhm.GetMeAsync(statusSettings).ConfigureAwait(true);
            if (!me.Result.Success)
            {
                UhmTestText = UhmErrorText(me.Result);
                return;
            }

            var status = await _uhm.GetStatusAsync(statusSettings).ConfigureAwait(true);
            var account = TerminalText.BaseCallsign(me.Callsign);
            UhmTestText = _l.Get("Terminal.Settings.Uhm.TestOk", string.IsNullOrWhiteSpace(account) ? "-" : account);
            if (status.Result.Success && !string.IsNullOrWhiteSpace(status.Message))
                UhmTestText += Environment.NewLine + status.Message;

            var terminalCall = new TerminalSettings { Callsign = Callsign }.EffectiveCallsign(_settings.Current.GroundStation.Callsign);
            if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(terminalCall)
                && !TerminalText.SameStation(account, terminalCall))
            {
                UhmWarningText = _l.Get("Terminal.Settings.Uhm.CallMismatch", account, terminalCall);
            }
        }
        finally
        {
            IsTestingUhm = false;
        }
    }

    private string UhmErrorText(UhmResult result) => result.Error switch
    {
        UhmErrorKind.MissingToken => _l.Get("Terminal.Uhm.Status.NoToken"),
        UhmErrorKind.Unauthorised => _l.Get("Terminal.Uhm.Status.Unauthorised"),
        UhmErrorKind.Forbidden => _l.Get("Terminal.Uhm.Status.Forbidden"),
        UhmErrorKind.NotEnabled => _l.Get("Terminal.Uhm.Status.NotEnabled"),
        UhmErrorKind.RateLimited => _l.Get("Terminal.Uhm.Status.RateLimited"),
        _ => _l.Get("Terminal.Settings.Uhm.TestFailed", string.IsNullOrWhiteSpace(result.Message) ? result.Error.ToString() : result.Message),
    };

    [RelayCommand]
    private void OpenAprsPasscode()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AprsPasscodeUrl,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private void Save()
    {
        var t = _settings.Current.Terminal;
        t.Transport = SelectedTransport.Kind;
        t.Agwpe.Host = AgwHost?.Trim() ?? "";
        t.Agwpe.Port = AgwPort;
        t.Agwpe.AutoConnect = AutoConnect;
        t.Agwpe.RadioPort = SelectedRadioPort?.Index ?? 0;
        t.Callsign = TerminalText.BaseCallsign(Callsign);
        t.Ssid = Ssid;
        t.Macros = Macros.Select(m => m.ToMacro()).ToList();
        t.SatelliteProfiles = Profiles
            .Select(p => p.ToSettings())
            .Where(p => p.NoradId.Length > 0)
            .GroupBy(p => p.NoradId, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        t.ToMeColour = ToMeColour;
        t.CqColour = CqColour;
        t.MyTxColour = MyTxColour;
        t.MyDigipeatedColour = MyDigipeatedColour;
        t.AprsIs.Enabled = AprsIsEnabled;
        t.AprsIs.Server = AprsIsServer?.Trim() ?? "";
        t.AprsIs.Port = AprsIsPort;
        t.AprsIs.Passcode = AprsIsPasscode?.Trim() ?? "";
        t.Uhm.Enabled = UhmEnabled;
        t.Uhm.FrameLevelMatch = UhmFrameLevelMatch;
        t.AutoLog = AutoLog;
        t.AprsAutoAck = AprsAutoAck;
        t.TxDelaySeconds = TxDelaySeconds;
        t.ModemTxDelayMs = Math.Clamp(ModemTxDelayMs, 0, TerminalSettings.MaxModemTimingMs);
        t.ModemTxTailMs = Math.Clamp(ModemTxTailMs, 0, TerminalSettings.MaxModemTimingMs);
        BuiltIn.ApplyTo(t.BuiltIn);
        t.FontSize = FontSize;
        t.PositionPrecision = SelectedPrecision.Value;
        t.PositionSymbol = PositionSymbol ?? "";
        t.PositionComment = PositionComment?.Trim() ?? "";
        t.Normalise();
        _settings.RequestSave();
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
