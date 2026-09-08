#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $Version = '1.0.0',
    [string] $IsccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    [string] $PublishDir
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
if (-not (Test-Path -LiteralPath $IsccPath)) {
    throw '找不到 Inno Setup 6 compiler。請安裝官方 Inno Setup，或使用 -IsccPath 指定 ISCC.exe。'
}
& (Join-Path $PSScriptRoot 'Test-Setup.ps1')

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $null = Get-Command dotnet -ErrorAction Stop
    # A fresh publish folder cannot accidentally bundle previous config, recordings, or API keys.
    $publish = Join-Path $repo ('artifacts\publish\' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish (Join-Path $repo 'AudioUI\AudioUI.csproj') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:AudioZenInstallerBuild=true -p:Version=$Version -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
} else {
    $publish = (Resolve-Path -LiteralPath $PublishDir -ErrorAction Stop).Path
}
if (Test-Path -LiteralPath (Join-Path $publish 'appsettings.json')) {
    throw 'Publish output contains a private appsettings.json. Refusing to package it.'
}
& $IsccPath /Qp "/DPublishDir=$publish" "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'AudioZen.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
Write-Host "Installer: $(Join-Path $repo 'artifacts\installer\AudioZen.Setup.exe')"
