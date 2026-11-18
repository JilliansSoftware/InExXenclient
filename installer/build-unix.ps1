# Builds the Linux and macOS packages: installer\output\InExXen-<rid>.tar.gz
#   powershell -ExecutionPolicy Bypass -File installer\build-unix.ps1 [-Version 1.4.0.0]
# Called by build.ps1 after it has staged the UO data; can be run on its own afterwards.
#
# The client is cross-compiled from Windows as a self-contained .NET build (not NativeAOT:
# NativeAOT can only target the OS it runs on). Each package has the client, its native
# libraries, the UO data and a launcher. No Razor (Windows only) and no auto-update: players
# download the new package each release. Needs: .NET 10 SDK, Python 3.
param(
    [string]$Version = "1.4.0.0",
    [string[]]$Targets = @('linux-x64', 'osx-arm64', 'osx-x64'),
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path $here -Parent
$stageUO = Join-Path $here 'stage\InExXen\UO'
$work = Join-Path $here 'stage\unix'
$out = Join-Path $here 'output'
if (-not (Test-Path "$stageUO\tiledata.mul")) { throw "No staged UO data in $stageUO (run build.ps1 first)" }
New-Item -ItemType Directory -Force $out | Out-Null

# Same defaults as the Windows installer writes (InExXen.iss), minus Razor. Shipped as
# settings.default.json; the launcher copies it once, so unpacking a new release over an old
# folder keeps the player's settings.json.
$settings = @'
{
  "username": "",
  "password": "",
  "ip": "play.inexxen.com",
  "port": 2593,
  "ultimaonlinedirectory": "UO",
  "profilespath": "",
  "clientversion": "5.0.6.5",
  "lang": "ENU",
  "lastservernum": 1,
  "fps": 60,
  "is_win_maximized": false,
  "saveaccount": true,
  "autologin": false,
  "reconnect": true,
  "reconnect_time": 3,
  "login_music": true,
  "login_music_volume": 70,
  "fixed_time_step": true,
  "use_verdata": false,
  "encryption": 0,
  "plugins": []
}
'@

# The client finds settings, profiles and UO data from the current folder, so always start it
# from its own folder.
$launcherLinux = @'
#!/bin/sh
cd "$(dirname "$0")" || exit 1
[ -f settings.json ] || cp settings.default.json settings.json
exec ./cuo "$@"
'@
$launcherMac = @'
#!/bin/sh
cd "$(dirname "$0")" || exit 1
# Downloaded files are quarantined; the client is unsigned, so lift it for this folder.
xattr -dr com.apple.quarantine . 2>/dev/null
[ -f settings.json ] || cp settings.default.json settings.json
exec ./cuo "$@"
'@
$readmeLinux = @'
In Ex Xen client for Linux (x64)
================================
Run InExXen.sh (double-click it, or ./InExXen.sh in a terminal).
Server: play.inexxen.com:2593

Updating: download the new InExXen-linux-x64.tar.gz and unpack it over this folder.
Your settings.json and Data/Profiles are kept.

No Razor on Linux (it is a Windows program).
'@
$readmeMac = @'
In Ex Xen client for macOS
==========================
First start: right-click InExXen.command and choose Open, then Open again
(macOS asks once because the client is not signed by Apple). After that, double-click it.
Server: play.inexxen.com:2593

Updating: download the new package and unpack it over this folder.
Your settings.json and Data/Profiles are kept.

No Razor on macOS (it is a Windows program).
'@

function Write-Unix($path, $text) {
    [IO.File]::WriteAllText($path, ($text -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding $false))
}

$winLibs = Get-ChildItem (Join-Path $repo 'external\x64') -File | ForEach-Object Name

foreach ($rid in $Targets) {
    Write-Host "=== $rid"
    $isMac = $rid -like 'osx-*'
    $dir = Join-Path $work $rid
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }

    Push-Location $repo
    try {
        & dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r $rid --self-contained `
            -p:PublishAot=false -p:IS_DEV_BUILD=true -p:AssemblyVersion=$Version -p:FileVersion=$Version `
            -o $dir -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Client build failed for $rid" }
    }
    finally { Pop-Location }

    # The project copies native libraries for the OS doing the build (Windows); swap them.
    foreach ($n in $winLibs) { Remove-Item (Join-Path $dir $n) -Force -ErrorAction SilentlyContinue }
    Get-ChildItem $dir -Recurse -Include *.pdb | Remove-Item -Force
    if ($isMac) {
        Copy-Item (Join-Path $repo 'external\osx\*') $dir
        New-Item -ItemType Directory -Force "$dir\vulkan\icd.d" | Out-Null
        Copy-Item (Join-Path $repo 'external\vulkan\icd.d\*') "$dir\vulkan\icd.d"
    } else {
        Copy-Item (Join-Path $repo 'external\lib64\*') $dir
    }

    Copy-Item (Join-Path $repo 'LICENSE.md') "$dir\ClassicUO-LICENSE.txt"
    Copy-Item $stageUO "$dir\UO" -Recurse
    Write-Unix "$dir\settings.default.json" $settings
    if ($isMac) {
        Write-Unix "$dir\InExXen.command" $launcherMac
        Write-Unix "$dir\README.txt" $readmeMac
        $launcher = 'InExXen.command'
    } else {
        Write-Unix "$dir\InExXen.sh" $launcherLinux
        Write-Unix "$dir\README.txt" $readmeLinux
        $launcher = 'InExXen.sh'
    }

    $tar = Join-Path $out "InExXen-$rid.tar.gz"
    & $Python (Join-Path $here 'maketar.py') $dir $tar 'InExXen' 'cuo' 'createdump' $launcher
    if ($LASTEXITCODE -ne 0) { throw "Packing $rid failed" }
    Get-Item $tar | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB) } }
}
