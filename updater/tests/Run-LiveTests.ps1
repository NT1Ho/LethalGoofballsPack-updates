$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll')
& $compiler /nologo /optimize+ /target:exe /utf8output @refs "/out:$(Join-Path $PSScriptRoot 'LiveTests.exe')" (Join-Path $projectRoot 'src\Common.cs') (Join-Path $PSScriptRoot 'LiveTests.cs')
if($LASTEXITCODE -ne 0){throw 'Live test build failed'}
& (Join-Path $PSScriptRoot 'LiveTests.exe') *>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot 'live-results.txt')
if($LASTEXITCODE -ne 0){throw 'Live tests failed'}
