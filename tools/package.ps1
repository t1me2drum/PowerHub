# Пакування зібраного застосунку: архів PowerHub-<версія>-win-x64.zip і MSI-інсталятор PowerHub-<версія>-win-x64.msi.
# Запускати на Windows після `dotnet build PowerHub.csproj -c Release -p:Platform=x64`, з кореня репозиторію.
# Для MSI потрібен WiX v5: dotnet tool install --global wix --version 5.0.2
# Результат — папка artifacts/ (архів, MSI і SHA256.txt).
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$SkipMsi
)
$ErrorActionPreference = 'Stop'

$out = 'bin/x64/Release/net8.0-windows10.0.22621.0/win-x64'
if (-not (Test-Path "$out/PowerHub.exe")) { throw "Немає $out/PowerHub.exe — спершу зберіть застосунок" }

$artifacts = Join-Path (Get-Location) 'artifacts'
$staging = Join-Path $artifacts 'PowerHub'
Remove-Item $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging | Out-Null

# Усе потрібне для запуску, без налагоджувальних .pdb
Copy-Item -Path "$out/*" -Destination $staging -Recurse
Get-ChildItem $staging -Recurse -Filter *.pdb | Remove-Item

$zip = Join-Path $artifacts "PowerHub-$Version-win-x64.zip"
Compress-Archive -Path $staging -DestinationPath $zip
$files = @($zip)

if (-not $SkipMsi) {
    $msi = Join-Path $artifacts "PowerHub-$Version-win-x64.msi"
    # Вивід wix — лише на екран, щоб у результат скрипта потрапили тільки хеші
    wix build installer/PowerHub.wxs -arch x64 -d "Version=$Version" -d "SourceDir=$staging" -o $msi | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "wix build завершився з кодом $LASTEXITCODE" }
    $files += $msi
}

$lines = foreach ($f in $files) { "$((Get-FileHash $f -Algorithm SHA256).Hash)  $(Split-Path $f -Leaf)" }
Set-Content -Path (Join-Path $artifacts 'SHA256.txt') -Value $lines -Encoding utf8
$lines
