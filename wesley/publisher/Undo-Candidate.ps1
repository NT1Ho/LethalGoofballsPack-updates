param([Parameter(Mandatory)][string]$CandidateRoot,[Parameter(Mandatory)][string]$Delivery)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $CandidateRoot).Path
if($root -ne (Resolve-Path -LiteralPath (Join-Path $Delivery 'LethalGoofballsPack-Wesley')).Path){throw 'This rollback is for the independent candidate only'}
if(Get-CimInstance Win32_Process -Filter "Name='Lethal Company.exe'"|Where-Object {!$_.ExecutablePath -or (Split-Path $_.ExecutablePath) -eq $root}){throw 'Close candidate game first'}
$rows=Get-Content -LiteralPath (Join-Path $Delivery 'changes.json') -Raw|ConvertFrom-Json
foreach($row in $rows){
 $path=[IO.Path]::GetFullPath((Join-Path $root $row.path))
 if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe manifest path'}
 if(!(Test-Path -LiteralPath $path) -and !$row.baseline){continue}
 $hash=(Get-FileHash -LiteralPath $path).Hash
 if($hash -ne $row.candidate -and $hash -ne $row.baseline){throw "Locally edited: $path"}
 if($row.baseline -and (Get-FileHash -LiteralPath (Join-Path "$Delivery/rollback" $row.path)).Hash -ne $row.baseline){throw "Invalid backup: $path"}
}
foreach($row in $rows){$path=Join-Path $root $row.path;if($row.baseline){Copy-Item -LiteralPath (Join-Path "$Delivery/rollback" $row.path) -Destination $path -Force}else{if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}}}
