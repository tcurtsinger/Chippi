// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text.RegularExpressions;
using Chippi.UI.Displays;

namespace Chippi.Features.Displays;

/// <summary>
/// Brightness and contrast for every display (docs/phase-7/displays.md): one <see cref="MonitorWorker"/> per display, the
/// levels last read, the brightness keys and the All displays slider. Nothing here touches a monitor on the calling thread.
/// </summary>
internal sealed partial class DisplayBrightness : IDisposable
{
    private readonly IMonitorBus _bus;
    private readonly Func<IReadOnlyList<DisplayInfo>> _displays;
    private readonly Func<nint?> _underPointer;
    private readonly Func<long> _nowMs;
    private readonly Action<TimeSpan> _sleep;
    private readonly object _gate = new();
    private readonly Dictionary<string, MonitorWorker> _workers = [];
    private IReadOnlyList<(DisplayInfo Info, string Name)> _list = [];
    private Dictionary<string, int>? _dragStart;
    private int _dragMain;
    private bool _disposed;

    public DisplayBrightness(IMonitorBus bus, Func<IReadOnlyList<DisplayInfo>> displays, Func<nint?> underPointer, Func<long>? nowMs = null, Action<TimeSpan>? sleep = null)
    {
        _bus = bus;
        _displays = displays;
        _underPointer = underPointer;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>A level, a list or a failure changed. Raised on a worker's thread or the caller's.</summary>
    public event Action? Changed;

    /// <summary>A write or read didn't go through: the display's id, and whether a key press was behind it (the bar says so).</summary>
    public event Action<string, bool>? ControlFailed;

    /// <summary>Displays shown in the panel: the first four by display order (Settings lists every display).</summary>
    public const int PanelMax = 4;

    public IReadOnlyList<DisplayStatus> Statuses
    {
        get
        {
            lock (_gate)
                return [.. _list.Select(d => Status(d.Info, d.Name))];
        }
    }

    private DisplayStatus Status(DisplayInfo info, string name)
    {
        var state = _workers.TryGetValue(info.Id, out var worker) ? worker.State : new MonitorState(false, false, null, false, null, false);
        return new DisplayStatus(info.Id, name, info.IsMain, state.Brightness, state.HasContrast, state.Failed, state.Retrying)
        {
            ContrastLevel = state.Contrast,
            WriteFailed = state.WriteFailed,
        };
    }

    /// <summary>The tile's tooltip: "OMEN 34c 54% · Samsung 60%".</summary>
    public string Tooltip => string.Join(" · ", Statuses.Where(s => s.Brightness is not null && !s.Failed).Select(s => $"{s.Name} {s.Brightness}%"));

    /// <summary>
    /// Reads the displays again: new ones get a worker, gone ones lose theirs. <paramref name="reopen"/> (after a display change
    /// or a resume) also reopens each monitor's handle.
    /// </summary>
    public void Refresh(bool reopen = false)
    {
        var current = _displays();
        var removed = new List<MonitorWorker>();
        lock (_gate)
        {
            if (_disposed) return;
            _list = Named(current);
            foreach (string id in _workers.Keys.Except(current.Select(d => d.Id)).ToArray())
            {
                removed.Add(_workers[id]);
                _workers.Remove(id);
            }
            foreach (var info in current)
            {
                if (_workers.TryGetValue(info.Id, out var worker))
                {
                    if (reopen) worker.Reopen();
                    else worker.Read();
                    continue;
                }
                worker = new MonitorWorker(info, _bus, _nowMs, _sleep);
                worker.Changed += OnChanged;
                var id = info.Id;
                worker.ControlFailed += fromKey => ControlFailed?.Invoke(id, fromKey);
                _workers[info.Id] = worker;
                worker.Read();
            }
        }
        foreach (var worker in removed)
        {
            worker.Changed -= OnChanged;
            worker.Dispose();
        }
        Changed?.Invoke();
    }

    private void OnChanged() => Changed?.Invoke();

    /// <summary>Names from the catalog; two of the same model get " 1" and " 2" in display order.</summary>
    internal static IReadOnlyList<(DisplayInfo Info, string Name)> Named(IReadOnlyList<DisplayInfo> displays)
    {
        string Base(DisplayInfo d) => d.Friendly.Length > 0 ? d.Friendly : d.Name;
        var counts = displays.GroupBy(Base).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var list = new List<(DisplayInfo, string)>();
        foreach (var d in displays)
        {
            string name = Base(d);
            if (counts[name] > 1)
            {
                seen[name] = seen.GetValueOrDefault(name) + 1;
                name = $"{name} {seen[name]}";
            }
            list.Add((d, name));
        }
        return list;
    }

    public void Set(string displayId, Vcp code, int percent)
    {
        MonitorWorker? worker;
        lock (_gate) _workers.TryGetValue(displayId, out worker);
        worker?.Set(code, percent);
    }

    /// <summary>Try again: reopens that display's handle and reads it again.</summary>
    public void TryAgain(string displayId)
    {
        MonitorWorker? worker;
        lock (_gate) _workers.TryGetValue(displayId, out worker);
        worker?.Reopen(retrying: true);
    }

    /// <summary>
    /// The All displays slider's drag starts: each display's level now is its start, so a display that stops at 0 or 100
    /// stays there while the others go on.
    /// </summary>
    public void BeginAll()
    {
        lock (_gate)
        {
            var statuses = _list.Select(d => Status(d.Info, d.Name)).Where(s => s is { Failed: false, Brightness: not null }).ToList();
            _dragStart = statuses.ToDictionary(s => s.Id, s => s.Brightness!.Value);
            _dragMain = AllLevel(statuses) ?? 0;
        }
    }

    /// <summary>The All displays slider's value: the main display's level (or the first that answers).</summary>
    public int? AllLevel() => AllLevel(Statuses);

    private static int? AllLevel(IReadOnlyList<DisplayStatus> statuses) =>
        (statuses.FirstOrDefault(s => s is { IsMain: true, Failed: false, Brightness: not null }) ?? statuses.FirstOrDefault(s => s is { Failed: false, Brightness: not null }))?.Brightness;

    /// <summary>Moves every display by what the slider moved since <see cref="BeginAll"/>.</summary>
    public void MoveAll(int sliderValue)
    {
        Dictionary<string, int>? start;
        int main;
        lock (_gate)
        {
            start = _dragStart;
            main = _dragMain;
        }
        if (start is null) return;
        int delta = sliderValue - main;
        foreach (var (id, level) in start)
            Set(id, Vcp.Brightness, Math.Clamp(level + delta, 0, 100));
    }

    public void EndAll()
    {
        lock (_gate) _dragStart = null;
    }

    /// <summary>
    /// A brightness key: the display under the pointer moves by <paramref name="delta"/> percent, or every display with All
    /// displays. Hands the press to the workers and returns.
    /// </summary>
    public void Step(int delta, BrightnessTarget target)
    {
        List<MonitorWorker> targets = [];
        lock (_gate)
        {
            if (target == BrightnessTarget.AllDisplays)
                targets.AddRange(_list.Select(d => _workers.GetValueOrDefault(d.Info.Id)).OfType<MonitorWorker>());
            else if (TargetInfo() is { } info && _workers.TryGetValue(info.Id, out var worker))
                targets.Add(worker);
        }
        foreach (var worker in targets) worker.Step(Vcp.Brightness, delta);
    }

    // The display under the pointer, else the main one. Call with the gate held.
    private DisplayInfo? TargetInfo()
    {
        nint? under = _underPointer();
        return _list.FirstOrDefault(d => under is not null && d.Info.Handle == under).Info ?? _list.FirstOrDefault(d => d.Info.IsMain).Info;
    }

    /// <summary>The id of the display under the pointer among <paramref name="allowed"/>, else null.</summary>
    public string? UnderPointerId(IReadOnlyList<DisplayStatus> allowed)
    {
        lock (_gate)
            return TargetInfo() is { } info && allowed.Any(a => a.Id == info.Id) ? info.Id : null;
    }

    /// <summary>"brightness 40", "bright 40%": a level to set, or null when the text isn't one. Over 100 is 100.</summary>
    public static bool TryParseLevel(string text, out int percent)
    {
        percent = 0;
        var m = LevelText().Match(text.Trim());
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return false;
        percent = Math.Min(value, 100);
        return true;
    }

    [GeneratedRegex(@"^(?:brightness|bright)\s+(\d{1,9})\s*%?$", RegexOptions.IgnoreCase)]
    private static partial Regex LevelText();

    public void Dispose()
    {
        List<MonitorWorker> workers;
        lock (_gate)
        {
            _disposed = true;
            workers = [.. _workers.Values];
            _workers.Clear();
        }
        foreach (var worker in workers)
        {
            worker.Changed -= OnChanged;
            worker.Dispose();
        }
    }
}
