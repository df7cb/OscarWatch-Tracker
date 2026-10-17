using OscarWatch.Core.Geo;

namespace OscarWatch.Core.Ft4;

/// <summary>Which station field is missing before FT4 may open the receiver.</summary>
public enum Ft4StationGap
{
    None,
    Callsign,
    Locator,
    Both
}

/// <summary>Why a callsign and locator pair cannot be saved for FT4.</summary>
public enum Ft4StationIdentityError
{
    None,
    CallsignRequired,
    LocatorRequired,
    BothRequired,
    LocatorInvalid,
    TooManyLocators
}

/// <summary>
/// Callsign and locator required before FT4 opens the receiver.
/// A CQ carries the first four characters of the locator, so a field-only code such as IO is not enough.
/// </summary>
public static class Ft4StationIdentity
{
    public static Ft4StationGap Gap(string? callsign, string? locator)
    {
        var needCall = Ft4MessageCodec.NormalizeCall(callsign ?? "").Length == 0;
        var needLocator = !HasLocator(locator);
        if (needCall && needLocator)
            return Ft4StationGap.Both;
        if (needCall)
            return Ft4StationGap.Callsign;
        if (needLocator)
            return Ft4StationGap.Locator;
        return Ft4StationGap.None;
    }

    public static bool IsReady(string? callsign, string? locator) =>
        Gap(callsign, locator) == Ft4StationGap.None;

    /// <summary>True when the locator can supply the four-character grid FT4 sends with CQ.</summary>
    public static bool HasLocator(string? locator)
    {
        var first = FirstSegment(locator);
        return first.Length >= 4;
    }

    public static bool TryNormalize(
        string? callsign,
        string? locator,
        out string call,
        out string grid,
        out Ft4StationIdentityError error,
        out string? invalidSegment)
    {
        call = Ft4MessageCodec.NormalizeCall(callsign ?? "");
        grid = "";
        invalidSegment = null;

        if (!string.IsNullOrWhiteSpace(locator)
            && !MaidenheadLocator.TryValidateGrids(locator, out grid, out var gridError, out invalidSegment))
        {
            error = gridError == GridValidationError.TooManyGrids
                ? Ft4StationIdentityError.TooManyLocators
                : Ft4StationIdentityError.LocatorInvalid;
            grid = "";
            return false;
        }

        var needCall = call.Length == 0;
        var needLocator = !HasLocator(grid);
        if (needCall && needLocator)
        {
            error = Ft4StationIdentityError.BothRequired;
            return false;
        }

        if (needCall)
        {
            error = Ft4StationIdentityError.CallsignRequired;
            return false;
        }

        if (needLocator)
        {
            error = Ft4StationIdentityError.LocatorRequired;
            return false;
        }

        error = Ft4StationIdentityError.None;
        return true;
    }

    private static string FirstSegment(string? locator)
    {
        if (string.IsNullOrWhiteSpace(locator))
            return "";

        var parts = locator.Split(
            [',', ';', '/', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? "" : parts[0];
    }
}
