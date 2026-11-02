# ProcWatch dağıtımı: launcher + yanında sandbox'a bağlanacak kendine yeten izleyici.
#
#   dist\ProcWatchLauncher.exe   sağ tık girdisinin hedefi (hostta çalışır)
#   dist\tool\ProcWatch.exe      sandbox içine salt okunur bağlanır, .NET kurulumu gerektirmez

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

if (Test-Path $dist) { [System.IO.Directory]::Delete($dist, $true) }
[System.IO.Directory]::CreateDirectory($dist) | Out-Null

Write-Host "izleyici yayimlaniyor (self-contained)..."
dotnet publish (Join-Path $root "ProcWatch.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o (Join-Path $dist "tool") --nologo -v q

Write-Host "launcher yayimlaniyor..."
dotnet publish (Join-Path $root "Launcher\ProcWatchLauncher.csproj") -c Release -r win-x64 `
    --self-contained false -o $dist --nologo -v q

Write-Host ""
Write-Host "hazir: $dist"
Write-Host "sag tik girdisini eklemek icin: $dist\ProcWatchLauncher.exe (buton) veya --install"
