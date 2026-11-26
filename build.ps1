param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    if (-not (Test-Path -LiteralPath $framework)) {
        $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
    }
    $compiler = Join-Path $framework 'csc.exe'
}
$dist = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
[void][IO.Directory]::CreateDirectory($dist)
$exe = Join-Path $dist 'Shiyi.exe'
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    "/out:$exe", "/win32manifest:$root\src\app.manifest", "/win32icon:$root\ui\App.ico",
    "/resource:$root\ui\App.ico,Shiyi.App.ico")
foreach ($name in 'System', 'System.Core', 'System.Net.Http', 'System.Security', 'System.Web.Extensions', 'System.Drawing', 'System.Windows.Forms', 'System.Xaml') {
    $options += "/reference:$framework\$name.dll"
}
foreach ($name in 'WindowsBase', 'PresentationCore', 'PresentationFramework', 'UIAutomationClient', 'UIAutomationTypes', 'System.Speech') {
    $options += "/reference:$framework\WPF\$name.dll"
}
foreach ($view in Get-ChildItem -LiteralPath (Join-Path $root 'ui') -Filter '*.xaml') {
    $options += "/resource:$($view.FullName),Shiyi.$($view.Name)"
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @options @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $exe
