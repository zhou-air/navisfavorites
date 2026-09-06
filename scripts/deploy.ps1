[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [ValidateSet('Bundle', 'InstallPlugins')]
    [string]$Mode = 'Bundle'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
}

$sourceDirectory = Join-Path $projectRoot "src\NavisFavorites\bin\x64\$Configuration\net48"
$bundleRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\NavisFavorites.bundle'
$contentsRoot = Join-Path $bundleRoot 'Contents\v21\0.2.0.0'
$directRoot = 'E:\auto\Navisworks Manage 2024\Plugins\NavisFavorites'

$dll = Join-Path $sourceDirectory 'NavisFavorites.dll'
$ribbon = Join-Path $sourceDirectory 'NavisFavoritesRibbon.xaml'
foreach ($required in @($dll, $ribbon)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "缺少部署文件：$required"
    }
}

function Copy-PluginPayload {
    param([Parameter(Mandatory = $true)][string]$Destination)

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $Destination 'NavisFavorites.dll') -Force
    Copy-Item -LiteralPath $ribbon -Destination (Join-Path $Destination 'NavisFavoritesRibbon.xaml') -Force

    foreach ($locale in @('zh-CN', 'en-US', 'ja-JP')) {
        $localeRoot = Join-Path $Destination $locale
        New-Item -ItemType Directory -Path $localeRoot -Force | Out-Null
        Copy-Item -LiteralPath $ribbon -Destination (Join-Path $localeRoot 'NavisFavoritesRibbon.xaml') -Force
    }

    $pdb = Join-Path $sourceDirectory 'NavisFavorites.pdb'
    if (Test-Path -LiteralPath $pdb) {
        Copy-Item -LiteralPath $pdb -Destination (Join-Path $Destination 'NavisFavorites.pdb') -Force
    }
}

if ($Mode -eq 'Bundle') {
    Copy-PluginPayload -Destination $contentsRoot
    New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bundle\PackageContents.xml') -Destination (Join-Path $bundleRoot 'PackageContents.xml') -Force

    # 两种加载方式保持互斥：只移除备用目录中由本脚本创建的明确文件。
    foreach ($path in @(
        (Join-Path $directRoot 'NavisFavorites.dll'),
        (Join-Path $directRoot 'NavisFavorites.pdb'),
        (Join-Path $directRoot 'NavisFavoritesRibbon.xaml'),
        (Join-Path $directRoot 'zh-CN\NavisFavoritesRibbon.xaml'),
        (Join-Path $directRoot 'en-US\NavisFavoritesRibbon.xaml'),
        (Join-Path $directRoot 'ja-JP\NavisFavoritesRibbon.xaml'))) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    $deployedRoot = $contentsRoot
}
else {
    Copy-PluginPayload -Destination $directRoot
    $manifest = Join-Path $bundleRoot 'PackageContents.xml'
    if (Test-Path -LiteralPath $manifest) {
        Move-Item -LiteralPath $manifest -Destination (Join-Path $bundleRoot 'PackageContents.disabled.xml') -Force
    }
    $deployedRoot = $directRoot
}

$hash = (Get-FileHash -LiteralPath (Join-Path $deployedRoot 'NavisFavorites.dll') -Algorithm SHA256).Hash
Write-Host "DEPLOY_OK|MODE=$Mode|ROOT=$deployedRoot|DLL_SHA256=$hash"
