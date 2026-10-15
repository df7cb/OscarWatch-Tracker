namespace OscarWatch.Localization;

/// <summary>
/// Turns a PTT failure into operator text. Known port messages are translated. Anything else is left as written.
/// </summary>
public static class PttFailureLocalizer
{
    public static string Localize(string message, ILocalizationService l)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var conflict = ComPortConflictLocalizer.Localize(message, l);
        if (!string.Equals(conflict, message, StringComparison.Ordinal))
            return conflict;

        if (TryReadPort("Serial port in use (", message, out var busy))
            return l.Get("Ptt.PortBusy", busy);

        if (TryReadPort("Serial port not found (", message, out var missing))
            return l.Get("Ptt.PortNotFound", missing);

        return message;
    }

    private static bool TryReadPort(string prefix, string message, out string port)
    {
        port = "";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var end = message.IndexOf(')', prefix.Length);
        if (end <= prefix.Length)
            return false;

        port = message[prefix.Length..end];
        return port.Length > 0;
    }
}
