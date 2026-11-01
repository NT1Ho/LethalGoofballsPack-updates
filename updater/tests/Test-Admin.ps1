param([Parameter(Mandatory=$true)][string]$AdminExe,[string]$ReceiverExe)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('lgp-admin-qa-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$bin=Join-Path $root 'bin'
New-Item -ItemType Directory -Path $bin|Out-Null
$admin=Join-Path $bin 'Lethal company pack Updater Admin.exe'
Copy-Item -LiteralPath $AdminExe -Destination $admin
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
function Compile([string]$name,[string[]]$refs,[string]$kind='exe'){
    $target=if($kind -eq 'dll'){'library'}else{'exe'}
    & $csc /nologo "/target:$target" "/out:$bin\$name.$kind" @refs "$PSScriptRoot\$name.cs"
    if($LASTEXITCODE){throw "Compilation failed: $name"}
}
Compile FakePlugin @() 'dll'
Compile AdminWorkflowTests @("/r:$admin",'/r:System.Net.Http.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
& "$bin\AdminWorkflowTests.exe" $root "$bin\FakePlugin.dll"
if($LASTEXITCODE){throw 'Admin workflow tests failed'}
Compile UiTests @("/r:$admin",'/r:System.Drawing.dll','/r:System.Windows.Forms.dll',"/win32manifest:$PSScriptRoot\..\src\app.manifest")
& "$bin\UiTests.exe" (Join-Path $root 'ui-workspace') $root
if($LASTEXITCODE){throw 'Admin UI tests failed'}
if($ReceiverExe){
    $client=Join-Path $bin 'Lethal company pack Updater.exe'
    Copy-Item -LiteralPath $ReceiverExe -Destination $client
    Compile VerifyAdminContract @("/r:$client",'/r:System.Net.Http.dll')
    & "$bin\VerifyAdminContract.exe" $root
    if($LASTEXITCODE){throw 'Receiver compatibility failed'}
}
"Evidence: $root"
