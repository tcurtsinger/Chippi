# Settings layout API

Use `Chippi.UI.Controls.SettingsLayout` for the F2/F3 page conversions. Feature modules keep their state, bindings and handlers; these helpers only build the layout. They use the existing `Card`, `SettingRow` and `ExpanderRow` controls and theme resources.

| Factory | Result |
| --- | --- |
| `Section(title, action: null, first: false)` | Header aligned to the card edge, 24 above / 8 below; `first` removes the top 24. Header actions do not change its 20 height. |
| `Card(params UIElement[] rows)` | One existing card with dividers. Do not nest cards. |
| `Row(title, control: null, caption: null)` | Existing row, 16,8 padding, 48 minimum or 60 with a caption. Standard combo, button and segmented controls fit 48. |
| `Empty(text)` | One 48-high row, body-sized text in caption color, inside a card. |
| `Expander(title, value, toggle, params UIElement[] rows)` | Header value in secondary text, the caller's switch and chevron; sub-rows indented 50. Folding does not change values or switch state. |
| `Combo(items)` | Existing 32-high combo, width 180. Set selection and handlers in the feature. |
| `AddButton(thing, clicked)` | Plus glyph followed by `Add <thing>`, using the existing button style. |

```csharp
var threshold = SettingsLayout.Combo(new[] { "90%", "95%" });
threshold.SelectedIndex = 0;
// Supply the feature's existing switch and handlers.
var value = new TextBlock { Text = "90%" };
page.Children.Add(SettingsLayout.Section("Alerts"));
page.Children.Add(SettingsLayout.Card(
    SettingsLayout.Expander("CPU load", value, existingToggle,
        SettingsLayout.Row("Threshold", threshold))));
```

Keep a reference to the value element when its text changes live, or bind it. The caller owns controls and callbacks. A caption added or removed later updates the row's minimum height through the existing template triggers. Existing explicit control names remain intact.

The About page is the proof migration. Its title, document views, buttons, handlers and copy remain as before. The other private page-helper sets are converted by their F2/F3 owners.
