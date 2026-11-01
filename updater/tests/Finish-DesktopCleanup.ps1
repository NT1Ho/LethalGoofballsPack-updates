$ErrorActionPreference='Stop'
Add-Type -AssemblyName Microsoft.VisualBasic
$source='C:\Users\nono4\OneDrive\Desktop\LethalGoofballsPack_Updater'
$destination='D:\Lethal company pack Updater'
$mapping=@{
 'LethalGoofballsPack Publisher.exe'='管理端\LethalGoofballsPack Publisher.exe'
 'Lethal company pack Updater.exe'='接收端\Lethal company pack Updater.exe'
 '源码.zip'='管理端\源码.zip'
 '验证记录.txt'='管理端\验证记录.txt'
 '使用说明.txt'='使用说明.txt'
}
if((Resolve-Path -LiteralPath $source).Path -ne $source){throw 'Unexpected cleanup directory'}
foreach($file in Get-ChildItem -LiteralPath $source -Force){
 if($file.PSIsContainer -or !$mapping.ContainsKey($file.Name)){throw 'Unexpected file in source folder'}
 if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destination $mapping[$file.Name])).Hash){throw 'Backup differs'}
 [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($file.FullName,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
}
if(@(Get-ChildItem -LiteralPath $source -Force).Count -eq 0){Remove-Item -LiteralPath $source}
