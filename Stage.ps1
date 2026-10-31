$ErrorActionPreference='Stop'
$base=$PSScriptRoot
$steam='D:\STEAM\steamapps\common\Lethal Company'
$stage=Join-Path $base 'stage'
$packages=@('mborsh-Wider_Ship_Mod','TestAccount666-ShipWindows','AtomicStudio-Moved_Magnet_Switch','mattymatty-Matty_Fixes','TheDeadSnake-Touchscreen','LethalCompanyModding-VertexLibrary','TestAccount666-TestAccountCore','WhiteSpike-Interactive_Terminal_API')
foreach($name in $packages){
  $src=Join-Path $base "downloads\$name"
  $dest=Join-Path $stage "BepInEx\plugins\$name"
  New-Item -ItemType Directory -Force $dest | Out-Null
  $content=$src
  if(Test-Path "$src\BepInEx\plugins"){$content="$src\BepInEx\plugins"}
  elseif(Test-Path "$src\plugins"){$content="$src\plugins"}
  Copy-Item "$content\*" $dest -Recurse -Force
  $info=Join-Path $stage "BepInEx\pack-info\$name"
  New-Item -ItemType Directory -Force $info | Out-Null
  foreach($f in @('manifest.json','README.md','LICENSE','CHANGELOG.md')){if(Test-Path "$src\$f"){Copy-Item "$src\$f" $info -Force}}
}
$cfg=Join-Path $stage 'BepInEx\config'
New-Item -ItemType Directory -Force $cfg | Out-Null
foreach($name in @('OpJosMod.ReviveCompany.cfg','MegaPiggy.BuyableShotgunShells.cfg')){
  $text=[IO.File]::ReadAllText("$steam\BepInEx\config\$name")
  $text=$text.Replace('DeadBodyWeight = 3.25','DeadBodyWeight = 2.125').Replace('ShotgunShellPrice = 100','ShotgunShellPrice = 80')
  [IO.File]::WriteAllText("$cfg\$name",$text)
}
# Preserve existing item offsets/rotations; only enable ship loading protection.
@'
[ReadableMeshes]
enabled = false
[Particles]
lightning = Vanilla
flies = false
[BadgeFixes]
enabled = false
[CupBoard]
enabled = true
[Radar]
enabled = false
[ItemClipping]
enabled = false
rotate_on_spawn = false
[OutOfBounds]
enabled = true
vertical_offset = 0.01
spawn_in_furniture = true
'@ | Set-Content "$cfg\mattymatty.MattyFixes.cfg" -Encoding utf8
# Keep Celestial Tint's sky and existing soundscape.
@'
[Skybox]
Override Celestial Tint Skybox = false
[Shutter Misc]
Play Wesley Shutter Voice Lines On Shutter Toggle = false
Play Wesley Shutter Voice Lines On Transitions = false
[Misc]
Enable Wesley Sell Audio = false
'@ | Set-Content "$cfg\TestAccount666.ShipWindows.cfg" -Encoding utf8
@'
[Layout]
Primary = <Keyboard>/e
Secondary = <Mouse>/leftButton
Switch =
SwitchAlternative =
'@ | Set-Content "$cfg\me.pm.TheDeadSnake.cfg" -Encoding utf8
Write-Output "Staged eight packages and scoped configuration changes: $stage"
