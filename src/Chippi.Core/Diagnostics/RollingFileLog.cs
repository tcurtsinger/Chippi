// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging;

namespace Chippi.Core.Diagnostics;

/// <summary>
/// Plain-text log under %LOCALAPPDATA%\Chippi\logs. When chippi.log would pass
/// <see cref="MaxFileBytes"/> it rolls to chippi.1.log (older files shift up), and only
/// <see cref="MaxFiles"/> files are kept, so the folder never grows past their product.
/// Logging never throws.
/// </summary>
public sealed class RollingFileLog(string directory, string baseName = "chippi", long maxFileBytes = 1024 * 1024, int maxFiles = 5)
{
    private readonly Lock _gate = new();

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi", "logs");

    public string Directory { get; } = directory;

    public long MaxFileBytes { get; } = maxFileBytes;

    public int MaxFiles { get; } = Math.Max(1, maxFiles);

    public string CurrentFile => Path.Combine(Directory, baseName + ".log");

    public void Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Abbreviation(level)} [{category}] {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;
        line = FitToCap(line + Environment.NewLine);

        // An antivirus or indexer can hold the file for a moment right after login; retry
        // briefly rather than dropping the entry.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                lock (_gate)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    RollIfNeeded(System.Text.Encoding.UTF8.GetByteCount(line));
                    using var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    writer.Write(line);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 2)
                    Thread.Sleep(20);
            }
        }
    }

    private void RollIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(CurrentFile);
        if (!current.Exists || current.Length + incomingBytes <= MaxFileBytes || current.Length == 0)
            return;

        // chippi.(N-1).log is the oldest kept; shift everything up one and start fresh.
        File.Delete(Numbered(MaxFiles - 1));
        for (int i = MaxFiles - 2; i >= 1; i--)
        {
            if (File.Exists(Numbered(i)))
                File.Move(Numbered(i), Numbered(i + 1), overwrite: true);
        }
        if (MaxFiles > 1)
            File.Move(CurrentFile, Numbered(1), overwrite: true);
        else
            File.Delete(CurrentFile);
    }

    /// <summary>An entry larger than one file (a huge exception, say) is cut so no file ever passes the cap.</summary>
    private string FitToCap(string entry)
    {
        var utf8 = System.Text.Encoding.UTF8;
        if (utf8.GetByteCount(entry) <= MaxFileBytes)
            return entry;
        var marker = " [truncated]" + Environment.NewLine;
        long budget = MaxFileBytes - utf8.GetByteCount(marker);
        if (budget <= 0)
            return "";
        int length = (int)Math.Min(entry.Length, budget); // at most one byte per char
        while (length > 0 && utf8.GetByteCount(entry.AsSpan(0, length)) > budget)
            length--;
        if (length > 0 && char.IsHighSurrogate(entry[length - 1]))
            length--; // never split a surrogate pair
        return entry[..length] + marker;
    }

    private string Numbered(int i) => Path.Combine(Directory, $"{baseName}.{i}.log");

    private static string Abbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };
}

/// <summary>Bridges <see cref="ILogger"/> to a <see cref="RollingFileLog"/>.</summary>
public sealed class RollingFileLoggerProvider(RollingFileLog log, LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(log, categoryName, minimumLevel);

    public void Dispose()
    {
    }

    private sealed class FileLogger(RollingFileLog log, string category, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                log.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}

/// <summary>
/// The process-wide log, for code that runs before dependency injection exists (startup,
/// unhandled-exception handlers). Everything else should take an <see cref="ILogger"/>.
/// </summary>
public static class Log
{
    public static RollingFileLog Default { get; set; } = new(RollingFileLog.DefaultDirectory);

    public static void Info(string message) => Default.Write(LogLevel.Information, "App", message);

    public static void Warn(string message, Exception? exception = null) => Default.Write(LogLevel.Warning, "App", message, exception);

    public static void Error(string message, Exception? exception = null) => Default.Write(LogLevel.Error, "App", message, exception);
}
