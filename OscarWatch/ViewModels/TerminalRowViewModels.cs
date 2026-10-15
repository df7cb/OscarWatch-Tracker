using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Uhm;
using OscarWatch.Terminal;

namespace OscarWatch.ViewModels;

/// <summary>
/// One message log row: a coloured tag pill and accent bar plus a faint row tint, so the kind is clear
/// without colour and long runs of one kind do not flood the log.
/// </summary>
public sealed class TerminalMessageRowViewModel
{
    private const byte TintAlpha = 0x2E;

    public TerminalMessageRowViewModel(TerminalLogEntry entry, string tag, Color? colour, string? textPrefix = null)
    {
        Entry = entry;
        Tag = tag;
        Text = string.IsNullOrEmpty(textPrefix) ? entry.Text : $"[{textPrefix}] {entry.Text}".TrimEnd();
        if (colour is { } c)
        {
            Accent = new SolidColorBrush(c);
            Background = new SolidColorBrush(Color.FromArgb(TintAlpha, c.R, c.G, c.B));
        }
        else
        {
            Accent = Brushes.Transparent;
            Background = Brushes.Transparent;
        }
    }

    public TerminalLogEntry Entry { get; }
    public TerminalMessageKind Kind => Entry.Kind;
    public string TimeText => Entry.Utc.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    public string Tag { get; }
    public string From => Entry.From;
    public string To => Entry.To;
    public string Text { get; }
    public string DelayText => Entry.Delay is { } d ? $"{d.TotalSeconds:0.0} s" : "";
    public string QualityText => Entry.Quality is { } q ? $"{q}%" : "";
    public string Monitor => Entry.Monitor;
    public IBrush Background { get; }
    public IBrush Accent { get; }
    public bool IsHighlighted => Accent is ISolidColorBrush { Color.A: > 0 };
    public bool IsOwn => Kind is TerminalMessageKind.MyTx or TerminalMessageKind.MyDigipeated;
    public bool InvolvesMe => IsOwn || Kind == TerminalMessageKind.ToMe;

    public string AccessibleText => Entry.Quality is { } q
        ? $"{TimeText} {Tag} {From} {To} {q}% {Text}"
        : $"{TimeText} {Tag} {From} {To} {Text}";
}

/// <summary>Heard station or QSO partner row; updated in place on every refresh.</summary>
public sealed partial class TerminalStationRowViewModel : ObservableObject
{
    public TerminalStationRowViewModel(string call) => Call = call;

    public string Call { get; }

    [ObservableProperty] private string _grid = "";
    [ObservableProperty] private string _sinceText = "";
    [ObservableProperty] private string _cqTxText = "";
    [ObservableProperty] private string _elevationText = "";
    [ObservableProperty] private string _distanceText = "";
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private string _uhmText = "";
    [ObservableProperty] private string _markerText = "";
    [ObservableProperty] private string _loggedText = "";
    [ObservableProperty] private UhmMatchState _uhm;

    public string AccessibleText => $"{Call} {Grid} {DistanceText} {StateText} {UhmText} {MarkerText}";
}

public sealed partial class TerminalMacroButtonViewModel : ObservableObject
{
    public TerminalMacroButtonViewModel(int index, IRelayCommand<string?> command)
    {
        Index = index;
        KeyLabel = $"F{index + 1}";
        Command = command;
    }

    public int Index { get; }
    public string KeyLabel { get; }
    public IRelayCommand<string?> Command { get; }
    public string Parameter => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Caption))]
    [NotifyPropertyChangedFor(nameof(HasMacro))]
    private string _label = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMacro))]
    private string _text = "";

    public bool HasMacro => !string.IsNullOrWhiteSpace(Text);
    public string Caption => string.IsNullOrWhiteSpace(Label) ? KeyLabel : $"{KeyLabel} {Label}";
}
