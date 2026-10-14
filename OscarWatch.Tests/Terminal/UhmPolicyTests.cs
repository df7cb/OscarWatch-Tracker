using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Uhm;

namespace OscarWatch.Tests.Terminal;

public sealed class UhmPolicyTests
{
    private const string Me = "MM9SQL";
    private static readonly DateTime T0 = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    private static TerminalDecodedMessage Rx(string from, string to, string text, int seconds = 0)
    {
        var frame = Ax25Frame.CreateUi(Ax25Address.Parse(to), Ax25Address.Parse(from), [], text);
        return TerminalMessageCodec.Decode(frame, TerminalMessageFormat.PlainText, T0.AddSeconds(seconds));
    }

    [Fact]
    public void Reports_frame_addressed_to_me_with_ssid_removed()
    {
        var policy = new UhmReportPolicy();
        Assert.True(policy.TryCreateReport(Rx("G0ABC-7", "MM9SQL-1", "JO45"), Me, "ISS", "client", out var report));
        Assert.Equal("G0ABC", report!.HeardTx);
        Assert.Equal("MM9SQL", report.HeardRx);
        Assert.Equal("ISS", report.Satellite);
        Assert.Equal("JO45", report.DecodedMessage);
        Assert.Equal(T0, report.HeardAtUtc);
    }

    [Fact]
    public void Does_not_report_own_frames_unrelated_cq_or_telemetry()
    {
        var policy = new UhmReportPolicy();
        Assert.False(policy.TryCreateReport(Rx(Me, "G0ABC", "IO85"), Me, "ISS", null, out _));
        Assert.False(policy.TryCreateReport(Rx("G0ABC", "OK2ZC", "599"), Me, "ISS", null, out _));
        Assert.False(policy.TryCreateReport(Rx("G0ABC", "CQ", "IO91"), Me, "ISS", null, out _));
        Assert.False(policy.TryCreateReport(Rx("GRNCUB", Me, "[TLM: x]"), Me, "ISS", null, out _));
        Assert.False(policy.TryCreateReport(Rx("G0ABC", Me, "x"), Me, "", null, out _));
    }

    [Fact]
    public void Dedups_same_pair_within_30_seconds()
    {
        var policy = new UhmReportPolicy();
        Assert.True(policy.TryCreateReport(Rx("G0ABC", Me, "JO45", 0), Me, "ISS", null, out _));
        Assert.False(policy.TryCreateReport(Rx("G0ABC", Me, "R73", 20), Me, "ISS", null, out _));
        Assert.True(policy.TryCreateReport(Rx("G0ABC", Me, "73", 31), Me, "ISS", null, out _));
        Assert.True(policy.TryCreateReport(Rx("G0ABC", Me, "73", 32), Me, "IO-117", null, out _));
    }

    [Fact]
    public void Match_tracker_moves_through_states()
    {
        var t = new UhmMatchTracker();
        Assert.Equal(UhmMatchState.NotChecked, t.GetState("G0ABC"));

        t.BeginPolling("G0ABC", T0);
        Assert.Equal(UhmMatchState.Waiting, t.GetState("G0ABC-7"));

        Assert.Equal(UhmMatchState.Waiting, t.ApplyMatch("G0ABC", false, false, T0.AddSeconds(10)));
        Assert.Equal(UhmMatchState.HeardMe, t.ApplyMatch("G0ABC", true, false, T0.AddSeconds(20)));
        Assert.Equal(UhmMatchState.Mutual, t.ApplyMatch("G0ABC", true, true, T0.AddSeconds(30)));
        Assert.Equal(UhmMatchState.Mutual, t.ApplyMatch("G0ABC", false, false, T0.AddSeconds(200)));
        Assert.True(t.IsMutual("G0ABC"));
    }

    [Fact]
    public void Match_tracker_expires_after_window_without_match()
    {
        var t = new UhmMatchTracker();
        t.BeginPolling("OK2ZC", T0);
        Assert.Equal(UhmMatchState.Expired, t.ApplyMatch("OK2ZC", false, false, T0 + UhmMatchTracker.MatchWindow + TimeSpan.FromSeconds(1)));

        t.BeginPolling("OK2ZC", T0.AddMinutes(5));
        Assert.Equal(UhmMatchState.Waiting, t.GetState("OK2ZC"));
    }

    [Fact]
    public void Active_users_strip_ssid()
    {
        var t = new UhmMatchTracker();
        t.SetActiveUsers(["2M0SQL", "g0abc-7"]);
        Assert.True(t.IsActiveUser("G0ABC"));
        Assert.Equal(2, t.ActiveUserCount);
    }
}
