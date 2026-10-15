namespace OscarWatch.Localization;

public static class ComPortConflictLocalizer
{
    public static string Localize(string message, ILocalizationService l)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        const string dualPrefix = "Downlink and uplink radios both use ";
        if (message.StartsWith(dualPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports for each radio.", StringComparison.Ordinal))
        {
            var port = message[dualPrefix.Length..^". Use different COM ports for each radio.".Length];
            return l.Get("ComPort.DualRadioSamePort", port);
        }

        const string rotatorAzElPrefix = "Rotator azimuth and elevation both use ";
        if (message.StartsWith(rotatorAzElPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports for each axis.", StringComparison.Ordinal))
        {
            var port = message[rotatorAzElPrefix.Length..^". Use different COM ports for each axis.".Length];
            return l.Get("ComPort.RotatorAzElSamePort", port);
        }

        const string rotatorDownlinkPrefix = "Rotator and downlink radio both use ";
        if (message.StartsWith(rotatorDownlinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[rotatorDownlinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.RotatorAndDownlink", port);
        }

        const string rotatorUplinkPrefix = "Rotator and uplink radio both use ";
        if (message.StartsWith(rotatorUplinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[rotatorUplinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.RotatorAndUplink", port);
        }

        const string rotatorRadioPrefix = "Rotator and radio both use ";
        if (message.StartsWith(rotatorRadioPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[rotatorRadioPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.RotatorAndRadio", port);
        }

        const string gpsRotatorPrefix = "GPS and rotator both use ";
        if (message.StartsWith(gpsRotatorPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[gpsRotatorPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.GpsAndRotator", port);
        }

        const string gpsRadioPrefix = "GPS and radio both use ";
        if (message.StartsWith(gpsRadioPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[gpsRadioPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.GpsAndRadio", port);
        }

        const string gpsDownlinkPrefix = "GPS and downlink radio both use ";
        if (message.StartsWith(gpsDownlinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[gpsDownlinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.GpsAndDownlink", port);
        }

        const string gpsUplinkPrefix = "GPS and uplink radio both use ";
        if (message.StartsWith(gpsUplinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[gpsUplinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.GpsAndUplink", port);
        }

        const string terminalPrefix = "Terminal separate PTT and ";
        const string deviceSuffix = " both use ";
        const string disableSuffix = ". Use different COM ports or disable one device.";
        if (message.StartsWith(terminalPrefix, StringComparison.Ordinal)
            && message.EndsWith(disableSuffix, StringComparison.Ordinal))
        {
            var rest = message[terminalPrefix.Length..^disableSuffix.Length];
            var split = rest.IndexOf(deviceSuffix, StringComparison.Ordinal);
            if (split > 0)
            {
                var key = rest[..split] switch
                {
                    "radio" => "ComPort.TerminalPttAndRadio",
                    "downlink radio" => "ComPort.TerminalPttAndDownlink",
                    "uplink radio" => "ComPort.TerminalPttAndUplink",
                    "rotator" => "ComPort.TerminalPttAndRotator",
                    "GPS" => "ComPort.TerminalPttAndGps",
                    _ => null
                };
                if (key is not null)
                    return l.Get(key, rest[(split + deviceSuffix.Length)..]);
            }
        }

        const string sharedSuffix = ". Use different COM ports, or key one of them another way.";
        if (message.EndsWith(sharedSuffix, StringComparison.Ordinal))
        {
            const string ft4Terminal = "FT4 separate PTT and Terminal separate PTT both use ";
            const string terminalFt4 = "Terminal separate PTT and FT4 separate PTT both use ";
            if (message.StartsWith(ft4Terminal, StringComparison.Ordinal))
                return l.Get("ComPort.Ft4PttAndTerminalPtt", message[ft4Terminal.Length..^sharedSuffix.Length]);
            if (message.StartsWith(terminalFt4, StringComparison.Ordinal))
                return l.Get("ComPort.TerminalPttAndFt4Ptt", message[terminalFt4.Length..^sharedSuffix.Length]);
        }

        const string ft4RadioPrefix = "FT4 separate PTT and radio both use ";
        if (message.StartsWith(ft4RadioPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[ft4RadioPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.Ft4PttAndRadio", port);
        }

        const string ft4DownlinkPrefix = "FT4 separate PTT and downlink radio both use ";
        if (message.StartsWith(ft4DownlinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[ft4DownlinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.Ft4PttAndDownlink", port);
        }

        const string ft4UplinkPrefix = "FT4 separate PTT and uplink radio both use ";
        if (message.StartsWith(ft4UplinkPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[ft4UplinkPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.Ft4PttAndUplink", port);
        }

        const string ft4RotatorPrefix = "FT4 separate PTT and rotator both use ";
        if (message.StartsWith(ft4RotatorPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[ft4RotatorPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.Ft4PttAndRotator", port);
        }

        const string ft4GpsPrefix = "FT4 separate PTT and GPS both use ";
        if (message.StartsWith(ft4GpsPrefix, StringComparison.Ordinal)
            && message.EndsWith(". Use different COM ports or disable one device.", StringComparison.Ordinal))
        {
            var port = message[ft4GpsPrefix.Length..^". Use different COM ports or disable one device.".Length];
            return l.Get("ComPort.Ft4PttAndGps", port);
        }

        return message;
    }
}
