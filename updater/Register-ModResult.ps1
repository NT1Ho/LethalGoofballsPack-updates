# 开发线程使用。Files 是游戏相对路径 -> 已验证的最终文件。
param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][ValidateSet('新增','升级','修复','优化','移除')][string]$Kind,
    [Parameter(Mandatory=$true)][string]$Summary,
    [Parameter(Mandatory=$true)][string]$Tests,
    [hashtable]$Files=@{},[string[]]$Remove=@(),[string[]]$Dependencies,
    [string]$Instructions,[Nullable[bool]]$Own,[switch]$ReconfirmFullResult,
    [string]$Workspace=(Join-Path $env:USERPROFILE '.LethalGoofballsPackPublisher'),
    [string]$AdminExe='D:\Lethal company pack Updater\管理端\Lethal company pack Updater Admin.exe'
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -ne 'Desktop'){throw '请用 Windows PowerShell 5.1 执行登记工具'}
[void][Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $AdminExe).Path)
$ws=[LcPack.Workspace]::new($Workspace)
$draft=[LcPack.AdminWorkspace]::new($ws)
if(!$draft.Base -or !$ws.Current.baseManifestHash){throw '请先在管理端核对正式基线'}
if($Package -notmatch '^[A-Za-z0-9_]+-[A-Za-z0-9_]+$' -or (!$Files.Count -and !$Remove.Count)){throw '请提供组件身份和明确的最终路径'}
$inbox=[LcPack.Cumulative]::Inbox($ws)
$store=[LcPack.Cumulative]::Home($ws)
$gate=Join-Path $store '登记.lock'
[LcPack.SafePath]::NoLinks($gate)
$lock=[IO.File]::Open($gate,'OpenOrCreate','ReadWrite','None')
try{
    $base=@{};foreach($f in $draft.Base.files){$base[$f.path]=$f}
    $stamp=$ws.Current.baseManifestHash
    $older=@(Get-ChildItem -LiteralPath $inbox -Directory|ForEach-Object {
        $doc=Join-Path $_.FullName 'result.json';[LcPack.SafePath]::NoLinks($doc)
        $read=Get-Content -LiteralPath $doc -Encoding UTF8 -Raw|ConvertFrom-Json
        if($read.package -eq $Package){[pscustomobject]@{m=$read;dir=$_.FullName}}
    }|Sort-Object {$_.m.revision} -Descending)
    $prior=$older|Select-Object -First 1
    if($prior -and $prior.m.baseHash -ne $stamp -and !$ReconfirmFullResult){throw '前次登记属于另一正式基线，请先重新核对，已保留原成果'}
    $versionBefore=$null;$deps=@()
    $cat=$base['BepInEx/pack-info/download-sources.json']
    if($cat){$cache=$ws.ObjectPath($cat.sha256);if(!(Test-Path $cache) -or [LcPack.Data]::Hash($cache) -ne $cat.sha256){throw '请先在管理端核对渠道并读取组件资料'}
        $pkg=Get-Content -LiteralPath $cache -Raw -Encoding UTF8|ConvertFrom-Json|Where-Object package -eq $Package
        if($pkg){$versionBefore=$pkg.version;$deps=@($pkg.dependencies|Where-Object {$_})}
    }
    $seq=1;$map=@{};$payload=@{}
    if($prior){$seq=$prior.m.revision+1}
    if($prior -and !$ReconfirmFullResult){
        $deps=@($prior.m.dependencies|Where-Object {$_})
        if(!$PSBoundParameters.ContainsKey('Own')){$Own=$prior.m.own}
        if(!$PSBoundParameters.ContainsKey('Instructions')){$Instructions=$prior.m.instructions}
        $Summary=$prior.m.summary+[Environment]::NewLine+$Summary
        $Tests=$prior.m.tests+[Environment]::NewLine+$Tests
        foreach($f in $prior.m.files){$map[$f.path]=$f;if($f.sha256){$payload[$f.path]=[LcPack.SafePath]::Target((Join-Path $prior.dir 'payload'),$f.path)}}
    }
    if($PSBoundParameters.ContainsKey('Dependencies')){$deps=@($Dependencies|Where-Object {$_})}
    foreach($p in @($Files.Keys)+@($Remove)){
        [void][LcPack.SafePath]::Validate($p)
        $removedInfo=$Kind -eq '移除' -and $Remove -contains $p -and !$Files.ContainsKey($p) -and $p.StartsWith("BepInEx/pack-info/$Package/",[StringComparison]::OrdinalIgnoreCase)
        $restoredInfo=$Kind -eq '新增' -and $Files.ContainsKey($p) -and $draft.Base.deletes -contains $p -and @("BepInEx/pack-info/$Package/manifest.json","BepInEx/pack-info/$Package/README.md","BepInEx/pack-info/$Package/CHANGELOG.md") -contains $p
        if(!$removedInfo -and !$restoredInfo -and $p -notmatch '^BepInEx/(config|plugins|patchers)/'){throw "登记范围不允许 $p"}
        if($Files.ContainsKey($p) -and $Remove -contains $p){throw "同时写入和删除 $p"}
        $before=if($base.ContainsKey($p)){$base[$p].sha256}else{$null}
        $sha=$null
        if($Files.ContainsKey($p)){
            $src=(Resolve-Path -LiteralPath $Files[$p]).Path;[LcPack.SafePath]::NoLinks($src);$sha=[LcPack.Data]::Hash($src);$payload[$p]=$src
        }else{$payload.Remove($p)}
        if($sha -eq $before){$map.Remove($p);$payload.Remove($p)}else{$map[$p]=[ordered]@{path=$p;before=$before;sha256=$sha}}
    }
    if(!$map.Count){throw '此成果相对正式基线无净变化，未新增登记'}
    $record=[ordered]@{package=$Package;version=$Version;fromVersion=$versionBefore;baseHash=$stamp;revision=$seq;kind=$Kind;summary=$Summary;tests=$Tests;instructions=$Instructions;own=$Own;dependencies=$deps;files=@($map.Values|Sort-Object path)}
    $stage=Join-Path $store ('登记中-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    foreach($p in $payload.Keys){
        $to=[LcPack.SafePath]::Target((Join-Path $stage 'payload'),$p);[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
        [IO.File]::Copy($payload[$p],$to)
        if([LcPack.Data]::Hash($to) -ne $map[$p].sha256){throw "最终文件发生变化 $p"}
    }
    $json=$record|ConvertTo-Json -Depth 8
    [LcPack.Cumulative]::ValidateReceipt($json,$stamp)
    [LcPack.Data]::Atomic((Join-Path $stage 'result.json'),$json)
    $dest=Join-Path $inbox ($Package+'-r'+$seq)
    [IO.Directory]::Move($stage,$dest)
    "已登记 $dest。管理端「发布中心 → 整理累计区」生成标准 ZIP 并检查冲突。"
}finally{$lock.Dispose()}
