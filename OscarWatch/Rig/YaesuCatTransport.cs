using System.IO.Ports;
using Serilog;

namespace OscarWatch.Rig;

/// <summary>FT-847 CAT serial link: 8 data bits, 2 stop bits, five-byte frames.</summary>
internal sealed class YaesuCatTransport : IYaesuCatTransport
{
    private static readonly ILogger Log = Serilog.Log.ForContext<YaesuCatTransport>();
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public YaesuCatTransport(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.Two)
        {
            Handshake = Handshake.None,
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            DtrEnable = false,
            RtsEnable = false
        };
    }

    public bool IsOpen => _port.IsOpen;

    public void Open()
    {
        if (!_port.IsOpen)
            _port.Open();
    }

    public bool SendFrame(ReadOnlySpan<byte> frame, int postDelayMs = 50)
    {
        if (frame.Length != 5)
            return false;

        _gate.Wait();
        try
        {
            if (!_port.IsOpen)
                return false;

            _port.DiscardInBuffer();
            _port.Write(frame.ToArray(), 0, frame.Length);
            Thread.Sleep(postDelayMs);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Yaesu CAT write failed on {Port}", _port.PortName);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public byte[]? QueryFrame(ReadOnlySpan<byte> pollFrame, int postDelayMs = 50)
    {
        if (pollFrame.Length != 5)
            return null;

        _gate.Wait();
        try
        {
            if (!_port.IsOpen)
                return null;

            _port.DiscardInBuffer();
            _port.Write(pollFrame.ToArray(), 0, pollFrame.Length);
            Thread.Sleep(postDelayMs);

            var buffer = new byte[5];
            var read = 0;
            try
            {
                while (read < 5)
                {
                    var b = _port.ReadByte();
                    if (b < 0)
                        break;
                    buffer[read++] = (byte)b;
                }
            }
            catch (TimeoutException)
            {
                while (read < 5 && _port.BytesToRead > 0)
                {
                    var b = _port.ReadByte();
                    if (b >= 0)
                        buffer[read++] = (byte)b;
                }
            }

            return read == 5 ? buffer : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Yaesu CAT query failed on {Port}", _port.PortName);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetHandshakeLine(bool useRts, bool assert)
    {
        if (!_port.IsOpen)
            return;

        if (useRts)
            _port.RtsEnable = assert;
        else
            _port.DtrEnable = assert;
    }

    public void Dispose()
    {
        try
        {
            if (_port.IsOpen)
                _port.Close();
        }
        catch
        {
            // ignore
        }

        _port.Dispose();
        _gate.Dispose();
    }
}
