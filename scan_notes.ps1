$content = Get-Content 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\extracted_notes.txt' -Raw
Write-Host "Total length:" $content.Length "characters"
Write-Host ""

# Show different sections
Write-Host "=== MIDDLE SECTION (around 500k) ==="
Write-Host $content.Substring(500000, 5000)
Write-Host ""
Write-Host "=== LATER SECTION (around 1M) ==="
Write-Host $content.Substring(1000000, 5000)
Write-Host ""
Write-Host "=== END SECTION (last 5000) ==="
Write-Host $content.Substring($content.Length - 5000)
