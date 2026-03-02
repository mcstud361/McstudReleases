$content = Get-Content 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\extracted_notes.txt' -Raw

# Clean up content
$content = $content -replace '\s+', ' '

# Extract all definitions with -Term- pattern
$definitions = @()
$matches = [regex]::Matches($content, '-([A-Za-z][^-]{2,80})-\s*([^-]{15,800})')

foreach ($m in $matches) {
    $term = $m.Groups[1].Value.Trim()
    $desc = $m.Groups[2].Value.Trim()

    # Skip invalid entries
    if ($term.Length -lt 3 -or $term.Length -gt 100) { continue }
    if ($desc.Length -lt 20) { continue }
    if ($term -match '^\d+$') { continue }
    if ($term -match 'wrong|Wrong|X wrong') { continue }

    # Clean up description
    $desc = $desc -replace '"', "'"
    $desc = $desc -replace '[\r\n]', ' '

    $definitions += [PSCustomObject]@{
        term = $term
        content = $desc
    }
}

# Remove duplicates based on term
$unique = $definitions | Sort-Object term -Unique

Write-Host "Extracted" $unique.Count "unique definitions"

# Convert to JSON and save
$json = $unique | ConvertTo-Json -Depth 3
$json | Out-File -FilePath 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\Data\EstimatingNotes.json' -Encoding UTF8

Write-Host "Saved to Data\EstimatingNotes.json"
