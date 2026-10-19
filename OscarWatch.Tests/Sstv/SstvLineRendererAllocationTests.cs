using OscarWatch.Core.Sstv;

namespace OscarWatch.Tests.Sstv;

/// <summary>
/// The line renderer reuses per-channel scratch buffers across lines instead of allocating
/// a <c>double[width]</c> per channel per line. These tests guard that the output is
/// unchanged and that the per-line allocation really is gone.
/// </summary>
public sealed class SstvLineRendererAllocationTests
{
    private const int Rate = SstvDecoder.InternalRate;

    // Robot 36 keeps per-line arrays for cross-line chroma pairing, so it still allocates
    // fresh arrays (its behaviour is unchanged) and cannot be rendered a line at a time by a
    // throwaway renderer. The round-trip tests cover it; these modes exercise buffer reuse.
    public static TheoryData<SstvModeId> StatelessModes()
    {
        var data = new TheoryData<SstvModeId>();
        foreach (var mode in SstvModeTable.All)
        {
            if (mode.Id != SstvModeId.Robot36)
                data.Add(mode.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StatelessModes))]
    public void Reusing_scratch_buffers_matches_a_fresh_buffer_per_line(SstvModeId id)
    {
        var mode = SstvModeTable.Get(id);
        var buffer = SweepBuffer(mode);
        var samplesPerMs = Rate / 1000.0;
        var lineSamples = mode.LineMs * samplesPerMs;

        // Reference: a brand-new renderer+image per line, so no scratch is ever reused.
        // Valid only for modes that render a line without state from earlier lines.
        var reference = new SstvImage(mode.Width, mode.Height);
        for (var line = 0; line < mode.TransmittedLines; line++)
        {
            var fresh = new SstvLineRenderer(mode, reference);
            fresh.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);
        }

        // Under test: one renderer reusing its scratch buffers across every line.
        var actual = new SstvImage(mode.Width, mode.Height);
        var renderer = new SstvLineRenderer(mode, actual);
        for (var line = 0; line < mode.TransmittedLines; line++)
            renderer.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);

        Assert.Equal(reference.Rgb, actual.Rgb);
    }

    /// <summary>
    /// Robot 36 is stateful across lines (even lines carry R-Y, odd lines B-Y, paired across
    /// line boundaries), so it keeps its own per-line arrays and is excluded from the stateless
    /// exact-equality reuse test above. It therefore has no exact-equality guard, only the
    /// tolerance-based round-trip tests. This renders the same signal twice with two separate
    /// renderers and asserts byte-identical RGB output, proving the rendering is deterministic
    /// and leaks no state between renderer instances.
    /// </summary>
    [Fact]
    public void Robot36_renders_deterministically_across_renderers()
    {
        var mode = SstvModeTable.Robot36;
        var buffer = SweepBuffer(mode);
        var samplesPerMs = Rate / 1000.0;
        var lineSamples = mode.LineMs * samplesPerMs;

        var first = new SstvImage(mode.Width, mode.Height);
        var firstRenderer = new SstvLineRenderer(mode, first);
        for (var line = 0; line < mode.TransmittedLines; line++)
            firstRenderer.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);

        var second = new SstvImage(mode.Width, mode.Height);
        var secondRenderer = new SstvLineRenderer(mode, second);
        for (var line = 0; line < mode.TransmittedLines; line++)
            secondRenderer.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);

        Assert.Equal(first.Rgb, second.Rgb);
    }

    [Theory]
    [InlineData(SstvModeId.Pd120)]
    [InlineData(SstvModeId.ScottieS1)]
    [InlineData(SstvModeId.MartinM1)]
    public void Rendering_more_lines_does_not_allocate_per_line(SstvModeId id)
    {
        var mode = SstvModeTable.Get(id);
        var buffer = SweepBuffer(mode);
        var samplesPerMs = Rate / 1000.0;
        var lineSamples = mode.LineMs * samplesPerMs;

        var image = new SstvImage(mode.Width, mode.Height);
        var renderer = new SstvLineRenderer(mode, image);

        // Warm up: first lines allocate the scratch buffers once.
        for (var line = 0; line < 8; line++)
            renderer.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);

        // A single sample is not enough. GetAllocatedBytesForCurrentThread also counts
        // one-off runtime allocations, such as a GC refilling this thread's allocation
        // context, and the full suite can trip that during the measured lines. A real
        // scratch array is allocated on every pass, so the quietest pass is the one
        // that reflects the renderer.
        var allocated = long.MaxValue;
        for (var pass = 0; pass < 5; pass++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var line = 8; line < mode.TransmittedLines; line++)
                renderer.Render(buffer, buffer.Start + line * lineSamples, samplesPerMs, 0, line);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            if (allocated == 0)
                break;
        }

        // Scratch is reused, so a quiet pass allocates nothing.
        // A single double[width] would be 8*width bytes; stay well under one such array.
        Assert.True(
            allocated < mode.Width * 8,
            $"{mode.Name}: {allocated} bytes allocated rendering {mode.TransmittedLines - 8} lines");
    }

    private static SstvSignalBuffer SweepBuffer(SstvMode mode)
    {
        var samplesPerMs = Rate / 1000.0;
        var total = (long)(mode.LineMs * samplesPerMs) * mode.TransmittedLines + Rate;
        var buffer = new SstvSignalBuffer();
        var block = new float[4096];
        var phase = 0.0;
        long written = 0;
        while (written < total)
        {
            var n = (int)Math.Min(block.Length, total - written);
            for (var i = 0; i < n; i++)
            {
                phase += 0.0007;
                block[i] = (float)(1900 + 400 * Math.Sin(phase));
            }

            if (n == block.Length)
            {
                buffer.Append(block);
            }
            else
            {
                var tail = new float[n];
                Array.Copy(block, tail, n);
                buffer.Append(tail);
            }

            written += n;
        }

        return buffer;
    }
}
