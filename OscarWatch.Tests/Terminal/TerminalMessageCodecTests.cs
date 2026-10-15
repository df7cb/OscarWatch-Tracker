using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalMessageCodecTests
{
    private static readonly DateTime Utc = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Ax25Address Me = Ax25Address.Parse("MM9SQL");

    [Fact]
    public void Plain_text_uses_destination_as_to()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("OZ9AAR"), Ax25Address.Parse("OK2ZC"), [], "JO45");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.PlainText, Utc);

        Assert.Equal("OK2ZC", msg.From);
        Assert.Equal("OZ9AAR", msg.To);
        Assert.Equal("JO45", msg.Grid);
        Assert.False(msg.IsCq);
    }

    [Fact]
    public void Plain_text_cq_destination_is_cq()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("CQ"), Ax25Address.Parse("LA6OP"), [], "JP67");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.PlainText, Utc);
        Assert.True(msg.IsCq);
        Assert.Equal("JP67", msg.Grid);
    }

    [Fact]
    public void Aprs_message_uses_addressee()
    {
        var frame = Ax25Frame.CreateUi(
            Ax25Address.Parse("APRS"),
            Ax25Address.Parse("G0ABC"),
            [new Ax25Address("RS0ISS", 0, true)],
            ":MM9SQL   :R73");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.Aprs, Utc);

        Assert.Equal("MM9SQL", msg.To);
        Assert.Equal("R73", msg.Text);
        Assert.True(msg.IsRoger);
        Assert.True(msg.IsSignOff);
        Assert.True(msg.IsDigipeated);
        Assert.Equal("RS0ISS*", msg.Path);
    }

    [Fact]
    public void Aprs_status_cq_and_tocall_destination()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("APDW17"), Ax25Address.Parse("ZS1TA"), [], ">CQ JF95FX op Tom");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.Aprs, Utc);
        Assert.Equal("CQ", msg.To);
        Assert.True(msg.IsCq);
        Assert.Equal("JF95fx", msg.Grid);
    }

    [Fact]
    public void Aprs_position_is_beacon_to_all()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("APRS"), Ax25Address.Parse("G0ABC"), [], "!5552.00N/00415.50W-");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.Aprs, Utc);
        Assert.Equal("ALL", msg.To);
        Assert.True(msg.IsBeacon);
    }

    [Fact]
    public void Telemetry_from_non_callsign_source()
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse("ALL"), Ax25Address.Parse("GRNCUB"), [], "[TLM: eps/obc]");
        var msg = TerminalMessageCodec.Decode(frame, TerminalMessageFormat.PlainText, Utc);
        Assert.True(msg.IsTelemetry);
    }

    [Fact]
    public void Outgoing_aprs_cq_status_keeps_the_typed_text()
    {
        var o = TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.Aprs, Me, "CQ", "test", Ax25Frame.ParsePath("ARISS"));
        Assert.Equal(">test", o.Frame.InfoText);
        Assert.Equal("test", o.Text);
        Assert.Equal("CQ", o.To);
        Assert.Equal(AprsPayloadCodec.Tocall, o.Frame.Destination.Text);
        Assert.Equal("ARISS", o.Frame.Digipeaters[0].Text);
    }

    [Fact]
    public void Outgoing_aprs_cq_keeps_a_cq_the_operator_typed()
    {
        var o = TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.Aprs, Me, "CQ", "CQ IO85", []);
        Assert.Equal(">CQ IO85", o.Frame.InfoText);
        Assert.Equal("CQ IO85", o.Text);
        Assert.Equal("CQ", o.To);
    }

    [Fact]
    public void Outgoing_aprs_directed_becomes_message()
    {
        var o = TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.Aprs, Me, "g0abc", "IO85 599", []);
        Assert.Equal(":G0ABC    :IO85 599", o.Frame.InfoText);
        Assert.Equal("G0ABC", o.To);
    }

    [Fact]
    public void Outgoing_plain_text_uses_to_as_destination()
    {
        var o = TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.PlainText, Me, "OK2ZC", "R73", []);
        Assert.Equal("OK2ZC", o.Frame.Destination.Text);
        Assert.Equal("R73", o.Frame.InfoText);

        var cq = TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.PlainText, Me, "", "IO85", []);
        Assert.Equal("CQ", cq.Frame.Destination.Text);
    }

    [Fact]
    public void Outgoing_plain_text_rejects_invalid_destination()
    {
        Assert.Throws<FormatException>(() =>
            TerminalMessageCodec.BuildOutgoing(TerminalMessageFormat.PlainText, Me, "G0/ABC", "x", []));
    }

    [Fact]
    public void Expands_macros()
    {
        Assert.Equal(
            "G0ABC de MM9SQL IO85 IO85ts",
            TerminalMessageCodec.ExpandMacro("{CALL} de {MYCALL} {MYGRID4} {MYGRID}", "mm9sql", "IO85TS", "g0abc"));
    }
}
