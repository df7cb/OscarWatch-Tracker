namespace OscarWatch.Core.Radio;

/// <summary>
/// Yaesu FT-847 five-byte CAT encoding (Hamlib-compatible).
/// Requires two-way CAT firmware (serial 8G05xxxx+). Frequency resolution is 10 Hz over CAT.
/// </summary>
public static class YaesuFt847CatCodec
{
    public const int CommandLength = 5;

    public static ReadOnlySpan<byte> CatOn => [0x00, 0x00, 0x00, 0x00, 0x00];
    public static ReadOnlySpan<byte> CatOff => [0x00, 0x00, 0x00, 0x00, 0x80];
    public static ReadOnlySpan<byte> SatelliteModeOn => [0x00, 0x00, 0x00, 0x00, 0x4e];
    public static ReadOnlySpan<byte> SatelliteModeOff => [0x00, 0x00, 0x00, 0x00, 0x8e];

    public static ReadOnlySpan<byte> PollMainFreqMode => [0x00, 0x00, 0x00, 0x00, 0x03];
    public static ReadOnlySpan<byte> PollSatRxFreqMode => [0x00, 0x00, 0x00, 0x00, 0x13];
    public static ReadOnlySpan<byte> PollSatTxFreqMode => [0x00, 0x00, 0x00, 0x00, 0x23];

    private static readonly int[] CtcssTonesHz =
    [
        670, 693, 719, 744, 770, 797, 825, 854, 885, 915,
        948, 974, 1000, 1035, 1072, 1109, 1148, 1188, 1230, 1273,
        1318, 1365, 1413, 1462, 1514, 1567, 1622, 1679, 1738, 1799,
        1862, 1928, 2035, 2107, 2181, 2257, 2336, 2418, 2503
    ];

    private static readonly byte[] CtcssCatCodes =
    [
        0x3F, 0x39, 0x1F, 0x3E, 0x0F, 0x3D, 0x1E, 0x3C, 0x0E, 0x3B,
        0x1D, 0x3A, 0x0D, 0x1C, 0x0C, 0x1B, 0x0B, 0x1A, 0x0A, 0x19,
        0x09, 0x18, 0x08, 0x17, 0x07, 0x16, 0x06, 0x15, 0x05, 0x14,
        0x04, 0x13, 0x03, 0x12, 0x02, 0x11, 0x01, 0x10, 0x00
    ];

    public static void EncodeFrequency10Hz(long hz, Span<byte> command)
    {
        if (command.Length != CommandLength)
            throw new ArgumentException($"Command span must be {CommandLength} bytes.", nameof(command));

        var units = hz / 10;
        for (var i = 3; i >= 0; i--)
        {
            var pair = (int)(units % 100);
            units /= 100;
            command[i] = (byte)((pair / 10 << 4) | (pair % 10));
        }
    }

    public static long DecodeFrequency10Hz(ReadOnlySpan<byte> response)
    {
        if (response.Length < 4)
            return 0;

        long units = 0;
        for (var i = 0; i < 4; i++)
        {
            var b = response[i];
            units = units * 100 + (b >> 4) * 10 + (b & 0x0F);
        }

        return units * 10;
    }

    public static string DecodeMode(byte modeByte) => modeByte switch
    {
        0x00 => "LSB",
        0x01 => "USB",
        0x02 => "CW",
        0x03 => "CWR",
        0x04 => "AM",
        0x08 => "FM",
        0x82 => "CW",
        0x83 => "CWR",
        0x84 => "AM",
        0x88 => "FM",
        _ => "USB"
    };

    public static byte[] BuildSetFrequencyCommand(long hz, YaesuFt847VfoTarget target, bool satelliteMode)
    {
        var cmd = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01 };
        EncodeFrequency10Hz(hz, cmd);
        ApplyVfoTarget(cmd, target, satelliteMode);
        return cmd;
    }

    /// <summary>
    /// Repeater direction (opcode 09). D1 is 09 minus, 49 plus, or 89 simplex.
    /// </summary>
    public static byte[] BuildRepeaterShiftCommand(int direction) =>
        direction switch
        {
            < 0 => [0x09, 0x00, 0x00, 0x00, 0x09],
            > 0 => [0x49, 0x00, 0x00, 0x00, 0x09],
            _ => [0x89, 0x00, 0x00, 0x00, 0x09]
        };

    public static byte[] BuildSetModeCommand(string mode, YaesuFt847VfoTarget target, bool satelliteMode, bool narrow = false)
    {
        var (modeByte, baseOpcode) = ResolveMode(mode, target, narrow);
        var cmd = new byte[] { modeByte, 0x00, 0x00, 0x00, baseOpcode };
        ApplyVfoTarget(cmd, target, satelliteMode);
        return cmd;
    }

    public static byte[] BuildCtcssFrequencyCommand(double toneHz, YaesuFt847VfoTarget target, bool satelliteMode)
    {
        if (!TryGetCtcssCatCode(toneHz, out var catCode))
            throw new ArgumentOutOfRangeException(nameof(toneHz), "CTCSS tone not supported by FT-847.");

        var baseOpcode = target switch
        {
            YaesuFt847VfoTarget.SatRx => (byte)0x1b,
            YaesuFt847VfoTarget.SatTx => (byte)0x2b,
            _ => (byte)0x0b
        };
        var cmd = new byte[] { catCode, 0x00, 0x00, 0x00, baseOpcode };
        ApplyVfoTarget(cmd, target, satelliteMode);
        return cmd;
    }

    public static byte[] BuildCtcssOnCommand(bool encoderOnly, YaesuFt847VfoTarget target, bool satelliteMode)
    {
        var p1 = encoderOnly ? (byte)0x4a : (byte)0x2a;
        var baseOpcode = target switch
        {
            YaesuFt847VfoTarget.SatRx => (byte)0x1a,
            YaesuFt847VfoTarget.SatTx => (byte)0x2a,
            _ => (byte)0x0a
        };
        var cmd = new byte[] { p1, 0x00, 0x00, 0x00, baseOpcode };
        ApplyVfoTarget(cmd, target, satelliteMode);
        return cmd;
    }

    public static byte[] BuildCtcssOffCommand(YaesuFt847VfoTarget target, bool satelliteMode)
    {
        var baseOpcode = target switch
        {
            YaesuFt847VfoTarget.SatRx => (byte)0x1a,
            YaesuFt847VfoTarget.SatTx => (byte)0x2a,
            _ => (byte)0x0a
        };
        var cmd = new byte[] { 0x8a, 0x00, 0x00, 0x00, baseOpcode };
        ApplyVfoTarget(cmd, target, satelliteMode);
        return cmd;
    }

    public static byte[] BuildPollCommand(YaesuFt847VfoTarget target, bool satelliteMode)
    {
        var cmd = target switch
        {
            YaesuFt847VfoTarget.SatRx => PollSatRxFreqMode.ToArray(),
            YaesuFt847VfoTarget.SatTx => PollSatTxFreqMode.ToArray(),
            _ => PollMainFreqMode.ToArray()
        };
        if (satelliteMode && target is YaesuFt847VfoTarget.Main)
            ApplyVfoTarget(cmd, YaesuFt847VfoTarget.SatRx, true);
        return cmd;
    }

    public static void ApplyVfoTarget(Span<byte> command, YaesuFt847VfoTarget target, bool satelliteMode)
    {
        if (!satelliteMode || target == YaesuFt847VfoTarget.Main)
            return;

        command[4] = (byte)((command[4] & 0x0F) | (byte)target);
    }

    public static YaesuFt847VfoTarget MapOscarWatchVfo(bool satelliteMode, bool isUplink)
    {
        if (!satelliteMode)
            return isUplink ? YaesuFt847VfoTarget.SatTx : YaesuFt847VfoTarget.Main;

        return isUplink ? YaesuFt847VfoTarget.SatTx : YaesuFt847VfoTarget.SatRx;
    }

    public static bool TryGetCtcssCatCode(double toneHz, out byte catCode)
    {
        // Hamlib tone list uses 0.1 Hz units (670 = 67.0 Hz).
        var target = (int)Math.Round(toneHz * 10);
        for (var i = 0; i < CtcssTonesHz.Length; i++)
        {
            if (CtcssTonesHz[i] == target)
            {
                catCode = CtcssCatCodes[i];
                return true;
            }
        }

        catCode = 0;
        return false;
    }

    private static (byte ModeByte, byte BaseOpcode) ResolveMode(
        string mode,
        YaesuFt847VfoTarget target,
        bool narrow)
    {
        var upper = mode.Trim().ToUpperInvariant();
        var useNarrow = upper switch
        {
            "FM" => false,
            "FMN" => true,
            _ => narrow
        };

        var modeByte = upper switch
        {
            "LSB" or "DATA-LSB" => (byte)0x00,
            "USB" or "DATA-USB" => (byte)0x01,
            "CW" => useNarrow ? (byte)0x82 : (byte)0x02,
            "CWR" => useNarrow ? (byte)0x83 : (byte)0x03,
            "AM" => useNarrow ? (byte)0x84 : (byte)0x04,
            "FM" or "FMN" or "DATA-FM" or "FM-DATA" => useNarrow ? (byte)0x88 : (byte)0x08,
            _ => (byte)0x01
        };

        var baseOpcode = target switch
        {
            YaesuFt847VfoTarget.SatRx => (byte)0x17,
            YaesuFt847VfoTarget.SatTx => (byte)0x27,
            _ => (byte)0x07
        };

        return (modeByte, baseOpcode);
    }
}
