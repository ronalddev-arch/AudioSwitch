# Publishes AudioSwitch as a single exe to %LocalAppData%\Programs\AudioSwitch (stable path for "Start with Windows").
# Stops a running instance first (it locks the exe). Requires the .NET 10 Desktop Runtime on the machine.
#   powershell -File tools/publish.ps1 [-Start]
param([switch]$Start)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\AudioSwitch'

$running = Get-Process AudioSwitch -ErrorAction SilentlyContinue
if ($running) {
    Write-Output "Stopping running AudioSwitch (pid $($running.Id -join ', '))"
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

dotnet publish (Join-Path $root 'src\AudioSwitch.App\AudioSwitch.App.csproj') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=embedded `
    -o $target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $target 'AudioSwitch.exe'
Write-Output "Published $exe ($((Get-Item $exe).VersionInfo.ProductVersion))"
if ($Start) { Start-Process $exe }
