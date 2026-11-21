// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Capture;

/// <summary>What <see cref="ScrollStitcher.Add"/> did with a frame.</summary>
public enum ScrollStatus
{
    /// <summary>Stitched: the first frame, or new rows appended (<see cref="ScrollStep.Shift"/> of them).</summary>
    Accepted,

    /// <summary>Nothing to add: the frame matches the last one used, or the page moved back up (a negative shift).</summary>
    NoMove,

    /// <summary>Refused: the frame has no single best overlap (blank, repeating, torn or changed), so the reference stays.</summary>
    Ambiguous,

    /// <summary>Refused: the frame has content but no overlap with the last one used ("Too fast").</summary>
    Gap,

    /// <summary>The size limit was reached; what fits was kept and later frames are ignored.</summary>
    Limit,
}

/// <summary>The outcome of one frame. <paramref name="Shift"/> is the rows appended for Accepted, the signed shift for NoMove (0 or negative), else 0.</summary>
public readonly record struct ScrollStep(ScrollStatus Status, int Shift, int Height);

/// <summary>The stitched size limits (docs/phase-7/scrolling-capture.md, Limits).</summary>
public sealed record ScrollLimits(int MaxHeight = 30_000, long MaxPixels = 80_000_000)
{
    public static ScrollLimits Default { get; } = new();
}

/// <summary>The fixed parts found on the first pair of frames that moved: sticky rows at the top and bottom, fixed columns at the sides.</summary>
public readonly record struct ScrollLayout(int Header, int Footer, int Left, int Right);

/// <summary>
/// Stitches frames of one area, taken while its content scrolls, into one tall picture, row for row, or refuses a frame. It
/// never guesses: a frame with no single overlap that matches the last frame used leaves the result alone. Pure and
/// WPF-free (docs/phase-7/scrolling-capture.md, The stitcher).
/// <para>
/// Frames are BGRA of the area, top row first. The first frame is the top of the result. Each later frame is matched to the
/// reference (the furthest frame used): a positive shift means the content moved up, so the frame's bottom rows are new;
/// a shift of 0 or less adds nothing and the reference doesn't advance. A controller that knows how far it scrolled (Auto)
/// passes <c>predictedShift</c> and the search narrows to ±<see cref="PredictionSlack"/> rows around it.
/// </para>
/// <para>
/// Not thread-safe. Memory is the appended strips (kept as strips, never one block until <see cref="Compose"/>) plus two
/// frame copies.
/// </para>
/// </summary>
public sealed class ScrollStitcher
{
    /// <summary>Rows around a predicted shift that are searched.</summary>
    public const int PredictionSlack = 12;

    private const int Bins = 48;
    private const int Stripes = 6;
    private const int InfoRange = 24;
    private const int MinInformative = 8;
    private const ulong Seed = 14695981039346656037UL;

    private readonly int _w;
    private readonly int _fh;
    private readonly int _rb;
    private readonly ScrollLimits _limits;

    private byte[]? _first;
    private byte[]? _ref;
    private byte[]? _scratch;

    private bool _committed;
    private ScrollLayout _layout;
    private uint[] _fill = [];

    private readonly List<byte[]> _strips = [];
    private readonly List<int> _stripStart = [];
    private readonly List<int> _seams = [];
    private int _appended;
    private byte[] _footer = [];
    private int _viewDelta;
    private bool _limited;
    private int _frames;

    // Reference signatures, for the layout they were taken with.
    private RowSig? _refSig;
    private ScrollLayout _refSigLayout;
    private RowIndex? _refIndex;
    private RowSig? _refStripe;
    private RowIndex[]? _refStripeIndex;

    public ScrollStitcher(int width, int frameHeight, ScrollLimits? limits = null)
    {
        if (width < 1 || frameHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "The area has no size.");
        _w = width;
        _fh = frameHeight;
        _rb = width * 4;
        _limits = limits ?? ScrollLimits.Default;
        Status = ScrollStatus.Accepted;
    }

    /// <summary>Rows at the end of the result (above the footer) that no later frame has vouched for yet; the next frame may replace them.</summary>
    public int ProvisionalRows => _committed && _verifiedTo < _fh - _layout.Header - _layout.Footer ? _fh - _layout.Header - _layout.Footer - _verifiedTo : 0;

    public int Width => _w;

    /// <summary>
    /// The share of the overlap pixels that may differ strongly at the accepted shift (a caret, a small animation). Kept
    /// small on purpose: a repeating page that differs only by line numbers differs in a few percent, and must be refused.
    /// </summary>
    public double MismatchLimit { get; set; } = 0.01;

    /// <summary>The area's height, the height of every frame.</summary>
    public int FrameHeight => _fh;

    /// <summary>The result's height so far, 0 before the first frame.</summary>
    public int Height => _first is null ? 0 : _fh + _appended;

    /// <summary>The last frame's outcome.</summary>
    public ScrollStatus Status { get; private set; }

    /// <summary>Frames that were stitched (the first included).</summary>
    public int Accepted => _frames;

    /// <summary>True once the size limit stopped the capture.</summary>
    public bool LimitReached => _limited;

    /// <summary>True when the last frame was refused for having no overlap, or an unreadable one ("Too fast").</summary>
    public bool Lost => Status is ScrollStatus.Gap or ScrollStatus.Ambiguous;

    /// <summary>The fixed parts, known after the first frame that moved; all 0 before.</summary>
    public ScrollLayout Layout => _committed ? _layout : default;

    /// <summary>Result rows where an appended strip starts, in order, for the preview's seams.</summary>
    public IReadOnlyList<int> Seams => _seams;

    /// <summary>The result row where the live area's top is now (it can be above the newest rows after scrolling back up).</summary>
    public int ViewportTop => _appended + _viewDelta;

    /// <summary>The result row just below the stitched rows' body, where a gap shows: the footer's first row.</summary>
    public int GapRow => Height - (_committed ? _layout.Footer : 0);

    /// <summary>Feeds a tight frame (<see cref="Width"/> × <see cref="FrameHeight"/>, stride width × 4).</summary>
    public ScrollStep Add(byte[] frame, int? predictedShift = null) => AddRows(frame, _rb, predictedShift);

    /// <summary>Feeds a frame whose rows are <paramref name="stride"/> bytes apart.</summary>
    public ScrollStep AddRows(ReadOnlySpan<byte> bgra, int stride, int? predictedShift = null)
    {
        if (stride < _rb || bgra.Length < (long)stride * (_fh - 1) + _rb)
            throw new ArgumentException("The frame doesn't match the area's size.", nameof(bgra));
        ReadOnlySpan<byte> cur;
        if (stride == _rb)
            cur = bgra[..(_rb * _fh)];
        else
        {
            _scratch ??= new byte[_rb * _fh];
            for (int y = 0; y < _fh; y++)
                bgra.Slice(y * stride, _rb).CopyTo(_scratch.AsSpan(y * _rb, _rb));
            cur = _scratch;
        }
        var step = Process(cur, predictedShift);
        Status = step.Status;
        return step;
    }

    private ScrollStep Process(ReadOnlySpan<byte> cur, int? p)
    {
        if (_limited)
            return Step(ScrollStatus.Limit, 0);
        if (_ref is null)
        {
            _first = cur.ToArray();
            _ref = _first;
            _frames = 1;
            return Step(ScrollStatus.Accepted, 0);
        }

        // A frame the same as a held candidate (see Hold) settles it: the picture stopped there.
        int held = _committed ? HeldEqual(cur) : -1;
        if (held >= 0)
            return CommitCandidate(held);

        // A frame identical to the last one. A controller that expects movement over a blank frame is the one case where
        // "identical" can still mean "scrolled".
        if (NearlyEqual(_ref, cur, _w * _fh) &&
            (p is null || Math.Abs(p.Value) <= PredictionSlack || InformativeRows(_ref) >= MinInformative))
        {
            if (_committed && _verifiedTo < _fh - _layout.Header - _layout.Footer && !_ref.AsSpan().SequenceEqual(cur))
                Hold(cur, 0); // the same place, a few pixels different: the settled picture of a provisional tail, or a tear
            return Step(ScrollStatus.NoMove, 0, 0);
        }

        ScrollLayout lay;
        int raw = 0;
        if (_committed)
            lay = _layout;
        else
        {
            var found = EstimateRaw(cur, p);
            if (found is null)
                return Step(_curStripeInfo >= MinInformative ? ScrollStatus.Gap : ScrollStatus.Ambiguous, 0);
            raw = found.Value;
            lay = AnalyzeLayout(_ref, cur, raw) ?? default;
        }
        _useCap = false;
        var m = Match(lay, cur, p);
        if (m.Status == ScrollStatus.Accepted && _committed && _cands.Count > 0 && m.D >= 0 && _verifiedTo < _fh - lay.Header - lay.Footer &&
            _verifiedTo - m.D < Math.Max((_fh - lay.Header - lay.Footer) / 10, 16))
            m = new MatchResult(ScrollStatus.Ambiguous, 0); // the overlap lies in rows nothing has vouched for: it vouches for nothing
        if (_cands.Count > 0 && _committed)
        {
            // Frames before this one were held: when this one fits one of them with more rows in common than it fits the
            // reference, that one was a true picture. (A torn frame fits the reference as well as its clean picture does.)
            int band0 = _fh - lay.Header - lay.Footer;
            int bestI = -1, bestOverlap = m.Status == ScrollStatus.Accepted ? band0 - Math.Abs(m.D) : 0;
            for (int i = 0; i < _cands.Count; i++)
            {
                var mc = MatchAgainstCandidate(i, lay, cur, p);
                if (mc.Status == ScrollStatus.Accepted && band0 - Math.Abs(mc.D) > bestOverlap)
                {
                    bestI = i;
                    bestOverlap = band0 - Math.Abs(mc.D);
                }
            }
            if (bestI >= 0)
            {
                CommitCandidate(bestI);
                return Process(cur, p);
            }
        }
        if (m.Status != ScrollStatus.Accepted && _committed)
        {
            int bandC = _fh - lay.Header - lay.Footer;
            if (_verifiedTo < bandC)
            {
                // Nothing fits the whole overlap: the rows the last frame left provisional may be torn. A frame that fits the
                // rows above them is held, not taken: the next frame says whether it or the provisional rows were the tear.
                _useCap = true;
                var capped = Match(lay, cur, p);
                _useCap = false;
                if (capped.Status == ScrollStatus.Accepted && capped.D >= 0)
                {
                    Hold(cur, capped.D);
                    return Step(ScrollStatus.Ambiguous, 0);
                }
            }
        }
        if (m.Status != ScrollStatus.Accepted)
            return Step(m.Status, 0);
        _cands.Clear();
        if (!_committed && m.D != 0 && m.D != raw)
        {
            // The first guess (by stripes) was another period of a repeating page: take the layout of the shift that won.
            lay = AnalyzeLayout(_ref, cur, m.D) ?? default;
            var again = Match(lay, cur, p);
            if (again.Status != ScrollStatus.Accepted || again.D != m.D)
                return Step(ScrollStatus.Ambiguous, 0);
        }
        if (m.D == 0)
            return Step(ScrollStatus.NoMove, 0, 0);

        if (!_committed)
            Commit(lay);
        if (m.D < 0)
        {
            _viewDelta = m.D;
            return Step(ScrollStatus.NoMove, m.D, m.D);
        }
        int bandP = _fh - lay.Header - lay.Footer;
        _nextVerified = Math.Max(0, OverlapRows(m.D, bandP).U1 - m.D - VerifyMargin);
        RepairPending(lay, cur, m.D, bandP);
        return Append(cur, m.D);
    }

    // A frame that fits the verified rows of the reference but not its provisional tail is held, with the shift it had. The
    // next frame decides: the same picture again (or one that fits it) makes it the reference and replaces the tail; one that
    // fits the old reference instead shows the held frame was torn.
    private const int MaxHeld = 3;
    private readonly List<(byte[] Frame, int D)> _cands = [];

    private void Hold(ReadOnlySpan<byte> cur, int d)
    {
        foreach (var (frame, _) in _cands)
            if (frame.AsSpan().SequenceEqual(cur))
                return;
        if (_cands.Count >= MaxHeld)
            _cands.RemoveAt(0);
        _cands.Add((cur.ToArray(), d));
    }

    private int HeldEqual(ReadOnlySpan<byte> cur)
    {
        for (int i = _cands.Count - 1; i >= 0; i--)
            if (_cands[i].Frame.AsSpan().SequenceEqual(cur))
                return i;
        return -1;
    }

    private MatchResult MatchAgainstCandidate(int i, ScrollLayout lay, ReadOnlySpan<byte> cur, int? p)
    {
        var saved = (_ref, _refSig, _refSigLayout, _refIndex, _verifiedTo, _useCap);
        _ref = _cands[i].Frame;
        _refSig = null;
        _refIndex = null;
        _verifiedTo = int.MaxValue;
        _useCap = false;
        var m = Match(lay, cur, p);
        (_ref, _refSig, _refSigLayout, _refIndex, _verifiedTo, _useCap) = saved;
        return m;
    }

    private ScrollStep CommitCandidate(int i)
    {
        var lay = _layout;
        var (cand, d) = _cands[i];
        _cands.Clear();
        int band = _fh - lay.Header - lay.Footer;
        _useCap = true;
        try
        {
            if (d == 0)
            {
                RepairPending(lay, cand, 0, band);
                cand.CopyTo(_ref!, 0);
                _footer = cand.AsSpan((_fh - lay.Footer) * _rb, lay.Footer * _rb).ToArray();
                _refSig = null;
                _refIndex = null;
                return Step(ScrollStatus.NoMove, 0, 0);
            }
            _nextVerified = Math.Max(0, OverlapRows(d, band).U1 - d - VerifyMargin);
            RepairPending(lay, cand, d, band);
        }
        finally
        {
            _useCap = false;
        }
        _curSig = ComputeSig(cand, _w, lay.Header, _fh - lay.Footer, lay.Left, _w - lay.Right, false);
        return Append(cand, d);
    }

    private ScrollStep Step(ScrollStatus status, int shift, int? viewDelta = null)
    {
        if (viewDelta is { } v)
            _viewDelta = v;
        else if (status == ScrollStatus.Accepted)
            _viewDelta = 0;
        return new ScrollStep(status, shift, Height);
    }

    private void Commit(ScrollLayout lay)
    {
        _layout = lay;
        _committed = true;
        int band = _fh - lay.Header - lay.Footer;
        _fill = new uint[lay.Left + lay.Right];
        var first = _first!;
        var counts = new Dictionary<uint, int>();
        for (int i = 0; i < _fill.Length; i++)
        {
            int x = i < lay.Left ? i : _w - lay.Right + (i - lay.Left);
            counts.Clear();
            uint best = 0;
            int bestCount = -1;
            for (int u = 0; u < band; u++)
            {
                uint px = MemoryMarshal.Read<uint>(first.AsSpan((lay.Header + u) * _rb + x * 4, 4));
                ref int c = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, px, out _);
                if (++c > bestCount)
                {
                    bestCount = c;
                    best = px;
                }
            }
            _fill[i] = best;
        }
        _footer = first.AsSpan((_fh - lay.Footer) * _rb, lay.Footer * _rb).ToArray();
    }

    private ScrollStep Append(ReadOnlySpan<byte> cur, int s)
    {
        var lay = _layout;
        long cap = Math.Min(_limits.MaxHeight, _limits.MaxPixels / _w) - _fh;
        int room = (int)Math.Max(0, cap - _appended);
        int take = Math.Min(s, room);
        if (take > 0)
        {
            int bandEnd = _fh - lay.Footer;
            var strip = new byte[take * _rb];
            cur.Slice((bandEnd - s) * _rb, take * _rb).CopyTo(strip);
            PaintFixed(strip, take);
            _seams.Add(bandEnd + _appended);
            _stripStart.Add(bandEnd + _appended);
            _strips.Add(strip);
            _appended += take;
        }
        _frames++;
        if (take < s || take == room)
        {
            // What fits is in; nothing more can be added.
            _limited = true;
            return Step(ScrollStatus.Limit, take);
        }
        // The frame is the new reference, and its footer is the one the result ends with.
        if (_ref == _first)
            _ref = new byte[_rb * _fh];
        cur.CopyTo(_ref);
        _footer = cur.Slice((_fh - lay.Footer) * _rb, lay.Footer * _rb).ToArray();
        _refSig = _curSig;
        _refSigLayout = lay;
        _refIndex = _refSig is null ? null : new RowIndex(_refSig.Hash, _refSig.Range);
        _refStripe = null;
        _refStripeIndex = null;
        _verifiedTo = _nextVerified;
        return Step(ScrollStatus.Accepted, take);
    }

    private void PaintFixed(byte[] strip, int rows)
    {
        var lay = _layout;
        for (int r = 0; r < rows; r++)
        {
            int o = r * _rb;
            for (int i = 0; i < lay.Left; i++)
                MemoryMarshal.Write(strip.AsSpan(o + i * 4, 4), _fill[i]);
            for (int i = 0; i < lay.Right; i++)
                MemoryMarshal.Write(strip.AsSpan(o + (_w - lay.Right + i) * 4, 4), _fill[lay.Left + i]);
        }
    }

    // ---- Composing ------------------------------------------------------------------------------------------------

    private int FirstEnd => _fh - (_committed ? _layout.Footer : 0);

    private void Locate(int r, out byte[] a, out int off, out int rows)
    {
        int firstEnd = FirstEnd;
        if (r < firstEnd)
        {
            a = _first!;
            off = r * _rb;
            rows = firstEnd - r;
        }
        else if (r < firstEnd + _appended)
        {
            int lo = 0, hi = _stripStart.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_stripStart[mid] <= r)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            a = _strips[lo];
            off = (r - _stripStart[lo]) * _rb;
            rows = _strips[lo].Length / _rb - (r - _stripStart[lo]);
        }
        else
        {
            a = _footer;
            off = (r - firstEnd - _appended) * _rb;
            rows = _footer.Length / _rb - (r - firstEnd - _appended);
        }
    }

    /// <summary>Copies result rows <paramref name="first"/> … into <paramref name="dest"/> (rows × width × 4 bytes), for encoding in bands.</summary>
    public void CopyRows(int first, int count, Span<byte> dest)
    {
        if (first < 0 || count < 0 || first + count > Height || dest.Length < (long)count * _rb)
            throw new ArgumentOutOfRangeException(nameof(count));
        int r = first, o = 0;
        while (count > 0)
        {
            Locate(r, out var a, out int off, out int avail);
            int segment = Math.Min(count, avail);
            a.AsSpan(off, segment * _rb).CopyTo(dest[o..]);
            r += segment;
            o += segment * _rb;
            count -= segment;
        }
    }

    /// <summary>The whole result as one image (all rows held at once; use <see cref="CopyRows"/> to encode in bands).</summary>
    public CapturedImage Compose()
    {
        if (_first is null)
            return new CapturedImage(0, 0, []);
        var pixels = new byte[(long)_rb * Height];
        CopyRows(0, Height, pixels);
        return new CapturedImage(_w, Height, pixels) { Opaque = true };
    }

    /// <summary>
    /// A cheap box-sampled copy at <paramref name="width"/> pixels wide (never wider than the result) for the preview strip:
    /// it reads at most 16 pixels per output pixel, so it costs the same at 2,000 rows as at 20,000.
    /// </summary>
    public CapturedImage ComposePreview(int width)
    {
        int total = Height;
        if (total == 0 || width < 1)
            return new CapturedImage(0, 0, []);
        int ow = Math.Min(width, _w);
        int oh = Math.Max(1, (int)((long)total * ow / _w));
        var pixels = new byte[(long)ow * oh * 4];
        Span<int> ys = stackalloc int[4];
        Span<int> xs = stackalloc int[4];
        for (int oy = 0; oy < oh; oy++)
        {
            long y0 = (long)oy * total / oh;
            long y1 = Math.Max(y0 + 1, (long)(oy + 1) * total / oh);
            int ny = (int)Math.Min(4, y1 - y0);
            for (int i = 0; i < ny; i++)
                ys[i] = (int)Math.Min(total - 1, y0 + (2 * i + 1) * (y1 - y0) / (2 * ny));
            for (int ox = 0; ox < ow; ox++)
            {
                int x0 = (int)((long)ox * _w / ow);
                int x1 = Math.Max(x0 + 1, (int)((long)(ox + 1) * _w / ow));
                int nx = Math.Min(4, x1 - x0);
                for (int i = 0; i < nx; i++)
                    xs[i] = Math.Min(_w - 1, x0 + (2 * i + 1) * (x1 - x0) / (2 * nx));
                int b = 0, g = 0, rr = 0, a = 0;
                for (int j = 0; j < ny; j++)
                {
                    Locate(ys[j], out var src, out int off, out _);
                    for (int i = 0; i < nx; i++)
                    {
                        int k = off + xs[i] * 4;
                        b += src[k];
                        g += src[k + 1];
                        rr += src[k + 2];
                        a += src[k + 3];
                    }
                }
                int n = nx * ny;
                int o = (oy * ow + ox) * 4;
                pixels[o] = (byte)(b / n);
                pixels[o + 1] = (byte)(g / n);
                pixels[o + 2] = (byte)(rr / n);
                pixels[o + 3] = (byte)(a / n);
            }
        }
        return new CapturedImage(ow, oh, pixels);
    }

    // ---- Signatures -----------------------------------------------------------------------------------------------

    private sealed class RowSig(int rows, int stripes)
    {
        public readonly ulong[] Hash = new ulong[rows];
        public readonly byte[] Range = new byte[rows];
        public readonly ulong[][] Stripe = [.. Enumerable.Range(0, stripes).Select(_ => new ulong[rows])];

        public int Informative()
        {
            int n = 0;
            foreach (byte r in Range)
                if (r > InfoRange)
                    n++;
            return n;
        }
    }

    private sealed class RowIndex
    {
        private readonly Dictionary<ulong, int> _head;
        private readonly int[] _next;

        public RowIndex(ulong[] hashes, byte[] range)
        {
            _head = new Dictionary<ulong, int>(hashes.Length);
            _next = new int[hashes.Length];
            for (int r = hashes.Length - 1; r >= 0; r--)
            {
                _next[r] = -1;
                if (range[r] <= InfoRange)
                    continue;
                ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_head, hashes[r], out bool exists);
                _next[r] = exists ? slot : -1;
                slot = r;
            }
        }

        public int First(ulong hash) => _head.TryGetValue(hash, out int r) ? r : -1;

        public int Next(int row) => _next[row];
    }

    private static ulong Mix(ulong h, int v)
    {
        h = (h ^ (uint)v) * 1099511628211UL;
        return h ^ (h >> 29);
    }

    /// <summary>A gray signature per row (48 bins over the columns, every second pixel), plus the row's gray range.</summary>
    private static unsafe RowSig ComputeSig(ReadOnlySpan<byte> f, int w, int y0, int y1, int x0, int x1, bool stripes)
    {
        int rows = y1 - y0, cols = x1 - x0;
        int nb = Math.Max(1, Math.Min(Bins, cols));
        int nStripes = 0;
        if (stripes && nb >= Stripes)
        {
            nb = nb / Stripes * Stripes;
            nStripes = Stripes;
        }
        int step = cols >= 128 ? 2 : 1;
        var sig = new RowSig(Math.Max(rows, 0), nStripes);
        var bs = new int[nb + 1];
        for (int b = 0; b <= nb; b++)
            bs[b] = x0 + (int)((long)b * cols / nb);
        int perStripe = nStripes > 0 ? nb / nStripes : 1;
        fixed (byte* p = f)
        {
            for (int r = 0; r < rows; r++)
            {
                byte* row = p + (long)(y0 + r) * w * 4;
                ulong all = Seed, hs = 0;
                int mn = 255, mx = 0;
                for (int b = 0; b < nb; b++)
                {
                    int sum = 0, n = 0;
                    for (int x = bs[b]; x < bs[b + 1]; x += step)
                    {
                        byte* px = row + x * 4;
                        int g = (29 * px[0] + 150 * px[1] + 77 * px[2]) >> 8;
                        sum += g;
                        n++;
                        if (g < mn)
                            mn = g;
                        if (g > mx)
                            mx = g;
                    }
                    int mean = n == 0 ? 0 : sum / n;
                    all = Mix(all, mean);
                    if (nStripes > 0)
                    {
                        if (b % perStripe == 0)
                            hs = Mix(Seed, b / perStripe + 1000);
                        hs = Mix(hs, mean);
                        if (b % perStripe == perStripe - 1)
                            sig.Stripe[b / perStripe][r] = hs;
                    }
                }
                sig.Hash[r] = all;
                sig.Range[r] = (byte)Math.Max(0, mx - mn);
            }
        }
        return sig;
    }

    private int InformativeRows(ReadOnlySpan<byte> frame) => ComputeSig(frame, _w, 0, _fh, 0, _w, false).Informative();

    private static unsafe bool NearlyEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int pixels)
    {
        if (a.SequenceEqual(b))
            return true;
        long limit = Math.Max(16, pixels / 2000);
        long diff = 0;
        fixed (byte* pa = a)
        fixed (byte* pb = b)
        {
            uint* x = (uint*)pa, y = (uint*)pb;
            for (int i = 0; i < pixels; i++)
                if (x[i] != y[i] && ++diff > limit)
                    return false;
        }
        return true;
    }

    private static void AddVotes(int[] votes, int offset, ulong[] curHash, byte[] curRange, RowIndex idx, int dLo, int dHi, bool skipZero)
    {
        Span<int> hit = stackalloc int[9];
        for (int y = 0; y < curHash.Length; y++)
        {
            if (curRange[y] <= InfoRange)
                continue;
            int r = idx.First(curHash[y]), n = 0;
            while (r >= 0 && n < 9)
            {
                hit[n++] = r;
                r = idx.Next(r);
            }
            if (n > 8)
                continue; // a row seen everywhere says nothing
            for (int i = 0; i < n; i++)
            {
                int d = hit[i] - y;
                if (d < dLo || d > dHi || (skipZero && d == 0))
                    continue;
                votes[d + offset]++;
            }
        }
    }

    private static List<(int D, int Votes)> Peaks(int[] votes, int offset, int dLo, int dHi, int minVotes, int k)
    {
        var peaks = new List<(int, int)>();
        var work = (int[])votes.Clone();
        for (int n = 0; n < k; n++)
        {
            int bestD = 0, best = 0;
            for (int d = dLo; d <= dHi; d++)
                if (work[d + offset] > best)
                {
                    best = work[d + offset];
                    bestD = d;
                }
            if (best < minVotes)
                break;
            peaks.Add((bestD, best));
            for (int d = Math.Max(dLo, bestD - 2); d <= Math.Min(dHi, bestD + 2); d++)
                work[d + offset] = 0;
        }
        return peaks;
    }

    // ---- First pair: the shift, then the fixed parts ----------------------------------------------------------------

    private int _curStripeInfo;
    private double _bestMad;

    /// <summary>
    /// The shift of the first pair, before the fixed parts are known. Each stripe of the width votes on its own, and a vote
    /// for 0 (a sticky bar, a sidebar) is left out, so fixed parts can't pull the shift toward zero.
    /// </summary>
    private int? EstimateRaw(ReadOnlySpan<byte> cur, int? p)
    {
        bool stripes = _w >= 96;
        _refStripe ??= ComputeSig(_ref, _w, 0, _fh, 0, _w, stripes);
        var curSig = ComputeSig(cur, _w, 0, _fh, 0, _w, stripes);
        _curStripeInfo = curSig.Informative();
        int maxD = _fh - Math.Max(_fh / 5, 16);
        int dLo = -maxD, dHi = maxD;
        if (p is { } pp)
        {
            dLo = Math.Max(dLo, pp - PredictionSlack);
            dHi = Math.Min(dHi, pp + PredictionSlack);
        }
        if (dLo > dHi)
            return null;
        var votes = new int[2 * _fh + 1];
        if (stripes)
        {
            _refStripeIndex ??= [.. _refStripe.Stripe.Select(s => new RowIndex(s, _refStripe.Range))];
            for (int s = 0; s < Stripes; s++)
                AddVotes(votes, _fh, curSig.Stripe[s], curSig.Range, _refStripeIndex[s], dLo, dHi, true);
        }
        else
            AddVotes(votes, _fh, curSig.Hash, curSig.Range, new RowIndex(_refStripe.Hash, _refStripe.Range), dLo, dHi, true);
        var peaks = Peaks(votes, _fh, dLo, dHi, stripes ? 10 : 4, 1);
        if (peaks.Count == 0 && p is not null)
            peaks = ScanWindow(default, cur, dLo, dHi);
        return peaks.Count == 0 ? null : peaks[0].D;
    }

    /// <summary>The shifts in a window that fit best (up to three, more than 2 apart), for a predicted shift on a page whose rows repeat.</summary>
    private List<(int D, int Votes)> ScanWindow(ScrollLayout lay, ReadOnlySpan<byte> cur, int dLo, int dHi)
    {
        int band = _fh - lay.Header - lay.Footer;
        var errs = new List<(int D, double Mad)>();
        for (int d = dLo; d <= dHi; d++)
        {
            if (Math.Abs(d) > band - Math.Max(band / 5, 16))
                continue;
            errs.Add((d, Eval(_ref!, cur, lay, d, 4, 8).Mad));
        }
        var peaks = new List<(int, int)>();
        foreach (var (d, mad) in errs.OrderBy(e => e.Mad))
        {
            if (mad > 8 || peaks.Count >= 3)
                break;
            if (peaks.All(q => Math.Abs(q.Item1 - d) > 2))
                peaks.Add((d, 0));
        }
        return peaks;
    }

    private static unsafe int Diff(byte* a, byte* b) => Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) + Math.Abs(a[2] - b[2]);

    /// <summary>
    /// Finds the sticky rows and fixed columns by what doesn't move: with the pair's shift <paramref name="d"/>, a column or
    /// row that matches in place and not shifted is fixed; one that matches both ways is flat and says nothing, so a blank
    /// margin is never taken for a header. Rows first over the middle columns (to keep sticky bars out of the column
    /// vote), then columns, then rows again over the columns that aren't fixed.
    /// </summary>
    private unsafe ScrollLayout? AnalyzeLayout(ReadOnlySpan<byte> refF, ReadOnlySpan<byte> curF, int d)
    {
        // Orient so that f2(y) = f1(y + dd) with dd > 0.
        var f1 = d > 0 ? refF : curF;
        var f2 = d > 0 ? curF : refF;
        int dd = Math.Abs(d);
        int w = _w, h = _fh;
        fixed (byte* p1 = f1)
        fixed (byte* p2 = f2)
        {
            var rows1 = RowClasses(p1, p2, dd, TexturedColumns(p1), 0, 0);
            var (h1, g1) = RowRuns(rows1);

            int lo = h1, hi = h - g1;
            int span = hi - dd - lo;
            if (span < 8)
                return null;
            int ystep = Math.Max(1, span / 300);
            var e0 = new long[w];
            var e1 = new long[w];
            int cnt = 0;
            for (int y = lo; y < hi - dd; y += ystep)
            {
                cnt++;
                byte* a = p1 + (long)y * _rb, b = p1 + (long)(y + dd) * _rb, c = p2 + (long)y * _rb;
                for (int x = 0; x < w; x++)
                {
                    e0[x] += Diff(a + x * 4, c + x * 4);
                    e1[x] += Diff(b + x * 4, c + x * 4);
                }
            }
            long thr = cnt * 6L;
            // 1 fixed, -1 content, 0 flat.
            var cls = new sbyte[w];
            for (int x = 0; x < w; x++)
                cls[x] = (sbyte)(e1[x] > thr && e0[x] * 10 < e1[x] * 6 ? 1 : e0[x] > thr && e1[x] * 10 < e0[x] * 6 ? -1 : 0);
            int left = 0, right = 0;
            int a0 = 0;
            while (a0 < w && cls[a0] != -1)
                a0++;
            for (int x = 0; x < a0; x++)
                if (cls[x] == 1)
                    left = x + 1;
            int b0 = w - 1;
            while (b0 >= 0 && cls[b0] != -1)
                b0--;
            for (int x = w - 1; x > b0; x--)
                if (cls[x] == 1)
                    right = w - x;
            if (left > w * 45 / 100 || right > w * 45 / 100 || left + right > w * 6 / 10)
                left = right = 0;

            var rows2 = RowClasses(p1, p2, dd, SpreadColumns(left, w - right), h1, g1);
            var (header, footer) = RowRuns(rows2);
            return new ScrollLayout(header, footer, left, right);
        }
    }

    private static int[] SpreadColumns(int x0, int x1)
    {
        int step = Math.Max(1, (x1 - x0) / 160);
        var cols = new List<int>();
        for (int x = x0; x < x1; x += step)
            cols.Add(x);
        return [.. cols];
    }

    /// <summary>Up to 160 columns that have something in them (several edges down the middle rows), spread across the width.</summary>
    private unsafe int[] TexturedColumns(byte* p)
    {
        int y0 = _fh / 4, y1 = _fh * 3 / 4;
        var textured = new List<int>();
        for (int x = 0; x < _w; x++)
        {
            int edges = 0;
            for (int y = y0 + 1; y < y1 && edges < 6; y++)
                if (Diff(p + (long)y * _rb + x * 4, p + (long)(y - 1) * _rb + x * 4) > 24)
                    edges++;
            if (edges >= 6)
                textured.Add(x);
        }
        if (textured.Count < 8)
            return SpreadColumns(0, _w);
        int stride = Math.Max(1, textured.Count / 160);
        return [.. textured.Where((_, i) => i % stride == 0)];
    }

    /// <summary>
    /// Per row: -1 it moved, 1 it's the same in place but not shifted (fixed), 0 it's the same both ways (flat) or has no
    /// partner to tell by. The shifted partner of a content row never lands in a sticky bar (<paramref name="header"/> and
    /// <paramref name="footer"/> rows known so far), or a flat margin would look fixed against it.
    /// </summary>
    private unsafe sbyte[] RowClasses(byte* p1, byte* p2, int dd, int[] cols, int header, int footer)
    {
        int h = _fh;
        int ns = cols.Length;
        int allowed = Math.Max(1, ns / 20); // a scrollbar thumb or a caret may differ in a row that is otherwise the same
        var cls = new sbyte[h];
        for (int y = 0; y < h; y++)
        {
            byte* r1 = p1 + (long)y * _rb, r2 = p2 + (long)y * _rb;
            int m0 = 0;
            foreach (int x in cols)
                if (Diff(r1 + x * 4, r2 + x * 4) > 24)
                    m0++;
            if (m0 > allowed)
            {
                cls[y] = -1;
                continue;
            }
            bool Valid(int q) => q >= 0 && q < h && !(q >= h - footer && y < h - footer) && !(q < header && y >= header);
            int fwd = y + dd, back = y - dd;
            bool useFwd;
            if (y < h / 2)
                useFwd = Valid(fwd) || !Valid(back);
            else
                useFwd = !Valid(back) && Valid(fwd);
            int q2 = useFwd ? fwd : back;
            if (!Valid(q2))
            {
                cls[y] = 0;
                continue;
            }
            byte* ra = useFwd ? p1 + (long)q2 * _rb : r1;
            byte* rb = useFwd ? r2 : p2 + (long)q2 * _rb;
            int m1 = 0;
            foreach (int x in cols)
                if (Diff(ra + x * 4, rb + x * 4) > 24)
                    m1++;
            cls[y] = (sbyte)(m1 <= allowed ? 0 : 1);
        }
        return cls;
    }

    /// <summary>The sticky rows at the top and bottom: the run of rows that didn't move, up to the last one that is fixed.</summary>
    private (int Header, int Footer) RowRuns(sbyte[] cls)
    {
        int h = cls.Length, header = 0, footer = 0;
        int t = 0;
        while (t < h && cls[t] != -1)
            t++;
        if (t == h)
            return (0, 0);
        for (int y = 0; y < t; y++)
            if (cls[y] == 1)
                header = y + 1;
        int u = h - 1;
        while (u >= 0 && cls[u] != -1)
            u--;
        for (int y = h - 1; y > u; y--)
            if (cls[y] == 1)
                footer = h - y;
        if (header > h * 4 / 10 || footer > h * 4 / 10 || header + footer > h * 6 / 10)
            header = footer = 0;
        return (header, footer);
    }

    // ---- Matching -------------------------------------------------------------------------------------------------

    private readonly record struct MatchResult(ScrollStatus Status, int D);

    private RowSig? _curSig;

    private void EnsureRefSig(ScrollLayout lay)
    {
        if (_refSig is not null && _refSigLayout == lay)
            return;
        _refSig = ComputeSig(_ref, _w, lay.Header, _fh - lay.Footer, lay.Left, _w - lay.Right, false);
        _refSigLayout = lay;
        _refIndex = new RowIndex(_refSig.Hash, _refSig.Range);
    }

    private (double Mad, double Bad) Eval(ReadOnlySpan<byte> r, ReadOnlySpan<byte> c, ScrollLayout lay, int d, int rowStep, int colStep)
        => EvalCore(r, c, lay, d, rowStep, colStep);

    private unsafe (double Mad, double Bad) EvalCore(ReadOnlySpan<byte> r, ReadOnlySpan<byte> c, ScrollLayout lay, int d, int rowStep, int colStep)
    {
        int band = _fh - lay.Header - lay.Footer;
        var (u0, u1) = OverlapRows(d, band);
        int x0 = lay.Left, x1 = _w - lay.Right;
        long sum = 0, n = 0, bad = 0;
        fixed (byte* pr = r)
        fixed (byte* pc = c)
        {
            for (int u = u0; u < u1; u += rowStep)
            {
                byte* a = pr + (long)(lay.Header + u) * _rb;
                byte* b = pc + (long)(lay.Header + u - d) * _rb;
                for (int x = x0; x < x1; x += colStep)
                {
                    int diff = Diff(a + x * 4, b + x * 4);
                    sum += diff;
                    if (diff > 48)
                        bad++;
                    n++;
                }
            }
        }
        return n == 0 ? (999, 1) : (sum / (3.0 * n), bad / (double)n);
    }

    private MatchResult Match(ScrollLayout lay, ReadOnlySpan<byte> cur, int? p)
    {
        int band = _fh - lay.Header - lay.Footer;
        if (band < 32 || _w - lay.Left - lay.Right < 16)
            return new(ScrollStatus.Ambiguous, 0);
        EnsureRefSig(lay);
        var refSig = _refSig!;
        var curSig = ComputeSig(cur, _w, lay.Header, _fh - lay.Footer, lay.Left, _w - lay.Right, false);
        _curSig = curSig;
        int minOverlap = Math.Max(band / 5, 16);
        int maxD = band - minOverlap;
        int dLo = -maxD, dHi = maxD;
        if (p is { } pp)
        {
            dLo = Math.Max(dLo, pp - PredictionSlack);
            dHi = Math.Min(dHi, pp + PredictionSlack);
            if (dLo > dHi)
                return new(ScrollStatus.Gap, 0);
        }

        var votes = new int[2 * band + 1];
        AddVotes(votes, band, curSig.Hash, curSig.Range, _refIndex!, dLo, dHi, false);
        var peaks = Peaks(votes, band, dLo, dHi, 4, 4);
        // Rows that repeat say nothing by their signature; a predicted shift still has its window to try.
        if (peaks.Count == 0 && p is not null)
            peaks = ScanWindow(lay, cur, dLo, dHi);

        _bestMad = double.MaxValue;
        if (peaks.Count > 0 && TryAccept(lay, cur, refSig, band, maxD, peaks) is { } d)
            return new(ScrollStatus.Accepted, d);
        if (p is { } pd && Math.Abs(pd) <= maxD && TryBlank(lay, cur, refSig, curSig, band, pd))
            return new(ScrollStatus.Accepted, pd);
        // No overlap anywhere (nothing fits even roughly) is a gap; a near fit that isn't unique is ambiguous.
        bool noOverlap = _bestMad > 20 && curSig.Informative() >= MinInformative;
        return new(noOverlap ? ScrollStatus.Gap : ScrollStatus.Ambiguous, 0);
    }

    private int? TryAccept(ScrollLayout lay, ReadOnlySpan<byte> cur, RowSig refSig, int band, int maxD, List<(int D, int Votes)> peaks)
    {
        var cands = new List<(int D, double Mad)>();
        foreach (var (d, _) in peaks)
            cands.Add((d, Eval(_ref, cur, lay, d, 2, 4).Mad));
        var best = cands[0];
        foreach (var c in cands)
            if (c.Mad < best.Mad)
                best = c;

        int u0 = best.D >= 0 ? best.D : 0, u1 = best.D >= 0 ? band : band + best.D;
        int info = 0;
        for (int u = u0; u < u1; u++)
            if (refSig.Range[u] > InfoRange)
                info++;
        _bestMad = best.Mad;
        if (info < MinInformative)
            return null;

        // The best must stand out from shifts that have nothing to do with it.
        var errors = cands.Select(c => c.Mad).ToList();
        for (int i = 0; i < 4; i++)
        {
            int dd = -maxD + (int)(2.0 * maxD * (0.17 + 0.22 * i));
            for (int guard = 0; guard < 8 && cands.Any(c => Math.Abs(c.D - dd) <= 3); guard++)
                dd = dd + 7 > maxD ? dd - 2 * maxD / 3 : dd + 7;
            errors.Add(Eval(_ref, cur, lay, dd, 6, 8).Mad);
        }
        double median = errors[errors.Count / 2];
        if (median <= 0.5 || best.Mad >= 0.8 * median)
            return null;
        foreach (var c in cands)
            if (Math.Abs(c.D - best.D) > 2 && c.Mad <= best.Mad * 1.15 + 0.15)
                return null; // another shift fits as well: a repeating page, never guess
        var (mad, bad) = Eval(_ref!, cur, lay, best.D, 2, 2);
        if (mad > 6.0 || bad > MismatchLimit)
            return null;

        // A shift that is a whole number of lines off fits almost as well on a page of lines that differ in one digit, and a
        // frame caught while the area repaints is half one position and half the other. Both differ from the true shift in
        // a few pixels per row, which the overall averages hide, so the rows are judged one by one and the winner is
        // compared with the others by the pixels that differ, not by their mean.
        ThumbColumns(lay, cur, best.D, band);
        if (!RowsAgree(lay, cur, refSig, best.D, band))
        {
            return null;
        }
        long bestCount = -1, bestN = 0;
        foreach (var c in cands)
        {
            if (Math.Abs(c.D - best.D) <= 2 || c.Mad > 2.0 + best.Mad * 4) // a shift that is plainly off needs no count
                continue;
            if (bestCount < 0)
                (bestCount, bestN) = Mismatches(lay, cur, best.D);
            var (count, n) = Mismatches(lay, cur, c.D);
            if (n == 0)
                continue;
            double equiv = (double)count / n * bestN; // the other's mismatches, over the best's overlap
            if (equiv < bestCount * UniqueFactor + UniqueMargin)
                return null;
        }
        return best.D;
    }


    /// <summary>The best shift's mismatching pixels must be under 1/UniqueFactor of any other shift's, and by this many pixels (sampled).</summary>
    private const double UniqueFactor = 4;
    private const double UniqueMargin = 8;
    private const int PixelTolerance = 16;

    // Columns left out of the row checks (see ThumbColumns), for the frame being judged.
    private bool[]? _skip;

    private unsafe int RowMismatch(byte* a, byte* b, int x0, int x1, int cap = int.MaxValue)
    {
        if (new ReadOnlySpan<byte>(a + x0 * 4, (x1 - x0) * 4).SequenceEqual(new ReadOnlySpan<byte>(b + x0 * 4, (x1 - x0) * 4)))
            return 0;
        int count = 0;
        var skip = _skip;
        for (int x = x0; x < x1; x += 2)
        {
            if (skip is not null && skip[x])
                continue;
            byte* pa = a + x * 4, pb = b + x * 4;
            if (*(uint*)pa == *(uint*)pb)
                continue;
            if ((Math.Abs(pa[0] - pb[0]) > PixelTolerance || Math.Abs(pa[1] - pb[1]) > PixelTolerance || Math.Abs(pa[2] - pb[2]) > PixelTolerance) && ++count > cap)
                break;
        }
        return count;
    }

    private (int X0, int X1) JudgedColumns(ScrollLayout lay) => (lay.Left, _w - lay.Right);

    // The reference's rows from _verifiedTo down (band rows) are provisional: the rows its frame added, and any rows above them
    // that were only compared with the previous frame's own provisional rows. A tear below what a frame's overlap vouched for
    // sits there. They are left out of the checks of the next frame (while enough rows remain above), and when that frame is
    // accepted its pixels overwrite every provisional row in its overlap, so the next frame replaces a tear.
    private int _verifiedTo = int.MaxValue;
    private bool _useCap;
    private const int VerifyMargin = 3; // the last rows of an overlap may hold the single tolerated bad row of a tear
    private int _nextVerified = int.MaxValue;

    private void RepairPending(ScrollLayout lay, ReadOnlySpan<byte> cur, int d, int band)
    {
        if (_verifiedTo >= band || d < 0)
            return;
        int x0 = lay.Left, x1 = _w - lay.Right;
        int baseRow = FirstEnd + _appended - band;
        for (int u = Math.Max(_verifiedTo, d); u < band; u++)
        {
            int r = baseRow + u, c = lay.Header + u - d;
            if (r < 0 || r >= FirstEnd + _appended)
                continue;
            Locate(r, out var a, out int off, out _);
            cur.Slice(c * _rb + x0 * 4, (x1 - x0) * 4).CopyTo(a.AsSpan(off + x0 * 4, (x1 - x0) * 4));
        }
    }

    private (int U0, int U1) OverlapRows(int d, int band)
    {
        int u0 = d >= 0 ? d : 0, u1 = d >= 0 ? band : band + d;
        int cap = _useCap ? _verifiedTo : int.MaxValue;
        if (u1 > cap && cap - u0 >= Math.Max(band / 10, 16))
            u1 = cap;
        return (u0, u1);
    }

    private const int EdgeSkip = 20;

    /// <summary>
    /// A scrollbar thumb too small to have been found as fixed differs in the few rows it moved through, in columns at the
    /// edge. Those columns (outermost EdgeSkip on each side, differing in some rows but under 15% of them) are left out of the
    /// row checks. A column that differs in most rows (a numbered stripe, at another shift of the page) stays in.
    /// </summary>
    private unsafe void ThumbColumns(ScrollLayout lay, ReadOnlySpan<byte> cur, int d, int band)
    {
        _skip = null;
        var (x0, x1) = JudgedColumns(lay);
        if (x1 - x0 < 8 * EdgeSkip)
            return;
        var (u0, u1) = OverlapRows(d, band);
        int rows = u1 - u0;
        if (rows < 1)
            return;
        bool[]? skip = null;
        fixed (byte* pr = _ref)
        fixed (byte* pc = cur)
        {
            for (int side = 0; side < 2; side++)
                for (int k = 0; k < EdgeSkip; k++)
                {
                    int x = side == 0 ? x0 + k : x1 - 1 - k;
                    int diffRows = 0;
                    for (int u = u0; u < u1; u++)
                    {
                        byte* pa = pr + (long)(lay.Header + u) * _rb + x * 4, pb = pc + (long)(lay.Header + u - d) * _rb + x * 4;
                        if (Math.Abs(pa[0] - pb[0]) > PixelTolerance || Math.Abs(pa[1] - pb[1]) > PixelTolerance || Math.Abs(pa[2] - pb[2]) > PixelTolerance)
                            diffRows++;
                    }
                    if (diffRows > 0 && diffRows * 100 <= rows * 15)
                        (skip ??= new bool[_w])[x] = true;
                }
        }
        _skip = skip;
    }

    private static int RowLimit(int x0, int x1) => Math.Max(3, (int)Math.Ceiling((x1 - x0) / 2 * 0.004));

    /// <summary>
    /// Every row of the overlap at <paramref name="d"/>, with its differing pixels counted. A row with more than a few
    /// (a caret or a thumb differs by one or two) is bad; a run of bad rows, or more than one, is a tear or another shift.
    /// </summary>
    private unsafe bool RowsAgree(ScrollLayout lay, ReadOnlySpan<byte> cur, RowSig refSig, int d, int band)
    {
        var (u0, u1) = OverlapRows(d, band);
        var (x0, x1) = JudgedColumns(lay);
        int limit = RowLimit(x0, x1);
        int badRows = 0, ink = 0, touched = 0;
        bool previous = false;
        fixed (byte* pr = _ref)
        fixed (byte* pc = cur)
        {
            for (int u = u0; u < u1; u++)
            {
                int diff = RowMismatch(pr + (long)(lay.Header + u) * _rb, pc + (long)(lay.Header + u - d) * _rb, x0, x1, limit);
                if (refSig.Range[u] > InfoRange)
                {
                    ink++;
                    if (diff > 0)
                        touched++;
                }
                bool bad = diff > limit;
                if (bad)
                {
                    if (previous || ++badRows > 1)
                        return false;
                }
                previous = bad;
            }
        }
        // A page that moved has the same rows at the shift, pixel for pixel, but for a caret or a thumb: touching many of the
        // rows with ink is another shift of a page whose lines differ a little (a digit), however small each row part is.
        return touched <= Math.Max(2, ink / 10);
    }

    /// <summary>The pixels that differ over the overlap at <paramref name="d"/> (every second row and column), and how many were looked at.</summary>
    private unsafe (long Count, long N) Mismatches(ScrollLayout lay, ReadOnlySpan<byte> cur, int d)
    {
        int band = _fh - lay.Header - lay.Footer;
        var (u0, u1) = OverlapRows(d, band);
        var (x0, x1) = JudgedColumns(lay);
        long count = 0, n = 0;
        fixed (byte* pr = _ref)
        fixed (byte* pc = cur)
        {
            for (int u = u0; u < u1; u += 2)
            {
                count += RowMismatch(pr + (long)(lay.Header + u) * _rb, pc + (long)(lay.Header + u - d) * _rb, x0, x1);
                n += (x1 - x0 + 1) / 2;
            }
        }
        return (count, n);
    }


    /// <summary>A blank stretch is crossed only on the controller's own prediction: both overlaps hold nothing to match.</summary>
    private bool TryBlank(ScrollLayout lay, ReadOnlySpan<byte> cur, RowSig refSig, RowSig curSig, int band, int d)
    {
        int u0 = d >= 0 ? d : 0, u1 = d >= 0 ? band : band + d;
        int infoRef = 0, infoCur = 0;
        for (int u = u0; u < u1; u++)
        {
            if (refSig.Range[u] > InfoRange)
                infoRef++;
            if (curSig.Range[u - d] > InfoRange)
                infoCur++;
        }
        return infoRef < MinInformative && infoCur < MinInformative && Eval(_ref, cur, lay, d, 3, 4).Mad <= 1.0;
    }
}
