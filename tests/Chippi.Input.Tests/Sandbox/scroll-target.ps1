# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The scroll check's target (Smooth scrolling), started by probe.ps1 in its own process: a topmost window with 2,000 lines that
# logs every wheel message it gets (Stopwatch ms, axis, delta, first visible line) to C:\results\scroll.csv.

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public sealed class ScrollBox : TextBox
{
    public static readonly List<string> Rows = new List<string>();
    [DllImport("user32.dll")] static extern int SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x20A || m.Msg == 0x20E)
        {
            double t = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            int line = SendMessage(Handle, 0xCE, IntPtr.Zero, IntPtr.Zero);
            lock (Rows) Rows.Add(t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," + (m.Msg == 0x20E ? "h" : "v") + "," + delta + "," + line);
        }
    }
}

public static class ScrollTarget
{
    public static void Run()
    {
        var form = new Form { Text = "Scroll target", StartPosition = FormStartPosition.Manual, Left = 100, Top = 100, Width = 800, Height = 600, TopMost = true };
        var box = new ScrollBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new System.Drawing.Font("Consolas", 12) };
        var lines = new string[2000];
        for (int i = 0; i < lines.Length; i++) lines[i] = (i + 1) + " " + new string('x', 300);
        box.Text = string.Join("\r\n", lines);
        form.Controls.Add(box);
        var timer = new Timer { Interval = 100 };
        timer.Tick += (s, e) =>
        {
            string[] rows;
            lock (ScrollBox.Rows) { rows = ScrollBox.Rows.ToArray(); ScrollBox.Rows.Clear(); }
            if (rows.Length == 0) return;
            try { File.AppendAllLines(@"C:\results\scroll.csv", rows); }
            catch (IOException) { lock (ScrollBox.Rows) ScrollBox.Rows.InsertRange(0, rows); } // the probe was reading: try again next tick
        };
        timer.Start();
        form.Shown += (s, e) => { form.Activate(); box.Focus(); box.Select(0, 0); };
        Application.Run(form);
    }
}

'@ -ReferencedAssemblies System.Windows.Forms, System.Drawing
[ScrollTarget]::Run()
