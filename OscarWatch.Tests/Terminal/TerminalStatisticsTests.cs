using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalStatisticsTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static TerminalDecodedMessage Rx(string from, string to, string text, bool digipeated = false)
    {
        var path = digipeated ? new[] { new Ax25Address("RS0ISS", 0, true) } : [];
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse(to), Ax25Address.Parse(from), path, text);
        return TerminalMessageCodec.Decode(frame, TerminalMessageFormat.PlainText, T0);
    }

    [Fact]
    public void Counts_unique_callsigns_rx_and_digipeated()
    {
        var s = new TerminalStatistics();
        s.RecordReceived(Rx("G0ABC", "MM9SQL", "x", true), TerminalMessageKind.ToMe);
        s.RecordReceived(Rx("G0ABC-7", "CQ", "x"), TerminalMessageKind.Cq);
        s.RecordReceived(Rx("OK2ZC", "CQ", "x"), TerminalMessageKind.Cq);
        s.RecordReceived(Rx("GRNCUB", "ALL", "x"), TerminalMessageKind.Telemetry);

        Assert.Equal(2, s.UniqueCallsigns);
        Assert.Equal(1, s.MyRx);
        Assert.Equal(1, s.Digipeated);
    }

    [Fact]
    public void Own_frame_with_repeated_bit_confirms_send_and_records_delay()
    {
        var s = new TerminalStatistics();
        s.RecordSent(">CQ IO85", T0);
        s.RecordSent(":G0ABC    :R73", T0.AddSeconds(10));

        var result = s.EvaluateOwnFrame(">CQ IO85", true, T0.AddSeconds(2), out var delay);

        Assert.Equal(TerminalOwnFrameResult.Digipeated, result);
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
        Assert.Equal(2, s.MyTx);
        Assert.Equal(1, s.MyDigipeated);
        Assert.Equal(50.0, s.MyDigipeatSuccessPercent);
        Assert.Equal(2.0, s.MedianDelaySeconds);
    }

    [Fact]
    public void Quick_copy_without_path_is_local_echo()
    {
        var s = new TerminalStatistics();
        s.RecordSent("IO85", T0);
        Assert.Equal(TerminalOwnFrameResult.LocalEcho, s.EvaluateOwnFrame("IO85", false, T0.AddMilliseconds(100), out _));
        Assert.Equal(0, s.MyDigipeated);

        Assert.Equal(TerminalOwnFrameResult.Digipeated, s.EvaluateOwnFrame("IO85", false, T0.AddSeconds(1.5), out _));
        Assert.Equal(1, s.MyDigipeated);
    }

    [Fact]
    public void Repeated_digipeats_count_once_for_success()
    {
        var s = new TerminalStatistics();
        s.RecordSent("IO85", T0);
        s.EvaluateOwnFrame("IO85", true, T0.AddSeconds(1), out _);
        s.EvaluateOwnFrame("IO85", true, T0.AddSeconds(3), out _);
        Assert.Equal(1, s.MyDigipeated);
        Assert.Equal(2, s.Digipeated);
        Assert.Equal(1.0, s.MedianDelaySeconds);
    }

    [Fact]
    public void Expired_sends_do_not_match()
    {
        var s = new TerminalStatistics();
        s.RecordSent("IO85", T0);
        s.EvaluateOwnFrame("IO85", true, T0 + TerminalStatistics.MatchWindow + TimeSpan.FromSeconds(1), out var delay);
        Assert.Null(delay);
        Assert.Equal(0, s.MyDigipeated);
    }
}
