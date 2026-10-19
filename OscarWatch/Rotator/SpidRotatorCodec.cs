namespace OscarWatch.Rotator;

/// <summary>
/// SPID Rot1Prog / Rot2Prog binary protocol (13-byte commands, 5- or 12-byte responses).
/// See http://ryeng.name/blog/3
/// </summary>
public static class SpidRotatorCodec
{
    public const byte StartByte = 0x57;
    public const byte EndByte = 0x20;
    public const int CommandLength = 13;
    public const int Rot1ResponseLength = 5;
    public const int Rot2ResponseLength = 12;

    public const byte CommandStop = 0x0F;
    public const byte CommandStatus = 0x1F;
    public const byte CommandSet = 0x2F;

    public static void BuildStopCommand(Span<byte> buffer)
    {
        BuildSimpleCommand(buffer, CommandStop);
    }

    public static void BuildStatusCommand(Span<byte> buffer)
    {
        BuildSimpleCommand(buffer, CommandStatus);
    }

    public static void BuildSetCommand(
        Span<byte> buffer,
        double azimuthDeg,
        double elevationDeg,
        int pulsesPerDegree,
        bool rot1Mode)
    {
        if (buffer.Length < CommandLength)
            throw new ArgumentException($"Buffer must be at least {CommandLength} bytes.", nameof(buffer));

        buffer.Clear();
        buffer[0] = StartByte;
        buffer[11] = CommandSet;
        buffer[12] = EndByte;

        if (rot1Mode)
        {
            var h = 360 + (int)Math.Round(azimuthDeg);
            WriteRot1AzimuthDigits(buffer, h);
            buffer[4] = (byte)'0';
        }
        else
        {
            var h = (int)Math.Round(pulsesPerDegree * (360.0 + azimuthDeg));
            var v = (int)Math.Round(pulsesPerDegree * (360.0 + elevationDeg));
            WriteRot2Digits(buffer, 1, h);
            WriteRot2Digits(buffer, 6, v);
            buffer[5] = (byte)pulsesPerDegree;
            buffer[10] = (byte)pulsesPerDegree;
        }
    }

    public static bool TryParseRot1Status(ReadOnlySpan<byte> response, out double azimuthDeg)
    {
        azimuthDeg = 0;
        if (response.Length < Rot1ResponseLength)
            return false;
        if (response[0] != StartByte || response[4] != EndByte)
            return false;
        if (!AreDigits(response.Slice(1, 3)))
            return false;

        azimuthDeg = response[1] * 100 + response[2] * 10 + response[3] - 360.0;
        return true;
    }

    public static bool TryParseRot2Status(
        ReadOnlySpan<byte> response,
        out double azimuthDeg,
        out double elevationDeg,
        out int pulsesPerDegree)
    {
        azimuthDeg = 0;
        elevationDeg = 0;
        pulsesPerDegree = 0;
        if (response.Length < Rot2ResponseLength)
            return false;
        if (response[0] != StartByte || response[11] != EndByte)
            return false;
        if (!AreDigits(response.Slice(1, 4)) || !AreDigits(response.Slice(6, 4)))
            return false;

        azimuthDeg = response[1] * 100 + response[2] * 10 + response[3] + response[4] / 10.0 - 360.0;
        elevationDeg = response[6] * 100 + response[7] * 10 + response[8] + response[9] / 10.0 - 360.0;
        pulsesPerDegree = response[5];
        return pulsesPerDegree > 0;
    }

    private static bool AreDigits(ReadOnlySpan<byte> values)
    {
        foreach (var value in values)
        {
            if (value > 9)
                return false;
        }

        return true;
    }

    private static void BuildSimpleCommand(Span<byte> buffer, byte command)
    {
        if (buffer.Length < CommandLength)
            throw new ArgumentException($"Buffer must be at least {CommandLength} bytes.", nameof(buffer));

        buffer.Clear();
        buffer[0] = StartByte;
        buffer[11] = command;
        buffer[12] = EndByte;
    }

    private static void WriteRot1AzimuthDigits(Span<byte> buffer, int value)
    {
        var clamped = Math.Clamp(value, 0, 999);
        buffer[1] = (byte)('0' + clamped / 100);
        buffer[2] = (byte)('0' + clamped / 10 % 10);
        buffer[3] = (byte)('0' + clamped % 10);
    }

    private static void WriteRot2Digits(Span<byte> buffer, int startIndex, int value)
    {
        var clamped = Math.Clamp(value, 0, 9999);
        buffer[startIndex] = (byte)('0' + clamped / 1000);
        buffer[startIndex + 1] = (byte)('0' + clamped / 100 % 10);
        buffer[startIndex + 2] = (byte)('0' + clamped / 10 % 10);
        buffer[startIndex + 3] = (byte)('0' + clamped % 10);
    }
}
