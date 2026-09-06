[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$NavisworksInstallDir = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($NavisworksInstallDir)) {
    $runtimeKey = 'HKLM:\SOFTWARE\Autodesk\Navisworks API Runtime\21\Navisworks Manage'
    if (Test-Path -LiteralPath $runtimeKey) {
        $NavisworksInstallDir = (Get-ItemProperty -LiteralPath $runtimeKey).Path
    }
}

if ([string]::IsNullOrWhiteSpace($NavisworksInstallDir)) {
    $NavisworksInstallDir = 'E:\auto\Navisworks Manage 2024'
}

$NavisworksInstallDir = $NavisworksInstallDir.TrimEnd('\', '/')

$apiPath = Join-Path $NavisworksInstallDir 'Autodesk.Navisworks.Api.dll'
if (-not (Test-Path -LiteralPath $apiPath)) {
    throw "未找到 Navisworks 2024 API：$apiPath"
}

$installProperty = "-p:NavisworksInstallDir=$NavisworksInstallDir"
& dotnet build (Join-Path $projectRoot 'NavisFavorites.sln') `
    -c $Configuration `
    -p:Platform=x64 `
    $installProperty `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "NavisFavorites 编译失败，退出码：$LASTEXITCODE"
}

$output = Join-Path $projectRoot "src\NavisFavorites\bin\x64\$Configuration\net48"
Write-Host "BUILD_OK|OUTPUT=$output"
