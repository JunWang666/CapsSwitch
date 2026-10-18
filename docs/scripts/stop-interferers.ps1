Stop-Process -Name AutoHotkey64 -Force -ErrorAction SilentlyContinue
Stop-Process -Name PowerToys.KeyboardManagerEngine -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
$ahk = Get-Process AutoHotkey64 -ErrorAction SilentlyContinue
$kbm = Get-Process PowerToys.KeyboardManagerEngine -ErrorAction SilentlyContinue
"AHK alive: $($null -ne $ahk); KBM alive: $($null -ne $kbm)"
