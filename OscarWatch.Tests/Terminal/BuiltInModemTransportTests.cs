using OscarWatch.Core.Hardware;
using OscarWatch.Core.Terminal;
using OscarWatch.Core.Terminal.Modem;
using OscarWatch.Hardware;
using OscarWatch.Terminal;
using OscarWatch.Tests.Hardware;
using OscarWatch.Tests.Terminal.Modem;

namespace OscarWatch.Tests.Terminal;

public sealed class BuiltInModemTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(Action<TerminalSettings>? configure = null)
        {
            Settings.Transport = TerminalTransportKind.BuiltIn;
            Settings.ModemTxDelayMs = 50;
            Settings.ModemTxTailMs = 20;
            Settings.BuiltIn.PttMethod = PttMethod.Cat;
            Settings.BuiltIn.Duplex = TerminalDuplexMode.Full;
            configure?.Invoke(Settings);
            Settings.Normalise();
            Ptt = new PttKeyer(Rig, () => Settings.BuiltIn.ToPttOptions(), "Terminal");
            Modem = new BuiltInModemTransport(Audio, Ptt, () => Settings, Rig, new Random(1));
            Modem.FrameReceived += f =>
            {
                lock (Received)
                    Received.Add(f);
            };
        }

        public TerminalSettings Settings { get; } = new();
        public RecordingRigController Rig { get; } = new();
        public FakePacketAudio Audio { get; } = new();
        public PttKeyer Ptt { get; }
        public BuiltInModemTransport Modem { get; }
        public List<TerminalReceivedFrame> Received { get; } = [];

        public Task StartAsync() => Modem.ConnectAsync(CancellationToken.None);

        public int KeyCount => Rig.Calls.Count(c => c == "ptt:on");

        public ValueTask DisposeAsync() => Modem.DisposeAsync();
    }

    [Theory]
    [InlineData(TerminalDuplexMode.Auto, TerminalLinkType.Simplex, true)]
    [InlineData(TerminalDuplexMode.Auto, TerminalLinkType.SameBand, true)]
    [InlineData(TerminalDuplexMode.Auto, TerminalLinkType.Crossband, false)]
    [InlineData(TerminalDuplexMode.Auto, TerminalLinkType.Unknown, true)]
    [InlineData(TerminalDuplexMode.Full, TerminalLinkType.Simplex, false)]
    [InlineData(TerminalDuplexMode.Half, TerminalLinkType.Crossband, true)]
    public void Auto_duplex_follows_the_link_type(TerminalDuplexMode mode, TerminalLinkType link, bool half) =>
        Assert.Equal(half, BuiltInModemTransport.ResolveHalfDuplex(mode, link));

    [Fact]
    public async Task Start_sends_cat_ptt_off_before_anything_else()
    {
        await using var h = new Harness();
        await h.StartAsync();

        Assert.True(h.Audio.Started);
        Assert.Equal(["ptt:off"], h.Rig.Calls);
        Assert.Equal("OscarWatch 1200 AFSK, full duplex", h.Modem.ModemDescription);
    }

    [Fact]
    public async Task Rig_reconnect_sends_cat_ptt_off_again()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Rig.Connected = false;
        await Task.Delay(1300);
        h.Rig.Connected = true;

        await WaitUntilAsync(() => h.Rig.Calls.Count(c => c == "ptt:off") >= 2);
        Assert.Equal(0, h.KeyCount);
    }

    [Fact]
    public async Task A_frame_is_keyed_played_and_decodable()
    {
        await using var h = new Harness();
        await h.StartAsync();
        var frame = ModemTestSignals.UiFrame(":MM9SQL   :hello");

        await h.Modem.SendAsync(frame, 0, CancellationToken.None).WaitAsync(Timeout);

        Assert.Equal(["ptt:off", "ptt:on", "ptt:off"], h.Rig.Calls);
        var decoded = AfskReceiver.DecodeAll(h.Audio.Played, h.Audio.PlaybackSampleRate, AfskBitFixMode.Off);
        Assert.Equal(frame, Assert.Single(decoded).Ax25);
    }

    [Fact]
    public async Task Frames_queued_while_keyed_are_chained_into_one_transmission()
    {
        await using var h = new Harness();
        await h.StartAsync();
        var first = ModemTestSignals.UiFrame(":MM9SQL   :first frame of the chain");
        var second = ModemTestSignals.UiFrame(":MM9SQL   :second frame");

        var sendFirst = h.Modem.SendAsync(first, 0, CancellationToken.None);
        await WaitUntilAsync(() => h.Modem.IsTransmitting);
        await Task.Delay(80);
        var sendSecond = h.Modem.SendAsync(second, 0, CancellationToken.None);
        await Task.WhenAll(sendFirst, sendSecond).WaitAsync(Timeout);

        Assert.Equal(1, h.KeyCount);
        var decoded = AfskReceiver.DecodeAll(h.Audio.Played, h.Audio.PlaybackSampleRate, AfskBitFixMode.Off);
        Assert.Equal([first, second], decoded.Select(d => d.Ax25).ToList());
    }

    [Fact]
    public async Task Failed_uplink_hold_means_no_transmission()
    {
        await using var h = new Harness();
        var pttCallsAtHold = -1;
        h.Modem.BeforeKey = (_, _) =>
        {
            pttCallsAtHold = h.KeyCount;
            return Task.FromResult(false);
        };
        await h.StartAsync();

        var ex = await Assert.ThrowsAsync<TerminalTransmitException>(() =>
            h.Modem.SendAsync(ModemTestSignals.UiFrame("x"), 0, CancellationToken.None).WaitAsync(Timeout));

        Assert.Equal(TerminalTransmitFailure.UplinkHoldFailed, ex.Failure);
        Assert.Equal(0, pttCallsAtHold);
        Assert.Equal(0, h.KeyCount);
        Assert.Empty(h.Audio.Played);
    }

    [Fact]
    public async Task Before_key_runs_before_ptt_and_gets_the_airtime()
    {
        await using var h = new Harness();
        var holds = new List<(TimeSpan Airtime, int Keys)>();
        h.Modem.BeforeKey = (airtime, _) =>
        {
            holds.Add((airtime, h.KeyCount));
            return Task.FromResult(true);
        };
        await h.StartAsync();

        await h.Modem.SendAsync(ModemTestSignals.UiFrame("hello"), 0, CancellationToken.None).WaitAsync(Timeout);

        var (airtime, keys) = Assert.Single(holds);
        Assert.Equal(0, keys);
        Assert.InRange(airtime.TotalMilliseconds, 200, 600);
        Assert.Equal(1, h.KeyCount);
    }

    [Fact]
    public async Task Watchdog_drops_ptt_on_an_over_long_transmission()
    {
        await using var h = new Harness(s => s.BuiltIn.WatchdogSeconds = 1);
        await h.StartAsync();
        var huge = ModemTestSignals.UiFrame(new string('A', 250));

        var ex = await Assert.ThrowsAsync<TerminalTransmitException>(() =>
            h.Modem.SendAsync(huge, 0, CancellationToken.None).WaitAsync(Timeout));

        Assert.Equal(TerminalTransmitFailure.Watchdog, ex.Failure);
        Assert.Equal("ptt:off", h.Rig.Calls[^1]);
        Assert.False(h.Ptt.IsKeyed);
        Assert.True(h.Audio.ClearCount > 0);
    }

    [Fact]
    public async Task Half_duplex_waits_for_a_busy_channel_then_gives_up()
    {
        await using var h = new Harness(s =>
        {
            s.BuiltIn.Duplex = TerminalDuplexMode.Half;
            s.BuiltIn.MaxChannelWaitMs = 400;
        });
        await h.StartAsync();

        using var feeding = new CancellationTokenSource();
        var busy = FeedFlagsAsync(h.Audio, feeding.Token);
        await WaitUntilAsync(() => h.Modem.Dcd);

        var ex = await Assert.ThrowsAsync<TerminalTransmitException>(() =>
            h.Modem.SendAsync(ModemTestSignals.UiFrame("x"), 0, CancellationToken.None).WaitAsync(Timeout));
        feeding.Cancel();
        await busy;

        Assert.Equal(TerminalTransmitFailure.ChannelBusy, ex.Failure);
        Assert.Equal(0, h.KeyCount);
    }

    [Fact]
    public async Task Half_duplex_blanks_receive_while_keyed_and_hears_the_reply_after_the_guard()
    {
        await using var h = new Harness(s =>
        {
            s.BuiltIn.Duplex = TerminalDuplexMode.Half;
            s.BuiltIn.TurnaroundGuardMs = 150;
        });
        await h.StartAsync();
        var duringTx = ModemTestSignals.UiFrame(":MM9SQL   :heard while keyed", source: "G4ABC");
        var afterTx = ModemTestSignals.UiFrame(":MM9SQL   :digipeated reply", source: "G4XYZ");

        var send = h.Modem.SendAsync(ModemTestSignals.UiFrame("my frame"), 0, CancellationToken.None);
        await WaitUntilAsync(() => h.Modem.IsTransmitting);
        h.Audio.Feed(ModemTestSignals.Transmission([duringTx], h.Audio.CaptureSampleRate));
        await send.WaitAsync(Timeout);
        await Task.Delay(250);
        h.Audio.Feed(ModemTestSignals.Transmission([afterTx], h.Audio.CaptureSampleRate));
        await WaitUntilAsync(() =>
        {
            lock (h.Received)
                return h.Received.Count > 0;
        });
        await Task.Delay(200);

        lock (h.Received)
            Assert.Equal(afterTx, Assert.Single(h.Received).Ax25);
    }

    [Fact]
    public async Task Full_duplex_keeps_receiving_while_keyed()
    {
        await using var h = new Harness();
        await h.StartAsync();
        var heard = ModemTestSignals.UiFrame(":MM9SQL   :own digipeat", source: "G4ABC");

        var send = h.Modem.SendAsync(ModemTestSignals.UiFrame(new string('B', 120)), 0, CancellationToken.None);
        await WaitUntilAsync(() => h.Modem.IsTransmitting);
        h.Audio.Feed(ModemTestSignals.Transmission([heard], h.Audio.CaptureSampleRate));
        await WaitUntilAsync(() =>
        {
            lock (h.Received)
                return h.Received.Count > 0;
        });
        var stillKeyed = h.Modem.IsTransmitting;
        await send.WaitAsync(Timeout);

        Assert.True(stillKeyed);
        lock (h.Received)
        {
            var frame = Assert.Single(h.Received);
            Assert.Equal(heard, frame.Ax25);
            Assert.Equal(0, frame.RadioPort);
            Assert.NotNull(frame.Quality);
        }
    }

    private static async Task FeedFlagsAsync(FakePacketAudio audio, CancellationToken ct)
    {
        var modulator = new AfskStreamModulator(audio.CaptureSampleRate, 0.5);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                audio.Feed(modulator.Append(HdlcEncoder.FlagBits(15)));
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task A_silent_capture_stream_is_reopened()
    {
        await using var h = new Harness();
        h.Modem.CaptureStallTimeout = TimeSpan.FromMilliseconds(300);
        await h.StartAsync();

        await WaitUntilAsync(() => h.Audio.StartCount >= 2);
        Assert.True(h.Modem.CaptureRestarts >= 1);
    }

    [Fact]
    public async Task Flowing_capture_is_not_reopened()
    {
        await using var h = new Harness();
        h.Modem.CaptureStallTimeout = TimeSpan.FromMilliseconds(400);
        await h.StartAsync();

        var chunk = new float[h.Audio.CaptureSampleRate / 20];
        for (var i = 0; i < 20; i++)
        {
            h.Audio.Feed(chunk);
            await Task.Delay(50);
        }

        Assert.Equal(1, h.Audio.StartCount);
        Assert.Equal(0, h.Modem.CaptureRestarts);
    }

    [Fact]
    public async Task Waterfall_spectrum_peaks_at_the_received_tone()
    {
        await using var h = new Harness();
        var bins = new float[271];
        Assert.False(h.Modem.TryBuildSpectrum(bins, 300, 3000));

        await h.StartAsync();
        var tone = new float[h.Audio.CaptureSampleRate / 2];
        for (var i = 0; i < tone.Length; i++)
            tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 1200 * i / h.Audio.CaptureSampleRate);
        h.Audio.Feed(tone);

        await WaitUntilAsync(() => h.Modem.TryBuildSpectrum(bins, 300, 3000));
        var peak = Array.IndexOf(bins, bins.Max());
        var peakHz = 300 + (3000 - 300) * peak / (double)(bins.Length - 1);
        Assert.InRange(peakHz, 1170, 1230);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met");
            await Task.Delay(5);
        }
    }
}
