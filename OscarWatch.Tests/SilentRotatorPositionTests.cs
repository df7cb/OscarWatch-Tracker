using OscarWatch.Core.Models;
using OscarWatch.Rotator;

namespace OscarWatch.Tests;

public sealed class SilentRotatorPositionTests
{
    [Fact]
    public void Gs232_stops_querying_after_the_controller_never_answers()
    {
        var transport = new RecordingRotatorSerialTransport();
        using var rotator = new Gs232Rotator(transport);
        rotator.Open();

        Assert.Equal((null, null), rotator.GetPosition());
        var queriesAfterFirstRead = transport.Written.Count;
        Assert.True(queriesAfterFirstRead > 0);

        Assert.Equal((null, null), rotator.GetPosition());
        Assert.Equal(queriesAfterFirstRead, transport.Written.Count);

        rotator.SetPosition(90, 10, new RotatorSettings());
        Assert.Equal("W090 010\r", transport.Written[^1]);
    }

    [Fact]
    public void Gs232_keeps_querying_once_the_controller_has_answered()
    {
        var transport = new RecordingRotatorSerialTransport { NextReadLine = "AZ=120 EL=045" };
        using var rotator = new Gs232Rotator(transport);
        rotator.Open();

        Assert.Equal((120, 45), rotator.GetPosition());
        var queriesAfterFirstRead = transport.Written.Count;

        transport.NextReadLine = null;
        rotator.GetPosition();
        Assert.True(transport.Written.Count > queriesAfterFirstRead);
    }

    [Fact]
    public void EasyComm_stops_querying_after_the_controller_never_answers()
    {
        var transport = new RecordingRotatorSerialTransport();
        using var rotator = new EasyCommRotator(transport);
        rotator.Open();

        Assert.Equal((null, null), rotator.GetPosition());
        var queriesAfterFirstRead = transport.Written.Count;
        Assert.True(queriesAfterFirstRead > 0);

        Assert.Equal((null, null), rotator.GetPosition());
        Assert.Equal(queriesAfterFirstRead, transport.Written.Count);
    }
}
