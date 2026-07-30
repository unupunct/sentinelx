#Requires -Version 5.1
<#
    Builds a single-file, self-contained, portable Sentinel X.exe.
    No installer, no external runtime dependency - copy the output folder anywhere and run.
#>

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "src\SentinelX.App\SentinelX.App.csproj"
$output = Join-Path $root "publish\win-x64"

Write-Host "Publishing Sentinel X (win-x64, self-contained, single-file)..." -ForegroundColor Cyan

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=embedded `
    -o $output

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$exePath = Join-Path $output "Sentinel X.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Expected output not found: $exePath"
    exit 1
}

$sizeMb = [Math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "Published: $exePath ($sizeMb MB)" -ForegroundColor Green
