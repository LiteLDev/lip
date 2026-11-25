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
    # user would have these. They come from the profile Windows made for this user, not from
    # GetFolderPath, which expands the USERPROFILE it is passed on into the runner's folders. The
    # folders of a new profile may not exist yet.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $env:USERPROFILE = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid").ProfileImagePath
    $env:HOME = $env:USERPROFILE
    $env:APPDATA = Join-Path $env:USERPROFILE 'AppData\Roaming'
    $env:LOCALAPPDATA = Join-Path $env:USERPROFILE 'AppData\Local'
    $env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
    $env:TMP = $env:TEMP
    foreach ($directory in $env:APPDATA, $env:LOCALAPPDATA, $env:TEMP) {
        "Folder: $directory"
        New-Item -ItemType Directory -Force $directory | Out-Null
    }
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

    # The URL scheme lip of the user, which opens a lip:// link with lip --uri
    # (protocol/lip-uri.md).
    $scheme = Get-ItemProperty 'HKCU:\Software\Classes\lip'
    if ($scheme.'URL Protocol' -ne '') {
        throw 'The package did not register the URL scheme lip for the user.'
    }
    $open = (Get-ItemProperty 'HKCU:\Software\Classes\lip\shell\open\command').'(default)'
    "lip:// opens with: $open"
    if ($open -ine "`"$lip`" --uri `"%1`"") {
        throw "The URL scheme lip opens links with $open, not with $lip --uri."
    }

    $bash = Join-Path $env:ProgramFiles 'Git\bin\bash.exe'
    $lipPath = & $bash -c 'cygpath -u "$1"' _ $lip
    # Windows PowerShell makes each line a native program writes to standard error an error
    # record when 2>&1 takes it, which stops the script under Stop; lip writes its progress there.
    $ErrorActionPreference = 'Continue'
    & $bash (Join-Path $Dir 'e2e-server.sh') $lipPath 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -ne 0) {
        throw "e2e-server.sh exited with $code"
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
