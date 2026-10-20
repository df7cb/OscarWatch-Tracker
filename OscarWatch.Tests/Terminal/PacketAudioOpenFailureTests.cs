using OscarWatch.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class PacketAudioOpenFailureTests
{
    [Fact]
    public void Recognises_the_portaudio_stream_open_message()
    {
        Assert.True(PacketAudioOpenFailure.IsStreamOpenFailure("Error opening PortAudio Stream"));
        Assert.True(PacketAudioOpenFailure.IsStreamOpenFailure(
            new InvalidOperationException("before", new InvalidOperationException("Error opening PortAudio Stream"))));
    }

    [Fact]
    public void Leaves_other_failures_alone()
    {
        Assert.False(PacketAudioOpenFailure.IsStreamOpenFailure("Terminal input soundcard is no longer available."));
        Assert.False(PacketAudioOpenFailure.IsStreamOpenFailure((string?)null));
        Assert.False(PacketAudioOpenFailure.IsStreamOpenFailure(new InvalidOperationException("no device")));
    }
}
