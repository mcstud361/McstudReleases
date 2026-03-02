$text = Get-Content "C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\extracted_notes.txt" -Raw
# Split into sentences
$sentences = $text -split '(?<=[.!?])\s+'
foreach ($s in $sentences) {
    if ($s -match 'carbon|fiber|composite|fiberglass|smc' -and $s.Length -gt 20) {
        Write-Host "---"
        Write-Host $s.Trim()
    }
}
