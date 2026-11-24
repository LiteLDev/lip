# Run by the job e2e of ci.yml on Windows, as a new standard user, not an administrator: installs
# lip with its Windows Installer package for that user, as a double click of it does but without
# its dialogs, finds lip on the PATH of the user, as a new terminal would, and runs
# e2e-server.sh with it.
#
# Usage: e2e-windows-user.ps1 MSI DIR, where MSI is the package and DIR holds e2e-server.sh;
# writes what it does to DIR\out.txt, as Start-Process -Credential shows no output.
param(
    [Parameter(Mandatory)]
    [string] $Msi,

    [Parameter(Mandatory)]
    [string] $Dir
)

$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $Dir 'out.txt') | Out-Null
try {
    whoami
    # Start-Process -Credential passes on the environment of the runner's user; a terminal of this
    # user would have these.
    $env:USERPROFILE = [Environment]::GetFolderPath('UserProfile')
    $env:HOME = $env:USERPROFILE
    $env:APPDATA = [Environment]::GetFolderPath('ApplicationData')
    $env:LOCALAPPDATA = [Environment]::GetFolderPath('LocalApplicationData')
    $env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
    $env:TMP = $env:TEMP
    "LOCALAPPDATA: $env:LOCALAPPDATA"
    $log = Join-Path $Dir 'msi.log'
    $p = Start-Process msiexec -Wait -PassThru -ArgumentList '/i', "`"$Msi`"", '/qn', '/l*v', "`"$log`""
    if ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 50
        throw "msiexec exited with $($p.ExitCode)"
    }

    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
        [Environment]::GetEnvironmentVariable('Path', 'User')
    $lip = (Get-Command lip -CommandType Application).Source
    "lip: $lip"
    if (-not $lip.StartsWith((Join-Path $env:LOCALAPPDATA 'Programs'), 'OrdinalIgnoreCase')) {
        throw "The package installed lip to $lip, not for the user."
    }

    $bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
    $lipPath = & $bash -c 'cygpath -u "$1"' _ $lip
    & $bash (Join-Path $Dir 'e2e-server.sh') $lipPath 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "e2e-server.sh exited with $LASTEXITCODE"
    }

    $runtime = Join-Path $env:LOCALAPPDATA 'lip\Data\vc-runtime'
    if (-not (Get-ChildItem $runtime -Filter msvcp140.dll -Recurse)) {
        throw "lip did not install the Visual C++ runtime for the user in $runtime."
    }
    "lip installed the Visual C++ runtime for the user in $runtime."
    exit 0
}
catch {
    "::error::$_"
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
