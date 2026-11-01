param([Parameter(Mandatory)][string]$HostRoot,[Parameter(Mandatory)][string]$ClientRoot,
      [Parameter(Mandatory)][string[]]$Scene,[int]$Seed=810031,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
function Run([string]$Peer,[string]$Op,[string]$Arg='') {
    $path=if($Peer -eq 'host'){$HostRoot}else{$ClientRoot}
    $r=& "$PSScriptRoot/Invoke-WesleyQA.ps1" -PeerRoot $path -Op $Op -Arg $Arg -Seed $Seed
    [ordered]@{sceneRequested=$script:current;role=$Peer;operation=$Op;result=$r} |
        ConvertTo-Json -Depth 6 -Compress|Add-Content -Encoding utf8 -LiteralPath $Output
    if(!$r.ok){throw "$Peer/$Op failed: $($r.message)"};return $r
}
if(Test-Path -LiteralPath $Output){throw "Use a new evidence file: $Output"}
$script:current='preflight'
$catalog=Run host catalog
$peer=Run client catalog
if(!$catalog.host -or $peer.host -or $catalog.players.Count -lt 2 -or $peer.players.Count -lt 2){throw 'Need connected host and client'}
foreach($s in $Scene){
    $script:current=$s
    try {
        $land=Run host land $s
        $wait=[Diagnostics.Stopwatch]::StartNew()
        do {
            $state=Run client state
            if($state.landed -and !$state.orbit -and $state.scene -eq $land.scene -and $state.seed -eq $land.seed -and $state.flow -eq $land.flow){break}
            if($wait.Elapsed.TotalSeconds -gt 120){throw 'Client did not converge on same landed scene/seed/flow'}
            Start-Sleep -Seconds 1
        }while($true)
        foreach($side in 'client','host'){
            $null=Run $side enter '0'
            $other=if($side -eq 'host'){'client'}else{'host'}
            $null=Run $other state
            $null=Run $side exit '0'
            $null=Run $side board
        }
        $null=Run host orbit
    }catch{
        [ordered]@{sceneRequested=$s;error=$_.Exception.Message;utc=[datetime]::UtcNow.ToString('o')}|ConvertTo-Json -Compress|Add-Content -LiteralPath $Output -Encoding utf8
        $state=Run host state
        if($state.orbit){continue}
        if(!$state.landed){throw 'Scene transition stuck; stop batch to preserve reproducible evidence'}
        # Only depart after verifying both players returned to the ship.
        foreach($side in 'client','host'){
            $p=Run $side state
            if($p.inside){$null=Run $side exit '0'}
            $null=Run $side board
        }
        $null=Run host orbit
    }
}
