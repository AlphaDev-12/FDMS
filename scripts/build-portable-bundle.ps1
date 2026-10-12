# Build Portable FDMS Gateway Bundle
Param(
    [string]$OutDir = "..\FDMS_Portable_Dist"
)

Write-Host "== Building FDMS Gateway Portable Release ==" -ForegroundColor Cyan

$GatewayPath = "..\FdmsGatewayApi\FdmsGatewayApi.csproj"
$ResolvedOutDir = Join-Path -Path $PSScriptRoot -ChildPath $OutDir

if (Test-Path $ResolvedOutDir) {
    Remove-Item -Recurse -Force $ResolvedOutDir
}

dotnet publish $GatewayPath -c Release -r win-x64 --self-contained true -o $ResolvedOutDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "== Build Succeeded! Portable dist created at $ResolvedOutDir ==" -ForegroundColor Green
} else {
    Write-Host "== Build Failed! ==" -ForegroundColor Red
}
