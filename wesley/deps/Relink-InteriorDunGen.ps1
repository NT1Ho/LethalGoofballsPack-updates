# Adapt the exact author's assembly locally; only legacy DunGen type scopes move.
param([string]$GameRoot='D:/STEAM/steamapps/common/Lethal Company',[string]$CandidateRoot=(Join-Path $PSScriptRoot '../candidate'))
$ErrorActionPreference='Stop'
$managed=Join-Path $GameRoot 'Lethal Company_Data/Managed'
$target=Join-Path $CandidateRoot 'BepInEx/plugins/Magic_Wesley-WesleysInteriors/WesleysInteriorShenanigans.dll'
$original='67BD403ADAF18F19C12E362B7EB43E0012F6E3BA101681F44CD0926A8F543F53'
if((Get-FileHash $target).Hash -ne $original){throw 'Expected unmodified 5.0.19 payload from the lockfile'}
Add-Type -Path "$GameRoot/BepInEx/core/Mono.Cecil.dll"
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
@($managed,"$GameRoot/BepInEx/core")|ForEach-Object{$resolver.AddSearchDirectory($_)}
$options=[Mono.Cecil.ReaderParameters]::new();$options.AssemblyResolver=$resolver
$dun=[Mono.Cecil.AssemblyDefinition]::ReadAssembly("$managed/DunGen.dll")
$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($target,$options)
$temp="$target.staged"
try {
    $dd=[Mono.Cecil.AssemblyNameReference]::Parse($dun.Name.FullName)
    $a.MainModule.AssemblyReferences.Add($dd)
    $refs=@($a.MainModule.GetTypeReferences() | Where-Object {$_.FullName -like 'DunGen.*'})
    if($refs.Count -ne 8){throw 'Unexpected DunGen type references'}
    foreach($type in $refs|Where-Object {!$_.IsNested}){
        if($type.Scope.Name -ne 'Assembly-CSharp'){throw "Unexpected scope $type"}
        $type.Scope=$dd
    }
    foreach($type in $refs){if(!$type.Resolve()){throw "Unresolved type $type"}}
    foreach($member in $a.MainModule.GetMemberReferences()|Where-Object {$_.DeclaringType.FullName -like 'DunGen.*'}){
        if(!$member.Resolve()){throw "Unresolved member $member"}
    }
    $a.Write($temp)
} finally {$a.Dispose();$dun.Dispose();$resolver.Dispose()}
Move-Item -LiteralPath $temp -Destination $target -Force
Get-FileHash $target
