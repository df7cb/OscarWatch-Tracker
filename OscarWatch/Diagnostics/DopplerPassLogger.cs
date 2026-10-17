using System.Globalization;
using System.Text;
using OscarWatch.Core.Display;
using OscarWatch.Core.Models;
using OscarWatch.Core.Services;
using Serilog;

namespace OscarWatch.Diagnostics;

public sealed class DopplerPassLogger : IDopplerPassLogger
{
    private static readonly ILogger Log = Serilog.Log.ForContext<DopplerPassLogger>();

    // Reusable StringBuilder buffer for CSV entry formatting to avoid string array and Join allocations
    private static readonly StringBuilder _entryBuffer = new(1024);

    private static readonly string[] HeaderColumns =
    [
        "Utc",
        "Event",
        "NoradId",
        "Satellite",
        "ElevDeg",
        "AzDeg",
        "RangeRateKmPerSec",
        "SlopeKmPerSec2",
        "SlewHzPerSec",
        "BaseThresholdHz",
        "EffectiveThresholdHz",
        "LeadEnabled",
        "LeadBlend",
        "LeadGainPct",
        "LeadMsRx",
        "LeadMsTx",
        "LeadRxRangeRate",
        "LeadTxRangeRate",
        "SatRxKHz",
        "SatTxKHz",
        "RadioRxKHz",
        "RadioTxKHz",
        "LastRigRxHz",
        "LastRigTxHz",
        "RxDeltaHz",
        "TxDeltaHz",
        "RxOffsetKHz",
        "TxOffsetKHz",
        "PassbandDlKHz",
        "PassbandUlKHz",
        "WroteRx",
        "WroteTx",
        "BelowThreshold",
        "Interactive",
        "DialTracking",
        "MainDialHz",
        "DialVsCatHz",
        "VfoStable",
        "RigTracking",
        "CatPaused",
        "SkipReason",
        "Notes"
    ];

    private readonly object _gate = new();
    private readonly string _logDirectory;
    private StreamWriter? _writer;
    private string? _activePath;

    public DopplerPassLogger(string? logDirectory = null) =>
        _logDirectory = logDirectory ?? DopplerPassLogFileNameFormat.GetDefaultLogDirectory();

    public string LogDirectory => _logDirectory;

    public string? ActiveLogPath
    {
        get
        {
            lock (_gate)
                return _activePath;
        }
    }

    public void BeginPass(RigSettings settings, RigTrackingContext context, DateTime utc)
    {
        if (!settings.DopplerPassLogEnabled)
            return;

        string path;
        lock (_gate)
        {
            CloseWriterUnlocked();

            try
            {
                var pruned = DopplerPassLogFileNameFormat.PruneOlderThanRetention(LogDirectory);
                if (pruned > 0)
                    Log.Information("Pruned {Count} Doppler pass log(s) older than {Days} days", pruned, DopplerPassLogFileNameFormat.RetainedFileDays);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to prune old Doppler pass logs in {Directory}", LogDirectory);
            }

            path = DopplerPassLogFileNameFormat.ResolveUniquePath(
                LogDirectory,
                context.TrackState.Name,
                utc);
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            _activePath = path;

            _writer.WriteLine(string.Join(',', HeaderColumns));
            _writer.WriteLine($"# pass_start,{Format(utc)},{Escape(context.TrackState.NoradId)},{Escape(context.TrackState.Name)}");
            _writer.WriteLine(
                $"# settings,threshold_linear={settings.DopplerThresholdLinearHz},threshold_fm={settings.DopplerThresholdFmHz},cat_delay_ms={settings.CatDelayMs},lead={settings.DopplerCatLeadEnabled},lead_ms={settings.DopplerCatLeadMs},lead_gain={settings.DopplerCatLeadGainPercent},adaptive={settings.DopplerAdaptiveThresholdEnabled}");
        }

        Log.Information("Doppler pass log started: {Path}", path);
    }

    public void Append(DopplerPassLogEntry entry)
    {
        lock (_gate)
        {
            if (_writer is null)
                return;

            _writer.WriteLine(FormatEntry(entry));
        }
    }

    public void EndPass(DateTime utc, string? reason = null)
    {
        lock (_gate)
        {
            if (_writer is null)
                return;

            _writer.WriteLine($"# pass_end,{Format(utc)},{Escape(reason ?? "")}");
            CloseWriterUnlocked();
        }
    }

    private void CloseWriterUnlocked()
    {
        _writer?.Dispose();
        _writer = null;
        _activePath = null;
    }

    internal static string FormatEntry(DopplerPassLogEntry entry)
    {
        lock (_entryBuffer)
        {
            _entryBuffer.Clear();
            
            // Build CSV line using StringBuilder buffer to eliminate string.Join array allocation
            AppendField(_entryBuffer, Format(entry.Utc));
            AppendField(_entryBuffer, Escape(entry.Event));
            AppendField(_entryBuffer, Escape(entry.NoradId));
            AppendField(_entryBuffer, Escape(entry.SatelliteName));
            AppendField(_entryBuffer, Format(entry.ElevationDeg));
            AppendField(_entryBuffer, Format(entry.AzimuthDeg));
            AppendField(_entryBuffer, Format(entry.RangeRateKmPerSec));
            AppendField(_entryBuffer, Format(entry.SlopeKmPerSec2));
            AppendField(_entryBuffer, Format(entry.SlewHzPerSec));
            AppendField(_entryBuffer, entry.BaseThresholdHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.EffectiveThresholdHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.LeadEnabled ? "1" : "0");
            AppendField(_entryBuffer, Format(entry.LeadBlend));
            AppendField(_entryBuffer, entry.LeadGainPercent.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, Format(entry.LeadMsRx));
            AppendField(_entryBuffer, Format(entry.LeadMsTx));
            AppendField(_entryBuffer, Format(entry.LeadRxRangeRate));
            AppendField(_entryBuffer, Format(entry.LeadTxRangeRate));
            AppendField(_entryBuffer, Format(entry.SatRxKHz));
            AppendField(_entryBuffer, Format(entry.SatTxKHz));
            AppendField(_entryBuffer, Format(entry.RadioRxKHz));
            AppendField(_entryBuffer, Format(entry.RadioTxKHz));
            AppendField(_entryBuffer, entry.LastRigRxHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.LastRigTxHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.RxDeltaHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.TxDeltaHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, Format(entry.RxOffsetKHz));
            AppendField(_entryBuffer, Format(entry.TxOffsetKHz));
            AppendField(_entryBuffer, Format(entry.PassbandDlKHz));
            AppendField(_entryBuffer, Format(entry.PassbandUlKHz));
            AppendField(_entryBuffer, entry.WroteRx ? "1" : "0");
            AppendField(_entryBuffer, entry.WroteTx ? "1" : "0");
            AppendField(_entryBuffer, entry.BelowThreshold ? "1" : "0");
            AppendField(_entryBuffer, entry.Interactive ? "1" : "0");
            AppendField(_entryBuffer, Escape(entry.DialTracking));
            AppendField(_entryBuffer, entry.MainDialHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.DialVsCatHz.ToString(CultureInfo.InvariantCulture));
            AppendField(_entryBuffer, entry.VfoStable ? "1" : "0");
            AppendField(_entryBuffer, entry.RigTracking ? "1" : "0");
            AppendField(_entryBuffer, entry.CatPaused ? "1" : "0");
            AppendField(_entryBuffer, Escape(entry.SkipReason));
            AppendField(_entryBuffer, Escape(entry.Notes));
            
            return _entryBuffer.ToString();
        }
    }

    private static string Format(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string Format(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? ""
            : value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Appends a CSV field to the StringBuilder buffer with comma separator.
    /// Used by FormatEntry optimization to eliminate string array allocation.
    /// </summary>
    private static void AppendField(StringBuilder buffer, string value)
    {
        if (buffer.Length > 0)
            buffer.Append(',');
        buffer.Append(value);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return value;
    }
}
