param([Parameter(Mandatory)][string]$Baseline,[Parameter(Mandatory)][string]$Overlay,[Parameter(Mandatory)][string]$LockFile)
$ErrorActionPreference='Stop'
$base=Join-Path $Baseline 'BepInEx/pack-info';$target=Join-Path $Overlay 'BepInEx/pack-info'
$pages=Get-Content -LiteralPath "$base/display-pages.json" -Raw|ConvertFrom-Json -AsHashtable
$files=@(Get-Content -LiteralPath "$base/original-files.json" -Raw|ConvertFrom-Json -AsHashtable)
$sources=@(Get-Content -LiteralPath "$base/download-sources.json" -Raw|ConvertFrom-Json -AsHashtable)
$lock=Get-Content -LiteralPath $LockFile -Raw|ConvertFrom-Json -AsHashtable
$labels=@{
'Wesleys_Moons'='剧情星球、航线解锁和探索';'WesleysInteriors'='可生成的室内、谜题和任务房';'Wesleys_Ememy_Variants'='为 Wesley 地图提供特殊敌人变体';'Wesleys_Shenanigans'='剧情物品、地图机关与事件';'Wesleys_Weathers'='地图天气与场景效果';'CustomStoryLogsFixed'='通过游戏终端收集和阅读剧情日志';'Facto'='Wesley 番外星球';'Decrepit_Store'='番外独立室内：废弃商店'
}
foreach($item in $lock.Values|Where-Object action -eq add){
 $name=($item.package -split '-',2)[1]
 if($sources.package -contains $item.package){throw "duplicate inventory source: $name"}
 $sources+=@{package=$item.package;version=$item.version;url=$item.url;sha256=$item.archive_sha256;dependencies=$item.dependencies}
 foreach($f in $item.files){$files+=@{path=$f.path;package=$item.package;source=$f.path;sha256=$(if($f.candidate_sha256){$f.candidate_sha256}else{$f.sha256})}}
 if($labels.ContainsKey($name)){
   $pages.Mods+=@{Key=$name;Name=$name;Purpose=$labels[$name];Order=100;New=$true;Own=$false;Details="作者 Magic_Wesley。$($labels[$name])。";VersionColor=$null}
 }
}
$pages.Mods+=@{Key="Wesley Pack Compatibility";Name="Wesley 整合兼容补丁";Purpose="修复 V81 存档注册和结算兼容问题";Order=1;New=$true;Own=$true;Details="本整合包自研兼容补丁。Wesley 原作署名保持独立。";VersionColor=$null}
$uw=$sources|Where-Object {$_['package'] -eq 'NT1Ho-UniversalWrench'}
$uw['version']='0.1.30-dev'
$uw['url']='https://github.com/NT1Ho/UniversalWrench/commit/d6222c8a8a51783f757ef4891c06b47845f3094f'
$uw['sha256']=$null
$record=$files|Where-Object {$_['path'] -eq 'BepInEx/plugins/UniversalWrench/UniversalWrench.dll'}
$record['sha256']=(Get-FileHash -LiteralPath (Join-Path $Overlay $record.path)).Hash.ToLowerInvariant()
$sources+=@{package='NT1Ho-WesleyZhCN';version='1.0.0';dependencies=@('Aueser-LC_Chinese_Project-3.3.2')}
foreach($file in @(Get-ChildItem (Join-Path $Overlay 'BepInEx/plugins/V81TestChn/translations-clean/cfg/zh-CN') -Filter 'wesley-*.cfg';Get-ChildItem (Join-Path $Overlay 'BepInEx/config/V81TestChn/custom-localization/Wesley') -Filter '*.cfg')){
 $rel=[IO.Path]::GetRelativePath((Resolve-Path $Overlay).Path,$file.FullName).Replace('\','/')
 $files+=@{package='NT1Ho-WesleyZhCN';path=$rel;source=$file.Name;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
}
$pages.Mods+=@{Key='WesleyZhCN';Name='Wesley 简体中文扩展';Purpose='剧情、谜题、图鉴与交互文字';Order=1;New=$true;Own=$true;Details='使用现有 LC Chinese Project 的显示词库。';VersionColor=$null}
$history=@{Version='Wesley 候选（未发布）';PublishedUtc='2026-11-01T00:00:00Z';Changes=@(
 @{Kind='新增';Mod='Wesley 系列与番外';Summary='加入剧情星球、室内、天气、敌人、日志、Facto 和 Decrepit Store，保留作者默认剧情解锁。'},
 @{Kind='新增';Mod='简体中文扩展';Summary='在现有汉化框架下增加 Wesley 内容的显示译文。英文终端指令保留。'},
 @{Kind='修复';Mod='候选兼容修改';Summary='电源之心识别原版设施电源；修复存档注册、室内生成规则、结算图片失效及特殊敌人选择玩家槽位的问题。'}
)}
$pages.History=@($history)+@($pages.History)
$pages|ConvertTo-Json -Depth 12|Set-Content "$target/display-pages.json"
$files|ConvertTo-Json -Depth 6|Set-Content "$target/original-files.json"
$sources|ConvertTo-Json -Depth 6|Set-Content "$target/download-sources.json"
if(!$pages.Mods[0].Own -or $pages.Mods[0].Key -ne 'UniversalWrench'){throw 'pinned first-party row missing'}
