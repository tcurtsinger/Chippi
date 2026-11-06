// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenText;

/// <summary>What a QR code's text is (docs/phase-7/text-and-qr.md, Scan QR): its kind, and for a web link the address Open may use.</summary>
public static class QrPayload
{
    public static QrCodeResult Parse(string payload, BitmapSource? image = null)
    {
        string head = payload.TrimStart();
        if (head.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.WiFi, payload, image) { Fields = WiFi(head[5..]) };
        if (head.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase) || head.StartsWith("MECARD:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.Contact, payload, image) { Fields = Contact(head) };
        if (head.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || head.StartsWith("matmsg:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.Email, payload, image);
        if (head.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.Phone, payload, image);
        if (head.StartsWith("sms:", StringComparison.OrdinalIgnoreCase) || head.StartsWith("smsto:", StringComparison.OrdinalIgnoreCase) || head.StartsWith("mms:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.Message, payload, image);
        if (head.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
            return new QrCodeResult(QrKind.Location, payload, image);
        if (Uri.TryCreate(payload.Trim(), UriKind.Absolute, out var uri) && !payload.Trim().Contains(' '))
        {
            if (uri.Scheme is "http" or "https" && uri.Host.Length > 0)
                return new QrCodeResult(QrKind.Link, payload, image) { Link = uri, CheckAddress = NeedsCheck(uri.Host) };
            if (uri.Scheme.Length > 1 && !int.TryParse(uri.Scheme, out _) && payload.Trim().StartsWith(uri.Scheme + ":", StringComparison.OrdinalIgnoreCase))
                return new QrCodeResult(QrKind.Other, payload, image);
        }
        return new QrCodeResult(QrKind.Text, payload, image);
    }

    /// <summary>A host with a non-ASCII character or an <c>xn--</c> label could pass for another address.</summary>
    public static bool NeedsCheck(string host) =>
        host.Any(c => c > 127) || host.Split('.').Any(label => label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase));

    /// <summary>"S:name;T:WPA;P:secret;;" with <c>\;</c> <c>\:</c> <c>\,</c> <c>\\</c> <c>\"</c> escapes.</summary>
    private static IReadOnlyList<QrField> WiFi(string body)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();
        string? key = null;
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length)
                token.Append(body[++i]);
            else if (c == ':' && key is null)
            {
                key = token.ToString();
                token.Clear();
            }
            else if (c == ';')
            {
                if (key is not null)
                    parts[key] = token.ToString();
                key = null;
                token.Clear();
            }
            else
                token.Append(c);
        }
        if (key is not null)
            parts[key] = token.ToString();
        var fields = new List<QrField>();
        if (parts.TryGetValue("S", out var name))
            fields.Add(new QrField("Network", name));
        string security = parts.TryGetValue("T", out var t) && t.Length > 0 && !t.Equals("nopass", StringComparison.OrdinalIgnoreCase) ? t : "None";
        fields.Add(new QrField("Security", security));
        if (parts.TryGetValue("P", out var password) && password.Length > 0)
            fields.Add(new QrField("Password", password, Secret: true));
        return fields;
    }

    /// <summary>A contact's lines as text: "Name: Ada", never imported.</summary>
    private static IReadOnlyList<QrField> Contact(string text)
    {
        var fields = new List<QrField>();
        if (text.StartsWith("MECARD:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string part in text[7..].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.IndexOf(':');
                if (colon > 0)
                    fields.Add(new QrField(Label(part[..colon]), part[(colon + 1)..].Replace("\\", "")));
            }
            return fields;
        }
        foreach (string raw in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0)
                continue;
            string key = raw[..colon].Split(';')[0];
            if (key.Equals("BEGIN", StringComparison.OrdinalIgnoreCase) || key.Equals("END", StringComparison.OrdinalIgnoreCase) || key.Equals("VERSION", StringComparison.OrdinalIgnoreCase))
                continue;
            fields.Add(new QrField(Label(key), raw[(colon + 1)..].Replace(";", " ").Trim()));
        }
        return fields;
    }

    private static string Label(string key) => key.ToUpperInvariant() switch
    {
        "FN" or "N" => "Name",
        "TEL" => "Phone",
        "EMAIL" => "Email",
        "ORG" => "Organization",
        "TITLE" => "Title",
        "ADR" => "Address",
        "URL" => "Web",
        "NOTE" => "Note",
        _ => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant(),
    };
}
