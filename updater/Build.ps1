param([string]$OutputDirectory,[string]$ChannelConfig,[switch]$SkipTests,[string]$GameDir,[string]$Dotnet="dotnet")
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
if(-not $ChannelConfig -and (Test-Path (Join-Path $projectRoot 'cos-channel.json'))){$ChannelConfig=Join-Path $projectRoot 'cos-channel.json'}
$diagnosticProject=Join-Path $projectRoot '..\diagnostics'
if(!(Test-Path $diagnosticProject)){$diagnosticProject=Join-Path $projectRoot 'diagnostics'}
if($GameDir){& $Dotnet build "$diagnosticProject/LethalDiagnostics.csproj" -c Release "-p:GameDir=$GameDir" --nologo;if($LASTEXITCODE){throw 'Diagnostic build failed'}}
$diagnosticDll=Join-Path $diagnosticProject 'bin\Release\netstandard2.1\LethalDiagnostics.dll'
if(!(Test-Path $diagnosticDll)){throw 'First build requires -GameDir <legal V81 game directory> and -Dotnet <SDK path>.'}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputDir = if($OutputDirectory){$OutputDirectory}else{Join-Path $projectRoot 'dist'}
$channelResource = @(); if($ChannelConfig){$channelResource=@("/resource:$ChannelConfig,cos-channel.json")}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Xaml.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll','/r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll')
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
foreach($dll in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','WindowsFormsIntegration.dll')){$refs += "/r:$wpf\$dll"}
$common = @((Join-Path $projectRoot 'src\Common.cs'),(Join-Path $projectRoot 'src\Cloud.cs'))
$ui = Join-Path $projectRoot 'src\UiStartup.cs'
$diagnostics=@((Join-Path $projectRoot 'src\DiagnosticPresentation.cs'),(Join-Path $projectRoot 'src\DiagnosticDialogs.cs'),(Join-Path $projectRoot 'src\DiagnosticsReview.cs'),(Join-Path $projectRoot 'src\DiagnosticsTimeline.cs'),(Join-Path $projectRoot 'src\DiagnosticsStore.cs'),(Join-Path $projectRoot 'src\DiagnosticsWindow.cs'),"/resource:$diagnosticDll,LethalDiagnostics.dll")
$selfUpdate=Join-Path $projectRoot 'src\SelfUpdate.cs'
$clientVersion=Join-Path $projectRoot 'src\ClientVersion.cs'
$inventory=@((Join-Path $projectRoot 'src\ModInventory.cs'),(Join-Path $projectRoot 'src\ModWindow.cs'),(Join-Path $projectRoot 'src\ModNames.cs'))
$inventory += "/resource:$(Join-Path $projectRoot 'assets\settings.ico'),settings.ico"
$inventory += (Join-Path $projectRoot 'src\PageContent.cs'),(Join-Path $projectRoot 'src\PageEditor.cs')
$inventory += (Join-Path $projectRoot 'src\HistoryWindow.cs'),"/resource:$(Join-Path $projectRoot 'assets\update-history.json'),update-history.json"
$cecil=@("/r:$(Join-Path $projectRoot 'lib\Mono.Cecil.dll')","/resource:$(Join-Path $projectRoot 'lib\Mono.Cecil.dll'),Mono.Cecil.dll","/resource:$(Join-Path $projectRoot 'lib\Mono.Cecil.LICENSE.txt'),Mono.Cecil.LICENSE.txt")
$manifest = Join-Path $projectRoot 'src\app.manifest'
$publisherCore = @((Join-Path $projectRoot 'src\PublisherCore.cs'),(Join-Path $projectRoot 'src\CosPublisher.cs'))
& $compiler /nologo /optimize+ /target:winexe /platform:anycpu "/win32manifest:$manifest" "/win32icon:$(Join-Path $projectRoot 'assets\client.ico')" /utf8output @refs @channelResource "/out:$(Join-Path $outputDir 'Lethal company pack Updater.exe')" $common $ui $selfUpdate $clientVersion @diagnostics @inventory @cecil (Join-Path $projectRoot 'src\Client.cs')
if ($LASTEXITCODE -ne 0) { throw 'Client build failed' }
& $compiler /nologo /optimize+ /target:winexe /platform:anycpu "/win32manifest:$manifest" "/win32icon:$(Join-Path $projectRoot 'assets\settings.ico')" /utf8output @refs @channelResource "/out:$(Join-Path $outputDir 'Lethal company pack Updater Admin.exe')" $common $ui $publisherCore @inventory @cecil (Join-Path $projectRoot 'src\AdminCatalog.cs') (Join-Path $projectRoot 'src\Publisher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Publisher build failed' }
if (-not $SkipTests -and (Test-Path (Join-Path $projectRoot 'tests\Tests.cs'))) {
    & $compiler /nologo /target:exe @refs "/out:$(Join-Path $outputDir 'PagePublishTests.exe')" $common $publisherCore (Join-Path $projectRoot 'tests\PagePublishTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Page publication test build failed' }
    & (Join-Path $outputDir 'PagePublishTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Page publication tests failed' }
    & $compiler /nologo /target:exe @refs "/r:$(Join-Path $outputDir 'Lethal company pack Updater Admin.exe')" "/out:$(Join-Path $outputDir 'PageEditorTests.exe')" (Join-Path $projectRoot 'tests\PageEditorTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Page editor test build failed' }
    & (Join-Path $outputDir 'PageEditorTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Page editor tests failed' }

    & $compiler /nologo /optimize+ /target:exe /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\Tests.exe')" $common $publisherCore (Join-Path $projectRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & (Join-Path $projectRoot 'tests\Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & $compiler /nologo /optimize+ /target:exe /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\CloudTests.exe')" $common $publisherCore $selfUpdate (Join-Path $projectRoot 'tests\CloudTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'COS test build failed' }
    & (Join-Path $projectRoot 'tests\CloudTests.exe') (Join-Path $outputDir 'Lethal company pack Updater.exe')
    if ($LASTEXITCODE -ne 0) { throw 'COS tests failed' }
    & $compiler /nologo /target:exe /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\ReferenceTests.exe')" $common $publisherCore (Join-Path $projectRoot 'tests\ReferenceTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Reference test build failed' }
    & (Join-Path $projectRoot 'tests\ReferenceTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Reference tests failed' }
    & $compiler /nologo /optimize+ /target:exe /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\RoutingTests.exe')" $common (Join-Path $projectRoot 'tests\RoutingTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Routing test build failed' }
    & (Join-Path $projectRoot 'tests\RoutingTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Routing tests failed' }
    & $compiler /nologo /optimize+ /target:exe /main:UiTests "/win32manifest:$manifest" /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\UiTests.exe')" $common $ui $publisherCore @inventory @cecil (Join-Path $projectRoot 'src\AdminCatalog.cs') (Join-Path $projectRoot 'src\Publisher.cs') (Join-Path $projectRoot 'tests\UiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI test build failed' }
    & (Join-Path $projectRoot 'tests\UiTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
    & $compiler /nologo /optimize+ /target:exe /main:DpiTests "/win32manifest:$manifest" /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\DpiTests.exe')" $common $ui $publisherCore @inventory @cecil (Join-Path $projectRoot 'src\AdminCatalog.cs') (Join-Path $projectRoot 'src\Publisher.cs') (Join-Path $projectRoot 'src\Client.cs') $selfUpdate $clientVersion @diagnostics (Join-Path $projectRoot 'tests\DpiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'DPI test build failed' }
    & (Join-Path $projectRoot 'tests\DpiTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'DPI tests failed' }
    & $compiler /nologo /optimize+ /target:exe /platform:anycpu /utf8output @refs "/out:$(Join-Path $projectRoot 'tests\SelfUpdateTests.exe')" $common $selfUpdate $clientVersion (Join-Path $projectRoot 'tests\SelfUpdateTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Self-update test build failed' }
    & (Join-Path $projectRoot 'tests\SelfUpdateTests.exe') (Join-Path $outputDir 'Lethal company pack Updater.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Self-update tests failed' }
    & $compiler /nologo /target:exe "/win32manifest:$manifest" @refs "/r:$(Join-Path $outputDir 'Lethal company pack Updater.exe')" "/out:$(Join-Path $outputDir 'ClientUiLiveTest.exe')" (Join-Path $projectRoot 'tests\ClientUiLiveTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Live UI test build failed' }
    & $compiler /nologo /target:library "/out:$(Join-Path $projectRoot 'tests\FakePlugin.dll')" (Join-Path $projectRoot 'tests\FakePlugin.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
    & $compiler /nologo /target:exe /utf8output @refs @cecil "/out:$(Join-Path $projectRoot 'tests\InventoryTests.exe')" $common $publisherCore (Join-Path $projectRoot 'src\ModInventory.cs') (Join-Path $projectRoot 'src\ModNames.cs') (Join-Path $projectRoot 'src\AdminCatalog.cs') (Join-Path $projectRoot 'tests\InventoryTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Inventory tests build failed' }
    & $compiler /nologo /target:exe /main:CatalogUiTests "/win32manifest:$manifest" /utf8output @refs @cecil "/out:$(Join-Path $projectRoot 'tests\CatalogUiTests.exe')" $common $ui $publisherCore @inventory (Join-Path $projectRoot 'src\AdminCatalog.cs') (Join-Path $projectRoot 'src\Publisher.cs') (Join-Path $projectRoot 'tests\CatalogUiTests.cs') (Join-Path $projectRoot 'tests\UiTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Catalog UI tests build failed' }
    & $compiler /nologo /target:exe /utf8output @refs "/r:$(Join-Path $outputDir 'Lethal company pack Updater.exe')" "/out:$(Join-Path $outputDir 'HistoryTests.exe')" (Join-Path $projectRoot 'tests\HistoryTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'History tests build failed' }
    & (Join-Path $outputDir 'HistoryTests.exe') (Join-Path $outputDir 'history-test-renders')
    if ($LASTEXITCODE -ne 0) { throw 'History tests failed' }
}
Get-ChildItem -LiteralPath $outputDir -Filter *.exe | Select-Object Name,Length


