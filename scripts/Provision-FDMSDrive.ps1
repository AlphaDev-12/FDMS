# Provision FDMS Drive Installation Script
Param(
    [Parameter(Mandatory=$true)]
    [string]$TargetDriveLetter,

    [Parameter(Mandatory=$false)]
    [string]$HardwareTier = "TillStick" # TillStick, BranchBox, or Cloud
)

$DriveRoot = "${TargetDriveLetter}:\"
if (-not (Test-Path $DriveRoot)) {
    Write-Error "Target drive letter $DriveRoot does not exist."
    exit 1
}

Write-Host "== Provisioning FDMS Bridge to Physical Drive $DriveRoot ==" -ForegroundColor Cyan

# 1. Obtain Volume Serial Number via Win32_LogicalDisk
$Disk = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DeviceID -eq "${TargetDriveLetter}:" }
if (-not $Disk) {
    Write-Error "Could not query Win32_LogicalDisk for ${TargetDriveLetter}:"
    exit 1
}

$VolumeSerialHex = $Disk.VolumeSerialNumber
$VolumeSerialInt = [Convert]::ToUInt32($VolumeSerialHex, 16)
$DerivedDriveId = "DRV-" + $VolumeSerialInt.ToString("X8")

Write-Host "Detected Volume Serial: $VolumeSerialHex" -ForegroundColor Yellow
Write-Host "Hardware-Bound Instance ID: $DerivedDriveId" -ForegroundColor Green
Write-Host "Selected Hardware Tier: $HardwareTier" -ForegroundColor Yellow

# 2. Prepare Directory Structure on Portable Drive
$FDMSDir = Join-Path $DriveRoot "FDMS_Bridge"
if (-not (Test-Path $FDMSDir)) {
    New-Item -ItemType Directory -Path $FDMSDir | Out-Null
}

# 3. Write Instance Identity & Tier File
$IdentityObj = @{
    DriveInstanceId = $DerivedDriveId
    VolumeSerialHex = $VolumeSerialHex
    HardwareTier    = $HardwareTier
    ProvisionedAt   = (Get-Date).ToString("o")
}

$IdentityJson = $IdentityObj | ConvertTo-Json -Depth 5
$IdentityPath = Join-Path $FDMSDir "instance-identity.json"
Set-Content -Path $IdentityPath -Value $IdentityJson -Encoding UTF8

Write-Host "== Drive Provisioning Complete! ==" -ForegroundColor Green
Write-Host "Instance Identity file created at: $IdentityPath" -ForegroundColor Green
Write-Host "You can now copy the compiled FDMS Gateway application files to $FDMSDir." -ForegroundColor Cyan
