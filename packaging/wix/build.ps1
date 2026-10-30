<#
.SYNOPSIS
Builds the Windows installers of lip: lip-<Version>-win-<Arch>.msi, and
lip-<Version>-win-<Arch>-setup.exe, which also installs the .NET runtime when it is missing.

.EXAMPLE
./packaging/wix/build.ps1 -Version 1.2.3 -Arch x64 -BinDir .tmp/artifacts -OutDir .tmp/wix
#>
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string] $Arch,

    # Directory containing the published lip.exe and lipd.exe.
    [Parameter(Mandatory)]
    [string] $BinDir,

    [Parameter(Mandatory)]
    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$BinDir = (Resolve-Path $BinDir).Path
$OutDir = New-Item -ItemType Directory -Force $OutDir | Select-Object -ExpandProperty FullName

# Windows Installer versions are numeric, so drop the SemVer pre-release and build metadata.
$productVersion = $Version -replace '[-+].*$', ''
if ($productVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Cannot derive a Windows Installer version from '$Version'."
}

# The runtime lip targets, e.g. net10.0 -> 10.
$csproj = Get-Content (Join-Path $repoRoot 'src/Lip.Cli/Lip.Cli.csproj') -Raw
if ($csproj -notmatch '<TargetFramework>net(\d+)\.0</TargetFramework>') {
    throw 'Cannot find the target framework of Lip.Cli.'
}
$dotNetMajorVersion = $Matches[1]

# The latest patch of that runtime, verified against Microsoft's release metadata.
$releases = Invoke-RestMethod "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$dotNetMajorVersion.0/releases.json" -MaximumRetryCount 5 -RetryIntervalSec 5
$runtimeVersion = $releases.'latest-runtime'
$runtimeFile = $releases.releases |
    Where-Object { $_.runtime.version -eq $runtimeVersion } |
    Select-Object -First 1 |
    ForEach-Object { $_.runtime.files } |
    Where-Object { $_.name -eq "dotnet-runtime-win-$Arch.exe" }
if (-not $runtimeFile) {
    throw "Cannot find the .NET runtime $runtimeVersion installer for win-$Arch."
}

$downloadDir = New-Item -ItemType Directory -Force (Join-Path $repoRoot ".tmp/wix-download/$runtimeVersion-$Arch")
$runtimeInstaller = Join-Path $downloadDir (Split-Path $runtimeFile.url -Leaf)
if (-not (Test-Path $runtimeInstaller)) {
    Write-Host "Downloading $($runtimeFile.url)"
    Invoke-WebRequest $runtimeFile.url -OutFile "$runtimeInstaller.partial" -MaximumRetryCount 5 -RetryIntervalSec 5
    Move-Item "$runtimeInstaller.partial" $runtimeInstaller
}
if ((Get-FileHash $runtimeInstaller -Algorithm SHA512).Hash -ne $runtimeFile.hash) {
    Remove-Item $runtimeInstaller
    throw "SHA-512 mismatch for $runtimeInstaller."
}

# Incremental builds would not notice that only the properties below changed.
dotnet build (Join-Path $PSScriptRoot 'Bundle/Lip.Bundle.wixproj') `
    --configuration Release `
    --no-incremental `
    "-p:Platform=$Arch" `
    "-p:ProductVersion=$productVersion" `
    "-p:BinDir=$BinDir" `
    "-p:DotNetMajorVersion=$dotNetMajorVersion" `
    "-p:DotNetRuntimeInstaller=$runtimeInstaller" `
    "-p:DotNetRuntimeUrl=$($runtimeFile.url)"

Copy-Item (Join-Path $PSScriptRoot "Package/bin/$Arch/Release/lip.msi") (Join-Path $OutDir "lip-$Version-win-$Arch.msi")
Copy-Item (Join-Path $PSScriptRoot "Bundle/bin/$Arch/Release/lip-setup.exe") (Join-Path $OutDir "lip-$Version-win-$Arch-setup.exe")
