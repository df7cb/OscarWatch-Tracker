namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Keeps the most recent audio samples for display (waterfall), written by the receive thread and read by the UI.
/// </summary>
public sealed class RecentSampleBuffer
{
    private readonly float[] _ring;
    private readonly object _gate = new();
    private int _next;
    private int _count;

    public RecentSampleBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _ring = new float[capacity];
    }

    public int Capacity => _ring.Length;

    public void Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length > _ring.Length)
            samples = samples[^_ring.Length..];

        lock (_gate)
        {
            var first = Math.Min(samples.Length, _ring.Length - _next);
            samples[..first].CopyTo(_ring.AsSpan(_next));
            samples[first..].CopyTo(_ring);
            _next = (_next + samples.Length) % _ring.Length;
            _count = Math.Min(_ring.Length, _count + samples.Length);
        }
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length of the newest samples, oldest first.</summary>
    public int CopyNewest(Span<float> destination)
    {
        lock (_gate)
        {
            var n = Math.Min(destination.Length, _count);
            var start = (_next - n + _ring.Length) % _ring.Length;
            var first = Math.Min(n, _ring.Length - start);
            _ring.AsSpan(start, first).CopyTo(destination);
            _ring.AsSpan(0, n - first).CopyTo(destination[first..]);
            return n;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _next = 0;
            _count = 0;
        }
    }
}
