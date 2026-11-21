# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The scrolling check's long text window, inside the Sandbox: "Long text", a rich text box of 1,000 lines. Each line starts with
# the index stripe of scrolling-verify.ps1 (four cells of two spaces, their backgrounds the eight palette colors spelling
# the number in base 8), so a capture of it can be checked row for row. Args: left top width height.
param([int]$Left = 0, [int]$Top = 0, [int]$Width = 900, [int]$Height = 700)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
. (Join-Path $PSScriptRoot 'scrolling-verify.ps1')

$colors = for ($i = 0; $i -lt 8; $i++) {
    $c = [System.Drawing.ColorTranslator]::FromHtml([ScrollVerify]::Hex[$i])
    '\red' + $c.R + '\green' + $c.G + '\blue' + $c.B + ';'
}
$rtf = New-Object System.Text.StringBuilder
[void]$rtf.Append('{\rtf1\ansi\deff0{\fonttbl{\f0\fmodern Consolas;}}{\colortbl ;' + ($colors -join '') + '}\f0\fs28 ')
for ($n = 0; $n -lt 1000; $n++) {
    $digits = @((($n -shr 9) -band 7), (($n -shr 6) -band 7), (($n -shr 3) -band 7), ($n -band 7))
    foreach ($d in $digits) { [void]$rtf.Append('\highlight' + ($d + 1) + "\'a0\'a0") }
    [void]$rtf.Append('\highlight0 \''a0Line ' + $n.ToString('0000') + ' ' + ('lorem ipsum dolor ' * (1 + $n % 3)) + '\par ')
}
[void]$rtf.Append('}')

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Long text'
$form.StartPosition = 'Manual'
$form.Bounds = New-Object System.Drawing.Rectangle $Left, $Top, $Width, $Height
$box = New-Object System.Windows.Forms.RichTextBox
$box.Dock = 'Fill'
$box.ReadOnly = $true
$box.WordWrap = $false
$box.DetectUrls = $false
$box.ScrollBars = 'Vertical'
$box.BackColor = [System.Drawing.Color]::White
$box.Rtf = $rtf.ToString()
$form.Controls.Add($box)
$form.Add_Shown({ $form.Activate(); $box.Select(0, 0) })
[System.Windows.Forms.Application]::Run($form)
