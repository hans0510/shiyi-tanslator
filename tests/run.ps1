$ErrorActionPreference = 'Stop'
$src = Join-Path (Split-Path -Parent $PSScriptRoot) 'src'
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$exe = Join-Path ([IO.Path]::GetTempPath()) ('ShiyiDictionaryTests-' + [guid]::NewGuid().ToString('N') + '.exe')
try {
    & (Join-Path $fx 'csc.exe') /nologo /codepage:65001 "/out:$exe" "/r:$fx\System.Net.Http.dll" "/r:$fx\System.Security.dll" "/r:$fx\System.Web.Extensions.dll" (Join-Path $src 'Lexicon.cs') (Join-Path $src 'Model.cs') (Join-Path $PSScriptRoot 'DictionaryTests.cs')
    if ($LASTEXITCODE) { throw 'Test compilation failed.' }
    & $exe
    if ($LASTEXITCODE) { throw 'Dictionary checks failed.' }
} finally {
    if ([IO.File]::Exists($exe)) { [IO.File]::Delete($exe) }
}
