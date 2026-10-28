// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static Chippi.Clipboard.Windows.Native;

namespace Chippi.Clipboard.Windows;

/// <summary>
/// The real clipboard. One thread with a message-only window owns every open, write, delayed render
/// and the listener (OpenClipboard and CloseClipboard must run on the same thread). The thread starts
/// on first use, so nothing runs while no clipboard feature does anything.
/// </summary>
public sealed class WindowsClipboardSystem : IClipboardSystem
{
    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetry = TimeSpan.FromMilliseconds(10);

    private readonly BlockingCollection<Action> _work = [];
    private readonly Dictionary<uint, (Func<byte[]> Render, Action? Requested)> _delayed = [];
    private readonly Lock _gate = new();
    private Thread? _thread;
    private nint _hwnd;
    private WndProc? _proc;
    private bool _listening, _disposed;

    public event Action? Changed;

    public uint Sequence => GetClipboardSequenceNumber();

    public bool IsAvailable(string format) => IsClipboardFormatAvailable(Id(format));

    public string? OwnerPath() => ProcessPath(GetClipboardOwner());

    public bool Listening
    {
        get { lock (_gate) return _listening; }
        set
        {
            lock (_gate)
            {
                if (_listening == value || _disposed)
                    return;
                _listening = value;
            }
            Run(() =>
            {
                bool on;
                lock (_gate) on = _listening;
                if (on) AddClipboardFormatListener(_hwnd);
                else RemoveClipboardFormatListener(_hwnd);
            });
        }
    }

    public Task<T> TransactAsync<T>(Func<IClipboardSession, T> work, CancellationToken cancel = default)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Run(() =>
        {
            if (cancel.IsCancellationRequested) { done.TrySetCanceled(cancel); return; }
            bool open = false;
            for (int i = 0; i < OpenAttempts && !(open = OpenClipboard(_hwnd)); i++)
                Thread.Sleep(OpenRetry);
            if (!open) { done.TrySetException(new ClipboardBusyException()); return; }
            try
            {
                done.TrySetResult(work(new Session(this)));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
            finally
            {
                CloseClipboard();
            }
        });
        return done.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        if (_thread is null)
            return;
        // Destroying the window renders any delayed format still offered (WM_RENDERALLFORMATS).
        _work.Add(() => DestroyWindow(_hwnd));
        _work.CompleteAdding();
        PostMessageW(_hwnd, WmWake, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run(Action action)
    {
        EnsureThread();
        _work.Add(action);
        PostMessageW(_hwnd, WmWake, 0, 0);
    }

    private void EnsureThread()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is not null)
                return;
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "Chippi clipboard" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
        }
    }

    private void Loop(ManualResetEventSlim ready)
    {
        _proc = WindowProc;
        _hwnd = CreateMessageWindow("ChippiClipboard", _proc);
        ready.Set();
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            DispatchMessageW(ref msg);
            while (_work.TryTake(out var action))
                action();
        }
    }

    private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmWake:
                return 0;
            case WM_CLIPBOARDUPDATE:
                Changed?.Invoke();
                return 0;
            case WM_RENDERFORMAT:
                // The requester holds the clipboard open: set the data without opening it.
                Render((uint)wParam);
                return 0;
            case WM_RENDERALLFORMATS:
                if (OpenClipboard(hwnd))
                {
                    try
                    {
                        if (GetClipboardOwner() == hwnd)
                            foreach (uint format in _delayed.Keys.ToArray())
                                Render(format);
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                return 0;
            case WM_DESTROYCLIPBOARD:
                _delayed.Clear();
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void Render(uint format)
    {
        if (!_delayed.Remove(format, out var offer))
            return;
        SetClipboardData(format, ToGlobal(offer.Render()));
        offer.Requested?.Invoke();
    }

    private sealed class Session(WindowsClipboardSystem owner) : IClipboardSession
    {
        public uint Sequence => GetClipboardSequenceNumber();

        public string? OwnerPath() => ProcessPath(GetClipboardOwner());

        public bool IsAvailable(string format) => IsClipboardFormatAvailable(Id(format));

        public IReadOnlyList<string> Formats()
        {
            var formats = new List<string>();
            for (uint id = EnumClipboardFormats(0); id != 0; id = EnumClipboardFormats(id))
                if (Restorable(id))
                    formats.Add(Name(id));
            // CF_DIB is synthesized from CF_DIBV5 (and the other way); keep one.
            if (formats.Contains(ClipFormat.DibV5))
                formats.Remove(ClipFormat.Dib);
            return formats;
        }

        public long SizeOf(string format)
        {
            nint handle = GetClipboardData(Id(format));
            return handle == 0 ? -1 : (long)GlobalSize(handle);
        }

        public byte[]? Get(string format)
        {
            nint handle = GetClipboardData(Id(format));
            return handle == 0 ? null : FromGlobal(handle);
        }

        public void Empty()
        {
            EmptyClipboard();
            owner._delayed.Clear();
        }

        public void Set(string format, byte[] data) => SetClipboardData(Id(format), ToGlobal(data));

        public void SetDelayed(string format, Func<byte[]> render, Action? requested = null)
        {
            uint id = Id(format);
            owner._delayed[id] = (render, requested);
            SetClipboardData(id, 0);
        }
    }

    // Formats a restore can copy back as plain memory. Text in other code pages and bitmaps are
    // synthesized by Windows from CF_UNICODETEXT and CF_DIB; GDI handles can't be copied as bytes.
    private static bool Restorable(uint id) => id >= 0xC000 || id is CF_UNICODETEXT or CF_DIB or CF_DIBV5 or CF_HDROP or CF_LOCALE;

    private static readonly Dictionary<string, uint> Standard = new()
    {
        [ClipFormat.UnicodeText] = CF_UNICODETEXT,
        [ClipFormat.Dib] = CF_DIB,
        [ClipFormat.DibV5] = CF_DIBV5,
        [ClipFormat.Files] = CF_HDROP,
        ["CF_LOCALE"] = CF_LOCALE,
    };

    private static readonly ConcurrentDictionary<string, uint> Registered = new();

    private static uint Id(string format) =>
        Standard.TryGetValue(format, out uint id) ? id : Registered.GetOrAdd(format, RegisterClipboardFormatW);

    private static unsafe string Name(uint id)
    {
        foreach (var (name, standard) in Standard)
            if (standard == id)
                return name;
        char* buffer = stackalloc char[256];
        int length = GetClipboardFormatNameW(id, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : $"#{id}";
    }

    private static unsafe nint ToGlobal(byte[] data)
    {
        nint handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(1, data.Length));
        if (handle == 0)
            throw new OutOfMemoryException();
        void* p = (void*)GlobalLock(handle);
        data.CopyTo(new Span<byte>(p, data.Length));
        GlobalUnlock(handle);
        return handle;
    }

    private static unsafe byte[]? FromGlobal(nint handle)
    {
        void* p = (void*)GlobalLock(handle);
        if (p == null)
            return null;
        try
        {
            return new ReadOnlySpan<byte>(p, checked((int)GlobalSize(handle))).ToArray();
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    internal static string? ProcessPath(nint hwnd)
    {
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0)
            return null;
        return Native.ProcessPath(pid);
    }
}
