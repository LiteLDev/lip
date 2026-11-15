<#
.SYNOPSIS
Builds the Windows installers of lip: lip-<Version>-win-<Arch>.msi, and
lip-<Version>-win-<Arch>-setup.exe, which installs that package.

.EXAMPLE
./packaging/wix/build.ps1 -Version 1.2.3 -Arch x64 -BinDir .tmp/artifacts -OutDir .tmp/wix
#>
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string] $Arch,

    # Directory containing the published, self-contained lip.exe.
    [Parameter(Mandatory)]
    [string] $BinDir,

    [Parameter(Mandatory)]
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$BinDir = (Resolve-Path $BinDir).Path
$OutDir = New-Item -ItemType Directory -Force $OutDir | Select-Object -ExpandProperty FullName

# Windows Installer versions are numeric, so drop the SemVer pre-release and build metadata.
$productVersion = $Version -replace '[-+].*$', ''
if ($productVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Cannot derive a Windows Installer version from '$Version'."
}

# Incremental builds would not notice that only the properties below changed.
dotnet build (Join-Path $PSScriptRoot 'Bundle/Lip.Bundle.wixproj') `
    --configuration Release `
    --no-incremental `
    "-p:Platform=$Arch" `
    "-p:ProductVersion=$productVersion" `
    "-p:BinDir=$BinDir"

Copy-Item (Join-Path $PSScriptRoot "Package/bin/$Arch/Release/lip.msi") (Join-Path $OutDir "lip-$Version-win-$Arch.msi")
Copy-Item (Join-Path $PSScriptRoot "Bundle/bin/$Arch/Release/lip-setup.exe") (Join-Path $OutDir "lip-$Version-win-$Arch-setup.exe")
