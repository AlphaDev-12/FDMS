#!/usr/bin/env bash
# ==============================================================================
# Linux Provisioning Script for FDMS Bridge Hardware Appliance
# ==============================================================================
# Usage: ./provision-fdms-drive.sh -d /mnt/usb_drive [-t TillStick|BranchBox|Cloud]
# ==============================================================================

set -euo pipefail

TARGET_DIR=""
HARDWARE_TIER="TillStick"

function print_usage() {
    echo "Usage: $0 -d <TargetMountDirectory> [-t <HardwareTier>]"
    echo "  -d Target mount directory (e.g. /mnt/usb_drive or /media/fdms)"
    echo "  -t Hardware Tier: TillStick, BranchBox, or Cloud (Default: TillStick)"
    exit 1
}

while getopts "d:t:h" opt; do
    case "${opt}" in
        d) TARGET_DIR="${OPTARG}" ;;
        t) HARDWARE_TIER="${OPTARG}" ;;
        h) print_usage ;;
        *) print_usage ;;
    esac
done

if [[ -z "${TARGET_DIR}" ]]; then
    echo "Error: Target directory (-d) is required." >&2
    print_usage
fi

if [[ ! -d "${TARGET_DIR}" ]]; then
    echo "Error: Target directory '${TARGET_DIR}' does not exist or is not a directory." >&2
    exit 1
fi

echo -e "\033[36m== Provisioning FDMS Bridge to Linux Target Drive ${TARGET_DIR} ==\033[0m"

# 1. Derive Volume / Device Serial Identifier via lsblk / blkid / udevadm
DEV_PATH=$(df -P "${TARGET_DIR}" | tail -1 | awk '{print $1}')
UUID=$(blkid -s UUID -o value "${DEV_PATH}" 2>/dev/null || echo "DEV-DEFAULT-UUID")

if [[ "${UUID}" == "DEV-DEFAULT-UUID" ]]; then
    # Fallback to hashed machine-id + device path
    RAW_SEED="${DEV_PATH}-$(cat /etc/machine-id 2>/dev/null || echo 'fallback-node')"
    HEX_HASH=$(echo -n "${RAW_SEED}" | sha256sum | awk '{print $1}' | tr '[:lower:]' '[:upper:]')
    DERIVED_DRIVE_ID="DRV-${HEX_HASH:0:8}"
else
    # Clean UUID string to 8 hex chars
    CLEAN_UUID=$(echo "${UUID}" | tr -d '-' | tr '[:lower:]' '[:upper:]')
    DERIVED_DRIVE_ID="DRV-${CLEAN_UUID:0:8}"
fi

echo -e "\033[33mDetected Target Device Path:\033[0m ${DEV_PATH}"
echo -e "\033[32mHardware-Bound Instance ID:\033[0m ${DERIVED_DRIVE_ID}"
echo -e "\033[33mSelected Hardware Tier:\033[0m ${HARDWARE_TIER}"

# 2. Prepare Directory Structure
FDMS_DIR="${TARGET_DIR}/FDMS_Bridge"
mkdir -p "${FDMS_DIR}"

# 3. Write Instance Identity JSON File
PROVISIONED_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
IDENTITY_JSON="${FDMS_DIR}/instance-identity.json"

cat <<EOF > "${IDENTITY_JSON}"
{
  "DriveInstanceId": "${DERIVED_DRIVE_ID}",
  "DevicePath": "${DEV_PATH}",
  "HardwareTier": "${HARDWARE_TIER}",
  "ProvisionedAt": "${PROVISIONED_AT}",
  "Platform": "Linux"
}
EOF

echo -e "\033[32m== Linux Drive Provisioning Complete! ==\033[0m"
echo -e "\033[32mInstance Identity file created at:\033[0m ${IDENTITY_JSON}"
echo -e "\033[36mYou can now copy the compiled FDMS Gateway application files to ${FDMS_DIR}.\033[0m"
