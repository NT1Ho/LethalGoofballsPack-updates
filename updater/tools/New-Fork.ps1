param([Parameter(Mandatory=$true)][string]$Destination,[Parameter(Mandatory=$true)][string]$Repository)
$ErrorActionPreference='Stop'
if($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9][A-Za-z0-9._-]*$' -or $Repository -eq 'NT1Ho/LethalGoofballsPack-updates'){throw '请填写你自己的 owner/repo，不能使用原作者渠道。'}
$dest=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $dest){throw '目标必须是尚不存在的新目录，避免覆盖原项目。'}
$source=Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Path $dest | Out-Null
foreach($name in @('src','assets','lib','tests','tools')){
 $from=Join-Path $source $name
 foreach($file in Get-ChildItem -LiteralPath $from -File -Recurse){
  $rel=$file.FullName.Substring($source.Length+1)
  if($name -eq 'tests' -and ($file.DirectoryName -ne $from -or $file.Extension -notin @('.cs','.ps1'))){continue}
  if($rel -match '\\runs\\|\.exe$|\.pdb$|FakePlugin\.dll$'){continue}
  $target=Join-Path $dest $rel;New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $target
 }
}
foreach($name in @('Build.ps1','Publish-Client.ps1','ClientPublish.cs')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $dest}
$common=Join-Path $dest 'src\Common.cs';$text=[IO.File]::ReadAllText($common)
$text=$text.Replace('NT1Ho/LethalGoofballsPack-updates',$Repository);[IO.File]::WriteAllText($common,$text,[Text.UTF8Encoding]::new($false))
$core=Join-Path $dest 'src\PublisherCore.cs';$text=[IO.File]::ReadAllText($core)
$text=$text.Replace('".LethalGoofballsPackPublisher"','".LcPackPublisher-'+$Repository.Replace('/','-')+'"').Replace('请使用 NT1Ho 本人的发布凭据。','请使用此仓库所有者的发布凭据。')
[IO.File]::WriteAllText($core,$text,[Text.UTF8Encoding]::new($false))
$checker=Join-Path $dest 'tools\Check-Release.ps1'
if(Test-Path -LiteralPath $checker){$text=[IO.File]::ReadAllText($checker).Replace('NT1Ho/LethalGoofballsPack-updates',$Repository);[IO.File]::WriteAllText($checker,$text,[Text.UTF8Encoding]::new($true))}
$diagnosticSource=Join-Path $source '..\diagnostics'
if(!(Test-Path $diagnosticSource)){$diagnosticSource=Join-Path $source 'diagnostics'}
foreach($file in Get-ChildItem $diagnosticSource -Recurse -File | Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}){$relative=$file.FullName.Substring((Resolve-Path $diagnosticSource).Path.Length+1);$target=Join-Path $dest ('diagnostics\'+$relative);New-Item -ItemType Directory -Force (Split-Path $target)|Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $target}
# No original endpoint, public key, saved draft, upload credentials or production EXE is copied.
@"
独立渠道：$Repository
1. 在自己的 GitHub 创建此公开仓库。
2. 运行 .\Build.ps1 -SkipTests -GameDir <正版游戏目录>，打开 dist 中的管理端，在本机填写自己的 COS 域名及密钥。
3. 管理端首次保存会生成全新的签名密钥；本机私密目录为 %USERPROFILE%\.LcPackPublisher-$($Repository.Replace('/','-'))。
4. 将该私密目录的 cos-channel.json（只有域名和公钥）复制到本项目根目录，重新运行 Build.ps1。
5. 使用重新构建的管理端和接收端；将独立 pack 副本全部拖入，填写自己的版本名后发布。
6. 不复制原作者签名清单、缓存或凭据。GitHub 备份凭据须由仓库主人本机保存；参见 docs/FORKING.md。
先完成上述绑定再发接收端。未绑定 COS 的临时构建不能作为正式分发版本。
"@ | Set-Content (Join-Path $dest '独立渠道初始化.txt') -Encoding UTF8
Write-Output "已生成独立源码副本：$dest"

