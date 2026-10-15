using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class KissCodecTests
{
    [Fact]
    public void Data_frame_escapes_fend_and_fesc()
    {
        byte[] payload = [0x01, KissCodec.Fend, 0x02, KissCodec.Fesc, 0x03];

        var bytes = KissCodec.EncodeData(0, payload);

        Assert.Equal(
            [KissCodec.Fend, 0x00, 0x01, KissCodec.Fesc, KissCodec.Tfend, 0x02, KissCodec.Fesc, KissCodec.Tfesc, 0x03, KissCodec.Fend],
            bytes);
    }

    [Fact]
    public void Data_frame_round_trips_through_reader()
    {
        byte[] payload = [KissCodec.Fend, KissCodec.Fesc, KissCodec.Tfend, KissCodec.Tfesc, 0x00, 0xFF];

        var frames = new KissFrameReader().Append(KissCodec.EncodeData(3, payload));

        var frame = Assert.Single(frames);
        Assert.Equal(3, frame.Port);
        Assert.Equal(KissCommand.Data, frame.Command);
        Assert.Equal(payload, frame.Data);
    }

    [Theory]
    [InlineData(0, KissCommand.Data, 0x00)]
    [InlineData(1, KissCommand.Data, 0x10)]
    [InlineData(15, KissCommand.Data, 0xF0)]
    [InlineData(0, KissCommand.TxDelay, 0x01)]
    [InlineData(0, KissCommand.Persistence, 0x02)]
    [InlineData(2, KissCommand.SlotTime, 0x23)]
    [InlineData(0, KissCommand.TxTail, 0x04)]
    [InlineData(0, KissCommand.FullDuplex, 0x05)]
    public void Command_byte_carries_port_and_command(int port, KissCommand command, byte expected) =>
        Assert.Equal(expected, KissCodec.CommandByte(port, command));

    [Fact]
    public void Timing_parameters_use_ten_millisecond_units()
    {
        Assert.Equal([KissCodec.Fend, 0x01, 30, KissCodec.Fend], KissCodec.EncodeTiming(0, KissCommand.TxDelay, 300));
        Assert.Equal([KissCodec.Fend, 0x03, 10, KissCodec.Fend], KissCodec.EncodeTiming(0, KissCommand.SlotTime, 100));
        Assert.Equal([KissCodec.Fend, 0x14, 5, KissCodec.Fend], KissCodec.EncodeTiming(1, KissCommand.TxTail, 50));
        Assert.Equal(255, KissCodec.ToTenMsUnits(5000));
        Assert.Equal(0, KissCodec.ToTenMsUnits(-10));
    }

    [Fact]
    public void Parameter_frames_are_sent_in_order()
    {
        var frames = KissCodec.ParameterFrames(0, 300, 63, 100, 50, fullDuplex: true);

        Assert.Equal(5, frames.Count);
        Assert.Equal([KissCodec.Fend, 0x01, 30, KissCodec.Fend], frames[0]);
        Assert.Equal([KissCodec.Fend, 0x02, 63, KissCodec.Fend], frames[1]);
        Assert.Equal([KissCodec.Fend, 0x03, 10, KissCodec.Fend], frames[2]);
        Assert.Equal([KissCodec.Fend, 0x04, 5, KissCodec.Fend], frames[3]);
        Assert.Equal([KissCodec.Fend, 0x05, 1, KissCodec.Fend], frames[4]);
    }

    [Fact]
    public void Reader_joins_a_frame_split_across_reads()
    {
        var bytes = KissCodec.EncodeData(0, [1, 2, 3, 4, 5]);
        var reader = new KissFrameReader();

        Assert.Empty(reader.Append(bytes.AsSpan(0, 3)));
        var frame = Assert.Single(reader.Append(bytes.AsSpan(3)));

        Assert.Equal([1, 2, 3, 4, 5], frame.Data);
    }

    [Fact]
    public void Reader_handles_an_escape_pair_split_across_reads()
    {
        var bytes = KissCodec.EncodeData(0, [0x41, KissCodec.Fend, 0x42]);
        var split = Array.IndexOf(bytes, KissCodec.Fesc) + 1;
        var reader = new KissFrameReader();

        Assert.Empty(reader.Append(bytes.AsSpan(0, split)));
        var frame = Assert.Single(reader.Append(bytes.AsSpan(split)));

        Assert.Equal([0x41, KissCodec.Fend, 0x42], frame.Data);
    }

    [Fact]
    public void Reader_returns_two_frames_from_one_buffer()
    {
        var bytes = KissCodec.EncodeData(0, [1, 2]).Concat(KissCodec.EncodeData(1, [3, 4])).ToArray();

        var frames = new KissFrameReader().Append(bytes);

        Assert.Equal(2, frames.Count);
        Assert.Equal([1, 2], frames[0].Data);
        Assert.Equal(1, frames[1].Port);
        Assert.Equal([3, 4], frames[1].Data);
    }

    [Fact]
    public void Reader_ignores_back_to_back_fends_and_leading_noise()
    {
        byte[] bytes = [0x55, 0x66, KissCodec.Fend, KissCodec.Fend, KissCodec.Fend, 0x00, 9, KissCodec.Fend, KissCodec.Fend];

        var frame = Assert.Single(new KissFrameReader().Append(bytes));

        Assert.Equal([9], frame.Data);
    }

    [Fact]
    public void Reader_keeps_command_frames_separate_from_data()
    {
        var bytes = KissCodec.EncodeTiming(0, KissCommand.TxDelay, 300)
            .Concat(KissCodec.EncodeData(0, [7, 8]))
            .ToArray();

        var frames = new KissFrameReader().Append(bytes);

        Assert.Equal(2, frames.Count);
        Assert.Equal(KissCommand.TxDelay, frames[0].Command);
        Assert.Equal([30], frames[0].Data);
        Assert.Equal(KissCommand.Data, frames[1].Command);
        Assert.Equal([7, 8], frames[1].Data);
    }
}
