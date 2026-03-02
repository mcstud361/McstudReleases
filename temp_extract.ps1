Add-Type -AssemblyName System.IO.Compression.FileSystem
$docxPath = "C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\Estimate Notes.docx"
$zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
$entry = $zip.Entries | Where-Object { $_.FullName -eq 'word/document.xml' }
$stream = $entry.Open()
$reader = New-Object System.IO.StreamReader($stream)
$content = $reader.ReadToEnd()
$reader.Close()
$stream.Close()
$zip.Dispose()
$text = $content -replace '<[^>]+>', "`n" -replace '\s+', ' '
$text
