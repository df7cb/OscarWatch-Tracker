using OscarWatch.Core.Display;
using OscarWatch.Core.Geo;
using OscarWatch.Core.Models;
using OscarWatch.Core.Radio;
using OscarWatch.Diagnostics;

namespace OscarWatch.Tests;

public class DopplerPassLoggerTests
{
    [Fact]
    public void FormatEntry_escapes_commas_in_notes()
    {
        var entry = SampleEntry(notes: "rx=0.000->-0.100;tx=0.000->0.000");

        var line = DopplerPassLogger.FormatEntry(entry);

        Assert.Contains("rx=0.000->-0.100;tx=0.000->0.000", line);
        Assert.StartsWith("2026-06-12 12:00:00.000,offset_change", line);
    }

    [Fact]
    public void BeginPass_writes_header_and_settings_comment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OscarWatchTests", Guid.NewGuid().ToString("N"));
        var logger = new DopplerPassLogger(tempDir);
        var settings = new RigSettings
        {
            DopplerPassLogEnabled = true,
            DopplerThresholdLinearHz = 50,
            DopplerCatLeadEnabled = true,
            DopplerCatLeadGainPercent = 70,
            DopplerCatLeadMs = 40,
            DopplerAdaptiveThresholdEnabled = true
        };
        var context = SampleContext();

        logger.BeginPass(settings, context, new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(logger.ActiveLogPath);
        var path = logger.ActiveLogPath!;
        logger.EndPass(DateTime.UtcNow, "test");
        var text = File.ReadAllText(path);
        Assert.Contains("Utc,Event,NoradId", text);
        Assert.Contains("DialTracking", text);
        Assert.Contains("MainDialHz", text);
        Assert.Contains("SkipReason", text);
        Assert.Contains("# pass_start", text);
        Assert.Contains("lead_gain=70", text);
        Assert.Contains("adaptive=True", text);

        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }

    [Fact]
    public void PruneOlderThanRetention_deletes_csvs_older_than_14_days()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OscarWatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
            var keepPath = Path.Combine(tempDir, "keep-doppler.csv");
            var dropPath = Path.Combine(tempDir, "old-doppler.csv");
            var otherPath = Path.Combine(tempDir, "notes.txt");
            File.WriteAllText(keepPath, "keep");
            File.WriteAllText(dropPath, "drop");
            File.WriteAllText(otherPath, "ignore");
            File.SetLastWriteTimeUtc(keepPath, now.AddDays(-13));
            File.SetLastWriteTimeUtc(dropPath, now.AddDays(-15));
            File.SetLastWriteTimeUtc(otherPath, now.AddDays(-30));

            var deleted = DopplerPassLogFileNameFormat.PruneOlderThanRetention(tempDir, now);

            Assert.Equal(1, deleted);
            Assert.True(File.Exists(keepPath));
            Assert.False(File.Exists(dropPath));
            Assert.True(File.Exists(otherPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void PruneOlderThanRetention_protects_active_path()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "OscarWatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
            var active = Path.Combine(tempDir, "active-doppler.csv");
            File.WriteAllText(active, "active");
            File.SetLastWriteTimeUtc(active, now.AddDays(-20));

            var deleted = DopplerPassLogFileNameFormat.PruneOlderThanRetention(
                tempDir, now, protectPath: active);

            Assert.Equal(0, deleted);
            Assert.True(File.Exists(active));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Capture_populates_lead_and_threshold_fields()
    {
        var settings = new RigSettings
        {
            DopplerCatLeadEnabled = true,
            DopplerCatLeadGainPercent = 70,
            DopplerThresholdLinearHz = 50,
            DopplerAdaptiveThresholdEnabled = true
        };
        var context = SampleContext();
        var corrected = new CorrectedFrequencies(145_950, 435_667, 145_950, 435_667, 0, false);

        var entry = DopplerDiagnostics.Capture(
            propagator: null,
            settings,
            new GroundStation(),
            context,
            DateTime.UtcNow,
            baseThresholdHz: 50,
            effectiveThresholdHz: 25,
            corrected,
            lastRigRxHz: 435_666_900,
            lastRigTxHz: 145_950_100,
            passbandDlKHz: 0,
            passbandUlKHz: 0,
            eventName: "snapshot",
            belowThreshold: true);

        Assert.Equal("snapshot", entry.Event);
        Assert.Equal(50, entry.BaseThresholdHz);
        Assert.Equal(25, entry.EffectiveThresholdHz);
        Assert.True(entry.BelowThreshold);
        Assert.Equal(70, entry.LeadGainPercent);
    }

    private static RigTrackingContext SampleContext() =>
        new()
        {
            TrackState = new SatelliteTrackState
            {
                Name = "FO-29",
                NoradId = "24278",
                Subpoint = new GeoCoordinate(58, -4, 600),
                LookAngles = new LookAngles(180, 30, 800, 1.0)
            },
            Mode = new SatelliteTransponderMode
            {
                Type = "USB",
                DownlinkKHz = 435_800,
                UplinkKHz = 145_920,
                DownlinkMode = "USB",
                UplinkMode = "USB",
                Doppler = "NOR"
            },
            Corrected = new CorrectedFrequencies(145_920, 435_800, 145_920, 435_800, 0, false)
        };

    private static DopplerPassLogEntry SampleEntry(string? notes = null) =>
        new(
            Utc: new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc),
            Event: "offset_change",
            NoradId: "99999",
            SatelliteName: "RS-44",
            ElevationDeg: 45,
            AzimuthDeg: 180,
            RangeRateKmPerSec: 1.2,
            SlopeKmPerSec2: 0.012,
            SlewHzPerSec: 17.4,
            BaseThresholdHz: 50,
            EffectiveThresholdHz: 38,
            LeadEnabled: true,
            LeadBlend: 0.55,
            LeadGainPercent: 70,
            LeadMsRx: 40,
            LeadMsTx: 40,
            LeadRxRangeRate: 1.25,
            LeadTxRangeRate: 1.25,
            SatRxKHz: 435_667,
            SatTxKHz: 145_950,
            RadioRxKHz: 435_667.1,
            RadioTxKHz: 145_949.9,
            LastRigRxHz: 435_667_000,
            LastRigTxHz: 145_950_000,
            RxDeltaHz: 100,
            TxDeltaHz: 100,
            RxOffsetKHz: -0.1,
            TxOffsetKHz: 0,
            PassbandDlKHz: 0,
            PassbandUlKHz: 0,
            WroteRx: false,
            WroteTx: false,
            BelowThreshold: true,
            Interactive: true,
            DialTracking: DopplerDialTrackingMode.HandsOff,
            MainDialHz: 435_667_050,
            DialVsCatHz: 50,
            VfoStable: true,
            RigTracking: true,
            CatPaused: false,
            SkipReason: null,
            Notes: notes);

    [Fact]
    public void FormatEntry_ProducesCorrectFieldCount()
    {
        // Verify both regular and special character entries produce exactly 42 fields
        var regularEntry = SampleEntry();
        var regularResult = DopplerPassLogger.FormatEntry(regularEntry);
        var regularFieldCount = CountCsvFields(regularResult);
        
        var specialEntry = SampleEntry(notes: "test,with\"quotes\nand\rbreaks");
        var specialResult = DopplerPassLogger.FormatEntry(specialEntry);
        var specialFieldCount = CountCsvFields(specialResult);
        
        Assert.Equal(42, regularFieldCount);
        Assert.Equal(42, specialFieldCount);
    }

    [Fact]
    public void CsvBufferOptimization_FormatEntry_HandlesSpecialCharacters()
    {
        // Verifies StringBuilder optimization correctly handles CSV escaping for special characters
        var entry = SampleEntry(notes: "test,with\"quotes\nand\rbreaks");
        
        var result = DopplerPassLogger.FormatEntry(entry);
        
        // Should properly escape the notes field (last field)
        Assert.EndsWith("\"test,with\"\"quotes\nand\rbreaks\"", result);
        
        // Should still have correct number of fields despite special characters
        // Use proper CSV field counting that handles quoted fields with commas
        var fieldCount = CountCsvFields(result);
        Assert.Equal(42, fieldCount);
    }

    [Fact]
    public void CsvBufferOptimization_FormatEntry_HandlesEmptyAndNullValues()
    {
        // Verifies StringBuilder optimization correctly handles empty/null string values
        var entry = new DopplerPassLogEntry(
            Utc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Event: "",
            NoradId: "",
            SatelliteName: "",
            ElevationDeg: double.NaN,
            AzimuthDeg: double.PositiveInfinity,
            RangeRateKmPerSec: double.NegativeInfinity,
            SlopeKmPerSec2: 0,
            SlewHzPerSec: 0,
            BaseThresholdHz: 0,
            EffectiveThresholdHz: 0,
            LeadEnabled: false,
            LeadBlend: 0,
            LeadGainPercent: 0,
            LeadMsRx: 0,
            LeadMsTx: 0,
            LeadRxRangeRate: 0,
            LeadTxRangeRate: 0,
            SatRxKHz: 0,
            SatTxKHz: 0,
            RadioRxKHz: 0,
            RadioTxKHz: 0,
            LastRigRxHz: 0,
            LastRigTxHz: 0,
            RxDeltaHz: 0,
            TxDeltaHz: 0,
            RxOffsetKHz: 0,
            TxOffsetKHz: 0,
            PassbandDlKHz: 0,
            PassbandUlKHz: 0,
            WroteRx: false,
            WroteTx: false,
            BelowThreshold: false,
            Interactive: false,
            DialTracking: DopplerDialTrackingMode.HandsOff,
            MainDialHz: 0,
            DialVsCatHz: 0,
            VfoStable: false,
            RigTracking: false,
            CatPaused: false,
            SkipReason: null,
            Notes: null);
        
        var result = DopplerPassLogger.FormatEntry(entry);
        
        // Should handle empty values gracefully (NaN/Infinity become empty strings)
        Assert.Contains(",,,,", result); // Empty string fields
        Assert.Contains(",0,", result);  // Zero numeric fields
        Assert.Contains(",0", result);   // Zero boolean fields
    }

    [Fact]
    public void CsvBufferOptimization_FormatEntry_HandlesBooleanValues()
    {
        // Verifies StringBuilder optimization correctly formats boolean values as "1"/"0"
        var entryWithTrueValues = SampleEntry();
        var entryWithFalseValues = new DopplerPassLogEntry(
            Utc: DateTime.UtcNow,
            Event: "test",
            NoradId: "12345",
            SatelliteName: "TEST",
            ElevationDeg: 0, AzimuthDeg: 0, RangeRateKmPerSec: 0, SlopeKmPerSec2: 0, SlewHzPerSec: 0,
            BaseThresholdHz: 0, EffectiveThresholdHz: 0,
            LeadEnabled: false, // Should be "0"
            LeadBlend: 0, LeadGainPercent: 0, LeadMsRx: 0, LeadMsTx: 0,
            LeadRxRangeRate: 0, LeadTxRangeRate: 0, SatRxKHz: 0, SatTxKHz: 0,
            RadioRxKHz: 0, RadioTxKHz: 0, LastRigRxHz: 0, LastRigTxHz: 0,
            RxDeltaHz: 0, TxDeltaHz: 0, RxOffsetKHz: 0, TxOffsetKHz: 0,
            PassbandDlKHz: 0, PassbandUlKHz: 0,
            WroteRx: false, WroteTx: false, BelowThreshold: false, Interactive: false, // All should be "0"
            DialTracking: DopplerDialTrackingMode.HandsOff,
            MainDialHz: 0, DialVsCatHz: 0,
            VfoStable: false, RigTracking: false, CatPaused: false, // All should be "0"
            SkipReason: null, Notes: null);
        
        var trueResult = DopplerPassLogger.FormatEntry(entryWithTrueValues);
        var falseResult = DopplerPassLogger.FormatEntry(entryWithFalseValues);
        
        // True values should be "1"
        Assert.Contains(",1,", trueResult); // LeadEnabled = true
        
        // False values should be "0"
        Assert.Contains(",0,", falseResult); // LeadEnabled = false
        Assert.EndsWith("hands_off,0,0,0,0,0,,", falseResult); // DialTracking, MainDialHz, DialVsCatHz, VfoStable, RigTracking, CatPaused, SkipReason (empty), Notes (empty)
    }

    [Fact]
    public void CsvBufferOptimization_FormatEntry_HandlesNumericFormatting()
    {
        // Verifies StringBuilder optimization correctly formats different numeric types
        var entry = new DopplerPassLogEntry(
            Utc: new DateTime(2026, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc),
            Event: "test", NoradId: "12345", SatelliteName: "TEST",
            ElevationDeg: 12.123456789,
            AzimuthDeg: 359.987654321,
            RangeRateKmPerSec: -7.654321,
            SlopeKmPerSec2: 0.000001,
            SlewHzPerSec: 1234.56789,
            BaseThresholdHz: 999999,
            EffectiveThresholdHz: 123456,
            LeadEnabled: false, LeadBlend: 0.123456789, LeadGainPercent: 85,
            LeadMsRx: 42.5, LeadMsTx: 42.5, LeadRxRangeRate: 1.234567, LeadTxRangeRate: 1.234567,
            SatRxKHz: 435123.456789, SatTxKHz: 145987.654321,
            RadioRxKHz: 435123.456789, RadioTxKHz: 145987.654321,
            LastRigRxHz: 435123456, LastRigTxHz: 145987654,
            RxDeltaHz: -100, TxDeltaHz: 200,
            RxOffsetKHz: -0.123456, TxOffsetKHz: 0.987654,
            PassbandDlKHz: 2.4, PassbandUlKHz: 2.4,
            WroteRx: false, WroteTx: false, BelowThreshold: false, Interactive: false,
            DialTracking: DopplerDialTrackingMode.HandsOff,
            MainDialHz: 435123500, DialVsCatHz: 44,
            VfoStable: false, RigTracking: false, CatPaused: false,
            SkipReason: null, Notes: null);
        
        var result = DopplerPassLogger.FormatEntry(entry);
        
        // Should format datetime with milliseconds
        Assert.StartsWith("2026-12-31 23:59:59.999,", result);
        
        // Should format doubles with up to 6 decimal places (no trailing zeros)
        Assert.Contains("12.123457,", result); // ElevationDeg (rounded to 6 decimals)
        Assert.Contains("359.987654,", result); // AzimuthDeg
        
        // Should format integers as-is
        Assert.Contains(",999999,", result); // BaseThresholdHz
        Assert.Contains(",435123456,", result); // LastRigRxHz
    }

    [Fact]
    public void CsvBufferOptimization_ConcurrentFormatEntry_DoesNotInterfere()
    {
        // Test that the lock-protected StringBuilder buffer handles concurrent CSV formatting
        // This verifies thread safety during high-frequency logging operations
        const int EntryCount = 100;
        var tasks = new Task<string>[EntryCount];
        
        // Create many concurrent formatting tasks with different data
        for (var i = 0; i < EntryCount; i++)
        {
            var entryIndex = i;
            tasks[i] = Task.Run(() => 
            {
                var entry = new DopplerPassLogEntry(
                    Utc: new DateTime(2026, 1, 1, 0, entryIndex / 60, entryIndex % 60, DateTimeKind.Utc),
                    Event: $"test_{entryIndex}",
                    NoradId: (10000 + entryIndex).ToString(),
                    SatelliteName: $"SAT_{entryIndex}",
                    ElevationDeg: entryIndex * 0.5,
                    AzimuthDeg: entryIndex * 3.6,
                    RangeRateKmPerSec: entryIndex * 0.01,
                    SlopeKmPerSec2: entryIndex * 0.001,
                    SlewHzPerSec: entryIndex * 0.1,
                    BaseThresholdHz: 50 + entryIndex,
                    EffectiveThresholdHz: 40 + entryIndex,
                    LeadEnabled: entryIndex % 2 == 0,
                    LeadBlend: entryIndex * 0.01,
                    LeadGainPercent: 70 + (entryIndex % 30),
                    LeadMsRx: 40 + entryIndex,
                    LeadMsTx: 40 + entryIndex,
                    LeadRxRangeRate: entryIndex * 0.01,
                    LeadTxRangeRate: entryIndex * 0.01,
                    SatRxKHz: 435000 + entryIndex,
                    SatTxKHz: 145000 + entryIndex,
                    RadioRxKHz: 435000 + entryIndex,
                    RadioTxKHz: 145000 + entryIndex,
                    LastRigRxHz: 435000000 + (entryIndex * 1000),
                    LastRigTxHz: 145000000 + (entryIndex * 1000),
                    RxDeltaHz: entryIndex,
                    TxDeltaHz: entryIndex,
                    RxOffsetKHz: entryIndex * 0.001,
                    TxOffsetKHz: entryIndex * 0.001,
                    PassbandDlKHz: entryIndex * 0.1,
                    PassbandUlKHz: entryIndex * 0.1,
                    WroteRx: entryIndex % 3 == 0,
                    WroteTx: entryIndex % 3 == 0,
                    BelowThreshold: entryIndex % 4 == 0,
                    Interactive: entryIndex % 5 == 0,
                    DialTracking: DopplerDialTrackingMode.HandsOff,
                    MainDialHz: 435000000 + (entryIndex * 1000),
                    DialVsCatHz: entryIndex,
                    VfoStable: entryIndex % 2 == 0,
                    RigTracking: entryIndex % 3 == 0,
                    CatPaused: entryIndex % 7 == 0,
                    SkipReason: null,
                    Notes: $"concurrent_test_{entryIndex}");
                
                return DopplerPassLogger.FormatEntry(entry);
            });
        }
        
        // Wait for all tasks to complete
        Task.WaitAll(tasks);
        
        // Verify all CSV lines were formatted correctly with no interference
        for (var i = 0; i < EntryCount; i++)
        {
            var result = tasks[i].Result;
            
            // Check that each result contains the expected unique data for that entry
            Assert.Contains($"test_{i}", result);
            Assert.Contains($"SAT_{i}", result);
            Assert.Contains((10000 + i).ToString(), result);
            Assert.Contains($"concurrent_test_{i}", result);
            
            // Verify structure is intact
            var fieldCount = CountCsvFields(result);
            Assert.Equal(42, fieldCount);
        }
    }
    
    /// <summary>
    /// Counts CSV fields correctly, handling quoted fields that may contain commas.
    /// </summary>
    private static int CountCsvFields(string csvLine)
    {
        if (string.IsNullOrEmpty(csvLine))
            return 0;
            
        int fieldCount = 1; // At least one field if string is not empty
        bool inQuotedField = false;
        
        for (int i = 0; i < csvLine.Length; i++)
        {
            char c = csvLine[i];
            
            if (c == '"')
            {
                // Check for escaped quote (double quote)
                if (i + 1 < csvLine.Length && csvLine[i + 1] == '"')
                {
                    i++; // Skip the escaped quote
                }
                else
                {
                    inQuotedField = !inQuotedField; // Toggle quoted field state
                }
            }
            else if (c == ',' && !inQuotedField)
            {
                fieldCount++; // Only count commas outside quoted fields
            }
        }
        
        return fieldCount;
    }
}
