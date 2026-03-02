$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$wb = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Invoice Generator - Shop.xlsx")

# Read full Special Color Tints database (columns K and L)
Write-Output "=== FULL COLOR TINTS DATABASE ==="
$ws = $wb.Worksheets.Item("Special Color Tints")

$tints = @()
for ($row = 7; $row -le 443; $row++) {
    $partNum = $ws.Cells.Item($row, 11).Text
    $desc = $ws.Cells.Item($row, 12).Text
    $price = $ws.Cells.Item($row, 13).Text
    if ($partNum -and $desc) {
        Write-Output "$partNum|$desc|$price"
    }
}

# Also check column 14 for prices
Write-Output "`n=== CHECKING COLUMN 14 FOR PRICES ==="
for ($row = 7; $row -le 50; $row++) {
    $col14 = $ws.Cells.Item($row, 14).Text
    if ($col14) {
        Write-Output "R${row} Col14 - $col14"
    }
}

# Check what's in the invoice area pricing
Write-Output "`n=== INVOICE AREA (Columns E-F for pricing) ==="
for ($row = 23; $row -le 35; $row++) {
    $qty = $ws.Cells.Item($row, 2).Text
    $partNum = $ws.Cells.Item($row, 3).Text
    $desc = $ws.Cells.Item($row, 4).Text
    $unitPrice = $ws.Cells.Item($row, 5).Text
    $lineTotal = $ws.Cells.Item($row, 6).Text
    if ($qty -or $partNum -or $desc -or $unitPrice) {
        Write-Output "R${row} - Qty[$qty] Part[$partNum] Desc[$desc] Price[$unitPrice] Total[$lineTotal]"
    }
}

$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
