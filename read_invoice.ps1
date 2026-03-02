$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$wb = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Invoice Generator - Shop.xlsx")

Write-Output "=== SHEETS ==="
foreach($ws in $wb.Worksheets) {
    Write-Output "Sheet: $($ws.Name)"
}

Write-Output "`n=== FIRST SHEET CONTENT ==="
$ws = $wb.Worksheets.Item(1)
$usedRange = $ws.UsedRange
$rowCount = $usedRange.Rows.Count
$colCount = $usedRange.Columns.Count

Write-Output "Used Range: $rowCount rows x $colCount columns"

for ($row = 1; $row -le [Math]::Min($rowCount, 50); $row++) {
    $rowData = @()
    for ($col = 1; $col -le [Math]::Min($colCount, 15); $col++) {
        $cell = $ws.Cells.Item($row, $col)
        $val = $cell.Text
        if ($val) {
            $rowData += "[$col]$val"
        }
    }
    if ($rowData.Count -gt 0) {
        Write-Output "Row ${row} - $($rowData -join ' | ')"
    }
}

$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
