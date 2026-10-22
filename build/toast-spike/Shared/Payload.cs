// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Xml.Linq;
using Chippi.Core.Notifications;

namespace Chippi.ToastSpike;

internal static class Payload
{
    // XML APIs escape file names, titles and opaque activation arguments.
    public static string Xml(Notice notice) => new XElement("toast",
        new XAttribute("launch", notice.Activation ?? ""),
        new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", notice.Title), new XElement("text", notice.Body ?? ""))),
        notice.Actions.Count == 0 ? null : new XElement("actions", notice.Actions.Select(action =>
            new XElement("action", new XAttribute("content", action.Label),
                new XAttribute("arguments", action.Activation), new XAttribute("activationType", "foreground")))))
        .ToString(SaveOptions.DisableFormatting);

    public static void Check()
    {
        var notice = new Notice("held", "A&B <file>")
        {
            Activation = "--organize \"C:\\A&B\"",
            Actions = [new("Review", "--review \"x&y\"")],
        };
        var xml = XDocument.Parse(Xml(notice));
        if (xml.Root!.Attribute("launch")!.Value != notice.Activation ||
            xml.Descendants("text").First().Value != notice.Title ||
            xml.Descendants("action").Single().Attribute("arguments")!.Value != notice.Actions[0].Activation)
            throw new InvalidOperationException("Toast payload round-trip failed.");
        Console.WriteLine("Payload escaping and body/button activation checks passed. No registration performed.");
    }
}
