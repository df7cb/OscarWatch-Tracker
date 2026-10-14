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
        Assert.Equal(AprsMicEStatus.Emergency, p.Position!.MicEStatus);
    }

    [Fact]
    public void Parses_mic_e_symbol_speed_course_altitude_and_status()
    {
        // Yaesu style: ` prefix and two character suffix around the comment, 100 m altitude as "4{}.
        var p = AprsPayloadCodec.Parse("`z+Nn\"O>/`\"4{}Hello ISS_%", "UU5RPP-1");
        var pos = p.Position!;
        Assert.Equal(AprsPacketType.MicE, p.Type);
        Assert.Equal(55.8667, pos.Latitude, 3);
        Assert.Equal(-4.2583, pos.Longitude, 3);
        Assert.Equal('/', pos.SymbolTable);
        Assert.Equal('>', pos.SymbolCode);
        Assert.Equal(20, pos.SpeedKnots);
        Assert.Equal(251, pos.CourseDeg);
        Assert.Equal(100, pos.AltitudeMetres);
        Assert.Equal(AprsMicEStatus.EnRoute, pos.MicEStatus);
        Assert.Equal("Hello ISS", p.Text);
    }

    [Theory]
    [InlineData(">Kenwood TH-D74=", "Kenwood TH-D74")]
    [InlineData("]TM-D710=", "TM-D710")]
    [InlineData("'FTM-400_\"", "FTM-400")]
    [InlineData("plain comment", "plain comment")]
    public void Mic_e_comment_drops_radio_type_characters(string raw, string expected)
    {
        Assert.Equal(expected, AprsPayloadCodec.CleanMicEComment(raw, out _));
    }

    [Theory]
    [InlineData("PPP", AprsMicEStatus.OffDuty)]
    [InlineData("PP0", AprsMicEStatus.EnRoute)]
    [InlineData("00P", AprsMicEStatus.Priority)]
    [InlineData("AAA", AprsMicEStatus.Custom0)]
    [InlineData("00A", AprsMicEStatus.Custom6)]
    public void Mic_e_status_comes_from_destination_message_bits(string bits, AprsMicEStatus expected)
    {
        var p = AprsPayloadCodec.Parse("`z+N!!!>/", bits + "RPP");
        Assert.Equal(expected, p.Position!.MicEStatus);
    }

    [Fact]
    public void Mic_e_with_short_destination_keeps_text()
    {
        var p = AprsPayloadCodec.Parse("`z+N!!!>/ test", "APRS");
        Assert.Equal(AprsPacketType.MicE, p.Type);
        Assert.Null(p.Position);
        Assert.Equal("test", p.Text);
    }

    [Fact]
    public void Uncompressed_position_has_symbol_course_speed_and_altitude()
    {
        var p = AprsPayloadCodec.Parse("!5552.00N/00415.50W>088/036/A=001234 via ARISS");
        var pos = p.Position!;
        Assert.Equal(55.8667, pos.Latitude, 3);
        Assert.Equal(-4.2583, pos.Longitude, 3);
        Assert.Equal('/', pos.SymbolTable);
        Assert.Equal('>', pos.SymbolCode);
        Assert.Equal(88, pos.CourseDeg);
        Assert.Equal(36, pos.SpeedKnots);
        Assert.Equal(376, pos.AltitudeMetres);
        Assert.Null(pos.AmbiguityDigits);
        Assert.Equal("/A=001234 via ARISS", p.Text);
    }

    [Fact]
    public void Southern_and_eastern_position_is_negative_latitude()
    {
        var p = AprsPayloadCodec.Parse("=3352.13S\\15112.56E&IO-117 test");
        Assert.Equal(-33.8688, p.Position!.Latitude, 3);
        Assert.Equal(151.2093, p.Position.Longitude, 3);
        Assert.Equal('\\', p.Position.SymbolTable);
        Assert.Equal("QF56od", p.Grid);
    }

    [Fact]
    public void Ambiguous_position_is_centre_of_the_blanked_area()
    {
        var p = AprsPayloadCodec.Parse("@092345z5552.  N/00415.  W-x");
        Assert.Equal(2, p.Position!.AmbiguityDigits);
        Assert.Equal(55 + 52.5 / 60, p.Position.Latitude, 6);
        Assert.Equal(-(4 + 15.5 / 60), p.Position.Longitude, 6);
    }

    [Fact]
    public void Compressed_position_decodes_course_and_speed()
    {
        var body = "/" + Base91(380926.0 * (90 - 55.8667)) + Base91(190463.0 * (180 - 4.2583)) + ">7E! ISS";
        var p = AprsPayloadCodec.Parse("!" + body);
        Assert.Equal(88, p.Position!.CourseDeg);
        Assert.Equal(15, p.Position.SpeedKnots);
        Assert.Equal('>', p.Position.SymbolCode);
        Assert.Equal("ISS", p.Text);
    }

    [Fact]
    public void Compressed_position_decodes_altitude()
    {
        var body = "/" + Base91(380926.0 * (90 - 55.8667)) + Base91(190463.0 * (180 - 4.2583)) + ">S!1";
        var p = AprsPayloadCodec.Parse("=" + body);
        Assert.Null(p.Position!.CourseDeg);
        Assert.InRange(p.Position.AltitudeMetres!.Value, 2690, 2720);
    }

    [Fact]
    public void Position_after_text_prefix_is_decoded()
    {
        var p = AprsPayloadCodec.Parse("TheNet X1J4!5552.00N/00415.50W-test");
        Assert.Equal(AprsPacketType.Position, p.Type);
        Assert.Equal("IO75uu", p.Grid);
        Assert.Equal("test", p.Text);
    }

    [Fact]
    public void Exclamation_mark_in_plain_text_is_not_a_position()
    {
        var p = AprsPayloadCodec.Parse("Hello! IO85 599");
        Assert.Equal(AprsPacketType.Other, p.Type);
        Assert.Null(p.Position);
        Assert.Equal("IO85", p.Grid);
    }

    [Fact]
    public void Malformed_position_falls_back_to_text()
    {
        var p = AprsPayloadCodec.Parse("!55AB.00N/00415.50W-broken");
        Assert.Equal(AprsPacketType.Position, p.Type);
        Assert.Null(p.Position);
        Assert.Equal("55AB.00N/00415.50W-broken", p.Text);
    }

    [Theory]
    [InlineData(0, "=5552.00N/00415.50W[IO75")]
    [InlineData(1, "=5552.0 N/00415.5 W[IO75")]
    [InlineData(2, "=5552.  N/00415.  W[IO75")]
    [InlineData(3, "=555 .  N/0041 .  W[IO75")]
    public void Builds_position_at_each_ambiguity(int ambiguity, string expected)
    {
        Assert.Equal(expected, AprsPayloadCodec.BuildPosition(55.8667, -4.2583, '/', '[', "IO75", ambiguity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Built_position_parses_back_near_the_original(int ambiguity)
    {
        var payload = AprsPayloadCodec.BuildPosition(-33.8688, 151.2093, '/', '-', "test", ambiguity);
        var p = AprsPayloadCodec.Parse(payload).Position!;
        var tolerance = ambiguity switch { 0 => 0.001, 2 => 0.02, _ => 0.2 };
        Assert.InRange(p.Latitude, -33.8688 - tolerance, -33.8688 + tolerance);
        Assert.InRange(p.Longitude, 151.2093 - tolerance, 151.2093 + tolerance);
        Assert.Equal('-', p.SymbolCode);
    }

    [Fact]
    public void Built_position_comment_is_cleaned_and_limited()
    {
        var payload = AprsPayloadCodec.BuildPosition(0.5, 0.5, '/', '[', "a|b~" + new string('x', 60));
        var comment = payload[20..];
        Assert.Equal(AprsPayloadCodec.MaxPositionCommentLength, comment.Length);
        Assert.StartsWith("abx", comment, StringComparison.Ordinal);
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
