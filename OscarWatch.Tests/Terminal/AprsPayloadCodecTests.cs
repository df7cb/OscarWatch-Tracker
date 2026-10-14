using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class AprsPayloadCodecTests
{
    [Fact]
    public void Builds_message_with_padded_addressee_and_number()
    {
        Assert.Equal(":G0ABC    :R73{12", AprsPayloadCodec.BuildMessage("g0abc", "R73", "12"));
        Assert.Equal(":G0ABC-7  :hi", AprsPayloadCodec.BuildMessage("G0ABC-7", "hi"));
    }

    [Fact]
    public void Message_text_strips_reserved_characters()
    {
        Assert.Equal(":G0ABC    :ab", AprsPayloadCodec.BuildMessage("G0ABC", "a{b|~"));
    }

    [Fact]
    public void Builds_ack()
    {
        Assert.Equal(":MM9SQL   :ack12", AprsPayloadCodec.BuildAck("MM9SQL", "12"));
    }

    [Fact]
    public void Parses_message_with_number_and_grid()
    {
        var p = AprsPayloadCodec.Parse(":MM9SQL   :IO91 599{7");
        Assert.Equal(AprsPacketType.Message, p.Type);
        Assert.Equal("MM9SQL", p.Addressee);
        Assert.Equal("IO91 599", p.Text);
        Assert.Equal("7", p.MessageNumber);
        Assert.Equal("IO91", p.Grid);
    }

    [Fact]
    public void Parses_reply_ack_message_number()
    {
        var p = AprsPayloadCodec.Parse(":MM9SQL   :hello{AB}CD");
        Assert.Equal("AB", p.MessageNumber);
        Assert.Equal("hello", p.Text);
    }

    [Fact]
    public void Parses_ack_and_reject()
    {
        var ack = AprsPayloadCodec.Parse(":MM9SQL   :ack7");
        Assert.Equal(AprsPacketType.Ack, ack.Type);
        Assert.Equal("7", ack.MessageNumber);

        var rej = AprsPayloadCodec.Parse(":MM9SQL   :rej7");
        Assert.Equal(AprsPacketType.Reject, rej.Type);
    }

    [Fact]
    public void Parses_status_with_maidenhead_prefix()
    {
        var p = AprsPayloadCodec.Parse(">IO91SX/G CQ via ISS");
        Assert.Equal(AprsPacketType.Status, p.Type);
        Assert.Equal("IO91sx", p.Grid);
    }

    [Fact]
    public void Parses_uncompressed_position_to_grid()
    {
        var p = AprsPayloadCodec.Parse("!5552.00N/00415.50W-Glasgow via ISS");
        Assert.Equal(AprsPacketType.Position, p.Type);
        Assert.Equal("IO75uu", p.Grid);
        Assert.Equal("Glasgow via ISS", p.Text);
    }

    [Fact]
    public void Parses_timestamped_position()
    {
        var p = AprsPayloadCodec.Parse("@092345z5552.00N/00415.50W-x");
        Assert.Equal("IO75uu", p.Grid);
    }

    [Fact]
    public void Parses_compressed_position_to_grid()
    {
        var body = "/" + Base91(380926.0 * (90 - 55.8667)) + Base91(190463.0 * (180 - 4.2583)) + ">   test";
        var p = AprsPayloadCodec.Parse("!" + body);
        Assert.Equal("IO75uu", p.Grid);
    }

    [Fact]
    public void Parses_mic_e_latitude_and_longitude()
    {
        // 55 52.00 N, 004 15.50 W: digits 555200, north in char 4, +100 offset in char 5, west in char 6.
        var p = AprsPayloadCodec.Parse("`z+N!!!>/ Mic-E test", "555RPP");
        Assert.Equal(AprsPacketType.MicE, p.Type);
        Assert.Equal("IO75uu", p.Grid);
    }

    [Fact]
    public void Parses_maidenhead_beacon()
    {
        var p = AprsPayloadCodec.Parse("[IO85TS]hello");
        Assert.Equal(AprsPacketType.MaidenheadBeacon, p.Type);
        Assert.Equal("IO85ts", p.Grid);
    }

    [Fact]
    public void Parses_telemetry()
    {
        Assert.Equal(AprsPacketType.Telemetry, AprsPayloadCodec.Parse("T#005,199,000,255,073,123,01101001").Type);
    }

    [Fact]
    public void Unknown_data_type_keeps_text_and_finds_grid()
    {
        var p = AprsPayloadCodec.Parse("CQ JO45 op Tom");
        Assert.Equal(AprsPacketType.Other, p.Type);
        Assert.Equal("JO45", p.Grid);
    }

    private static string Base91(double value)
    {
        var v = (long)value;
        var chars = new char[4];
        for (var i = 3; i >= 0; i--)
        {
            chars[i] = (char)(v % 91 + 33);
            v /= 91;
        }

        return new string(chars);
    }
}
