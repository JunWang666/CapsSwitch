foreach ($name in @('AutoHotkey64', 'PowerToys.KeyboardManagerEngine')) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    foreach ($proc in $procs) {
        try {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            "killed $name ($($proc.Id))"
        } catch {
            "FAILED to kill $name ($($proc.Id)): $($_.Exception.Message)"
        }
    }
}
Start-Sleep -Seconds 2
"--- after ---"
Get-Process AutoHotkey64, PowerToys.KeyboardManagerEngine -ErrorAction SilentlyContinue | Select-Object Name, Id | Format-Table -AutoSize
