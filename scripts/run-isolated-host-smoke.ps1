[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModelPath,
    [Parameter(Mandatory = $true)]
    [string]$PluginAssembly,
    [Parameter(Mandatory = $true)]
    [string]$PluginId,
    [Parameter(Mandatory = $true)]
    [string]$ReportPath,
    [string[]]$PluginParameters = @()
)

$ErrorActionPreference = 'Stop'
$installDirectory = 'E:\auto\Navisworks Manage 2024'
foreach ($required in @($ModelPath, $PluginAssembly)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required file not found: $required"
    }
}

$ModelPath = (Resolve-Path -LiteralPath $ModelPath).Path
$PluginAssembly = (Resolve-Path -LiteralPath $PluginAssembly).Path
if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path (Get-Location).Path $ReportPath
}

Set-Location -LiteralPath $installDirectory
[void][Reflection.Assembly]::LoadFrom((Join-Path $installDirectory 'Autodesk.Navisworks.Automation.dll'))

$automation = $null
try {
    $automation = New-Object Autodesk.Navisworks.Api.Automation.NavisworksApplication
    $automation.Visible = $false
    $automation.DisableProgress()
    $automation.OpenFile($ModelPath, [string[]]@())
    $automation.AddPluginAssembly($PluginAssembly)
    $parameters = [string[]](@($ReportPath) + $PluginParameters)
    $exitCode = $automation.ExecuteAddInPlugin($PluginId, $parameters)
    Write-Host "ISOLATED_HOST_OK|EXIT=$exitCode|REPORT=$ReportPath"
}
finally {
    if ($null -ne $automation) {
        $automation.Dispose()
    }
}
