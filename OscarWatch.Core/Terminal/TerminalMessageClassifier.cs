namespace OscarWatch.Core.Terminal;

/// <summary>Message log highlight class. The UI pairs each with a colour and a text tag.</summary>
public enum TerminalMessageKind
{
    Normal,
    ToMe,
    Cq,
    MyTx,
    MyDigipeated,
    Telemetry,
    Beacon
}

public static class TerminalMessageClassifier
{
    /// <param name="locallySent">True for the row added when OscarWatch sends a frame (not a received copy).</param>
    public static TerminalMessageKind Classify(TerminalDecodedMessage message, string myCall, bool locallySent)
    {
        if (locallySent)
            return TerminalMessageKind.MyTx;
        if (TerminalText.SameStation(message.From, myCall))
            return TerminalMessageKind.MyDigipeated;
        if (TerminalText.SameStation(message.To, myCall))
            return TerminalMessageKind.ToMe;
        if (message.IsTelemetry)
            return TerminalMessageKind.Telemetry;
        if (message.IsCq)
            return TerminalMessageKind.Cq;
        if (message.IsBeacon)
            return TerminalMessageKind.Beacon;
        return TerminalMessageKind.Normal;
    }
}
