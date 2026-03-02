# Extract images from Excel file
$excelPath = "C:\Users\mcnee\OneDrive\Remote Estimating\App\3.0\MET Information.xlsx"
$outputDir = "C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\Assets\GuideImages"

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Excel files are actually ZIP archives, so we can extract images directly
$tempDir = "$env:TEMP\ExcelExtract_$(Get-Random)"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Copy and rename to .zip
$zipPath = "$tempDir\excel.zip"
Copy-Item $excelPath $zipPath

# Extract the zip
Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force

# Find all images
$mediaDir = "$tempDir\xl\media"
if (Test-Path $mediaDir) {
    Write-Host "Found images in Excel file:"
    $images = Get-ChildItem -Path $mediaDir -File
    $counter = 1
    foreach ($img in $images) {
        Write-Host "  $($img.Name) - $([math]::Round($img.Length/1KB, 1)) KB"

        # Copy to output with descriptive names based on order
        $ext = $img.Extension
        $newName = "guide_image_$counter$ext"
        Copy-Item $img.FullName "$outputDir\$newName"
        $counter++
    }
    Write-Host ""
    Write-Host "Copied $($images.Count) images to: $outputDir"
} else {
    Write-Host "No media folder found - checking for drawings..."

    # Check for drawings
    $drawingsDir = "$tempDir\xl\drawings"
    if (Test-Path $drawingsDir) {
        Write-Host "Found drawings folder"
        Get-ChildItem -Path $drawingsDir -Recurse | ForEach-Object { Write-Host "  $($_.Name)" }
    }
}

# Also check for embedded objects
$embeddingsDir = "$tempDir\xl\embeddings"
if (Test-Path $embeddingsDir) {
    Write-Host ""
    Write-Host "Found embeddings:"
    Get-ChildItem -Path $embeddingsDir -Recurse | ForEach-Object { Write-Host "  $($_.Name)" }
}

# Cleanup
Remove-Item $tempDir -Recurse -Force

Write-Host ""
Write-Host "Done! Check $outputDir for extracted images."
