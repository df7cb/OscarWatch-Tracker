using System.Globalization;
using OscarWatch.Core.Ft4;

namespace OscarWatch.Tests.Ft4;

/// <summary>
/// DT display convention: WSJT-X measures from 0.5 s into the slot (an on-time signal
/// reads near 0); OscarWatch measures from the slot start (on-time reads about +0.5 s).
/// The conversion is display only and must not change the stored DT used for filtering.
/// </summary>
public sealed class Ft4DisplayDtTests
{
    [Fact]
    public void Offset_constant_is_half_a_second()
    {
        Assert.Equal(0.5, Ft4DecodeDepth.WsjtxDtOffsetSec);
    }

    [Theory]
    [InlineData(0.5, 0.0)]    // on-time FT4 start -> WSJT-X 0
    [InlineData(0.6, 0.1)]    // WSJT-X +0.1 lines up with OscarWatch +0.6 (the WT0DX report)
    [InlineData(0.0, -0.5)]   // half a second early
    [InlineData(1.0, 0.5)]
    public void DisplayDt_subtracts_the_offset_in_wsjtx_mode(double rawDt, double expected)
    {
        Assert.Equal(expected, Ft4DecodeDepth.DisplayDt(rawDt, wsjtxConvention: true), precision: 6);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.0)]
    [InlineData(1.2)]
    public void DisplayDt_is_unchanged_in_slot_relative_mode(double rawDt)
    {
        Assert.Equal(rawDt, Ft4DecodeDepth.DisplayDt(rawDt, wsjtxConvention: false), precision: 6);
    }

    [Fact]
    public void Message_display_dt_matches_the_helper_and_keeps_the_raw_value()
    {
        var message = Decode(timeSec: 0.6f);

        Assert.Equal(0.1, message.DisplayDtSec(wsjtxConvention: true), precision: 6);
        Assert.Equal(0.6, message.DisplayDtSec(wsjtxConvention: false), precision: 6);

        // The stored DT (used by satellite/moonbounce filtering) is untouched.
        Assert.Equal(0.6f, message.TimeSec);
    }

    [Theory]
    [InlineData(0.6f, true, "+0.1")]
    [InlineData(0.5f, true, "0.0")]
    [InlineData(0.0f, true, "-0.5")]
    [InlineData(0.6f, false, "+0.6")]
    public void Dt_column_text_follows_the_convention(float timeSec, bool wsjtx, string expected)
    {
        // Mirrors the StringFormat used by the FT4 decode list (+0.0;-0.0;0.0).
        var text = Decode(timeSec)
            .DisplayDtSec(wsjtx)
            .ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

        Assert.Equal(expected, text);
    }

    private static Ft4DecodedMessage Decode(float timeSec) => new(
        SlotUtc: DateTime.UtcNow,
        Text: "CQ MM9SQL IO75",
        FreqHz: 1500f,
        TimeSec: timeSec,
        SnrDb: 0f,
        CallTo: null,
        CallDe: "MM9SQL",
        Extra: "IO75",
        IsOwnEcho: false);
}
