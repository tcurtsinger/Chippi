# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The scrolling check's verifier (scrolling.ps1 dot-sources it; it also runs on the host over any PNG, reading only the file):
# every row of a fixture carries a colored index stripe, four cells of eight colors that spell its number in base 8, so a
# saved scrolling capture can be checked row for row. A repeated row, a missing one, one out of order, one cut in half, or a
# header or footer that appears more than once is a problem. Usage: [ScrollVerify]::Check($png, $headerRgb, $footerRgb, $backgroundRgb)
# (each color an int[3], or $null to skip that check) returns a ScrollVerdict; .Ok is true when .Problems is empty.
if (-not ('ScrollVerify' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

public sealed class ScrollVerdict
{
    public int Width, Height, StripeX, CellWidth, RowHeight, Runs, First = -1, Last = -1, Repeats, Gaps, Backwards, BadRuns, HeaderBlocks, FooterBlocks;
    public long ForeignPixels;
    public List<string> Problems = new List<string>();
    public bool Ok { get { return Problems.Count == 0; } }
    public string Summary
    {
        get
        {
            return Width + "x" + Height + " rows " + First + ".." + Last + " runs " + Runs + " rowHeight " + RowHeight + " repeats " + Repeats + " gaps " + Gaps +
                " backwards " + Backwards + " bad " + BadRuns + " header " + HeaderBlocks + " footer " + FooterBlocks + " foreign " + ForeignPixels +
                (Ok ? " OK" : " PROBLEMS: " + string.Join("; ", Problems.ToArray()));
        }
    }
}

public static class ScrollVerify
{
    // The eight cell colors, far apart from each other and from the fixtures' header, footer, sidebar and page colors.
    public static readonly string[] Hex = { "#E6194B", "#3CB44B", "#FFE119", "#4363D8", "#F58231", "#911EB4", "#42D4F4", "#808080" };
    static readonly int[][] Palette = Parse();
    const int Tolerance = 40;

    static int[][] Parse()
    {
        var list = new int[Hex.Length][];
        for (int i = 0; i < Hex.Length; i++)
            list[i] = new[] { Convert.ToInt32(Hex[i].Substring(1, 2), 16), Convert.ToInt32(Hex[i].Substring(3, 2), 16), Convert.ToInt32(Hex[i].Substring(5, 2), 16) };
        return list;
    }

    /// <summary>The number a row's four digits (most significant first) spell.</summary>
    public static int Value(int d3, int d2, int d1, int d0) { return d3 * 512 + d2 * 64 + d1 * 8 + d0; }

    static bool Near(byte[] p, int i, int[] c, int tol)
    {
        int db = p[i] - c[2], dg = p[i + 1] - c[1], dr = p[i + 2] - c[0];
        return db * db + dg * dg + dr * dr <= tol * tol;
    }

    static int Digit(byte[] p, int i)
    {
        for (int k = 0; k < Palette.Length; k++) if (Near(p, i, Palette[k], Tolerance)) return k;
        return -1;
    }

    public static ScrollVerdict Check(string path, int[] header, int[] footer, int[] background)
    {
        byte[] pix; int w, h, stride;
        Load(path, out pix, out w, out h, out stride);
        var v = new ScrollVerdict { Width = w, Height = h };

        // The stripe: the widest run of columns where most sampled pixels are a palette color.
        var score = new int[w];
        int samples = 0;
        for (int y = 0; y < h; y += 5)
        {
            samples++;
            for (int x = 0; x < w; x++) if (Digit(pix, y * stride + x * 4) >= 0) score[x]++;
        }
        int bestStart = -1, bestLen = 0, start = -1;
        for (int x = 0; x <= w; x++)
        {
            bool on = x < w && score[x] * 2 > samples;
            if (on && start < 0) start = x;
            if (!on && start >= 0) { if (x - start > bestLen) { bestLen = x - start; bestStart = start; } start = -1; }
        }
        if (bestLen < 8) { v.Problems.Add("no index stripe found"); return v; }
        int cell = bestLen / 4;
        v.StripeX = bestStart; v.CellWidth = cell;

        // One value per pixel row, -1 where the row isn't a whole stripe.
        var value = new int[h];
        for (int y = 0; y < h; y++)
        {
            int n = 0; bool ok = true;
            for (int k = 0; k < 4 && ok; k++)
            {
                int d = Digit(pix, y * stride + (bestStart + k * cell + cell / 2) * 4);
                if (d < 0) ok = false; else n = n * 8 + d;
            }
            value[y] = ok ? n : -1;
        }

        var runs = new List<int[]>(); // value, first y, length
        for (int y = 0; y < h;)
        {
            if (value[y] < 0) { y++; continue; }
            int e = y; while (e < h && value[e] == value[y]) e++;
            runs.Add(new[] { value[y], y, e - y });
            y = e;
        }
        v.Runs = runs.Count;
        if (runs.Count < 2) { v.Problems.Add("fewer than two rows"); return v; }

        // The row height is the commonest run length.
        var hist = new Dictionary<int, int>();
        foreach (var r in runs) { int c; hist.TryGetValue(r[2], out c); hist[r[2]] = c + 1; }
        int rowH = 0, rowC = 0;
        foreach (var kv in hist) if (kv.Value > rowC) { rowC = kv.Value; rowH = kv.Key; }
        v.RowHeight = rowH;
        int slack = Math.Max(2, rowH / 12);

        for (int i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            bool edge = i == 0 || i == runs.Count - 1;
            int len = r[2];
            if (Math.Abs(len - rowH) <= slack) { /* a whole row */ }
            else if (len > rowH + slack)
            {
                int times = (int)Math.Round((double)len / rowH);
                if (times >= 2 && Math.Abs(len - times * rowH) <= slack * times) { v.Repeats += times - 1; v.Problems.Add("row " + r[0] + " appears " + times + " times in a row at y " + r[1]); }
                else { v.BadRuns++; v.Problems.Add("row " + r[0] + " has length " + len + " (row height " + rowH + ") at y " + r[1]); }
            }
            else if (!edge) { v.BadRuns++; v.Problems.Add("row " + r[0] + " is cut to " + len + " of " + rowH + " at y " + r[1]); }
            if (i > 0)
            {
                int d = r[0] - runs[i - 1][0];
                if (d == 0) { v.Repeats++; v.Problems.Add("row " + r[0] + " repeats at y " + r[1]); }
                else if (d < 0) { v.Backwards++; v.Problems.Add("row " + r[0] + " follows " + runs[i - 1][0] + " at y " + r[1]); }
                else if (d > 1) { v.Gaps += d - 1; v.Problems.Add("rows " + (runs[i - 1][0] + 1) + ".." + (r[0] - 1) + " are missing before y " + r[1]); }
            }
        }
        v.First = runs[0][0]; v.Last = runs[runs.Count - 1][0];

        // The sticky bars: one block each, header touching the top and footer the bottom.
        bool[] headerRow = Rows(pix, w, h, stride, header), footerRow = Rows(pix, w, h, stride, footer);
        if (header != null)
        {
            v.HeaderBlocks = Blocks(headerRow);
            if (v.HeaderBlocks != 1) v.Problems.Add("header appears " + v.HeaderBlocks + " times");
            else if (!headerRow[0]) v.Problems.Add("header is not at the top");
        }
        if (footer != null)
        {
            v.FooterBlocks = Blocks(footerRow);
            if (v.FooterBlocks != 1) v.Problems.Add("footer appears " + v.FooterBlocks + " times");
            else if (!footerRow[h - 1]) v.Problems.Add("footer is not at the bottom");
        }

        // The page's own color between rows: anything else there is overlay or seam bleed.
        if (background != null)
        {
            long foreign = 0;
            for (int y = 0; y < h; y++)
            {
                if (value[y] >= 0 || headerRow[y] || footerRow[y]) continue;
                for (int x = bestStart; x < bestStart + 4 * cell; x++) if (!Near(pix, y * stride + x * 4, background, 8)) foreign++;
            }
            v.ForeignPixels = foreign;
            if (foreign > 0) v.Problems.Add(foreign + " stripe-column pixels between rows are not the page color");
        }
        return v;
    }

    static bool[] Rows(byte[] p, int w, int h, int stride, int[] color)
    {
        var rows = new bool[h];
        if (color == null) return rows;
        for (int y = 0; y < h; y++)
        {
            int hit = 0, seen = 0;
            for (int x = 0; x < w; x += 4) { seen++; if (Near(p, y * stride + x * 4, color, 12)) hit++; }
            rows[y] = hit * 2 > seen;
        }
        return rows;
    }

    static int Blocks(bool[] rows)
    {
        int n = 0;
        for (int y = 0; y < rows.Length; y++) if (rows[y] && (y == 0 || !rows[y - 1])) n++;
        return n;
    }

    static void Load(string path, out byte[] pix, out int w, out int h, out int stride)
    {
        byte[] file;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var ms = new MemoryStream()) { fs.CopyTo(ms); file = ms.ToArray(); }
        using (var src = new Bitmap(new MemoryStream(file)))
        using (var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            w = bmp.Width; h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(data.Stride);
                pix = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pix, 0, pix.Length);
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
'@
}
