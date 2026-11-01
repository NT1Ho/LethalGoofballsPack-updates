param([Parameter(Mandatory=$true)][string]$GameDirectory,[Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference='Stop'
function Get-Sha256([string]$path){$stream=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower()}finally{$sha.Dispose();$stream.Dispose()}}
if(Get-Process 'Lethal Company' -ErrorAction SilentlyContinue){throw '请先退出游戏；本工具不关闭游戏。'}
$game=[IO.Path]::GetFullPath($GameDirectory).TrimEnd('\');$dest=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $dest){throw '输出目录必须不存在。'}
function Check-Path([string]$base,[string]$relative){
 if($relative -match '\\|:|(^|/)\.\.?(/|$)' -or $relative -notmatch '^(BepInEx/(config|core|plugins|patchers|pack-info)/|winhttp\.dll$|doorstop_config\.ini$|\.doorstop_version$)'){throw '非法备份路径'}
 $p=[IO.Path]::GetFullPath((Join-Path $base $relative));if(-not $p.StartsWith($base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw '路径越界'}
 for($q=$p;$q;$q=Split-Path $q -Parent){if((Test-Path -LiteralPath $q) -and ((Get-Item -LiteralPath $q -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '不支持链接路径'}}
 return $p
}
$stateHome=Join-Path $game '.lcpack-updater'
$lock=[IO.File]::Open((Join-Path $stateHome 'update.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
 if(Test-Path -LiteralPath (Join-Path $stateHome 'pending')){throw '有未完成更新；请先让接收端恢复。'}
 $archive=Get-ChildItem -LiteralPath (Join-Path $stateHome 'backups') -Directory | Sort-Object Name -Descending | Select-Object -First 1
 if(-not $archive){throw '没有可用的更新备份。'}
 if($archive.Attributes -band [IO.FileAttributes]::ReparsePoint){throw '备份目录不能是链接'}
 $journal=Get-Content -LiteralPath (Join-Path $archive.FullName 'journal.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if(-not $journal.committed -or -not $journal.hadState){throw '此备份没有上一版管理记录，不能重建完整上一版。'}
 $stateFile=Join-Path $archive.FullName 'state.old'
 if((Get-Sha256 $stateFile) -ne $journal.stateOldHash){throw '旧管理记录校验失败'}
 $state=Get-Content -LiteralPath $stateFile -Raw -Encoding UTF8|ConvertFrom-Json
 $plan=@();$seen=@{}
 foreach($f in $state.files){
  if($seen.ContainsKey($f.path.ToLower())){throw '重复路径'};$seen[$f.path.ToLower()]=$true
  $current=Check-Path $game $f.path;$out=Check-Path $dest $f.path
  $change=@($journal.changes|Where-Object {$_.path -eq $f.path})
  if($change.Count -gt 1){throw '重复备份记录'}
  if($change.Count){
   $c=$change[0];if(-not $c.existed -or $c.backup -notmatch '^old-[0-9]+$'){throw '无效备份记录'}
   $input=Join-Path $archive.FullName $c.backup;$expected=$c.oldHash
  }else{$input=$current;$expected=$f.sha256}
  if((Get-Item -LiteralPath $input -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '文件不能是链接'}
  $hash=(Get-Sha256 $input)
  if($hash -ne $expected -and -not ($change.Count -eq 0 -and $f.path -like 'BepInEx/config/*.cfg')){throw "文件已改变，停止重建：$($f.path)"}
  $plan+=@{input=$input;output=$out;hash=$hash}
 }
 # Write only an isolated export; original installation, saves and backups are never edited.
 New-Item -ItemType Directory -Path $dest|Out-Null
 foreach($p in $plan){New-Item -ItemType Directory -Force -Path (Split-Path $p.output)|Out-Null;Copy-Item -LiteralPath $p.input -Destination $p.output;if((Get-Sha256 $p.output) -ne $p.hash){throw '导出校验失败，勿使用不完整输出'}}
 '仅导出上一版托管文件。恢复时需移除本次新加文件，避免新旧 DLL 同时加载；不要只覆盖。恢复期间不要运行在线更新器。'|Set-Content (Join-Path $dest '回退注意.txt') -Encoding UTF8
 @($journal.changes|Where-Object {-not $_.existed}|Select-Object -ExpandProperty path)|Set-Content (Join-Path $dest '恢复前需移出的新增文件.txt') -Encoding UTF8
 Write-Output "已重建上一版 $($state.version)：$dest（未修改游戏）"
}finally{$lock.Dispose()}
