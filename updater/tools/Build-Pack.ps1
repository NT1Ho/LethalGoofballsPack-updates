param([Parameter(Mandatory=$true)][string]$PackRoot,[Parameter(Mandatory=$true)][string]$ManifestPath,[Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference='Stop'
function Get-Sha256([string]$path){$stream=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower()}finally{$sha.Dispose();$stream.Dispose()}}
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath($PackRoot).TrimEnd('\');$out=[IO.Path]::GetFullPath($ZipPath)
if(Test-Path -LiteralPath $out){throw '输出 ZIP 已存在；请使用新文件名，验证后再替换正式包。'}
if($out.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'ZIP 不能输出到包目录内。'}
$record=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$files=@(if($record.files){$record.files}else{$record.PSObject.Properties|ForEach-Object {[PSCustomObject]@{path=$_.Name;sha256=$_.Value.sha256;size=$_.Value.size}}})
if(-not $files.Count){throw '审核清单为空。'}
$actual=@(Get-ChildItem -LiteralPath $root -Recurse -File -Force);$entries=Get-ChildItem -LiteralPath $root -Recurse -Force
foreach($p in @((Get-Item -LiteralPath $root))+$entries){if($p.Attributes -band [IO.FileAttributes]::ReparsePoint){throw '包内不能包含链接。'}}
if($actual.Count -ne $files.Count){throw '包文件数与审核清单不一致。'}
$seen=@{}
foreach($f in $files){
 if($f.path -match '\\|:|(^|/)\.\.?(/|$)' -or $f.path -notmatch '^(BepInEx/(config|core|plugins|patchers|pack-info)/|winhttp\.dll$|doorstop_config\.ini$|\.doorstop_version$)' -or $f.sha256 -notmatch '^[a-f0-9]{64}$'){throw '审核清单包含非法路径或哈希。'}
 $key=$f.path.ToLower();if($seen.ContainsKey($key)){throw '审核清单重复路径。'};$seen[$key]=$true
 $p=[IO.Path]::GetFullPath((Join-Path $root $f.path));if(-not $p.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '路径越界。'}
 if((Get-Item -LiteralPath $p).Length -ne $f.size -or (Get-Sha256 $p) -ne $f.sha256){throw "文件未通过审核清单：$($f.path)"}
}
New-Item -ItemType Directory -Force -Path (Split-Path $out)|Out-Null
$temporary=$out+'.pending-'+[guid]::NewGuid().ToString('N')
try{
 $writer=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($f in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer,(Join-Path $root $f.path),$f.path,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$writer.Dispose()}
 $zip=[IO.Compression.ZipFile]::OpenRead($temporary)
 try{
  $fileEntries=@($zip.Entries|Where-Object {-not $_.FullName.EndsWith('/')});if($fileEntries.Count -ne $files.Count){throw 'ZIP 文件数不符。'}
  foreach($f in $files){$entry=$zip.GetEntry($f.path);if(-not $entry -or $entry.Length -ne $f.size){throw 'ZIP 缺少文件或大小不符。'};$s=$entry.Open();$hash=[Security.Cryptography.SHA256]::Create();try{$hex=([BitConverter]::ToString($hash.ComputeHash($s))).Replace('-','').ToLower();if($hex -ne $f.sha256){throw "ZIP 内容校验失败：$($f.path)"}}finally{$hash.Dispose();$s.Dispose()}}
 }finally{$zip.Dispose()}
 Move-Item -LiteralPath $temporary -Destination $out
 ((Get-Sha256 $out)+'  '+[IO.Path]::GetFileName($out))|Set-Content -LiteralPath ($out+'.sha256') -Encoding ASCII
 Write-Output "已打包并逐文件校验 $($files.Count) 项：$out"
}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
