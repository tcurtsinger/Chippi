# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The capture results check's known screen, inside the Sandbox: a borderless window at 100,60, 1000 x 640. Left: a
# #FF2050 panel (screen x 100-350). Text "Quarterly review of the numbers" in black on white at screen 370..900 x 100..170.
# Below it a QR code (https://example.com/chippi-check) at screen 370..698 x 300..628, from results-qr.png.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Results fixture'
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = New-Object System.Drawing.Rectangle 100, 60, 1000, 640
$form.BackColor = [System.Drawing.Color]::White
$red = New-Object System.Windows.Forms.Panel
$red.Bounds = New-Object System.Drawing.Rectangle 0, 0, 250, 640
$red.BackColor = [System.Drawing.Color]::FromArgb(0xFF, 0x20, 0x50)
$text = New-Object System.Windows.Forms.Label
$text.Text = 'Quarterly review of the numbers'
$text.Font = New-Object System.Drawing.Font 'Segoe UI', 24
$text.ForeColor = [System.Drawing.Color]::Black
$text.AutoSize = $false
$text.Bounds = New-Object System.Drawing.Rectangle 270, 40, 600, 70
$text.TextAlign = 'MiddleLeft'
$qr = New-Object System.Windows.Forms.PictureBox
$qr.Image = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'results-qr.png'))
$qr.Bounds = New-Object System.Drawing.Rectangle 270, 240, 328, 328
$qr.SizeMode = 'Normal'
$damaged = New-Object System.Windows.Forms.PictureBox
$damaged.Image = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'results-qr-damaged.png'))
$damaged.Bounds = New-Object System.Drawing.Rectangle 640, 240, 328, 328
$form.Controls.AddRange(@($red, $text, $qr, $damaged))
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
