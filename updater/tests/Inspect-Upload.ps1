$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
[Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $PSScriptRoot) 'dist\LethalGoofballsPack Publisher.exe')) | Out-Null
$workspace=New-Object LcPack.Workspace((Join-Path $env:LOCALAPPDATA 'LethalGoofballsPackPublisher'))
$headers=@{Authorization=('Bearer '+$workspace.Token());'User-Agent'='LethalGoofballsPack-Publisher/1.0';'X-GitHub-Api-Version'='2022-11-28'}
$releases=Invoke-RestMethod -Uri 'https://api.github.com/repos/NT1Ho/LethalGoofballsPack-updates/releases?per_page=10' -Headers $headers -TimeoutSec 30
foreach($release in $releases) {
    $assets=@();$page=1
    do {
        $batch=Invoke-RestMethod -Uri ('https://api.github.com/repos/NT1Ho/LethalGoofballsPack-updates/releases/'+$release.id+'/assets?per_page=100&page='+$page) -Headers $headers -TimeoutSec 30
        $assets+=$batch;$page++
    } while($batch.Count -eq 100)
    [pscustomobject]@{Id=$release.id;Tag=$release.tag_name;Draft=$release.draft;Assets=$assets.Count;Bytes=($assets|Measure-Object size -Sum).Sum;Last=($assets|Select-Object -Last 1|Select-Object name,size,state,created_at)}|ConvertTo-Json -Compress
    if($release.draft) { [pscustomobject]@{id=$release.id;tag_name=$release.tag_name;draft=$release.draft;assets=$assets}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'current-cloud-draft.json') -Encoding UTF8 }
}
