$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$workbook = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\MET Information.xlsx")

Write-Host "=== SHEETS IN WORKBOOK ==="
foreach($sheet in $workbook.Sheets) {
    Write-Host "Sheet: $($sheet.Name)"
}
Write-Host ""

foreach($sheet in $workbook.Sheets) {
    Write-Host "=========================================="
    Write-Host "=== $($sheet.Name) ==="
    Write-Host "=========================================="

    $usedRange = $sheet.UsedRange
    $rows = $usedRange.Rows.Count
    $cols = $usedRange.Columns.Count
    Write-Host "Rows: $rows, Cols: $cols"
    Write-Host ""

    for($r = 1; $r -le [Math]::Min($rows, 150); $r++) {
        $rowData = @()
        for($c = 1; $c -le [Math]::Min($cols, 12); $c++) {
            $val = $sheet.Cells.Item($r, $c).Text
            if($val -and $val.Trim() -ne "") {
                $rowData += $val.Trim()
            }
        }
        if($rowData.Count -gt 0) {
            Write-Host ($rowData -join " | ")
        }
    }
    Write-Host ""
}

$workbook.Close($false)
$excel.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
