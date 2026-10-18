Get-Process | Where-Object { $_.Name -match 'powertoys|keyboard|caps|sogou|rime|weasel|autohotkey|sharpkeys|keyman|input' } | Select-Object Name, Id | Format-Table -AutoSize
