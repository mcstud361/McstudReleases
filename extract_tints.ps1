$excel = New-Object -ComObject Excel.Application
$excel.Visible = $false
$excel.DisplayAlerts = $false

$wb = $excel.Workbooks.Open("C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Invoice Generator - Shop.xlsx")
$ws = $wb.Worksheets.Item("Special Color Tints")

$tints = @()
for ($row = 7; $row -le 443; $row++) {
    $partNum = $ws.Cells.Item($row, 11).Text
    $desc = $ws.Cells.Item($row, 12).Text
    $priceRaw = $ws.Cells.Item($row, 13).Text
    if ($partNum -and $desc) {
        $price = $priceRaw -replace '[$,]', ''
        if ($price -eq '') { $price = '0' }
        $tints += [PSCustomObject]@{
            partNumber = $partNum
            description = $desc
            price = [decimal]$price
        }
    }
}

$json = $tints | ConvertTo-Json -Depth 3
$json | Out-File -FilePath "C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\Data\ColorTints.json" -Encoding UTF8

Write-Output "Exported $($tints.Count) tints to ColorTints.json"

$wb.Close($false)
$excel.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
