$content = Get-Content 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\extracted_notes.txt' -Raw
Write-Host "Total length:" $content.Length
Write-Host ""
Write-Host "=== FIRST 5000 CHARACTERS ==="
Write-Host $content.Substring(0, [Math]::Min(5000, $content.Length))
