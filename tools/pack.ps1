# Builds a release for end users with Velopack: a self-contained win-x64 build (no .NET needed on the target machine),
# packed into Setup.exe (per-user install to %LocalAppData%\AudioSwitch, no admin, Start-menu shortcut, uninstall entry,
# starts the app when done) plus the update packages (*-full.nupkg, *-delta.nupkg, releases.win.json) that the
# installed app downloads from GitHub Releases.
#   powershell -File tools/pack.ps1 [-NoDeltas]
# Output: artifacts\releases (git-ignored). Version, authors and repository come from Directory.Build.props.
# With RepositoryUrl set, the latest published release is downloaded first so that a delta package can be made;
# -NoDeltas skips that. Upload with:  dotnet vpk upload github -o artifacts\releases --repoUrl <url> --token <token> --publish --tag v<version>
# Does not touch the installed or running app (unlike publish.ps1). Don't run the produced Setup.exe on a machine whose
# audio you care about: it installs and starts AudioSwitch.
param([switch]$NoDeltas)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src\AudioSwitch.App\AudioSwitch.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$releasesDir = Join-Path $root 'artifacts\releases'
$packId = 'AudioSwitch' # also the install folder name; changing it later orphans existing installs

$props = dotnet msbuild $project -getProperty:Version -getProperty:Authors -getProperty:Product -getProperty:RepositoryUrl | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "Reading the project properties failed ($LASTEXITCODE)" }
$version = $props.Properties.Version
$repoUrl = $props.Properties.RepositoryUrl
Write-Output "Packing $($props.Properties.Product) $version"

dotnet tool restore --tool-manifest (Join-Path $root '.config\dotnet-tools.json') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# English UI, so leave out the framework's translated resource assemblies (saves several MB).
dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=embedded -p:SatelliteResourceLanguages=en `
    -o $publishDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

New-Item -ItemType Directory -Force $releasesDir | Out-Null
if ($repoUrl -and -not $NoDeltas) {
    Write-Output "Fetching the latest release from $repoUrl for a delta package"
    dotnet vpk download github --repoUrl $repoUrl -o $releasesDir
    if ($LASTEXITCODE -ne 0) { Write-Warning "No previous release downloaded; this release gets no delta package" }
}

dotnet vpk pack `
    --packId $packId `
    --packVersion $version `
    --runtime win-x64 `
    --packDir $publishDir `
    --mainExe 'AudioSwitch.exe' `
    --packTitle $props.Properties.Product `
    --packAuthors ($props.Properties.Authors -replace ';', ', ') `
    --icon (Join-Path $root 'src\AudioSwitch.App\app.ico') `
    --shortcuts StartMenuRoot `
    --noPortable `
    --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)" }

Get-ChildItem $releasesDir -File | ForEach-Object { Write-Output ("{0,-45} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
