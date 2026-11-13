[CmdletBinding()]
param(
    [string]$WowPath
)

$ErrorActionPreference = 'Stop'

if (-not $WowPath) {
    foreach ($registryPath in @(
        'HKLM:\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft',
        'HKLM:\SOFTWARE\Blizzard Entertainment\World of Warcraft'
    )) {
        $candidate = (Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue).InstallPath
        if ($candidate -and (Split-Path $candidate.TrimEnd('\', '/') -Leaf) -eq '_forever_') {
            $WowPath = $candidate
            break
        }
    }
}

if (-not $WowPath) {
    throw 'Forever installation not found. Supply -WowPath pointing to the _forever_ client folder.'
}

$clientPath = (Resolve-Path -LiteralPath $WowPath).Path
if ((Split-Path $clientPath.TrimEnd('\', '/') -Leaf) -ne '_forever_' -or
    -not (Test-Path -LiteralPath (Join-Path $clientPath 'Wow.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $clientPath 'Interface') -PathType Container)) {
    throw 'Expected a _forever_ folder containing Wow.exe and Interface.'
}

$sourcePath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\addon\WowForeverC64'))
$destinationPath = Join-Path $clientPath 'Interface\AddOns\WowForeverC64'
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
foreach ($fileName in @('WowForeverC64.toc', 'Api.lua', 'Collector.lua', 'Bridge.lua', 'View.lua', 'Main.lua')) {
    Copy-Item -LiteralPath (Join-Path $sourcePath $fileName) -Destination (Join-Path $destinationPath $fileName) -Force
}

Write-Output "Installed addon in: $destinationPath"
Write-Output 'Source stays in addon/WowForeverC64. Run this script after editing it.'
Write-Output 'Log into Forever and type /c64. If already in-game, use /reload; restart the client if the addon is not discovered.'
