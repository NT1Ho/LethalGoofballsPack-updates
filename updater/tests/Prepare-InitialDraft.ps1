$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'dist\LethalGoofballsPack Publisher.exe')) | Out-Null
$publisherRoot = Join-Path $env:LOCALAPPDATA 'LethalGoofballsPackPublisher'
$workspace = New-Object LcPack.Workspace($publisherRoot)
if ($workspace.Current.baseVersion -ne '' -or $workspace.Current.deletes.Count -ne 0 -or @($workspace.Current.files | Where-Object { $_.path -ne 'BepInEx/plugins/updater-ui-test.txt' }).Count -ne 0) {
    throw 'Existing user draft found; refusing to replace it.'
}
$packFolder = 'C:\Users\nono4\OneDrive\Desktop\LethalGoofballsPack_V1.4'
$archivePath = 'C:\Users\nono4\OneDrive\Desktop\LethalGoofballsPack_V1.4.zip'
$expectedArchiveHash = '054218821027d2ea94d3825ba43bc74123af678b66acabec7bf8c54288dd0498'
if ([LcPack.Data]::Hash($archivePath) -ne $expectedArchiveHash) { throw 'Pack ZIP changed; refusing to assume its contents.' }
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
$inventory = @()
try {
    foreach ($entry in $archive.Entries) {
        if (!$entry.Name) { continue }
        $relative = [LcPack.SafePath]::Validate($entry.FullName)
        $localPath = [LcPack.SafePath]::Target($packFolder, $relative)
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [LcPack.Data]::Hex($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
        if (!(Test-Path -LiteralPath $localPath) -or [LcPack.Data]::Hash($localPath) -ne $hash) { throw "Extracted pack differs from ZIP: $relative" }
        $inventory += [pscustomobject]@{Path=$relative;Size=$entry.Length;Sha256=$hash;Source=$localPath}
    }
} finally { $archive.Dispose() }
if ($inventory.Count -ne 475) { throw 'Unexpected pack file count' }
$workspace.SetRemote($null)
$timer = [Diagnostics.Stopwatch]::StartNew()
foreach ($item in $inventory) { $workspace.Add($item.Source, $item.Path) }
$timer.Stop()
$inventory | Select-Object Path,Size,Sha256 | Export-Csv -LiteralPath (Join-Path $PSScriptRoot 'initial-pack-inventory.csv') -NoTypeInformation -Encoding UTF8
Write-Output ("Prepared {0} files, {1} bytes; all match the unchanged V1.4 ZIP. Snapshot time: {2:0.00} s. No upload performed." -f $inventory.Count, ($inventory | Measure-Object Size -Sum).Sum, $timer.Elapsed.TotalSeconds)
