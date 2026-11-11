$proc = Start-Process -FilePath "V:\GitHub Repos\File-converter\publish\RadialConvert.exe" -WorkingDirectory "V:\GitHub Repos\File-converter\publish" -PassThru
$proc.WaitForExit()
