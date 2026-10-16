using System.Runtime.InteropServices;

namespace OscarWatch.Core.Terminal.Modem;

/// <summary>
/// Complete 9600 baud G3RUH receiver. Resamples to 48 kHz when needed, runs dual-polarity decoders,
/// checks FCS, merges paths, and optionally repairs frames.
/// </summary>
public sealed class G3ruhReceiver
{
    private const int ClusterSamples = 960;
    private const int DuplicateSamples = G3ruhDemodulator.PreferredSampleRate;
    private const int DcdHoldSamples = G3ruhDemodulator.PreferredSampleRate / 10;
    private const int MaxFixAttemptsPerCluster = 6;

    private readonly PolyphaseResampler? _resampler;
    private readonly G3ruhDecoderBank _bank;
    private readonly List<float> _resampled = new(4096);
    private readonly List<G3ruhCandidate> _candidates = new();
    private readonly List<(ulong Hash, long Sample)> _recent = new();
    private readonly int[] _pathCounts;
    private long _lastDcdSample = long.MinValue / 2;
    private float _peak;
    private bool _blanked;

    public G3ruhReceiver(int inputSampleRate, AfskBitFixMode bitFix = AfskBitFixMode.SingleBit)
    {
        if (inputSampleRate < G3ruhDemodulator.MinSampleRate)
            throw new ArgumentOutOfRangeException(nameof(inputSampleRate),
                $"9600 baud G3RUH needs capture at least {G3ruhDemodulator.MinSampleRate} Hz.");

        InputSampleRate = inputSampleRate;
        var workRate = G3ruhDemodulator.PreferredSampleRate;
        if (inputSampleRate != workRate)
            _resampler = new PolyphaseResampler(inputSampleRate, workRate);
        _bank = new G3ruhDecoderBank(workRate);
        BitFix = bitFix;
        _pathCounts = new int[_bank.PathCount];
    }

    public int InputSampleRate { get; }
    public AfskBitFixMode BitFix { get; set; }
    public int PathCount => _bank.PathCount;
    public string PathName(int path) => _bank.PathName(path);
    public int PathDecodeCount(int path) => _pathCounts[path];
    public int FrameCount { get; private set; }
    public int FixedCount { get; private set; }
    public long Sample => _bank.Sample;
    public bool Dcd => _bank.Sample - _lastDcdSample <= DcdHoldSamples;
    public float InputPeak => _peak;

    public bool Blanked
    {
        get => _blanked;
        set
        {
            if (_blanked == value)
                return;
            _blanked = value;
            if (!value)
                _bank.Reset();
        }
    }

    public void Reset()
    {
        _resampler?.Reset();
        _bank.Reset();
        _candidates.Clear();
    }

    public void Process(ReadOnlySpan<float> input, List<AfskDecodedFrame> output)
    {
        var peak = _peak;
        foreach (var s in input)
        {
            var a = Math.Abs(s);
            peak = a > peak ? a : peak * 0.99995f;
        }

        _peak = peak;

        var work = input;
        if (_resampler is not null)
        {
            _resampled.Clear();
            _resampler.Process(input, _resampled);
            work = CollectionsMarshal.AsSpan(_resampled);
        }

        if (_blanked)
        {
            _bank.Skip(work.Length);
        }
        else
        {
            foreach (var s in work)
            {
                _bank.Process(s, _candidates);
                if (_bank.Dcd)
                    _lastDcdSample = _bank.Sample;
            }
        }

        Resolve(output, flush: false);
    }

    public void Flush(List<AfskDecodedFrame> output) => Resolve(output, flush: true);

    public static List<AfskDecodedFrame> DecodeAll(
        ReadOnlySpan<float> samples,
        int sampleRate,
        AfskBitFixMode bitFix = AfskBitFixMode.SingleBit)
    {
        var receiver = new G3ruhReceiver(sampleRate, bitFix);
        var frames = new List<AfskDecodedFrame>();
        const int block = 4096;
        for (var i = 0; i < samples.Length; i += block)
            receiver.Process(samples.Slice(i, Math.Min(block, samples.Length - i)), frames);

        receiver.Process(new float[sampleRate / 20], frames);
        receiver.Flush(frames);
        return frames;
    }

    private void Resolve(List<AfskDecodedFrame> output, bool flush)
    {
        var now = _bank.Sample;
        _recent.RemoveAll(r => now - r.Sample > DuplicateSamples);

        while (_candidates.Count > 0)
        {
            var first = _candidates[0].Sample;
            if (!flush && now - first <= ClusterSamples * 2)
                break;

            var end = 0;
            while (end < _candidates.Count && _candidates[end].Sample - first <= ClusterSamples)
                end++;

            ResolveCluster(_candidates.GetRange(0, end), output);
            _candidates.RemoveRange(0, end);
        }
    }

    private void ResolveCluster(List<G3ruhCandidate> cluster, List<AfskDecodedFrame> output)
    {
        var good = new List<(ulong Hash, byte[] Ax25, long Sample, List<int> Paths)>();
        foreach (var c in cluster)
        {
            if (!Fcs16.Check(c.FrameWithFcs))
                continue;
            var ax25 = c.FrameWithFcs.AsSpan(0, c.FrameWithFcs.Length - 2);
            if (!Ax25Frame.TryParse(ax25, out _))
                continue;

            var hash = Hash(ax25);
            var existing = good.FindIndex(g => g.Hash == hash);
            if (existing >= 0)
            {
                if (!good[existing].Paths.Contains(c.Path))
                    good[existing].Paths.Add(c.Path);
            }
            else
            {
                good.Add((hash, ax25.ToArray(), c.Sample, [c.Path]));
            }
        }

        if (good.Count > 0)
        {
            foreach (var g in good)
            {
                if (IsRecent(g.Hash))
                    continue;
                _recent.Add((g.Hash, g.Sample));
                foreach (var p in g.Paths)
                    _pathCounts[p]++;
                FrameCount++;
                var quality = (int)Math.Round(100.0 * g.Paths.Count / _bank.PathCount);
                output.Add(new AfskDecodedFrame(g.Ax25, g.Sample, _bank.PathName(g.Paths[0]), quality, Fixed: false));
            }

            return;
        }

        if (BitFix == AfskBitFixMode.Off)
            return;

        var tried = new HashSet<ulong>();
        foreach (var c in cluster)
        {
            if (tried.Count >= MaxFixAttemptsPerCluster)
                break;
            if (!tried.Add(Hash(c.FrameWithFcs)))
                continue;
            if (FrameBitFixer.TryFix(c.FrameWithFcs, BitFix) is not { } repaired)
                continue;

            var hash = Hash(repaired);
            if (IsRecent(hash))
                return;
            _recent.Add((hash, c.Sample));
            _pathCounts[c.Path]++;
            FrameCount++;
            FixedCount++;
            output.Add(new AfskDecodedFrame(repaired, c.Sample, _bank.PathName(c.Path) + " fixed", 0, Fixed: true));
            return;
        }
    }

    private bool IsRecent(ulong hash)
    {
        foreach (var r in _recent)
        {
            if (r.Hash == hash)
                return true;
        }

        return false;
    }

    private static ulong Hash(ReadOnlySpan<byte> data)
    {
        var h = 14695981039346656037UL;
        foreach (var b in data)
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        return h ^ (ulong)data.Length;
    }
}
