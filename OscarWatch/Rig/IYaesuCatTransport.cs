namespace OscarWatch.Rig;

internal interface IYaesuCatTransport : IDisposable
{
    bool IsOpen { get; }
    void Open();
    bool SendFrame(ReadOnlySpan<byte> frame, int postDelayMs = 50);
    byte[]? QueryFrame(ReadOnlySpan<byte> pollFrame, int postDelayMs = 50);

    /// <summary>Assert or release RTS or DTR on the CAT port. Used for hardware PTT.</summary>
    void SetHandshakeLine(bool useRts, bool assert);
}
