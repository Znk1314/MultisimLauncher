# =============================================================================
#  get-innosetup.ps1 - fetch Inno Setup and build the installer in one go.
#
#  Inno Setup is only needed to PRODUCE the installer. End users never need it:
#  they just run MultisimLauncher-Setup.exe.
#
#  Usage:
#     powershell -ExecutionPolicy Bypass -File installer\get-innosetup.ps1
# =============================================================================
[CmdletBinding()]
param(
    [string]$DownloadUrl = 'https://jrsoftware.org/download.php/is.exe',
    [switch]$KeepInstallerCopy
)

$ErrorActionPreference = 'Stop'

function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok($m)   { Write-Host "    $m" -ForegroundColor Green }

$root   = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$distDir = Join-Path $root 'dist'
$setupExe = Join-Path $distDir 'MultisimLauncher-Setup.exe'

# ---------------------------------------------------------------------------
# already present?
# ---------------------------------------------------------------------------
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 5\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $iscc) {
    Write-Step 'Inno Setup not found - downloading it'
    $tmp = Join-Path $env:TEMP 'innosetup-installer.exe'
    try {
        Write-Host "    from $DownloadUrl"
        $wc = New-Object System.Net.WebClient
        $wc.Headers.Add('User-Agent', 'MultisimLauncher-build')
        $wc.DownloadFile($DownloadUrl, $tmp)
        Write-Ok ("downloaded " + [math]::Round((Get-Item $tmp).Length / 1MB, 1) + ' MB')
    }
    catch {
        Write-Host '    download failed.' -ForegroundColor Red
        Write-Host ''
        Write-Host 'Please install Inno Setup manually, then re-run build.ps1 -MakeInstaller:' -ForegroundColor Yellow
        Write-Host '    https://jrsoftware.org/isdl.php'
        throw
    }

    Write-Step 'installing Inno Setup silently (per-user)'
    $p = Start-Process -FilePath $tmp -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
                       '/CURRENTUSER', '/SP-' -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Inno Setup installer exited with $($p.ExitCode)" }
    Write-Ok 'installed'

    if (-not $KeepInstallerCopy) { try { Remove-Item -LiteralPath $tmp -Force } catch { } }

    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

    if (-not $iscc) { throw 'ISCC.exe still not found after installing Inno Setup.' }
}

Write-Ok "ISCC: $iscc"

# ---------------------------------------------------------------------------
# build the launcher (if needed) and then the installer
# ---------------------------------------------------------------------------
$launcher = Join-Path $distDir 'MultisimLauncher.exe'
if (-not (Test-Path -LiteralPath $launcher)) {
    Write-Step 'launcher not built yet - running build.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
}

Write-Step 'compiling the installer'
$iss = Join-Path $root 'installer\MultisimLauncher.iss'
& $iscc "/DSourceExe=$launcher" "/DOutputDir=$distDir" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

if (Test-Path -LiteralPath $setupExe) {
    Write-Ok ("installer: {0} ({1:N1} MB)" -f $setupExe, ((Get-Item $setupExe).Length / 1MB))
} else {
    throw 'installer was not produced'
}
