$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$wb = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Invoice Generator - Shop.xlsx")

# Read Special Color Tints sheet
Write-Output "=== SPECIAL COLOR TINTS SHEET ==="
$ws = $wb.Worksheets.Item("Special Color Tints")
$usedRange = $ws.UsedRange
$rowCount = $usedRange.Rows.Count
$colCount = $usedRange.Columns.Count
Write-Output "Used Range - $rowCount rows x $colCount columns"

for ($row = 1; $row -le [Math]::Min($rowCount, 40); $row++) {
    $rowData = @()
    for ($col = 1; $col -le [Math]::Min($colCount, 12); $col++) {
        $cell = $ws.Cells.Item($row, $col)
        $val = $cell.Text
        if ($val) {
            $rowData += "[$col]$val"
        }
    }
    if ($rowData.Count -gt 0) {
        Write-Output "R${row} - $($rowData -join ' | ')"
    }
}

# Read Material Costs sheet
Write-Output "`n=== MATERIAL COSTS SHEET ==="
$ws = $wb.Worksheets.Item("Material Costs")
$usedRange = $ws.UsedRange
$rowCount = $usedRange.Rows.Count
$colCount = $usedRange.Columns.Count
Write-Output "Used Range - $rowCount rows x $colCount columns"

for ($row = 1; $row -le [Math]::Min($rowCount, 40); $row++) {
    $rowData = @()
    for ($col = 1; $col -le [Math]::Min($colCount, 12); $col++) {
        $cell = $ws.Cells.Item($row, $col)
        $val = $cell.Text
        if ($val) {
            $rowData += "[$col]$val"
        }
    }
    if ($rowData.Count -gt 0) {
        Write-Output "R${row} - $($rowData -join ' | ')"
    }
}

# Read PPF Invoice sheet
Write-Output "`n=== PPF INVOICE SHEET ==="
$ws = $wb.Worksheets.Item("PPF Invoice")
$usedRange = $ws.UsedRange
$rowCount = $usedRange.Rows.Count
$colCount = $usedRange.Columns.Count
Write-Output "Used Range - $rowCount rows x $colCount columns"

for ($row = 1; $row -le [Math]::Min($rowCount, 50); $row++) {
    $rowData = @()
    for ($col = 1; $col -le [Math]::Min($colCount, 12); $col++) {
        $cell = $ws.Cells.Item($row, $col)
        $val = $cell.Text
        if ($val) {
            $rowData += "[$col]$val"
        }
    }
    if ($rowData.Count -gt 0) {
        Write-Output "R${row} - $($rowData -join ' | ')"
    }
}

$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
