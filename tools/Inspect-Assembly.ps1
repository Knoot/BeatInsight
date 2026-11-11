param(
    [Parameter(Mandatory=$true)][string]$Assembly,
    [string]$Filter = '.',
    [switch]$Bodies,
    [string]$CecilPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LocalSettings.ps1')
Add-Type -Path (Get-BeatInsightSetting 'CecilPath' $CecilPath)
$path = (Resolve-Path -LiteralPath $Assembly).Path
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($path)
try {
    "ASSEMBLY $($module.Assembly.Name.FullName)"
    "SHA256 $((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
    function Write-Type($type) {
        if ($type.FullName -match $Filter) {
            "TYPE $($type.FullName)"
            foreach ($field in $type.Fields) { " FIELD $field" }
            foreach ($method in $type.Methods) {
                " METHOD $method"
                if ($Bodies -and $method.HasBody) {
                    foreach ($instruction in $method.Body.Instructions) { "  $instruction" }
                }
            }
        }
        foreach ($nested in $type.NestedTypes) { Write-Type $nested }
    }
    foreach ($type in $module.Types) { Write-Type $type }
} finally { $module.Dispose() }
