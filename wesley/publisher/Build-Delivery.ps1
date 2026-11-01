param([Parameter(Mandatory)][string]$Work)
$ErrorActionPreference='Stop'
$base=Join-Path $Work 'baseline'
$delta=Join-Path $Work 'candidate'
$delivery=Join-Path $Work 'delivery'
$pack=Join-Path $delivery 'LethalGoofballsPack-Wesley'
# Only ever mirror into our own pack directory inside this staging workspace.
if(!(Test-Path -LiteralPath "$Work/evidence/baseline.json") -or !(Test-Path -LiteralPath "$Work/deps/packages.lock.json")){throw 'Invalid staging root'}
New-Item -ItemType Directory -Force $pack,"$delivery/rollback"|Out-Null
foreach($dir in $base,$delta){robocopy $dir $pack /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP|Out-Null;if($LASTEXITCODE -ge 8){throw "Copy failed $dir"}}
$set=@{}
foreach($dir in $base,$delta){foreach($f in Get-ChildItem -LiteralPath $dir -Recurse -File){$rel=[IO.Path]::GetRelativePath($dir,$f.FullName);$set[$rel]=$true}}
foreach($f in Get-ChildItem -LiteralPath $pack -Recurse -File){$rel=[IO.Path]::GetRelativePath($pack,$f.FullName);if(!$set.ContainsKey($rel) -or $rel -match 'Slot3Preparation|GameHarness|IsolatedQA|TestProbe|NT1Ho-WesleyZh|Assembly-CSharp\.dll|\.pfx$|\.pem$|\.es3$'){throw "Unexpected output: $rel"}}
$rows=foreach($f in Get-ChildItem -LiteralPath $delta -File -Recurse){$rel=[IO.Path]::GetRelativePath($delta,$f.FullName);$prev=Join-Path $base $rel;$hash=(Get-FileHash -LiteralPath $f.FullName).Hash
if((Get-FileHash -LiteralPath (Join-Path $pack $rel)).Hash -ne $hash){throw "Digest mismatch at $rel"}
$before=$null
if(Test-Path -LiteralPath $prev){$before=(Get-FileHash -LiteralPath $prev).Hash;if($before -eq $hash){continue};$copy=Join-Path "$delivery/rollback" $rel;New-Item -ItemType Directory -Force (Split-Path $copy)|Out-Null;Copy-Item -LiteralPath $prev -Destination $copy}
[ordered]@{path=$rel.Replace('\','/');baseline=$before;candidate=$hash;size=$f.Length}}
$rows|ConvertTo-Json|Set-Content "$delivery/changes.json"
Write-Output "Pack $pack, $($rows.Count) modified or added files"
