using OscarWatch.Recording;

namespace OscarWatch.Tests;

public sealed class PortAudioRuntimeTests
{
    [Fact]
    public void TryRefreshHost_skips_when_a_stream_is_registered()
    {
        PortAudioRuntime.NotifyStreamOpened();
        try
        {
            Assert.False(PortAudioRuntime.TryRefreshHost());
        }
        finally
        {
            PortAudioRuntime.NotifyStreamClosed();
        }
    }
}
