$wsh = New-Object -ComObject WScript.Shell

$startupFolder = [Environment]::GetFolderPath('Startup')
$startupPath = Join-Path $startupFolder "OpenTangerine.lnk"
$shortcut = $wsh.CreateShortcut($startupPath)
$shortcut.TargetPath = "V:\GitHub Repos\File-converter\publish\RadialConvert.exe"
$shortcut.WorkingDirectory = "V:\GitHub Repos\File-converter\publish"
$shortcut.Description = "Open Tangerine - Instant File Converter"
$shortcut.Save()

$desktopFolder = [Environment]::GetFolderPath('Desktop')
$desktopPath = Join-Path $desktopFolder "Open Tangerine.lnk"
$shortcut2 = $wsh.CreateShortcut($desktopPath)
$shortcut2.TargetPath = "V:\GitHub Repos\File-converter\publish\RadialConvert.exe"
$shortcut2.WorkingDirectory = "V:\GitHub Repos\File-converter\publish"
$shortcut2.Description = "Open Tangerine - Instant File Converter"
$shortcut2.Save()

Write-Host "Created Startup shortcut: $startupPath"
Write-Host "Created Desktop shortcut: $desktopPath"
