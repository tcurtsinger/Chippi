# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The app the probe's input is aimed at, inside the Sandbox: a text box at 100,100 that logs every mouse button and key
# reaching it (C:\results\target.log) and keeps its text in C:\results\target-text.txt.

Add-Type -AssemblyName System.Windows.Forms
$log = 'C:\results\target.log'
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Probe target'
$form.StartPosition = 'Manual'
$form.Left = 100
$form.Top = 100
$form.Width = 800
$form.Height = 600
$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = 'Fill'
$box.Add_MouseDown({ Add-Content -LiteralPath $log -Value "mouse down $($_.Button)" })
$box.Add_MouseUp({ Add-Content -LiteralPath $log -Value "mouse up $($_.Button)" })
$box.Add_KeyDown({ Add-Content -LiteralPath $log -Value "key down $($_.KeyCode)" })
$box.Add_TextChanged({ Set-Content -LiteralPath 'C:\results\target-text.txt' -Value $box.Text -NoNewline })
$form.Controls.Add($box)
$form.Add_Shown({ $form.Activate(); $box.Focus() })
[System.Windows.Forms.Application]::Run($form)
