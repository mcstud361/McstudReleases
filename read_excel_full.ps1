$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$workbook = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\MET Information.xlsx")

# Focus on "How to use MET" sheet
$sheet = $workbook.Sheets.Item("How to use MET")

Write-Host "=========================================="
Write-Host "=== How to use MET - FULL CONTENT ==="
Write-Host "=========================================="

$usedRange = $sheet.UsedRange
$rows = $usedRange.Rows.Count
$cols = $usedRange.Columns.Count
Write-Host "Total Rows: $rows, Cols: $cols"
Write-Host ""

for($r = 1; $r -le $rows; $r++) {
    $rowData = @()
    for($c = 1; $c -le [Math]::Min($cols, 25); $c++) {
        $val = $sheet.Cells.Item($r, $c).Text
        if($val -and $val.Trim() -ne "") {
            $rowData += $val.Trim()
        }
    }
    if($rowData.Count -gt 0) {
        Write-Host "Row $r : $($rowData -join ' | ')"
    }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "=== Descriptions and Notes - FULL ==="
Write-Host "=========================================="

$sheet2 = $workbook.Sheets.Item("Descriptions and Notes")
$usedRange2 = $sheet2.UsedRange
$rows2 = $usedRange2.Rows.Count

for($r = 1; $r -le $rows2; $r++) {
    $rowData = @()
    for($c = 1; $c -le 5; $c++) {
        $val = $sheet2.Cells.Item($r, $c).Text
        if($val -and $val.Trim() -ne "") {
            $rowData += $val.Trim()
        }
    }
    if($rowData.Count -gt 0) {
        Write-Host "Row $r : $($rowData -join ' | ')"
    }
}

$workbook.Close($false)
$excel.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
