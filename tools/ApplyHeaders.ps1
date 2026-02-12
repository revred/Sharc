$headerPath = "c:\Code\Sharc\src\Sharc.Core\BTree\BTreeCursor.cs"
$headerLines = Get-Content $headerPath -TotalCount 16
# Join with CRLF explicitly
$headerText = $headerLines -join "`r`n"
# Add two newlines: one to complete the header block, one for the empty line following it
$headerText += "`r`n`r`n"

$files = Get-ChildItem -Path "c:\Code\Sharc" -Recurse -Filter "*.cs" | Where-Object { 
    $_.FullName -notmatch "\\obj\\" -and 
    $_.FullName -notmatch "\\bin\\" -and 
    $_.FullName -notmatch "\\BenchmarkDotNet.Artifacts\\" -and
    $_.FullName -notmatch "\\.git\\"
}

foreach ($file in $files) {
    # Skip the reference file itself (though check would catch it)
    if ($file.FullName -eq $headerPath) { continue }
    
    $content = Get-Content $file.FullName -Raw
    if ($null -eq $content) { continue }

    # Check for signature phrase to avoid duplication
    if ($content.Contains("Where the mind is free to imagine")) {
        Write-Host "Skipping $($file.Name) - Header already present"
        continue
    }

    $newContent = $headerText + $content
    # Use UTF8 (no BOM usually preferred, but PS default might vary. Standardizing on UTF8)
    Set-Content -Path $file.FullName -Value $newContent -NoNewline -Encoding UTF8
    Write-Host "Updated $($file.Name)"
}
