namespace OscarWatch.Core.Terminal;

/// <summary>How Terminal text is put on the air for a satellite.</summary>
public enum TerminalMessageFormat
{
    /// <summary>APRS status (<c>&gt;</c>) for CQ and broadcasts, APRS messages for directed text.</summary>
    Aprs,

    /// <summary>GreenCube Terminal style: AX.25 destination is the To call, info field is the text.</summary>
    PlainText
}
