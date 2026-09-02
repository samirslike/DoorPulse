$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "DoorPulse Build"
Write-Host "==============="
Write-Host ""

$version = & dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw ".NET SDK was not found."
}

Write-Host "Using .NET SDK $version"

& dotnet restore .\DoorPulse.csproj
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& dotnet publish .\DoorPulse.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o .\publish

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path (Resolve-Path .\publish).Path "DoorPulse.exe"

if (-not (Test-Path $exe)) {
    throw "Build reported success but DoorPulse.exe was not found at: $exe"
}

Write-Host ""
Write-Host "BUILD SUCCESS"
Write-Host "DoorPulse.exe:"
Write-Host $exe
Write-Host ""
