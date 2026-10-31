$ErrorActionPreference='Stop'
$steam='D:\STEAM\steamapps\common\Lethal Company'
$stage=Join-Path $PSScriptRoot 'stage'
if(Get-CimInstance Win32_Process -Filter "Name='Lethal Company.exe'" | Where-Object {$_.ExecutablePath -eq "$steam\Lethal Company.exe"}){throw 'Steam game is running; do not replace active files.'}
$baseline=Get-Content (Join-Path $PSScriptRoot 'baseline\steam-hashes.json') -Raw | ConvertFrom-Json
$files=Get-ChildItem $stage -Recurse -File
foreach($f in $files){
  $relative=$f.FullName.Substring($stage.Length+1)
  if($relative -notlike 'BepInEx\*' -or $relative -like '*UniversalWrench*' -or $relative -like '*TestProbe*'){throw "Disallowed install path: $relative"}
  $target=Join-Path $steam $relative
  $old=$baseline | Where-Object Path -eq $relative
  if((Test-Path -LiteralPath $target) -and $old -and (Get-FileHash -LiteralPath $target).Hash -ne $old.SHA256){throw "Target changed since baseline: $relative"}
}
$backup=Join-Path $PSScriptRoot ('backups\steam-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$record=@()
foreach($f in $files){
  $relative=$f.FullName.Substring($stage.Length+1)
  $target=Join-Path $steam $relative
  $exists=Test-Path -LiteralPath $target
  if($exists){
    $saved=Join-Path $backup $relative
    New-Item -ItemType Directory -Path (Split-Path $saved) -Force | Out-Null
    Copy-Item -LiteralPath $target -Destination $saved
  }
  $record+=[pscustomobject]@{Path=$relative;Existed=$exists;SHA256=(Get-FileHash -LiteralPath $f.FullName).Hash}
}
$record | ConvertTo-Json | Set-Content (Join-Path $backup 'install-plan.json') -Encoding utf8
foreach($f in $files){
  $target=Join-Path $steam $f.FullName.Substring($stage.Length+1)
  New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
  Copy-Item -LiteralPath $f.FullName -Destination $target -Force
  if((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $f.FullName).Hash){throw "Install hash mismatch: $target"}
}
# All pre-existing plugin files, including unpublished Universal Wrench, must be unchanged.
foreach($old in $baseline | Where-Object {$_.Path -like 'BepInEx\plugins\*' -and $_.SHA256}){
  if((Get-FileHash -LiteralPath (Join-Path $steam $old.Path)).Hash -ne $old.SHA256){throw "Existing plugin changed: $($old.Path)"}
}
Set-Content (Join-Path $PSScriptRoot 'installation.txt') "Installed $($record.Count) files; all installed hashes verified.`nBackup: $backup`nExisting plugin hashes checked where baseline recorded them. No save files touched."
Get-Content (Join-Path $PSScriptRoot 'installation.txt')
