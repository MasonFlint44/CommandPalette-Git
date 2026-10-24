<#
.SYNOPSIS
Builds the extension and installs it for the current user straight from the build output, so
Command Palette loads your local changes.

.DESCRIPTION
Needs Windows Developer Mode (Settings > System > For developers), which allows installing an
unsigned package from a folder. Running it again replaces the installed build.

.PARAMETER Platform
x64 or ARM64. Defaults to this machine's architecture.

.PARAMETER Uninstall
Removes the installed build instead.
#>
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }),
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$packageName = 'MasonFlint44.CommandPaletteGit'

if ($Uninstall) {
    Get-AppxPackage $packageName | Remove-AppxPackage
    Write-Host "Uninstalled $packageName."
    return
}

$devMode = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Developer Mode is off. Turn it on in Settings > System > For developers, then run this again.'
}

# An installed build runs from the build folder, so stop it before building over it. Only this
# package's process is stopped, not other installs such as the Store version of the original.
$installed = Get-AppxPackage $packageName
if ($installed) {
    Get-Process GitExtension -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($installed.InstallLocation, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force
}

$project = Join-Path $PSScriptRoot '..\GitExtension'
$runtime = "win-$($Platform.ToLowerInvariant())"
dotnet build "$project\GitExtension.csproj" -p:Platform=$Platform -r $runtime --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

$manifest = Get-ChildItem "$project\bin\$Platform\Debug\*\$runtime\AppxManifest.xml" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $manifest) {
    throw "No AppxManifest.xml in the build output for $Platform."
}

Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
Write-Host "Installed $packageName from $($manifest.DirectoryName)."
Write-Host "If Command Palette doesn't pick it up, run its 'Reload' command."
