param([string]$ClientPath=(Join-Path $PSScriptRoot 'dist\Lethal company pack Updater.exe'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tool=Join-Path $PSScriptRoot 'dist\ClientPublish.exe'
& $compiler /nologo /target:exe /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll "/out:$tool" (Join-Path $PSScriptRoot 'src\Common.cs') (Join-Path $PSScriptRoot 'src\Cloud.cs') (Join-Path $PSScriptRoot 'src\CosPublisher.cs') (Join-Path $PSScriptRoot 'src\SelfUpdate.cs') (Join-Path $PSScriptRoot 'src\PublisherCore.cs') (Join-Path $PSScriptRoot 'ClientPublish.cs')
if($LASTEXITCODE -ne 0){throw 'Client publisher build failed'}
& $tool $ClientPath
if($LASTEXITCODE -ne 0){throw 'Client publication failed'}
