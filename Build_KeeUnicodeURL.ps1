[CmdletBinding()]
param(
    [string]$KeePassDir,

    [ValidateSet('DLL','PLGX','Both')]
    [string]$BuildType
)

$ErrorActionPreference = 'Stop'

# -----------------------------------------------------------------------------
# Self-elevate. Building a plugin under Program Files and invoking KeePass's
# PLGX compiler is much more reliable when the whole script runs elevated.
# Do this before resolving paths or touching build output.
# -----------------------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'Administrator privileges are required.'
    Write-Host 'Restarting Build_KeeUnicodeURL.ps1 with elevation (UAC)...'

    $scriptPath = $MyInvocation.MyCommand.Path
    $argumentList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$scriptPath`"")

    if ($KeePassDir) {
        $argumentList += @('-KeePassDir', "`"$KeePassDir`"")
    }
    if ($BuildType) {
        $argumentList += @('-BuildType', $BuildType)
    }

    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList ($argumentList -join ' ') -Verb RunAs -Wait -PassThru
        exit $p.ExitCode
    }
    catch {
        throw 'Elevation was cancelled or failed. The build was not started.'
    }
}

$scriptRoot = (Get-Item -LiteralPath $PSScriptRoot).FullName
$projectFile = Join-Path $scriptRoot 'KeeUnicodeURL.csproj'
$sourceFiles = @('KeeUnicodeURL.cs', 'IdnUrlUtil.cs', 'Punycode.cs', 'UnicodeUrlColumnProvider.cs')
$assemblyInfo = Join-Path $scriptRoot 'Properties\AssemblyInfo.cs'

if (-not (Test-Path -LiteralPath $projectFile)) { throw "Project file not found: $projectFile" }
foreach ($sf in $sourceFiles) {$p = Join-Path $scriptRoot $sf
if (-not (Test-Path -LiteralPath $p)) { throw "Source file not found: $p" }
}
if (-not (Test-Path -LiteralPath $assemblyInfo)) { throw "AssemblyInfo.cs not found: $assemblyInfo" }

# -----------------------------------------------------------------------------
# Locate KeePass automatically if -KeePassDir was not supplied.
# -----------------------------------------------------------------------------
if (-not $KeePassDir) {
    $possibleDirs = @(
        (Join-Path $env:ProgramFiles 'KeePass Password Safe 2'),
        (Join-Path ${env:ProgramFiles(x86)} 'KeePass Password Safe 2'),         (Join-Path $env:LOCALAPPDATA 'KeePass Password Safe 2')
        (Join-Path $env:LOCALAPPDATA 'KeePass Password Safe 2')
    ) | Where-Object { $_ -and $_ -ne '' } | Select-Object -Unique

    foreach ($d in $possibleDirs) {
        if (Test-Path -LiteralPath (Join-Path $d 'KeePass.exe')) {
            $KeePassDir = (Get-Item -LiteralPath $d).FullName
            break
        }
    }
}

if (-not $KeePassDir) {
    throw 'KeePassDir was not supplied and KeePass.exe could not be found in the standard installation directories.'
}

$KeePassDir = (Get-Item -LiteralPath $KeePassDir).FullName
$KeePassExe = Join-Path $KeePassDir 'KeePass.exe'
if (-not (Test-Path -LiteralPath $KeePassExe)) {
    throw "KeePass.exe not found at: $KeePassExe"
}

# -----------------------------------------------------------------------------
# Interactive menu. When -BuildType is supplied, the script performs that
# build once and exits (useful for automation).
# -----------------------------------------------------------------------------
$interactive = [string]::IsNullOrWhiteSpace($BuildType)

$releaseDir = Join-Path $scriptRoot 'release'
if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

# Clean normal MSBuild output before a DLL build.
foreach ($dir in @('bin','obj')) {
    $p = Join-Path $scriptRoot $dir
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Recurse -Force
    }
}

function Find-MSBuild {
    $candidates = @()

# Visual Studio / Build Tools installations.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        try {
            $installationPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
            if ($installationPath) {
                $candidates += (Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe')
            }
        } catch {}
    }

    $candidates += @(
        (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\MSBuild.exe'),
        (Join-Path ${env:WINDIR} 'Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'),
        (Join-Path ${env:WINDIR} 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe')
    )

    foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    return $null
}
function Build-DLL {
# This is the same MSBuild invocation as the standalone Build-DLL.ps1
# supplied by the user and known to produce the DLL successfully.
    $msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'

    if (-not (Test-Path -LiteralPath $msbuild)) {
        $msbuild = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
    }

    if (-not (Test-Path -LiteralPath $msbuild)) {
        throw 'MSBuild.exe was not found in the .NET Framework v4.0.30319 directory.'
    }

    Write-Host ''
    Write-Host "Building DLL with: $msbuild"
    Write-Host "Command:"
    Write-Host "`"$msbuild`" `"$projectFile`" /p:Configuration=Release /p:KeePassDir=`"$KeePassDir`""
    Write-Host ''

    $logFile = Join-Path $scriptRoot 'DLL-build.log'
    Write-Host "Detailed log: $logFile"
    Write-Host ''

# Use the exact command that is known to work in the supplied
# Build-DLL.ps1. Tee-Object keeps the output visible and also saves it.
    & $msbuild $projectFile /p:Configuration=Release /p:KeePassDir="$KeePassDir" 2>&1 |
    Tee-Object -FilePath $logFile

    $exitCode = $LASTEXITCODE

    Write-Host ''
    Write-Host "MSBuild exit code: $exitCode"

    if ($exitCode -ne 0) {
        throw "DLL build failed with MSBuild exit code $exitCode. See: $logFile"
    }

    $dll = Join-Path $scriptRoot 'bin\Release\KeeUnicodeURL.dll'
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "MSBuild returned exit code 0, but DLL was not found at: $dll. See: $logFile"
    }

    $destination = Join-Path $releaseDir 'KeeUnicodeURL.dll'
    Copy-Item -LiteralPath $dll -Destination $destination -Force
    Write-Host "DLL created: $destination"
}

function Build-PLGX {
# IMPORTANT: stage beside the source folder, not under %TEMP%. KeePass's
# PLGX path calculation can mix long and 8.3 Windows paths and generate
# ../../ traversal entries when source and staging use different path
# representations. Keeping both under the same parent avoids that.
    $sourceParent = Split-Path -Parent $scriptRoot
    $stageRoot = Join-Path $sourceParent ('KeeUnicodeURL-plgx-' + [Guid]::NewGuid().ToString('N'))
    $stage = Join-Path $stageRoot 'KeeUnicodeURL'

    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    try {
        Copy-Item -LiteralPath $projectFile -Destination (Join-Path $stage 'KeeUnicodeURL.csproj') -Force
        foreach ($sf in $sourceFiles) {
            Copy-Item -LiteralPath (Join-Path $scriptRoot $sf) -Destination (Join-Path $stage $sf) -Force
        }
        New-Item -ItemType Directory -Path (Join-Path $stage 'Properties') -Force | Out-Null
        Copy-Item -LiteralPath $assemblyInfo -Destination (Join-Path $stage 'Properties\AssemblyInfo.cs') -Force

        Write-Host ''
        Write-Host "Building PLGX from staging directory: $stage"

        $argList = @(
            '--plgx-create',
            "`"$stage`"",
            '--plgx-prereq-kp:2.61.1'
        )
        $runningKeePass = Get-Process -Name 'KeePass' -ErrorAction SilentlyContinue
        if ($runningKeePass) {
            throw "KeePass is already running (PID $($runningKeePass.Id -join ', ')). Close KeePass completely, including the tray icon, before building PLGX."
        }

        $logFile = Join-Path $scriptRoot 'PLGX-build.log'
        $commandLine = "`"$KeePassExe`" --plgx-create `"$stage`" --plgx-prereq-kp:2.61.1 --debug"

        Add-Content -LiteralPath $logFile -Value @(
            '',
            ('=' * 78),
            ('PLGX build started: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
            ('KeePass: ' + $KeePassExe),
            ('Staging: ' + $stage),
            ('Command: ' + $commandLine),
            ('=' * 78)
        )

        Write-Host ''
        Write-Host "Detailed log: $logFile"
        Write-Host "Command: $commandLine"
        Write-Host ''

        $startTime = Get-Date
        $stdoutFile = Join-Path $stageRoot 'plgx-stdout.log'
        $stderrFile = Join-Path $stageRoot 'plgx-stderr.log'

        $process = Start-Process -FilePath $KeePassExe `
        -ArgumentList @('--plgx-create', "`"$stage`"", '--plgx-prereq-kp:2.61.1', '--debug') `
        -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdoutFile `
        -RedirectStandardError $stderrFile
        $exitCode = $process.ExitCode

        if (Test-Path -LiteralPath $stdoutFile) {
            $stdout = Get-Content -LiteralPath $stdoutFile -Raw -ErrorAction SilentlyContinue
            if ($stdout) { Add-Content -LiteralPath $logFile -Value $stdout }
        }
        if (Test-Path -LiteralPath $stderrFile) {
            $stderr = Get-Content -LiteralPath $stderrFile -Raw -ErrorAction SilentlyContinue
            if ($stderr) { Add-Content -LiteralPath $logFile -Value $stderr }
        }
        Add-Content -LiteralPath $logFile -Value @(
            ('Exit code: ' + $exitCode),
            ('Finished: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
        )

        if ($exitCode -ne 0) {
            throw "KeePass PLGX creation failed with process exit code $exitCode. See: $logFile"
        }

        $stageParent = Split-Path -Parent $stage
        $candidates = Get-ChildItem -LiteralPath $stageParent -Filter '*.plgx' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $startTime } |
        Sort-Object LastWriteTime -Descending

        if (-not $candidates) {
            throw "KeePass reported success but no .plgx file was found next to the staging directory: $stageParent"
        }

        $plgx =$candidates[0].FullName
        $destination = Join-Path $releaseDir 'KeeUnicodeURL.plgx'
        Copy-Item -LiteralPath $plgx -Destination $destination -Force
        Add-Content -LiteralPath $logFile -Value @(
            ('PLGX source: ' + $plgx),
            ('PLGX output: ' + $destination),
            ('PLGX size: ' + (Get-Item -LiteralPath $destination).Length + ' bytes')
        )
        Write-Host "PLGX created: $destination"
    }
    finally {
        if (Test-Path -LiteralPath $stageRoot) {
            Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host ''
Write-Host "KeePass: $KeePassExe"
Write-Host "Output:  $releaseDir"

function Invoke-Build {
    param([ValidateSet('DLL','PLGX','Both')][string]$Mode)

    Write-Host ''
    Write-Host "Mode:    $Mode"

    try {
        switch ($Mode) {
            'DLL'  { Build-DLL }
            'PLGX' { Build-PLGX }
            'Both' { Build-DLL; Build-PLGX }
        }
        Write-Host ''
        Write-Host 'Build completed successfully.' -ForegroundColor Green
        Write-Host 'Files in release:'
        Get-ChildItem -LiteralPath $releaseDir -File | ForEach-Object {
            Write-Host ("  {0}  ({1:N0} bytes)" -f $_.Name, $_.Length)
        }
        return $true
    }
    catch {
        Write-Host ''
        Write-Host '========================================' -ForegroundColor Red
        Write-Host 'BUILD FAILED' -ForegroundColor Red
        Write-Host '========================================' -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red
        Write-Host ''
        Write-Host "Error details: $($_ | Out-String)"
        return $false
    }
}

if (-not $interactive) {
    $ok = Invoke-Build -Mode $BuildType
    if (-not $ok) { exit 1 }
    exit 0
}

while ($true) {
    Write-Host ''
    Write-Host 'KeeUnicodeURL build'
    Write-Host '==================='
    Write-Host '1. DLL'
    Write-Host '2. PLGX'
    Write-Host '3. DLL + PLGX'
    Write-Host '4. Exit'
    Write-Host ''

    do {
        $choice = Read-Host 'Choose [1-4]'
    } while ($choice -notin @('1','2','3','4'))

    if ($choice -eq '4') {
        Write-Host ''
        Write-Host 'Exit.'
        break
    }

    switch ($choice) {
        '1' { [void](Invoke-Build -Mode 'DLL') }
        '2' { [void](Invoke-Build -Mode 'PLGX') }
        '3' { [void](Invoke-Build -Mode 'Both') }
    }

    Write-Host ''
    [void](Read-Host 'Press Enter to return to the menu')
}