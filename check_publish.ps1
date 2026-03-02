$files = Get-ChildItem 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\publish' -Recurse
$size = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "Publish folder:"
Write-Host "  Files: $($files.Count)"
Write-Host "  Size: $([math]::Round($size / 1MB, 2)) MB"
Write-Host ""
Write-Host "Main executable: McstudDesktop.exe"
