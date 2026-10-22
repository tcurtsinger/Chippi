// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Markup;

// Lets any XAML (App or a feature module) write xmlns:c="urn:chippi-ui" instead of a clr-namespace.
[assembly: XmlnsDefinition("urn:chippi-ui", "Chippi.UI.Controls")]
[assembly: XmlnsDefinition("urn:chippi-ui", "Chippi.UI.Themes")]
[assembly: XmlnsPrefix("urn:chippi-ui", "c")]
