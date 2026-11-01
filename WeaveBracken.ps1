# Regenerate the missing NGO RPC glue of the installed MIT-licensed 2.0.4 fork.
param([Parameter(Mandatory)][string]$GameRoot,[Parameter(Mandatory)][string]$OutputDir,
      [Parameter(Mandatory)][string]$Dotnet,[Parameter(Mandatory)][string]$Patcher)
$ErrorActionPreference='Stop'
$src=Join-Path $GameRoot 'BepInEx/plugins/readthisifbad-SnatchinBracken/SnatchingBracken.dll'
if((Get-FileHash $src).Hash -ne 'F21BFE226BDC21C53267FD3AFD824733452225A4515DD59D60D6125CB5A9662D'){throw 'Expected the original pack binary'}
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$target=Join-Path (Resolve-Path $OutputDir) 'SnatchingBracken.dll'
if([IO.Path]::GetFullPath($src) -eq $target){throw 'Write only to a staging directory'}
$core=Join-Path $GameRoot 'BepInEx/core'
Add-Type -Path (Join-Path $core 'Mono.Cecil.dll')
# The shipped fork has no PDB. NetcodePatcher requires a symbol stream even when there are no sequence points.
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($dir in @((Join-Path $GameRoot 'Lethal Company_Data/Managed'),$core,(Get-ChildItem (Join-Path $GameRoot 'BepInEx/plugins') -Recurse -Filter LethalConfig.dll).DirectoryName)){$resolver.AddSearchDirectory($dir)}
$rp=[Mono.Cecil.ReaderParameters]::new()
$rp.AssemblyResolver=$resolver
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($src,$rp)
try {
    $wp=[Mono.Cecil.WriterParameters]::new()
    $wp.WriteSymbols=$true
    $wp.SymbolWriterProvider=[Mono.Cecil.Cil.PortablePdbWriterProvider]::new()
    $a.Write($target,$wp)
} finally {$a.Dispose()}
$deps=Join-Path $OutputDir 'ncp-deps.list'
@(Get-ChildItem (Join-Path $GameRoot 'Lethal Company_Data/Managed'),$core -File -Filter '*.dll';Get-ChildItem (Join-Path $GameRoot 'BepInEx/plugins') -Recurse -Filter LethalConfig.dll).FullName|Set-Content $deps
$output=& $Dotnet $Patcher -uv 2022.3.62 -nv 1.12.2 -tv 1.0.0 $target --dependency-file $deps
if($LASTEXITCODE -ne 0 -or ($output -match '\bERR\]')){throw ($output -join [Environment]::NewLine)}
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($target)
try {if(!($a.MainModule.GetType('SnatchingBracken.Patches.network.FlowermanBinding').Methods.Name -match '^__rpc_handler_')){throw 'No generated RPC handlers'}} finally {$a.Dispose()}
(Get-FileHash $target).Hash
