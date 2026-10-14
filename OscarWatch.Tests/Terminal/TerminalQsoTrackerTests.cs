using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class TerminalQsoTrackerTests
{
    private const string Me = "MM9SQL";
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static TerminalDecodedMessage Rx(string from, string to, string text, int seconds, TerminalMessageFormat format = TerminalMessageFormat.PlainText)
    {
        var dest = Ax25Address.TryParse(to, out var d) ? d : Ax25Address.Parse("CQ");
        var frame = Ax25Frame.CreateUi(dest, Ax25Address.Parse(from), [], text);
        return TerminalMessageCodec.Decode(frame, format, T0.AddSeconds(seconds));
    }

    [Fact]
    public void Caller_flow_completes_when_they_send_r73()
    {
        var t = new TerminalQsoTracker();
        t.OnReceived(Rx("OK2ZC", "CQ", "JN89", 0), Me);
        Assert.Equal(TerminalStationState.Cq, t.Get("OK2ZC")!.State);

        t.OnSent("OK2ZC", "IO85", T0.AddSeconds(5));
        Assert.Equal(TerminalStationState.ReportSent, t.Get("OK2ZC")!.State);

        t.OnReceived(Rx("OK2ZC", Me, "R73", 10), Me);
        var s = t.Get("OK2ZC")!;
        Assert.Equal(TerminalStationState.Complete, s.State);
        Assert.True(TerminalQsoTracker.IsReadyToLog(s));
        Assert.Equal("JN89", s.Grid);
    }

    [Fact]
    public void Cq_flow_completes_when_we_send_r73()
    {
        var t = new TerminalQsoTracker();
        t.OnSent("CQ", "CQ IO85", T0);
        t.OnReceived(Rx("OZ9AAR", Me, "JO45", 8), Me);

        var s = t.Get("OZ9AAR")!;
        Assert.True(s.ExchangeSent);
        Assert.Equal(TerminalStationState.ReportsExchanged, s.State);
        Assert.False(TerminalQsoTracker.IsReadyToLog(s));

        t.OnSent("OZ9AAR", "R73", T0.AddSeconds(15));
        Assert.True(TerminalQsoTracker.IsReadyToLog(s));
    }

    [Fact]
    public void Old_cq_does_not_count_as_exchange_sent()
    {
        var t = new TerminalQsoTracker();
        t.OnSent("CQ", "CQ IO85", T0);
        t.OnReceived(Rx("OZ9AAR", Me, "JO45", (int)TerminalQsoTracker.CqCopyWindow.TotalSeconds + 5), Me);
        Assert.False(t.Get("OZ9AAR")!.ExchangeSent);
    }

    [Fact]
    public void Exchange_without_roger_is_not_ready_to_log()
    {
        var t = new TerminalQsoTracker();
        t.OnSent("G0ABC", "IO85 599", T0);
        t.OnReceived(Rx("G0ABC", Me, "IO91 599", 5), Me);
        var s = t.Get("G0ABC")!;

        Assert.False(TerminalQsoTracker.IsReadyToLog(s));
        Assert.Equal("599", s.RstSent);
        Assert.Equal("599", s.RstReceived);
    }

    [Fact]
    public void Logged_station_is_not_ready_again()
    {
        var t = new TerminalQsoTracker();
        t.OnSent("G0ABC", "IO85", T0);
        t.OnReceived(Rx("G0ABC", Me, "R73", 5), Me);
        t.MarkLogged("G0ABC-7");
        var s = t.Get("G0ABC")!;
        Assert.Equal(TerminalStationState.Logged, s.State);
        Assert.False(TerminalQsoTracker.IsReadyToLog(s));
    }

    [Fact]
    public void Ignores_own_frames_and_telemetry()
    {
        var t = new TerminalQsoTracker();
        Assert.Null(t.OnReceived(Rx(Me, "G0ABC", "IO85", 0), Me));
        Assert.Null(t.OnReceived(Rx("GRNCUB", "ALL", "[TLM: x]", 0), Me));
        Assert.Empty(t.Stations);
    }

    [Fact]
    public void Active_partners_respects_window()
    {
        var t = new TerminalQsoTracker();
        t.OnSent("G0ABC", "IO85", T0);
        t.OnSent("OK2ZC", "IO85", T0.AddSeconds(100));
        t.OnReceived(Rx("LA6OP", "CQ", "JP67", 100), Me);

        var active = t.ActivePartners(T0.AddSeconds(150), TimeSpan.FromMinutes(2));
        Assert.Equal(["OK2ZC"], active.Select(s => s.Call));
    }

    [Fact]
    public void Clear_qso_progress_keeps_heard_only_stations()
    {
        var t = new TerminalQsoTracker();
        t.OnReceived(Rx("LA6OP", "CQ", "JP67", 0), Me);
        t.OnSent("G0ABC", "IO85", T0);
        t.OnReceived(Rx("G0ABC", Me, "R73", 5), Me);

        t.ClearQsoProgress();

        var heard = t.Get("LA6OP")!;
        Assert.Equal(TerminalStationState.Cq, heard.State);
        Assert.Equal(1, heard.CqCount);
        Assert.False(TerminalQsoTracker.ShowsInQsoList(heard));

        var worked = t.Get("G0ABC")!;
        Assert.Equal(TerminalStationState.Heard, worked.State);
        Assert.False(worked.IsInQso);
        Assert.False(worked.RogerReceived);
        Assert.False(worked.ExchangeSent);
        Assert.Null(worked.RstSent);
        Assert.False(TerminalQsoTracker.ShowsInQsoList(worked));
        Assert.Equal(2, t.Stations.Count);
    }

    [Fact]
    public void Clear_removes_every_station()
    {
        var t = new TerminalQsoTracker();
        t.OnReceived(Rx("LA6OP", "CQ", "JP67", 0), Me);
        t.OnSent("G0ABC", "IO85", T0);
        t.Clear();
        Assert.Empty(t.Stations);
    }

    [Fact]
    public void Classifier_orders_kinds()
    {
        Assert.Equal(TerminalMessageKind.MyTx, TerminalMessageClassifier.Classify(Rx(Me, "G0ABC", "x", 0), Me, true));
        Assert.Equal(TerminalMessageKind.MyDigipeated, TerminalMessageClassifier.Classify(Rx("MM9SQL-1", "G0ABC", "x", 0), Me, false));
        Assert.Equal(TerminalMessageKind.ToMe, TerminalMessageClassifier.Classify(Rx("G0ABC", Me, "CQ", 0), Me, false));
        Assert.Equal(TerminalMessageKind.Cq, TerminalMessageClassifier.Classify(Rx("G0ABC", "CQ", "IO91", 0), Me, false));
        Assert.Equal(TerminalMessageKind.Telemetry, TerminalMessageClassifier.Classify(Rx("GRNCUB", "ALL", "x", 0), Me, false));
        Assert.Equal(TerminalMessageKind.Normal, TerminalMessageClassifier.Classify(Rx("G0ABC", "OK2ZC", "599", 0), Me, false));
    }
}
