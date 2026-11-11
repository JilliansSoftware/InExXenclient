# Builds installer\output\InExXen-Setup.exe from this repo.
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-UODir "C:\Program Files\Ultima Online 2D"]
# Steps: compile the client (Bootstrap exe + NativeAOT cuo.dll), download Razor CE,
# copy UO data from a reference install (read only, never modified), compile InExXen.iss.
# Needs: .NET 10 SDK, VS C++ build tools (for NativeAOT), Inno Setup 6.
param(
    [string]$UODir = "C:\Program Files\Ultima Online 2D",
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [string]$Version = "1.2.0.0"
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path $here -Parent
$dl = Join-Path $here 'downloads'
$stage = Join-Path $here 'stage\InExXen'

$RazorUrl = 'https://github.com/markdwags/Razor/releases/download/v1.9.77.0/Razor-x64-1.9.77.0.zip'

if (-not (Test-Path (Join-Path $UODir 'tiledata.mul'))) { throw "No UO data in $UODir" }
if (-not (Test-Path $Iscc)) { throw "Inno Setup not found: $Iscc (winget install JRSoftware.InnoSetup)" }

New-Item -ItemType Directory -Force $dl | Out-Null
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\UO\Music\Digital", "$stage\Data\Plugins\Razor" | Out-Null

# NativeAOT runs vcvarsall.bat, which calls vswhere.exe from PATH; without it the
# "not recognized" error text ends up in the linker path and linking fails.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path "$vsInstaller\vswhere.exe") { $env:PATH = "$vsInstaller;$env:PATH" }

# Client: same two publish steps as upstream's deploy workflow.
Push-Location $repo
try {
    git submodule update --init --recursive
    dotnet publish src/ClassicUO.Bootstrap/src/ClassicUO.Bootstrap.csproj -c Release -o $stage `
        -p:OutputType=WinExe -p:AssemblyVersion=$Version -p:FileVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Bootstrap build failed" }
    dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r win-x64 -o $stage `
        -p:IS_DEV_BUILD=true -p:NativeLib=Shared -p:OutputType=Library `
        -p:AssemblyVersion=$Version -p:FileVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw "Client build failed" }
}
finally { Pop-Location }

# Razor CE, loaded as a ClassicUO plugin.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$razorZip = Join-Path $dl ([IO.Path]::GetFileName($RazorUrl))
if (-not (Test-Path $razorZip)) { Write-Host "Downloading $RazorUrl"; Invoke-WebRequest $RazorUrl -OutFile $razorZip -UseBasicParsing }
Expand-Archive $razorZip "$stage\Data\Plugins\Razor" -Force
Get-ChildItem $stage -Recurse -Include *.pdb, *.nsi, *.nsh | Remove-Item -Force

# ClassicUO is BSD-2-Clause: its copyright notice ships with the binaries.
Copy-Item (Join-Path $repo 'LICENSE.md') "$stage\ClassicUO-LICENSE.txt"

# UO data: everything ClassicUO reads; skip EA executables, UO3D files, personal config.
$skip = @('*.exe', '*.dll', '*.zip', '*.bin', 'Blueprints.tbp', 'Login.cfg', 'uo.cfg', 'Uoreg.cfg',
    'Uoupg.cfg', 'Uomax.cfg', 'chardata.mul', 'Ignore.lst', 'macros.txt', 'macros2d.txt', 'Umacros.txt',
    'metrics.txt', 'desktop.nwb', 'Default.mac', 'Vercfg', 'Verinfo', 'Version.dat', 'Version.txt',
    'entitled.def', 'Readme.txt')
Get-ChildItem $UODir -File | Where-Object { $n = $_.Name; -not ($skip | Where-Object { $n -like $_ }) } |
    Copy-Item -Destination "$stage\UO"
Copy-Item "$UODir\music\digital\*" "$stage\UO\Music\Digital"

& $Iscc /Q (Join-Path $here 'InExXen.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-Item (Join-Path $here 'output\InExXen-Setup.exe') | Select-Object Name, Length
