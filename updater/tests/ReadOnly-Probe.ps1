$ErrorActionPreference='Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path (Get-Location) 'updater\dist\Lethal company pack Updater.exe'))
$sourceRoot = 'C:\Users\nono4\OneDrive\Desktop\LethalGoofballsPack_V1.4'
$inventory = Import-Csv -LiteralPath .\updater\tests\initial-pack-inventory.csv
$timer = [Diagnostics.Stopwatch]::StartNew()
foreach ($f in $inventory) {
  $path = [LcPack.SafePath]::Target($sourceRoot,$f.Path)
  if ([LcPack.Data]::Hash($path) -ne $f.Sha256) { throw ('Changed source file: '+$f.Path) }
}
$timer.Stop()
Write-Output ('Read-only SHA-256 verification: '+$inventory.Count+' files; '+$timer.Elapsed.TotalSeconds.ToString('0.00')+' s; warm local cache; no files changed.')
$transfer = New-Object LcPack.Transfer
$timer.Restart()
try { $m=$transfer.Latest().GetAwaiter().GetResult(); Write-Output ('Live GitHub manifest: '+$m.version) } catch { Write-Output ('Live GitHub check: '+$_.Exception.GetBaseException().Message) } finally { $timer.Stop(); $transfer.Dispose() }
Write-Output ('Live GitHub check elapsed: '+$timer.Elapsed.TotalSeconds.ToString('0.00')+' s')