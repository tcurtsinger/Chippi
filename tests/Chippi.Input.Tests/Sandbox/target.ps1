# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# The app the probe's input is aimed at, inside the Sandbox: a text box at 100,100 that logs every mouse button and key
# reaching it (C:\results\target.log) and keeps its text in C:\results\target-text.txt. With -Fullscreen, a borderless
# window covering the screen, as a game is, logging to C:\results\fullscreen.log. With -Other, a second, short window at
# 400,0 for the App switcher to switch to, logging to C:\results\other.log and other-text.txt.
param([switch]$Fullscreen, [switch]$Other)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition @"
public class CapsProbeForm : System.Windows.Forms.Form
{
    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == 0x8042)
        {
            message.Result = new System.IntPtr(System.Windows.Forms.Control.IsKeyLocked(System.Windows.Forms.Keys.CapsLock) ? 1 : 0);
            return;
        }
        base.WndProc(ref message);
    }
}
"@
$log = if ($Fullscreen) { 'C:\results\fullscreen.log' } elseif ($Other) { 'C:\results\other.log' } else { 'C:\results\target.log' }
$textFile = if ($Other) { 'C:\results\other-text.txt' } else { 'C:\results\target-text.txt' }
$form = New-Object CapsProbeForm
$form.Text = if ($Fullscreen) { 'Probe game' } elseif ($Other) { 'Probe other' } else { 'Probe target' }
$form.StartPosition = 'Manual'
if ($Fullscreen) {
    $form.FormBorderStyle = 'None'
    $form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
}
elseif ($Other) {
    $form.Left = 400
    $form.Top = 0
    $form.Width = 400
    $form.Height = 95
}
else {
    $form.Left = 100
    $form.Top = 100
    $form.Width = 800
    $form.Height = 600
}
$box = New-Object System.Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = 'Fill'
$box.Add_MouseDown({ Add-Content -LiteralPath $log -Value "mouse down $($_.Button)" })
$box.Add_MouseUp({ Add-Content -LiteralPath $log -Value "mouse up $($_.Button)" })
$box.Add_KeyDown({ Add-Content -LiteralPath $log -Value "key down $($_.KeyCode)" })
$box.Add_KeyUp({ if ($_.KeyCode -eq 'F24') { Add-Content -LiteralPath $log -Value "key up $($_.KeyCode)" } })
if (-not $Fullscreen) { $box.Add_TextChanged({ Set-Content -LiteralPath $textFile -Value $box.Text -NoNewline -Encoding UTF8 }) }
$form.Controls.Add($box)
$form.Add_Shown({ $form.Activate(); $box.Focus() })
if ((Get-Content -LiteralPath 'C:\results\checks.txt') -match 'caps') {
    $box.Add_KeyUp({ Add-Content -LiteralPath $log -Value "key up $($_.KeyCode)" })
}
[System.Windows.Forms.Application]::Run($form)
