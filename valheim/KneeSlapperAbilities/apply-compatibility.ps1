param(
    [Parameter(Mandatory=$true)][string]$ProfilePath,
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim first.' }
$ProfilePath = (Resolve-Path -LiteralPath $ProfilePath).Path
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$receipt = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'compat-tools\receipt.json') -Raw | ConvertFrom-Json
$pending = @()
foreach ($mod in $receipt.mods) {
    $target = Join-Path $ProfilePath $mod.relativePath
    $hash = (Get-FileHash -LiteralPath $target).Hash
    if ($hash -eq $mod.patchedSHA256) { Write-Output "Already patched: $($mod.dll)"; continue }
    if ($hash -ne $mod.originalSHA256) { throw "Unknown DLL version: $target. Review compatibility before patching." }
    $pending += $mod
}
if (!$pending.Count) { return }
& $Dotnet build (Join-Path $PSScriptRoot 'compat-tools\CompatTools.csproj') -c Release "-p:GamePath=$GamePath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed.' }
$backupDir = Join-Path $PSScriptRoot ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$outputDir = Join-Path $backupDir 'patched'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
foreach ($mod in $pending) {
    $target = Join-Path $ProfilePath $mod.relativePath
    Copy-Item -LiteralPath $target -Destination (Join-Path $backupDir $mod.dll)
    & $Dotnet (Join-Path $PSScriptRoot 'compat-tools\bin\Release\net8.0\CompatTools.dll') $target (Join-Path $outputDir $mod.dll) $GamePath
    if ($LASTEXITCODE -ne 0) { throw "Compatibility validation failed for $($mod.dll); no outputs installed." }
}
foreach ($mod in $pending) {
    Copy-Item -LiteralPath (Join-Path $outputDir $mod.dll) -Destination (Join-Path $ProfilePath $mod.relativePath) -Force
    Write-Output "Patched $($mod.dll). Original saved in $backupDir"
}
