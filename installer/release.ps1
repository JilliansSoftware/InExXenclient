# Prepares (and optionally publishes) an auto-update release from installer\stage (run build.ps1 first).
#   powershell -ExecutionPolicy Bypass -File installer\release.ps1 -Tag v1.1            # prepare only
#   powershell -ExecutionPolicy Bypass -File installer\release.ps1 -Tag v1.1 -Publish   # create the GitHub release
#
# Writes installer\output\release\: manifest.txt plus a .gz asset for every file that changed since
# the latest published manifest. Unchanged files keep the URL of the release that first shipped them,
# so each release only uploads what changed. The release is created as a draft, filled, then
# published, so clients never see a manifest whose assets are still uploading.
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Repo = 'JilliansSoftware/InExXenclient',
    [string]$PreviousManifest = '',   # URL or path; default = latest published release
    [string]$BaseUrl = '',            # testing: serve assets from e.g. http://127.0.0.1:8000
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$stage = Join-Path $here 'stage\InExXen'
$out = Join-Path $here 'output\release'
$installer = Join-Path $here 'output\InExXen-Setup.exe'
if (-not (Test-Path "$stage\ClassicUO.exe")) { throw "Run build.ps1 first (no $stage)" }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $out | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Previous manifest: path -> (hash, url)
$prev = @{}
if (-not $PreviousManifest) { $PreviousManifest = "https://github.com/$Repo/releases/latest/download/manifest.txt" }
try {
    $text = if (Test-Path $PreviousManifest) { Get-Content $PreviousManifest -Raw } else { (Invoke-WebRequest $PreviousManifest -UseBasicParsing).Content }
    if ($text -is [byte[]]) { $text = [Text.Encoding]::UTF8.GetString($text) }
    foreach ($line in $text -split "`n") {
        $p = $line.TrimEnd("`r") -split "`t"
        if ($p.Length -eq 5 -and $p[0] -eq 'file') { $prev[$p[3]] = @{ Hash = $p[1]; Url = $p[4] } }
    }
    Write-Host "Previous manifest: $($prev.Count) files"
} catch { Write-Host "No previous manifest ($($_.Exception.Message)); every file is new." }

$assetBase = if ($BaseUrl) { $BaseUrl.TrimEnd('/') } else { "https://github.com/$Repo/releases/download/$Tag" }
$version = (Get-Item "$stage\ClassicUO.exe").VersionInfo.FileVersion
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("version`t$version")
$newAssets = 0; $newBytes = 0

Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($prev.ContainsKey($rel) -and $prev[$rel].Hash -eq $hash -and -not $BaseUrl) {
        $url = $prev[$rel].Url
    } else {
        $asset = ($rel -replace '[^A-Za-z0-9._-]', '.') + '.gz'
        $dest = Join-Path $out $asset
        $in = [IO.File]::OpenRead($_.FullName)
        $fs = [IO.File]::Create($dest)
        $gz = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
        $in.CopyTo($gz); $gz.Dispose(); $fs.Dispose(); $in.Dispose()
        $url = "$assetBase/$asset"
        $newAssets++; $newBytes += (Get-Item $dest).Length
    }
    $lines.Add("file`t$hash`t$($_.Length)`t$rel`t$url")
}
[IO.File]::WriteAllLines((Join-Path $out 'manifest.txt'), $lines, (New-Object Text.UTF8Encoding $false))
Write-Host ("Manifest: {0} files, {1} new asset(s), {2:N1} MB to upload" -f ($lines.Count - 1), $newAssets, ($newBytes / 1MB))

if (-not $Publish) { Write-Host "Prepared in $out (not published)."; return }

# Token from Git Credential Manager (same login git push uses).
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw "No GitHub credential available" }
$h = @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' }

$body = @{
    tag_name = $Tag; name = "In Ex Xen Client $($Tag.TrimStart('v'))"; draft = $true
    body = "**New players:** download ``InExXen-Setup.exe`` below and run it.`n**Existing players:** just start the game; it updates itself.`n`nClient version $version."
} | ConvertTo-Json
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" -Headers $h -Body $body
$upload = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name="

$files = @(Get-ChildItem $out -File) + @(Get-Item $installer)
$i = 0
foreach ($f in $files) {
    $i++; Write-Host "[$i/$($files.Count)] $($f.Name)"
    Invoke-RestMethod -Method Post -Uri ($upload + [Uri]::EscapeDataString($f.Name)) -Headers $h `
        -ContentType 'application/octet-stream' -InFile $f.FullName -TimeoutSec 3600 | Out-Null
}
Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/$Repo/releases/$($release.id)" -Headers $h `
    -Body (@{ draft = $false } | ConvertTo-Json) | Out-Null
Write-Host "Published $($release.html_url -replace '/untagged-.*', "/tag/$Tag")"
