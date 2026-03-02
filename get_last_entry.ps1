$content = Get-Content 'C:\Users\mcnee\Downloads\McStudDesktop\McStudDesktop\Data\Definitions.json' -Raw
Write-Host "Last 500 chars:"
Write-Host $content.Substring($content.Length - 500)
