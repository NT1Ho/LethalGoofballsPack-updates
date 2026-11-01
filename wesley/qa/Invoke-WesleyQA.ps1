param(
 [Parameter(Mandatory)][string]$PeerRoot,
 [Parameter(Mandatory)][ValidateSet('state','readui','catalog','story','tapegrab','tapesubmit','save','wrench','terminal','land','enter','exit','board','orbit')][string]$Op,
 [string]$Arg='', [int]$Seed=810031, [int]$Timeout=150
)
$ErrorActionPreference='Stop'
if(!(Test-Path -LiteralPath "$PeerRoot/UNIVERSAL_WRENCH_ISOLATED_TEST")){throw "Not an isolated game: $PeerRoot"}
$dir=Join-Path $PeerRoot 'wesley-qa'
if(!(Test-Path $dir)){throw 'Start the isolated game with Wesley.IsolatedQA first'}
if($Op -eq 'enter'){
    $ready=[Diagnostics.Stopwatch]::StartNew()
    do{
        $state=& $PSCommandPath -PeerRoot $PeerRoot -Op state -Timeout ([Math]::Min($Timeout,30))
        if($state.ok -and $state.landed -and !$state.orbit -and $state.exits.Count -ge 2){break}
        if($ready.Elapsed.TotalSeconds -ge $Timeout){throw "No landed peer with paired entrances on $PeerRoot after timeout"}
        Start-Sleep -Milliseconds 700
    }while($true)
}
$id=[Guid]::NewGuid().ToString('N')
$file=Join-Path $dir 'request.json'
if(Test-Path $file){throw "Previous command still pending on $PeerRoot"}
$req=[ordered]@{id=$id;op=$Op;arg=$Arg;seed=$Seed}
$tmp=Join-Path $dir "$id.request.tmp"; [IO.File]::WriteAllText($tmp,($req|ConvertTo-Json -Compress))
[IO.File]::Move($tmp,$file)
$timer=[Diagnostics.Stopwatch]::StartNew()
$result=Join-Path $dir "$id.json"
while(!(Test-Path -LiteralPath $result) -and $timer.Elapsed.TotalSeconds -lt $Timeout){Start-Sleep -Milliseconds 250}
if(!(Test-Path -LiteralPath $result)){throw "Timed out: $Op, req $id on $PeerRoot. Inspect process/logs before retry."}
Get-Content -Raw -Encoding utf8 -LiteralPath $result|ConvertFrom-Json
