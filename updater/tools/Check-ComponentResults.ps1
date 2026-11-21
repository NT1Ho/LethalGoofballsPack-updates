# Run in PowerShell 7 before importing approved first-party results into Pack Updater.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Records,
    [Parameter(Mandatory)][hashtable]$Checkouts,
    [Parameter(Mandatory)][hashtable]$Deliveries,
    [Parameter(Mandatory)][string]$PreviousManifest,
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$Plan,
    [string]$PreviousSources,
    [hashtable]$DeliveryRoots=@{}
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$previous=Get-Content -LiteralPath $PreviousManifest -Raw | ConvertFrom-Json -AsHashtable
$installed=@{}
foreach($file in $previous.files){$installed[$file.path]=$file.sha256}
$results=@(Get-Content -LiteralPath $Records -Raw | ConvertFrom-Json -AsHashtable)
$versions=@{}
if($PreviousSources){foreach($entry in (Get-Content -LiteralPath $PreviousSources -Raw|ConvertFrom-Json)){if($entry.package){$versions[$entry.package]=$entry.version}}}
$components=@()
$files=@{}
$root=[IO.Path]::GetFullPath($Stage)
[void][IO.Directory]::CreateDirectory($root)

foreach($result in $results){
    $repo=$Checkouts[$result.source_repository]
    $delivery=$Deliveries[$result.package]
    if(!$repo -or !$delivery){throw "Supply the checkout and verified delivery for $($result.package)"}
    $origin=git -C $repo remote get-url origin
    if($LASTEXITCODE -ne 0 -or $origin -ne ($result.source_repository+'.git')){throw "Incorrect source checkout for $($result.package)"}
    $ref='refs/heads/'+$result.source_branch
    $remote=git -C $repo ls-remote --exit-code origin $ref
    if($LASTEXITCODE -ne 0){throw "Cannot verify $ref"}
    $head=($remote -split '\s+')[0]
    $versionFile=git -C $repo show "${head}:$($result.version_file)"
    if($LASTEXITCODE -ne 0){throw "Inspect the new source head $head before packaging"}
    if(($versionFile -join "`n") -notmatch $result.version_marker){throw "The development version changed for $($result.package). Check for a newer tested delivery."}
    foreach($commit in @($result.source_commit,$result.evidence_commit)){
        if($commit -cnotmatch '^[a-f0-9]{40}$'){throw "Incomplete provenance for $($result.package)"}
        git -C $repo merge-base --is-ancestor $commit $head
        if($LASTEXITCODE -ne 0){throw "$commit is absent from $ref"}
    }
    git -C $repo merge-base --is-ancestor $result.source_commit $result.evidence_commit
    if($LASTEXITCODE -ne 0){throw "Evidence predates the selected build for $($result.package)"}
    # Evidence and build inputs can advance independently. Review new runtime changes before choosing a build.
    $changed=git -C $repo diff --name-only $result.source_commit $head -- @($result.runtime_source_paths)
    if($LASTEXITCODE -ne 0 -or $changed){throw "Review changes after the selected build for $($result.package): $changed"}

    $prefix=$result.install_prefix
    $runtime=@{}
    if($result.archive_sha256){
        if((Get-FileHash -LiteralPath $delivery).Hash -ne $result.archive_sha256){throw "Archive hash failed for $($result.package)"}
        $archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $delivery).Path)
        try{
            foreach($entry in $archive.Entries){
                $name=$entry.FullName.Replace('\','/')
                if(!$entry.Name -or $name -notmatch '^BepInEx/(plugins|config|patchers)/'){continue}
                if((!$name.StartsWith($prefix,[StringComparison]::Ordinal) -and $name -cnotin $result.additional_runtime_files) -or
                   $name -match ':|(^|/)\.\.?(/|$)|//|GameHarness|AuthorTools|CompanyPractice' -or $runtime.ContainsKey($name)){throw "Review unexpected component path $name"}
                $destination=Join-Path $root $name
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$true)
                $runtime[$name]=(Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()
            }
        }finally{$archive.Dispose()}
        if(!$runtime.Count){throw "No runtime files in $($result.package)"}
    }else{
        $path=$result.runtime_file
        $runtime[$path]=(Get-FileHash -LiteralPath $delivery).Hash.ToLowerInvariant()
        $destination=Join-Path $root $path
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        Copy-Item -LiteralPath $delivery -Destination $destination
    }
    foreach($path in $result.additional_runtime_files){if(!$runtime.ContainsKey($path)){throw "Missing approved additional runtime file $path"}}
    if($runtime[$result.runtime_file] -ne $result.verified_dll_sha256){throw "Wrong built DLL for $($result.package)"}
    $stale=@($installed.Keys|Where-Object {$prefix -and $_.StartsWith($prefix,[StringComparison]::Ordinal) -and !$runtime.ContainsKey($_)})
    if($stale.Count){throw "Review removed runtime files before importing $($result.package): $stale"}
    foreach($path in @(($runtime.Keys)|Sort-Object)){
        if($files.ContainsKey($path)){throw "Component paths overlap: $path"}
        $before=$installed[$path]
        $files[$path]=[ordered]@{package=$result.package;old_sha256=$before;sha256=$runtime[$path];changed=($before -ne $runtime[$path])}
    }
    $state=git -C $repo status --short --untracked-files=no
    $newer=@()
    if($result.delivery_glob -and $DeliveryRoots[$result.package]){
        $approved=[version]($result.version -replace '-.*$','')
        $newer=@(Get-ChildItem -LiteralPath $DeliveryRoots[$result.package] -Filter $result.delivery_glob -Recurse -File | Where-Object {
            $_.Name -match '[-_](\d+\.\d+\.\d+)(?:-|\.zip$)' -and [version]$Matches[1] -gt $approved
        }|ForEach-Object FullName)
        if($newer.Count){Write-Warning "Newer delivery files found for $($result.package). Review their source and evidence before selecting them: $newer"}
    }
    $components+=[ordered]@{package=$result.package;previous_version=$versions[$result.package];version=$result.version;commit=$result.source_commit;evidence=$result.evidence_commit;reviewed_head=$head;runtime_files=$runtime.Count;local_changes=@($state);newer_deliveries=$newer}
    "$($result.package) $($versions[$result.package]) -> $($result.version) · $($runtime.Count) checked files · source $head"
}
[IO.File]::WriteAllText($Plan,(ConvertTo-Json -InputObject @{components=$components;files=$files} -Depth 8))
