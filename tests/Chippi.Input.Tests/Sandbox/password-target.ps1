# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# A window with a password box in front, inside the Sandbox (the snippets check): its text goes to C:\results\password-text.txt.
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Probe password'
$form.StartPosition = 'Manual'
$form.Left = 100
$form.Top = 100
$form.Width = 800
$form.Height = 600
$box = New-Object System.Windows.Forms.TextBox
$box.UseSystemPasswordChar = $true
$box.Dock = 'Top'
$box.Add_TextChanged({ Set-Content -LiteralPath 'C:\results\password-text.txt' -Value $box.Text -NoNewline -Encoding UTF8 })
$form.Controls.Add($box)
$form.Add_Shown({ $form.Activate(); $box.Focus() })
[System.Windows.Forms.Application]::Run($form)
