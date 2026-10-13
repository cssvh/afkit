# Installs or updates AFKit: builds it, copies it to %LOCALAPPDATA%\Programs\AFKit and creates the shortcuts.
# Profiles, logins and settings live in %LOCALAPPDATA%\AFKit and are not touched.
# (ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.)

param(
    # No questions: never forces a running AFKit to close, and opens the new version when done.
    [switch]$Auto
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

function Pause-AndExit([int]$code) {
    Write-Host ""
    Read-Host "Press Enter to close"
    exit $code
}

Write-Host "=== AFKit installer ===" -ForegroundColor Cyan

# 1. .NET SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host ".NET 8 SDK was not found." -ForegroundColor Red
    Write-Host "Install it with:  winget install Microsoft.DotNet.SDK.8   and run this installer again."
    Pause-AndExit 1
}

# 2. Is AFKit open? Ask to quit through the tray (it closes saving the tabs) and wait.
$running = Get-Process AFKit -ErrorAction SilentlyContinue
if ($running -and $Auto) {
    # Asks the running AFKit to exit by itself (it saves the sessions first). Versions without this signal
    # ignore it, so after the grace period the process is closed anyway.
    Write-Host "Closing AFKit..." -ForegroundColor Yellow
    try {
        $exit = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::AutoReset, "AFKit.SingleInstance.$env:USERNAME.Exit")
        [void]$exit.Set()
    } catch { }
    $deadline = (Get-Date).AddSeconds(45)
    while ((Get-Process AFKit -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (Get-Process AFKit -ErrorAction SilentlyContinue) {
        Get-Process AFKit -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    $running = $null
}
if ($running) {
    Write-Host ""
    Write-Host "AFKit is open. Close it from the tray icon -> 'Exit AFKit'." -ForegroundColor Yellow
    Write-Host "Waiting up to 2 minutes... (Ctrl+C to cancel)"
    $deadline = (Get-Date).AddMinutes(2)
    while ((Get-Process AFKit -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }
    if (Get-Process AFKit -ErrorAction SilentlyContinue) {
        if ($Auto) {
            Write-Host "AFKit is still open. Nothing was changed; close it and run again." -ForegroundColor Red
            Pause-AndExit 1
        }
        $answer = Read-Host "AFKit is still open. Force it to close? Tabs opened since the last save may be lost (y/N)"
        if ($answer -notmatch '^[yY]') { Pause-AndExit 1 }
        Get-Process AFKit -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

# 2b. Old version (Deck) open? It has to close so the data can migrate to AFKit's folder.
if (Get-Process Deck -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host "Deck (the old name) is open. Close it from the tray icon -> 'Exit Deck'." -ForegroundColor Yellow
    $deadline = (Get-Date).AddMinutes(2)
    while ((Get-Process Deck -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }
    if (Get-Process Deck -ErrorAction SilentlyContinue) {
        if ($Auto) {
            Write-Host "Deck is still open. Nothing was changed; close it and run again." -ForegroundColor Red
            Pause-AndExit 1
        }
        $answer = Read-Host "Deck is still open. Force it to close? (y/N)"
        if ($answer -notmatch '^[yY]') { Pause-AndExit 1 }
        Get-Process Deck -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

# 3. Build and publish to a fixed folder
$dest = Join-Path $env:LOCALAPPDATA 'Programs\AFKit'
Write-Host ""
Write-Host "Building..." -ForegroundColor Cyan
dotnet publish "src\AFKit" -c Release -r win-x64 --self-contained false -o $dest --nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Build failed. Copy the error messages above and report them." -ForegroundColor Red
    Pause-AndExit 1
}

# 4. Shortcuts in the Start Menu and on the desktop
$exe = Join-Path $dest 'AFKit.exe'
$shell = New-Object -ComObject WScript.Shell
$links = @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AFKit.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'AFKit.lnk')
)
foreach ($lnk in $links) {
    $s = $shell.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dest
    $s.IconLocation = "$exe,0"
    $s.Save()
}

# 5. Remove the old name's installation and shortcuts (data in %LOCALAPPDATA%\Deck migrates by itself on the first run)
$oldLinks = @(
    (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Deck.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Deck.lnk')
)
foreach ($old in $oldLinks) { Remove-Item $old -Force -ErrorAction SilentlyContinue }
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\Deck') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done! AFKit installed at $exe" -ForegroundColor Green
if ($Auto) {
    Start-Process $exe
    exit 0
}
$open = Read-Host "Open AFKit now? (Y/n)"
if ($open -notmatch '^[nN]') { Start-Process $exe }
