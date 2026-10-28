param([string]$ChannelConfig=(Join-Path $PSScriptRoot 'channel.public.json'),[string]$Repository='NT1Ho/LethalGoofballsPack-updates',[string]$DownloadDirectory)
$ErrorActionPreference='Stop'
function Get-Sha256([string]$path){$stream=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLower()}finally{$sha.Dispose();$stream.Dispose()}}
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$channel=Get-Content -LiteralPath $ChannelConfig -Raw -Encoding UTF8|ConvertFrom-Json
if($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$' -or $channel.endpoint -notmatch '^https://[a-z0-9-]+-[0-9]+\.cos\.ap-(guangzhou|hongkong)\.myqcloud\.com$'){throw '非法渠道'}
function Read-Text([string]$url){$r=Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 60 -Headers @{'User-Agent'='LcPack-ReadOnlyCheck'};if($r.Content -is [byte[]]){return [Text.Encoding]::UTF8.GetString($r.Content)};return [string]$r.Content}
function Verify([string]$text,[string]$kind){
 $d=$text|ConvertFrom-Json
 if($d.schema -ne 1 -or $d.kind -ne $kind -or $d.payload.Length -gt 7MB){throw '清单格式/用途无效'}
 $rsa=[Security.Cryptography.RSACryptoServiceProvider]::new();$rsa.PersistKeyInCsp=$false
 try{$rsa.FromXmlString($channel.publicKey);if(-not $rsa.PublicOnly -or $rsa.KeySize -lt 2048){throw '公钥无效'};$b=[Text.Encoding]::UTF8.GetBytes("LethalGoofballsPack/signed-v1/$kind`n"+$d.payload);if(-not $rsa.VerifyData($b,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'),[Convert]::FromBase64String($d.signature))){throw '签名校验失败'}}finally{$rsa.Dispose()}
 return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($d.payload))
}
$nonce='?check='+[guid]::NewGuid().ToString('N');$base=$channel.endpoint+'/lcpack/'
$payload=Verify (Read-Text ($base+'manifest.json'+$nonce)) 'mods'
$github=Verify (Read-Text ("https://raw.githubusercontent.com/$Repository/main/mod-update.signed.json"+$nonce)) 'mods'
$m=$payload|ConvertFrom-Json
if($m.version -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or $m.version.Contains('..')){throw '版本名无效'}
$release=Read-Text ("https://github.com/$Repository/releases/download/$($m.version)/manifest.json"+$nonce)
$version=Verify (Read-Text ($base+'versions/'+$m.version+'.json'+$nonce)) 'mods'
if($payload -cne $github -or $payload -cne $release -or $payload -cne $version){throw '双源签名 payload / Release / 固定版本清单不一致；也可能仍在 CDN 缓存期，请稍后重新检查。'}
$client=Verify (Read-Text ($base+'client-update.json'+$nonce)) 'client'
$clientBackup=Verify (Read-Text ("https://raw.githubusercontent.com/$Repository/main/client-update.signed.json"+$nonce)) 'client'
if($client -cne $clientBackup){throw '接收端自更新签名清单不一致。'}
$pattern='^https://github\.com/'+[regex]::Escape($Repository)+'/releases/download/([A-Za-z0-9._-]+)/([a-f0-9]{64})\.bin$'
$groups=@{}
foreach($f in $m.files){if($f.url -notmatch $pattern -or $matches[2] -ne $f.sha256 -or $f.size -lt 0){throw '文件 URL 与 SHA 不符'};$tag=$matches[1];if(-not $groups.ContainsKey($tag)){$groups[$tag]=@()};$groups[$tag]+=$f}
foreach($tag in $groups.Keys){
 $r=(Read-Text "https://api.github.com/repos/$Repository/releases/tags/$tag")|ConvertFrom-Json;$assets=@()
 for($page=1;;$page++){$batch=@((Read-Text "https://api.github.com/repos/$Repository/releases/$($r.id)/assets?per_page=100&page=$page")|ConvertFrom-Json);$assets+=$batch;if($batch.Count -lt 100){break}}
 foreach($f in $groups[$tag]){$a=@($assets|Where-Object {$_.name -eq ($f.sha256+'.bin')});if($a.Count -ne 1 -or $a[0].size -ne $f.size -or ($a[0].digest -and $a[0].digest -ne ('sha256:'+$f.sha256))){throw "附件缺失或校验不符：$($f.path)"};if(-not $a[0].digest -and -not $DownloadDirectory){throw '历史附件无 SHA 元数据，需指定 DownloadDirectory 做真实下载校验。'}}
}
if($DownloadDirectory){
 New-Item -ItemType Directory -Force -Path $DownloadDirectory|Out-Null
 foreach($f in ($m.files|Group-Object sha256|ForEach-Object {$_.Group[0]})){
  $path=Join-Path $DownloadDirectory ($f.sha256+'.bin');$temporary=$path+'.pending-'+[guid]::NewGuid().ToString('N')
  try{Invoke-WebRequest -UseBasicParsing -Uri $f.url -OutFile $temporary -TimeoutSec 180;if((Get-Item -LiteralPath $temporary).Length -ne $f.size -or (Get-Sha256 $temporary) -ne $f.sha256){throw "真实下载校验失败：$($f.path)"};Move-Item -LiteralPath $temporary -Destination $path -Force}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
 }
}
Write-Output "PASS $($m.version)：双源签名、Release 内容与 $($m.files.Count) 条附件引用一致。"
if(-not $DownloadDirectory){Write-Output '本次使用附件 SHA 元数据；未重复下载全部二进制。使用 -DownloadDirectory 可做完整二进制校验。'}
