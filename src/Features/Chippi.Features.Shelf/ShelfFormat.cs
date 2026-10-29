// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows.Media;
using Chippi.UI.Controls;

namespace Chippi.Features.Shelf;

/// <summary>An item's glyph, name and caption (design notes #37, #42, #43), and the limit lines (#44, #45).</summary>
internal static class ShelfFormat
{
    /// <summary>How a file item stands on disk, read from attributes only.</summary>
    public enum FileState
    {
        There,
        Missing,
        OnlineOnly,
    }

    public static FileState State(ShelfItem item)
    {
        if (item.Kind != ShelfKind.Files)
            return FileState.There;
        if (ShelfService.Missing(item) > 0)
            return FileState.Missing;
        return item.Files is { Count: 1 } one && ShelfService.OnlineOnly(one[0]) ? FileState.OnlineOnly : FileState.There;
    }

    public static Geometry Glyph(ShelfItem item) => item.Kind switch
    {
        ShelfKind.Text => LucideIcons.AlignLeft,
        ShelfKind.Link => LucideIcons.Globe,
        ShelfKind.Picture => LucideIcons.Image,
        _ => State(item) switch
        {
            FileState.Missing => LucideIcons.FileX,
            FileState.OnlineOnly => LucideIcons.Cloud,
            _ => item.Files?.Count == 1 ? LucideIcons.File : LucideIcons.Files,
        },
    };

    /// <summary>"invoice-0412.pdf", "3 files", "Picture", the text's first line, the link without its scheme.</summary>
    public static string Name(ShelfItem item) => item.Kind switch
    {
        ShelfKind.Files when item.Files is { Count: 1 } one => System.IO.Path.GetFileName(one[0].TrimEnd('\\')),
        ShelfKind.Files => $"{item.Files?.Count ?? 0} files",
        ShelfKind.Picture => "Picture",
        ShelfKind.Link => LinkName(item.Text ?? ""),
        _ => FirstLine(item.Text ?? ""),
    };

    /// <summary>"PDF · 2.1 MB", "Files · 4.8 MB", "Text · 2 lines", "Link", "1280 × 720", "Unavailable", "Online only".</summary>
    public static string Caption(ShelfItem item)
    {
        switch (item.Kind)
        {
            case ShelfKind.Text:
                int lines = Lines(item.Text ?? "");
                return lines > 1 ? $"Text · {lines} lines" : "Text";
            case ShelfKind.Link:
                return "Link";
            case ShelfKind.Picture:
                return $"{item.Width} × {item.Height}";
        }
        switch (State(item))
        {
            case FileState.Missing:
                return "Unavailable";
            case FileState.OnlineOnly:
                return "Online only";
        }
        var files = item.Files ?? [];
        if (files.Count == 1)
        {
            string path = files[0];
            if (Directory.Exists(path))
                return "Folder";
            string kind = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            return $"{(kind.Length > 0 ? kind : "File")} · {Size(SizeOf(path))}";
        }
        return $"Files · {Size(files.Sum(SizeOf))}";
    }

    public static string Limit(ShelfLimit limit) => limit switch
    {
        ShelfLimit.Files => "Over 100 files",
        ShelfLimit.Picture => "Picture over 16 MiB",
        ShelfLimit.Full => "Shelf full · 128 MiB",
        ShelfLimit.Items => "Shelf full",
        _ => "",
    };

    /// <summary>The tile's state line, "Unavailable" or "Online only", or null for none.</summary>
    public static string? StateLine(ShelfItem item) => State(item) switch
    {
        FileState.Missing => "Unavailable",
        FileState.OnlineOnly => "Online only",
        _ => null,
    };

    public static string Size(long bytes) => bytes switch
    {
        < 1000 => $"{bytes} B",
        < 1_000_000 => $"{Math.Max(1, bytes / 1000)} KB",
        < 1_000_000_000 => (bytes / 1e6).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / 1e9).ToString("0.#", CultureInfo.CurrentCulture) + " GB",
    };

    public static int Lines(string text) => text.Length == 0 ? 0 : text.TrimEnd('\r', '\n').Split('\n').Length;

    /// <summary>A file's size from its attributes; a folder or an unreadable file counts nothing.</summary>
    private static long SizeOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string FirstLine(string text) =>
        text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";

    private static string LinkName(string text)
    {
        string link = text.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
            return link;
        string path = uri.AbsolutePath.TrimEnd('/');
        return uri.Host + (path.Length > 0 ? path : "");
    }
}
