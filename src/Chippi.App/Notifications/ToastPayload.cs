// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Xml.Linq;
using Chippi.Core.Notifications;

namespace Chippi.App.Notifications;

internal static class ToastPayload
{
    internal static string Build(string module, Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.Actions.Count > 5) throw new ArgumentException("Windows supports at most five notification actions");
        string Activation(string? command) => ToastActivation.Encode(module, ToastActivation.Parse(command));
        return new XElement("toast", new XAttribute("launch", Activation(notice.Activation)),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", notice.Title), string.IsNullOrEmpty(notice.Body) ? null : new XElement("text", notice.Body))),
            notice.Actions.Count == 0 ? null : new XElement("actions", notice.Actions.Select(action =>
                new XElement("action", new XAttribute("content", action.Label),
                    new XAttribute("arguments", Activation(action.Activation)), new XAttribute("activationType", "foreground")))))
            .ToString(SaveOptions.DisableFormatting);
    }
}