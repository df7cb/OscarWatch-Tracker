using OscarWatch.Core.Radio;

namespace OscarWatch.Tests;

public sealed class YaesuFt847CatCodecTests
{
    [Fact]
    public void DecodeFrequency10Hz_matches_faq_example()
    {
        var response = new byte[] { 0x14, 0x69, 0x40, 0x00, 0x08 };
        var hz = YaesuFt847CatCodec.DecodeFrequency10Hz(response);
        Assert.Equal(146_940_000, hz);
        Assert.Equal("FM", YaesuFt847CatCodec.DecodeMode(0x08));
    }

    [Fact]
    public void EncodeFrequency10Hz_round_trips()
    {
        var cmd = YaesuFt847CatCodec.BuildSetFrequencyCommand(435_825_000, YaesuFt847VfoTarget.SatRx, satelliteMode: true);
        Assert.Equal(0x11, cmd[4]);
        Assert.Equal(435_825_000, YaesuFt847CatCodec.DecodeFrequency10Hz(cmd));
    }

    [Fact]
    public void BuildRepeaterOffsetCommand_matches_manual_5MHz_example()
    {
        var cmd = YaesuFt847CatCodec.BuildRepeaterOffsetCommand(5_000_000);
        Assert.Equal(new byte[] { 0x00, 0x50, 0x00, 0x00, 0xF9 }, cmd);
    }

    [Fact]
    public void BuildRepeaterShiftCommand_uses_manual_direction_bytes()
    {
        Assert.Equal(new byte[] { 0x09, 0x00, 0x00, 0x00, 0x09 }, YaesuFt847CatCodec.BuildRepeaterShiftCommand(-1));
        Assert.Equal(new byte[] { 0x49, 0x00, 0x00, 0x00, 0x09 }, YaesuFt847CatCodec.BuildRepeaterShiftCommand(1));
        Assert.Equal(new byte[] { 0x89, 0x00, 0x00, 0x00, 0x09 }, YaesuFt847CatCodec.BuildRepeaterShiftCommand(0));
    }

    [Fact]
    public void ApplyVfoTarget_sat_tx_uses_opcode_21()
    {
        var cmd = YaesuFt847CatCodec.BuildSetFrequencyCommand(145_900_000, YaesuFt847VfoTarget.SatTx, satelliteMode: true);
        Assert.Equal(0x21, cmd[4]);
    }

    [Fact]
    public void BuildCtcssFrequencyCommand_uses_sat_tx_opcode()
    {
        var cmd = YaesuFt847CatCodec.BuildCtcssFrequencyCommand(67.0, YaesuFt847VfoTarget.SatTx, satelliteMode: true);
        Assert.Equal(0x2b, cmd[4]);
        Assert.Equal(0x3F, cmd[0]);
    }

    [Fact]
    public void TryGetCtcssCatCode_maps_67Hz_to_first_code()
    {
        Assert.True(YaesuFt847CatCodec.TryGetCtcssCatCode(67.0, out var code));
        Assert.Equal(0x3F, code);
    }

    [Fact]
    public void BuildSetModeCommand_FM_uses_wide_mode_byte()
    {
        var cmd = YaesuFt847CatCodec.BuildSetModeCommand("FM", YaesuFt847VfoTarget.SatRx, satelliteMode: true);
        Assert.Equal(0x08, cmd[0]);
        Assert.Equal(0x17, cmd[4]);
    }

    [Fact]
    public void BuildSetModeCommand_FMN_uses_narrow_mode_byte()
    {
        var cmd = YaesuFt847CatCodec.BuildSetModeCommand("FMN", YaesuFt847VfoTarget.SatRx, satelliteMode: true);
        Assert.Equal(0x88, cmd[0]);
    }

    [Theory]
    [InlineData(" FM ", 0x08)]
    [InlineData("fmn", 0x88)]
    public void BuildSetModeCommand_trims_and_normalizes_case(string mode, byte expectedModeByte)
    {
        var cmd = YaesuFt847CatCodec.BuildSetModeCommand(mode, YaesuFt847VfoTarget.SatTx, satelliteMode: true);
        Assert.Equal(expectedModeByte, cmd[0]);
        Assert.Equal(0x27, cmd[4]);
    }
}
