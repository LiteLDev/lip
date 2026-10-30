<#
.SYNOPSIS
Installs, upgrades and uninstalls the Windows installers of lip on this machine, and checks
the result of each step. Must run elevated, on a machine where lip is not installed.
Compatible with Windows PowerShell 5.1, so that it also runs in Windows Server Core containers.

.PARAMETER ArtifactsDir
Directory containing lip-<version>-win-<Arch>.msi and lip-<version>-win-<Arch>-setup.exe
for the versions 0.0.1 and 0.0.2.

.PARAMETER InnoSetup
A setup.exe of a lip version released with the former Inno Setup installer. Required by the
Inno scenario.
#>
param(
    [Parameter(Mandatory)]
    [string] $ArtifactsDir,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string] $Arch,

    [ValidateSet('Inno', 'Msi', 'Bundle')]
    [string[]] $Scenario = @('Inno', 'Msi', 'Bundle'),

    [string] $InnoSetup
)

$ErrorActionPreference = 'Stop'

$ArtifactsDir = (Resolve-Path $ArtifactsDir).Path
$logDir = New-Item -ItemType Directory -Force (Join-Path $ArtifactsDir 'logs') | Select-Object -ExpandProperty FullName
$installDir = Join-Path $env:ProgramFiles 'lip'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
$innoUninstallKey = "$uninstallKey\{C9BEB1D4-E698-4D84-A644-9E0C4B2E72BD}_is1"

function Assert([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw "FAILED: $Message"
    }
    Write-Host "ok - $Message"
}

function Write-NewLogs([datetime] $Since) {
    Get-ChildItem $logDir -File |
        Where-Object { $_.LastWriteTime -ge $Since } |
        ForEach-Object {
            Write-Host "----- $($_.Name) (last 40 lines)"
            Get-Content $_.FullName -Tail 40 | Write-Host
        }
}

function Invoke-Installer([string] $Name, [string] $File, [string[]] $Arguments, [int] $ExpectedExitCode = 0) {
    Write-Host "> $Name"
    $start = Get-Date
    $process = Start-Process $File -ArgumentList $Arguments -PassThru
    # Caches the process handle, without which ExitCode is lost once the process exits.
    $null = $process.Handle
    if (-not $process.WaitForExit(10 * 60 * 1000)) {
        & taskkill.exe /PID $process.Id /T /F
        Write-NewLogs $start
        throw "FAILED: $Name did not exit within 10 minutes"
    }
    if ($process.ExitCode -ne $ExpectedExitCode) {
        Write-NewLogs $start
    }
    Assert ($process.ExitCode -eq $ExpectedExitCode) "$Name exits with $ExpectedExitCode (got $($process.ExitCode))"
}

function Get-Artifact([string] $Version, [string] $Suffix) {
    Join-Path $ArtifactsDir "lip-$Version-win-$Arch$Suffix"
}

function Invoke-Msi([string] $Action, [string] $Version, [int] $ExpectedExitCode = 0) {
    $msi = Get-Artifact $Version '.msi'
    $log = Join-Path $logDir "msi-$($Action.TrimStart('/'))-$Version-$(Get-Random).log"
    Invoke-Installer "msiexec $Action lip $Version" 'msiexec.exe' @($Action, "`"$msi`"", '/qn', '/norestart', '/l*v', "`"$log`"") $ExpectedExitCode
}

function Test-DotNetRuntime {
    $csproj = Get-Content (Join-Path $PSScriptRoot '../src/Lip.Cli/Lip.Cli.csproj') -Raw
    $major = [regex]::Match($csproj, '<TargetFramework>net(\d+)\.0</TargetFramework>').Groups[1].Value
    Test-Path (Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.NETCore.App\$major.*")
}

function Invoke-Bundle([string] $Action, [string] $Version) {
    $bundle = Get-Artifact $Version '-setup.exe'
    $log = Join-Path $logDir "bundle-$($Action.TrimStart('/'))-$Version-$(Get-Random).log"
    $hadRuntime = Test-DotNetRuntime
    Invoke-Installer "setup.exe $Action lip $Version" $bundle @($Action, '/quiet', '/norestart', '/log', "`"$log`"")
    if ($Action -eq '/install') {
        $downloaded = Select-String -Path $log -Pattern 'Acquiring package: DotNetRuntime' -SimpleMatch -Quiet
        if ($hadRuntime) {
            Assert (-not $downloaded) 'setup.exe does not download the installed .NET runtime'
        } else {
            Assert ($downloaded -and (Test-DotNetRuntime)) 'setup.exe installs the missing .NET runtime'
        }
    }
}

function Get-LipEntries {
    @(Get-ChildItem $uninstallKey |
        Get-ItemProperty |
        Where-Object { $_.DisplayName -like 'lip*' })
}

function Get-PathEntryCount {
    @([Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';' |
        Where-Object { $_.TrimEnd('\') -eq $installDir }).Count
}

function Assert-Installed([string] $Version) {
    Assert (Test-Path (Join-Path $installDir 'lip.exe')) 'lip.exe is installed'
    Assert (Test-Path (Join-Path $installDir 'lipd.exe')) 'lipd.exe is installed'
    Assert ((Get-PathEntryCount) -eq 1) 'the machine PATH contains the install directory once'

    $visible = @(Get-LipEntries | Where-Object { -not $_.SystemComponent })
    Assert ($visible.Count -eq 1) "Installed Apps lists lip once (got $($visible.Count))"
    Assert ($visible[0].DisplayVersion -eq $Version) "Installed Apps lists lip $Version (got $($visible[0].DisplayVersion))"

    # Resolve lip the way a newly opened shell does.
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    foreach ($command in 'lip', 'lipd') {
        $source = (Get-Command $command).Source
        Assert ($source -eq (Join-Path $installDir "$command.exe")) "$command resolves to the install directory (got $source)"
        & $command --version
        Assert ($LASTEXITCODE -eq 0) "$command --version succeeds"
    }
}

function Assert-NotInstalled {
    $leftovers = @(Get-ChildItem $installDir -Recurse -Force -Name -ErrorAction SilentlyContinue)
    Assert (-not (Test-Path $installDir)) "the install directory does not exist (contains: $($leftovers -join ', '))"
    Assert ((Get-PathEntryCount) -eq 0) 'the machine PATH does not contain the install directory'
    $entries = Get-LipEntries
    Assert ($entries.Count -eq 0) "Installed Apps does not list lip (got $($entries.DisplayName -join ', '))"
}

Assert-NotInstalled

if ($Scenario -contains 'Inno') {
    Write-Host '# Upgrade from an Inno Setup installation'
    Assert ([bool] $InnoSetup) 'an Inno Setup installer is given'
    Invoke-Installer 'Inno Setup install' $InnoSetup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$logDir\inno.log`"")
    Assert (Test-Path $innoUninstallKey) 'the Inno Setup installation is registered'
    Assert (Test-Path (Join-Path $installDir 'unins000.exe')) 'the Inno Setup uninstaller exists'

    Invoke-Msi '/i' '0.0.1'
    Assert (-not (Test-Path $innoUninstallKey)) 'the Inno Setup installation is unregistered'
    Assert (-not (Test-Path (Join-Path $installDir 'unins000.exe'))) 'the Inno Setup uninstaller is removed'
    Assert-Installed '0.0.1'

    Invoke-Msi '/x' '0.0.1'
    Assert-NotInstalled
}

if ($Scenario -contains 'Msi') {
    Write-Host '# MSI install, upgrade, downgrade and uninstall'
    Invoke-Msi '/i' '0.0.1'
    Assert-Installed '0.0.1'

    Invoke-Msi '/i' '0.0.2'
    Assert-Installed '0.0.2'

    # ERROR_INSTALL_FAILURE: downgrades are refused.
    Invoke-Msi '/i' '0.0.1' 1603
    Assert-Installed '0.0.2'

    Invoke-Msi '/x' '0.0.2'
    Assert-NotInstalled
}

if ($Scenario -contains 'Bundle') {
    Write-Host '# setup.exe install, upgrade and uninstall'
    Invoke-Bundle '/install' '0.0.1'
    Assert-Installed '0.0.1'

    Invoke-Bundle '/install' '0.0.2'
    Assert-Installed '0.0.2'

    Invoke-Bundle '/uninstall' '0.0.2'
    Assert-NotInstalled
}

Write-Host 'All installer tests passed.'
