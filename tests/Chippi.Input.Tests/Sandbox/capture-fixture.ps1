# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The capture check's known pixels, inside the Sandbox: a window "Capture fixture" at 200,150, 600 x 400, its left half
# #FF2050 and its right half #20C060, so the frozen image can be checked against what's really on screen.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Capture fixture'
$form.StartPosition = 'Manual'
$form.Bounds = New-Object System.Drawing.Rectangle 200, 150, 600, 400
$left = New-Object System.Windows.Forms.Panel
$left.Dock = 'Left'
$left.Width = 290
$left.BackColor = [System.Drawing.Color]::FromArgb(0xFF, 0x20, 0x50)
$right = New-Object System.Windows.Forms.Panel
$right.Dock = 'Fill'
$right.BackColor = [System.Drawing.Color]::FromArgb(0x20, 0xC0, 0x60)
$form.Controls.Add($right)
$form.Controls.Add($left)
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
