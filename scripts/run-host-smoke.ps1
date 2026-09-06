[CmdletBinding()]
param(
    [string]$ReportPath = '',
    [string]$PluginAssembly = '',
    [string]$PluginId = 'NavisFavorites.Diagnostics.2E3F445B-6C6E-4A98-9A9C-203680A746B8'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = 'E:\auto\Navisworks Manage 2024'

if ([string]::IsNullOrWhiteSpace($PluginAssembly)) {
    $PluginAssembly = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\NavisFavorites.bundle\Contents\v21\0.2.0.0\NavisFavorites.dll'
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $projectRoot 'artifacts\host-smoke.json'
}
if (-not (Test-Path -LiteralPath $PluginAssembly)) {
    throw "Plugin assembly not found: $PluginAssembly"
}

Set-Location -LiteralPath $installDirectory
[void][Reflection.Assembly]::LoadFrom((Join-Path $installDirectory 'Autodesk.Navisworks.Automation.dll'))

$automation = [Autodesk.Navisworks.Api.Automation.NavisworksApplication]::TryGetRunningInstance()
if ($null -eq $automation) {
    throw 'No running Navisworks instance was found.'
}

try {
    # Keep the existing Navisworks session alive when this wrapper is disposed.
    $automation.StayOpen()
    $automation.AddPluginAssembly($PluginAssembly)
    $exitCode = $automation.ExecuteAddInPlugin($PluginId, [string[]]@($ReportPath))
    Write-Host "HOST_COMMAND_OK|EXIT=$exitCode|REPORT=$ReportPath"
}
finally {
    $automation.Dispose()
}
