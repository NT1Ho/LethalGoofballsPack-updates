param([string]$GameRoot='D:/STEAM/steamapps/common/Lethal Company')
$ErrorActionPreference='Stop'
Add-Type -Path "$GameRoot/BepInEx/core/Mono.Cecil.dll"
$candidate=(Resolve-Path (Join-Path $PSScriptRoot '../candidate')).Path
$lockFile=Join-Path $PSScriptRoot 'packages.lock.json'
$lock=Get-Content $lockFile -Raw|ConvertFrom-Json -AsHashtable
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($dir in @("$GameRoot/BepInEx/core","$GameRoot/Lethal Company_Data/Managed")){$resolver.AddSearchDirectory($dir)}
$rp=[Mono.Cecil.ReaderParameters]::new(); $rp.AssemblyResolver=$resolver
$guids=@{}; $issues=[System.Collections.Generic.List[string]]::new();$plugins=@()
foreach($root in @($GameRoot,$candidate)) {
  foreach($file in Get-ChildItem (Join-Path $root 'BepInEx/plugins') -Recurse -File -Filter '*.dll'){
    try{$a=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($file.FullName,$rp)}catch{continue}
    try{
      foreach($t in $a.MainModule.Types){
        foreach($attr in $t.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'}){
          $id=[string]$attr.ConstructorArguments[0].Value;$version=[string]$attr.ConstructorArguments[2].Value
          $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
          if($guids.ContainsKey($id) -and $guids[$id].file -ne $relative -and ($root -eq $candidate -or $guids[$id].source -eq 'candidate')){$issues.Add("Duplicate plugin GUID $id $relative")}
          $hard=@()
          foreach($dep in $t.CustomAttributes|Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInDependency'}){
            if($dep.ConstructorArguments.Count -eq 2 -and $dep.ConstructorArguments[1].Value -eq 2){continue}
            if(!$dep.HasConstructorArguments){$issues.Add("Unreadable dependency on $id");continue}
            $hard+=[string]$dep.ConstructorArguments[0].Value
          }
          $p=[ordered]@{guid=$id;version=$version;file=$relative;source=$(if($root -eq $candidate){'candidate'}else{'installed'});hard_dependencies=$hard}
          $guids[$id]=$p;$plugins+=$p
        }
      }
    }finally{$a.Dispose()}
  }
}
foreach($p in $plugins |Where-Object source -eq 'candidate'){
  foreach($req in $p.hard_dependencies){if(!$guids.ContainsKey($req)){$issues.Add("Missing hard plugin dependency $($p.guid): $req")}}
}
foreach($v in $lock.Values){
  foreach($file in $v.files){
    if($v.action -ne 'reuse' -and $file.path.EndsWith('/WesleysInteriorShenanigans.dll')){
      $actual=(Get-FileHash (Join-Path $candidate $file.path)).Hash.ToLowerInvariant()
      $file['candidate_sha256']=$actual
      if($actual -ne $file.sha256){$file['candidate_note']='V81 DunGen scope relocation; see Relink-InteriorDunGen.ps1'}
    }
  }
  $v['plugins']=@($plugins|Where-Object {$v.files.path -contains $_.file})
}
[IO.File]::WriteAllText($lockFile,($lock|ConvertTo-Json -Depth 12))
[ordered]@{static_issues=$issues;candidate_plugins=@($plugins|Where-Object source -eq 'candidate')} |ConvertTo-Json -Depth 8|Set-Content (Join-Path $PSScriptRoot 'guid-audit.json')
$issues
