$content = Get-Content 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\extracted_notes.txt' -Raw

# Find all definitions/terms (patterns like "-Term Name-" or "Term Name -")
$definitions = @()

# Pattern 1: -Term- format
$matches1 = [regex]::Matches($content, '-([A-Z][^-]{2,50})-\s*([^-]{10,500})')
foreach ($m in $matches1) {
    $term = $m.Groups[1].Value.Trim()
    $desc = $m.Groups[2].Value.Trim()
    if ($term.Length -gt 3 -and $desc.Length -gt 20) {
        $definitions += @{Term=$term; Description=$desc}
    }
}

Write-Host "Found" $definitions.Count "definitions"
Write-Host ""

# Show first 30 definitions
$count = 0
foreach ($def in $definitions) {
    if ($count -ge 30) { break }
    Write-Host "=== $($def.Term) ==="
    Write-Host $def.Description.Substring(0, [Math]::Min(200, $def.Description.Length))
    Write-Host ""
    $count++
}
