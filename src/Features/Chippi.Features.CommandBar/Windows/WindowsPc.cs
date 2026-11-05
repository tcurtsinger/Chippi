// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.Features.CommandBar.Search;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Chippi.Features.CommandBar.Windows;

/// <summary>The real PC: the Start menu, Alt+Tab's windows, Windows Search, and Explorer's way of opening things.</summary>
public sealed class WindowsPc : IPc
{
    private const string SearchConnection = "Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\"";

    private readonly StartMenu _startMenu;
    private readonly ConcurrentDictionary<string, string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<StartApp> _apps = [];
    private int _reading;
    private readonly Func<string, int, CancellationToken, IReadOnlyList<FoundFile>> _query;
    private readonly SemaphoreSlim _fileQuery = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private readonly TimeSpan _fileTimeout;
    private readonly TimeProvider _timeProvider;
    private int _disposed;

    public WindowsPc() : this(Query) { }

    internal WindowsPc(Func<string, int, CancellationToken, IReadOnlyList<FoundFile>> query, StartMenu? startMenu = null, TimeSpan? fileTimeout = null, TimeProvider? timeProvider = null)
    {
        _query = query;
        _startMenu = startMenu ?? new();
        _fileTimeout = fileTimeout ?? TimeSpan.FromSeconds(5);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<StartApp> Apps => Volatile.Read(ref _apps);

    public event Action? AppsChanged;

    public void RefreshApps()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _reading, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                var apps = await _startMenu.ReadAsync();
                if (Volatile.Read(ref _disposed) == 0)
                {
                    Volatile.Write(ref _apps, apps);
                    AppsChanged?.Invoke();
                }
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0) { }
            catch (Exception ex) { Log.Warn("Command bar: reading the Start menu failed", ex); }
            finally { Volatile.Write(ref _reading, 0); }
        });
    }

    public Task<ImageSource?> AppIconAsync(StartApp app, int pixels) => _startMenu.IconAsync(app, pixels);

    public Task<ImageSource?> WindowIconAsync(OpenWindow window, int pixels) => _startMenu.WindowIconAsync(window, pixels);

    /// <summary>A file's, a folder's or a program's icon, <paramref name="pixels"/> square, or null; read off the UI thread and kept.</summary>
    public Task<ImageSource?> PathIconAsync(string path, int pixels) => _startMenu.PathIconAsync(path, pixels);

    public unsafe IReadOnlyList<OpenWindow> Windows()
    {
        uint self = (uint)Environment.ProcessId;
        var shell = PInvoke.GetShellWindow();
        var found = new List<OpenWindow>();
        PInvoke.EnumWindows((hwnd, _) =>
        {
            if (hwnd != shell && AltTab(hwnd) && Title(hwnd) is { Length: > 0 } title)
            {
                uint pid;
                PInvoke.GetWindowThreadProcessId(hwnd, &pid);
                if (pid != self)
                {
                    string? path = ImagePath(pid);
                    found.Add(new OpenWindow(hwnd, title, AppName(path, title)) { IconPath = path });
                }
            }
            return true;
        }, 0);
        return found;
    }

    /// <summary>Visible, not cloaked (another desktop, a suspended Store app), not a tool window, and owned only if it asks for the taskbar.</summary>
    private static unsafe bool AltTab(HWND hwnd)
    {
        if (!PInvoke.IsWindowVisible(hwnd))
            return false;
        var extended = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (extended.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) || extended.HasFlag(WINDOW_EX_STYLE.WS_EX_NOACTIVATE))
            return false;
        if (!PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER).IsNull && !extended.HasFlag(WINDOW_EX_STYLE.WS_EX_APPWINDOW))
            return false;
        int cloaked = 0;
        PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int));
        return cloaked == 0;
    }

    private static unsafe string Title(HWND hwnd)
    {
        int length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        char* text = stackalloc char[length + 1];
        int read = PInvoke.GetWindowText(hwnd, text, length + 1);
        return new string(text, 0, read);
    }

    /// <summary>The app's own name ("Google Chrome"), from its program's description; a Store app's frame says its title.</summary>
    private string AppName(string? path, string title)
    {
        if (path is null)
            return "App";
        if (path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return title;
        return _appNames.GetOrAdd(path, p =>
        {
            try
            {
                if (FileVersionInfo.GetVersionInfo(p).FileDescription is { Length: > 0 } description)
                    return description.Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return Path.GetFileNameWithoutExtension(p);
        });
    }

    private static unsafe string? ImagePath(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
            return null;
        char* path = stackalloc char[1024];
        uint length = 1024;
        return PInvoke.QueryFullProcessImageName((HANDLE)process.DangerousGetHandle(), PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, &length)
            ? new string(path, 0, (int)length) : null;
    }

    public async Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var caller = ct;
        using var timeout = new CancellationTokenSource(_fileTimeout, _timeProvider);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping.Token, timeout.Token);
        ct = lifetime.Token;
        ct.ThrowIfCancellationRequested();
        // Each word a prefix of a word in the name: "chr set" finds ChromeSetup.exe's "chromesetup", not "set" inside it.
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')]))
            .Where(w => w.Length > 0)
            .Select(w => $"\"{w}*\"")
            .ToList();
        if (words.Count == 0 || count <= 0)
            return [];
        // A canceled native query can take a moment to stop. New typing waits asynchronously instead of stacking workers.
        try
        {
            await _fileQuery.WaitAsync(ct);
            var query = Task.Run(() => _query(string.Join(" AND ", words), count, ct), ct);
            // Keep the gate until native work really ends, even if the caller timed out or changed the text.
            _ = query.ContinueWith(completed =>
            {
                if (completed.IsFaulted) _ = completed.Exception;
                _fileQuery.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return await query.WaitAsync(ct);
        }
        catch (OperationCanceledException ex) when (!caller.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
        {
            throw new FileSearchUnavailableException(new TimeoutException("Windows Search timed out", ex));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            Log.Warn("Command bar: Windows Search failed", ex);
            throw new FileSearchUnavailableException(ex);
        }
    }

    private static List<FoundFile> Query(string words, int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = new OleDbConnection(SearchConnection);
        connection.Open();
        // Start menu shortcuts are the apps' own rows.
        ct.ThrowIfCancellationRequested();
        var command = new OleDbCommand(
            $"SELECT TOP {count} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemFolderNameDisplay, System.ItemType FROM SystemIndex " +
            $"WHERE SCOPE='file:' AND System.ItemType <> '.lnk' AND CONTAINS(System.FileName, '{words}') ORDER BY System.Search.Rank DESC", connection)
        { CommandTimeout = 5 };
        OleDbDataReader? reader = null;
        using var cancel = new QueryCancellation(ct, command.Cancel, () =>
        {
            try { reader?.Dispose(); }
            finally { command.Dispose(); }
        });
        ct.ThrowIfCancellationRequested();
        reader = command.ExecuteReader();
        var found = new List<FoundFile>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is not string path || path.Length == 0)
                continue;
            string name = reader.GetValue(1) as string ?? Path.GetFileName(path);
            string folder = reader.GetValue(2) as string ?? "";
            bool isFolder = string.Equals(reader.GetValue(3) as string, "Directory", StringComparison.OrdinalIgnoreCase);
            found.Add(new FoundFile(path, name, folder, isFolder));
        }
        return found;
    }

    public void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

    public void Switch(nint window)
    {
        var hwnd = (HWND)window;
        if (!PInvoke.IsWindow(hwnd))
            throw new InvalidOperationException("Window closed");
        if (PInvoke.IsIconic(hwnd))
            PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.SetForegroundWindow(hwnd);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping.Cancel();
        _startMenu.Dispose();
    }

    /// <summary>Never run a native Cancel on the caller's UI thread, or overlap it with native object disposal.</summary>
    internal sealed class QueryCancellation : IDisposable
    {
        private readonly object _gate = new();
        private readonly Action _cleanup;
        private readonly CancellationTokenRegistration _registration;
        private bool _finished;

        internal QueryCancellation(CancellationToken token, Action cancel, Action cleanup)
        {
            _cleanup = cleanup;
            _registration = token.Register(() => ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_gate)
                {
                    if (_finished) return;
                    try { cancel(); }
                    catch (Exception ex) { Log.Warn("Command bar: canceling Windows Search failed", ex); }
                }
            }));
        }

        public void Dispose()
        {
            _registration.Dispose();
            lock (_gate)
            {
                if (_finished) return;
                _finished = true;
                _cleanup();
            }
        }
    }
}
