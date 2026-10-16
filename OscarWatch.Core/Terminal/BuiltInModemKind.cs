namespace OscarWatch.Core.Terminal;

/// <summary>Manual override for the built-in soundcard modem data rate.</summary>
public enum BuiltInModemRateMode
{
    /// <summary>1200 AFSK unless the satellite profile hint names 9600 G3RUH.</summary>
    Auto,

    Afsk1200,
    G3ruh9600
}

public enum BuiltInModemDataRate
{
    Afsk1200,
    G3ruh9600
}

/// <summary>Resolves 1200 AFSK vs 9600 G3RUH for the built-in modem.</summary>
public static class BuiltInModemKind
{
    public static BuiltInModemDataRate ResolveDataRate(TerminalBuiltInModemSettings settings, string? modemHint) =>
        settings.RateMode switch
        {
            BuiltInModemRateMode.Afsk1200 => BuiltInModemDataRate.Afsk1200,
            BuiltInModemRateMode.G3ruh9600 => BuiltInModemDataRate.G3ruh9600,
            _ => TerminalSatelliteProfiles.ModemHintIsG3ruh9600(modemHint)
                ? BuiltInModemDataRate.G3ruh9600
                : BuiltInModemDataRate.Afsk1200
        };

    public static int BaudFor(BuiltInModemDataRate rate) =>
        rate == BuiltInModemDataRate.G3ruh9600 ? Modem.HdlcEncoder.G3ruhBaud : Modem.HdlcEncoder.Baud;
}
