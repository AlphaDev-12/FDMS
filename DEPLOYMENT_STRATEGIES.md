# FDMS API Gateway Deployment Strategies: Single-Tenant vs. Multi-Tenant Architecture

This document provides a comprehensive operational and technical reference for deploying the **ZIMRA FDMS API Gateway** under two production strategies: **Single-Tenant On-Premises Appliance Strategy** and **Multi-Tenant Cloud Native Gateway Strategy**. Both strategies are designed to guarantee compliance with ZIMRA TaRMS regulations, protect Chirandu Software Solutions intellectual property (IP), and enforce subscription and device upgrade revenue streams.

---

## 1. Architectural Overview & Deployment Topologies

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                                    FDMS DEPLOYMENT TOPOLOGIES                           │
├──────────────────────────────────────────┬──────────────────────────────────────────────┤
│    STRATEGY A: SINGLE-TENANT APPLIANCE   │     STRATEGY B: MULTI-TENANT CLOUD HUB      │
├──────────────────────────────────────────┼──────────────────────────────────────────────┤
│ - Dedicated Physical Box / Single SME VM │ - Centralized Cloud Cluster (AWS/Azure/DO)   │
│ - Hardware-Bound via DriveInstanceId     │ - Hosted by ERP Consultancy (e.g. TTCS)     │
│ - Local SQLite Database in WAL Mode      │ - Concurrent Multi-SME Ingestion Hub         │
│ - Fail-Fast Tamper Protection (Exit 403) │ - Dynamic mTLS Socket Connection Pool        │
│ - Per-Device Upgrade Keys & Renewals     │ - Wholesale Token Balance Reconciliation     │
└──────────────────────────────────────────┴──────────────────────────────────────────────┘
```

---

## 2. Strategy A: Single-Tenant On-Premises Appliance Topology

The Single-Tenant Appliance model is targeted at independent SME businesses requiring a dedicated fiscalization node on their local network or dedicated virtual private server (VPS).

### 2.1 Hardware Identity & IP Protection
1. **Hardware Carrier Media Binding**:
   The runtime uses `HardwareIdentityFilter` to probe physical storage controller attributes (serial number, `DriveInstanceId`, partition geometry).
2. **Binary Obfuscation**:
   Release builds are compiled with `Obfuscar` (`obfuscar.xml`). Internal cryptographic routines and hardware validation checks are embedded into binary execution paths.
3. **Fail-Fast Tamper Enforcement**:
   If hardware identity signatures mismatch or external carrier files are modified, the engine triggers an instant fail-fast lockdown (`Environment.Exit(403)` within <500ms).

### 2.2 Revenue Stream Protection (Single-Tenant)
- **Subscription Renewals**: Managed by `ISubscriptionService` enforcing expiration timestamps signed against the specific appliance `DriveInstanceId`.
- **Device Upgrade Capacity**: Hardware device capacity expansions require signed Device Upgrade Keys bound to `DriveInstanceId` and verified against `UsedUpgradeNonceEntity` to prevent replay attacks.

### 2.3 Transactional Durability & Offline Resilience
- **Database Engine**: Uses SQLite operating in Write-Ahead Logging mode (`PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;`).
- **Offline Queueing**: If ZIMRA servers are unreachable, `OfflineQueueWorker` buffers signed fiscal receipts locally and auto-flushes them upon reconnect.

---

## 3. Strategy B: Multi-Tenant Cloud Native API Gateway Hub Topology

The Multi-Tenant Cloud Native Architecture allows enterprise ERP consultancies (e.g., TTCS or M&J SERPA) to host a shared, highly scalable cloud cluster (AWS, Azure, DigitalOcean) servicing hundreds of independent SME client businesses streaming receipts from accounting systems (Odoo, Sage, QuickBooks, SAP).

### 3.1 Request Lifecycle & Dynamic Routing Topology

```
 [ Client A Till (Odoo) ] ──(Tenant API Key A)──► ┌────────────────────────────────────────────────────────┐
                                                  │         CENTRALIZED MULTI-TENANT GATEWAY HUB         │
 [ Client B Till (Sage) ] ──(Tenant API Key B)──► │                                                        │
                                                  │  1. Ingress Header Routing Interceptor                 │
                                                  │  2. Tenant Boundary & Token Capacity Validation        │
                                                  │  3. Dynamic mTLS Certificate Routing Context           │
                                                  └───────────────────────────┬────────────────────────────┘
                                                                              │
                                                       [Isolated Memory Sockets Channel Execution]
                                                                              │
                                                ┌─────────────────────────────┼─────────────────────────────┐
                                                ▼                             ▼                             ▼
                                    ┌───────────────────────┐     ┌───────────────────────┐     ┌───────────────────────┐
                                    │    Tenant A Buffer    │     │    Tenant B Buffer    │     │    Tenant C Buffer    │
                                    │ - Secure Memory Queue │     │ - Secure Memory Queue │     │ - Secure Memory Queue │
                                    │ - Cert A (USD Bucket) │     │ - Cert B (ZWG Bucket) │     │ - Cert C (Multi-Curr) │
                                    └───────────┬───────────┘     └───────────┬───────────┘     └───────────┬───────────┘
                                                │                             │                             │
                                                └─────────────────────────────┼─────────────────────────────┘
                                                                              ▼
                                                                  🇿🇼 ZIMRA TaRMS GATEWAY API
```

### 3.2 Ingress Request Schema (`POST /api/v1/receipts`)

Multi-tenant requests are submitted to the ingress hub using standard payload structures:

```json
{
  "tenantApiKey": "mjs_tenant_prod_77a412c01b",
  "zimraDeviceId": "90014285",
  "invoiceMetadata": {
    "invoiceUuid": "a8f3b1c2-9d4e-4f6a-8b2c-3d4e5f6a7b8c",
    "invoiceNumber": "INV-2026-10492",
    "dateTime": "2026-08-24T08:55:00Z"
  },
  "financialSummary": {
    "receiptCurrency": "USD",
    "exchangeRate": 1.0000,
    "exclusiveAmount": 200.00,
    "taxAmount": 30.00,
    "grossInclusiveAmount": 230.00
  },
  "lineItems": [
    {
      "itemCode": "MED-4002",
      "description": "Amoxicillin 500mg Batch B4",
      "quantity": 2,
      "unitPrice": 115.00,
      "taxCategoryCode": "A",
      "taxPercentage": 15.00
    }
  ]
}
```

### 3.3 Dynamic mTLS Socket Routing Engine (`ReceiptsController.cs`)

The core gateway uses `ITenantRegistryService` and `SocketsHttpHandler` dynamic pooling to route transactions using device-specific X.509 PFX certificates:

1. **Tenant Identification**: Validates `tenantApiKey` and target `zimraDeviceId`.
2. **Capacity Check**: Rejects inactive subscriptions (HTTP 403) or unprovisioned devices (HTTP 423).
3. **mTLS Socket Caching**: Caches `HttpClient` instances in `_tenantClientCache` keyed by `${TenantId}_${ZimraDeviceId}`.
4. **Certificate Isolation**: Loads raw PFX certificate BLOBs from `TenantZimraDevices` table per device.

### 3.4 Database Partitioning & Index Optimization (`cloud_cluster.sql`)

The database runs shared tables partitioned logically with tenant discriminator columns and composite indices:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

CREATE TABLE SharedPartnerProfiles (
    PartnerId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartnerKey TEXT UNIQUE NOT NULL,
    WholesaleTokenBalance INTEGER DEFAULT 0,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE TenantBusinessAccounts (
    TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartnerId INTEGER NOT NULL,
    TenantApiKey TEXT UNIQUE NOT NULL,
    BusinessName TEXT NOT NULL,
    IsActive BOOLEAN DEFAULT 1,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(PartnerId) REFERENCES SharedPartnerProfiles(PartnerId)
);

CREATE TABLE TenantZimraDevices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    ZimraDeviceId TEXT NOT NULL,
    EncryptedPfxCertificate BLOB NOT NULL,
    CertificateDecryptionSalt TEXT NOT NULL,
    CertificatePassword TEXT NULL,
    LastSequenceNumber INTEGER DEFAULT 0,
    IsActive BOOLEAN DEFAULT 1,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(TenantId, ZimraDeviceId),
    FOREIGN KEY(TenantId) REFERENCES TenantBusinessAccounts(TenantId)
);

CREATE TABLE MultiTenantFiscalLedger (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    ZimraDeviceId TEXT NOT NULL,
    InvoiceUuid TEXT UNIQUE NOT NULL,
    InvoiceNumber TEXT NOT NULL,
    CurrencyCode TEXT NOT NULL,
    GrossInclusiveAmount REAL NOT NULL,
    CryptographicSignature TEXT NOT NULL,
    ZimraSyncStatus TEXT DEFAULT 'SYNCED',
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(TenantId) REFERENCES TenantBusinessAccounts(TenantId)
);

CREATE INDEX idx_tenant_routing_lookup ON TenantZimraDevices (TenantId, ZimraDeviceId);
CREATE INDEX idx_ledger_tenant_isolation ON MultiTenantFiscalLedger (TenantId, ZimraDeviceId, CreatedAt);
```

### 3.5 Security Isolation & "Data Wall" Policy
Every database query in the microservice layer forces an explicit parameter filter (`WHERE TenantId = :tenantId`). Developers cannot construct queries without passing this discriminator variable, preventing cross-tenant leakage.

### 3.6 Dynamic Wholesale Token Deduction Engine & Revenue Safeguard
- **Wholesale Token Pricing Model**: Enterprise partners pay USD $5.00/month per active device token wallet.
- **Automated Daemon (`WholesaleTokenReconciliationWorker.cs`)**:
  - Periodically counts active provisioned devices per partner (`COUNT(DISTINCT ZimraDeviceId)`).
  - Deducts active device count from `SharedPartnerProfiles.WholesaleTokenBalance`.
  - **Account Suspension Enforcement**: If `WholesaleTokenBalance <= 0`, all downstream `TenantBusinessAccounts` under that partner are toggled to `IsActive = 0`.
  - Ingress requests for suspended tenants instantly return HTTP 403 Forbidden ("Subscription Suspended: Tenant context is inactive or token wallet balance has depleted.").

### 3.7 Cloud KMS Attestation & License Capacity Ceiling
Multi-tenant cloud nodes require KMS attestation via `CloudKmsVerificationEngine`:
- Verifies `CLOUD_ENV_VALIDATION_TOKEN` JWT against hardcoded embedded master RSA public keys.
- Enforces `MaxLicensedLanes` across cluster worker nodes to safeguard wholesale licensing limits.

---

## 4. Partner Self-Hosted Cloud Deployment & SME Onboarding Engine

The FDMS API Gateway enables enterprise ERP partners (e.g. TTCS or M&J SERPA) to host their own independent instance of the FDMS Bridge in any cloud environment (AWS ECS/EKS, Azure Container Instances, DigitalOcean App Platform, or Docker Compose).

### 4.1 Partner Cloud Deployment Modes (`MultiTenancyOptions`)

Partners can configure their cloud deployment in `appsettings.json` or via environment variables:

```json
{
  "MultiTenancy": {
    "Mode": "MultiTenant",
    "PartnerKey": "mjs_serpa_master_key",
    "PartnerName": "M&J SERPA ERP Consultancy",
    "DefaultTenantApiKey": "mjs_tenant_prod_77a412c01b",
    "InitialWholesaleTokens": 500
  }
}
```

| Deployment Mode | Scenario & Operational Behavior |
| :--- | :--- |
| **`MultiTenant`** | Partner hosts a shared cloud cluster serving multiple SME clients concurrently. Incoming payloads specify `tenantApiKey` and `zimraDeviceId`. |
| **`SingleTenantCloud`** | Partner deploys a cloud instance dedicated to a single SME merchant. Omitting `tenantApiKey` automatically resolves to `DefaultTenantApiKey`. |
| **`SingleTenantAppliance`** | Dedicated on-premises hardware appliance deployment bound to physical `DriveInstanceId`. |

---

### 4.2 Partner Onboarding API Reference (`/api/v1/partner/*`)

Partner administrators can onboard SME customers and provision ZIMRA fiscal devices programmatically using the `PartnerAdminController`:

#### 1. Onboard a New SME Customer (`POST /api/v1/partner/tenants`)

```http
POST /api/v1/partner/tenants HTTP/1.1
Content-Type: application/json

{
  "partnerKey": "mjs_serpa_master_key",
  "businessName": "Harare Medical Supplies Ltd",
  "desiredTenantApiKey": "mjs_tenant_med_supplies_01"
}
```

**Response (200 OK)**:
```json
{
  "tenantId": 12,
  "partnerId": 1,
  "tenantApiKey": "mjs_tenant_med_supplies_01",
  "businessName": "Harare Medical Supplies Ltd",
  "isActive": true,
  "createdAt": "2026-08-24T09:25:00Z"
}
```

#### 2. Register ZIMRA Device & Upload PFX Certificate (`POST /api/v1/partner/devices`)

```http
POST /api/v1/partner/devices HTTP/1.1
Content-Type: application/json

{
  "tenantApiKey": "mjs_tenant_med_supplies_01",
  "zimraDeviceId": "90014285",
  "pfxBase64Certificate": "MIIK9gIBAzCCCt4GCSqGSIb3DQEHAaCCC...",
  "certificateDecryptionSalt": "salt_key_88a",
  "certificatePassword": "pfx_password_secure"
}
```

**Response (200 OK)**:
```json
{
  "message": "Device 90014285 successfully provisioned under TenantApiKey 'mjs_tenant_med_supplies_01'."
}
```

#### 3. View Partner Fleet Telemetry & Token Balance (`GET /api/v1/partner/dashboard`)

```http
GET /api/v1/partner/dashboard?partnerKey=mjs_serpa_master_key HTTP/1.1
```

**Response (200 OK)**:
```json
{
  "partnerId": 1,
  "partnerKey": "mjs_serpa_master_key",
  "wholesaleTokenBalance": 498,
  "totalTenantAccounts": 15,
  "activeTenantAccounts": 15,
  "totalRegisteredDevices": 28,
  "activeRegisteredDevices": 28,
  "tenants": [
    {
      "tenantId": 12,
      "tenantApiKey": "mjs_tenant_med_supplies_01",
      "businessName": "Harare Medical Supplies Ltd",
      "isActive": true,
      "deviceCount": 2,
      "createdAt": "2026-08-24T09:25:00Z"
    }
  ]
}
```

---

## 5. Deployment Strategy Comparison Matrix

| Feature | Single-Tenant Appliance Strategy | Single-Tenant Cloud Strategy | Multi-Tenant Cloud Hub Strategy |
| :--- | :--- | :--- | :--- |
| **Primary Operator** | Merchant On-Premises | Single SME Dedicated Cloud | Enterprise Partner (TTCS / M&J SERPA) |
| **Hosting Environment** | Physical Appliance / VM | Partner Managed VPS / Container | Shared Cloud Cluster (AWS / Azure / DO) |
| **Database Model** | Local SQLite (WAL Mode) | Dedicated SQLite / SQL Server | Shared Schema with Tenant Discriminator |
| **mTLS Certificates** | Local Drive Certificate File | Single Device Certificate | BLOB-Stored PFX in `TenantZimraDevices` |
| **IP Protection** | Hardware (`DriveInstanceId`) | Cloud KMS (`CLOUD_ENV_TOKEN`) | Cloud KMS (`CLOUD_ENV_TOKEN`) |
| **Revenue Model** | Annual Key Renewal | Monthly Token Subscription | USD $5.00/mo Wholesale Token Balance |
| **Billing Enforcement** | License Expiry Lockout | Token Balance Check | Dynamic Token Reconciliation Daemon |
| **Failure Response** | Exit 403 Tamper Lockdown | HTTP 403 Account Suspension | HTTP 403 Tenant Account Suspension |

---

## 6. Summary & Verification

Both Single-Tenant (Appliance & Dedicated Cloud) and Multi-Tenant Cloud deployment strategies are fully operational in the `FdmsGatewayApi` codebase:
- Single-tenant on-premises appliances use hardware identity filters and offline queue workers.
- Single-tenant cloud deployments auto-resolve default tenant contexts.
- Multi-tenant cloud hubs use `ReceiptsController`, `TenantRegistryService`, `PartnerAdminController`, `WholesaleTokenReconciliationWorker`, and `cloud_cluster.sql` schema.
- All deployment modes protect Chirandu Software Solutions IP and enforce subscription/device upgrade revenue streams.

