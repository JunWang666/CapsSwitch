$p = Get-Process notepad -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Start-Process notepad; Start-Sleep -Milliseconds 1500; $p = Get-Process notepad | Select-Object -First 1 }
"title=$($p.MainWindowTitle)"
$ws = New-Object -ComObject WScript.Shell
$ok = $ws.AppActivate($p.Id)
Start-Sleep -Milliseconds 800
"AppActivate=$ok"
