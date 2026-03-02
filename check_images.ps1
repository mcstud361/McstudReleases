Add-Type -AssemblyName System.IO.Compression.FileSystem
$docxPath = "C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Estimate Notes.docx"
$zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
$images = $zip.Entries | Where-Object { $_.FullName -like 'word/media/*' }
Write-Host "Found $($images.Count) images in document:"
foreach ($img in $images) {
    Write-Host "  $($img.FullName) - $($img.Length) bytes"
}
$zip.Dispose()
