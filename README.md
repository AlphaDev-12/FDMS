# ZIMRA FDMS Fiscal Gateway & Bridge Suite

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker)](https://www.docker.com/)
[![ZIMRA TaRMS FDMS](https://img.shields.io/badge/ZIMRA-FDMS%20v7.2-008751)](https://www.zimra.co.zw/)
[![Database Support](https://img.shields.io/badge/Databases-SQLite%20|%20PostgreSQL%20|%20SQL%20Server-blue)](#database-options)

An enterprise-grade fiscalization middleware and gateway platform engineered to bridge ERP and POS accounting systems with the **Zimbabwe Revenue Authority (ZIMRA) Fiscal Document Management System (FDMS / TaRMS)**.

Supports both **Single-Tenant On-Premises Appliances** (hardware-bound, SQLite WAL, tamper-resistant) and **Multi-Tenant Cloud Native API Hubs** (multi-organization, dynamic mTLS context routing, bulk ingestion).

---

## Table of Contents

- [Key Features](#key-features)
- [Architecture & Solution Layout](#architecture--solution-layout)
- [Deployment Topologies](#deployment-topologies)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
  - [1. Running via Docker Compose (Cloud / Multi-Service Stack)](#1-running-via-docker-compose-cloud--multi-service-stack)
  - [2. Running via Appliance Compose (On-Premises Single-Tenant)](#2-running-via-appliance-compose-on-premises-single-tenant)
  - [3. Running Locally via .NET CLI](#3-running-locally-via-net-cli)
- [Configuration & Environment Variables](#configuration--environment-variables)
- [API Reference & ERP Integration](#api-reference--erp-integration)
- [Testing](#testing)
- [Security & Tamper Resistance](#security--tamper-resistance)
- [Documentation & Guides](#documentation--guides)
- [License](#license)

---

## Key Features

- **ZIMRA TaRMS v7.2 Compliance**: Native implementation of the ZIMRA Fiscal Device Gateway specification, including secp256r1 ECC digital signing and SHA-256 hash chaining.
- **Dual Currency & Multi-Tax Support**: Full transaction support for `USD` and `ZIG` / `ZWG` with automated tax categorization and VAT reporting.
- **Zero-Loss Offline Resilience**: Automatic local queue buffering with Polly-powered exponential backoff and retries during internet disruptions or ZIMRA service outages.
- **Plug-and-Play Database Providers**: Seamless support for **SQLite** (local embedded / WAL mode), **PostgreSQL**, and **Microsoft SQL Server** via Entity Framework Core.
- **QR Code & Verification Engine**: Instant generation of standard compliant ZIMRA verification QR codes and printable thermal receipt payloads (48/80 column).
- **Multi-Tenant Ingress Hub**: Tenant API key isolation, quota/token balance metering, and dynamic mTLS client certificate routing.
- **Observability**: Built-in OpenTelemetry APM instrumentation (HTTP, ASP.NET Core) and Prometheus metrics endpoint.

---

## Architecture & Solution Layout

The solution is divided into focused microservices and web interfaces:

```
FDMS/
├── FdmsGatewayApi/          # Core fiscal gateway API (ZIMRA communication, ERP endpoints, signing, offline queue)
├── FdmsBridgeManager/       # Internal bridge manager service (device administration, state, telemetry)
├── FdmsBridgeAdmin/         # Web administration portal & dashboard
├── FdmsGatewayApi.Tests/    # Unit, integration, and security test suites
├── FdmsTestIntegration/     # End-to-end integration test harness
├── deploy/                  # Production deployment manifests and configurations
├── docs/                    # Architecture documents, partner integrations, and IP pricing strategies
├── docker-compose.yml       # Full stack container configuration (Gateway, Bridge Manager, Admin, DBs)
└── docker-compose.appliance.yml # Lightweight on-prem single-tenant appliance deployment
```

### Component Details

| Component | Default Port | Description |
| :--- | :--- | :--- |
| **FdmsGatewayApi** | `5045` | Main fiscal REST API exposing ERP endpoints (`/api/v1/erp/*`), receipt fiscalization, and ZIMRA device handlers. |
| **FdmsBridgeManager**| `5050` | Background bridge orchestrator connecting ERP queues, certificate lifecycle, and appliance synchronization. |
| **FdmsBridgeAdmin**  | `5070` | Web administration UI for inspecting receipts, managing tenants/devices, and monitoring health. |

---

## Deployment Topologies

The gateway is built to operate under two primary strategies:

1. **Single-Tenant On-Premises Appliance **
   - Deployed on a dedicated local machine, mini-PC, or virtual appliance.
   - Storage backed by local SQLite in WAL mode (`PRAGMA journal_mode = WAL;`).
   - Hardware identity binding (`DriveInstanceId`) and fail-fast tamper detection.
2. **Multi-Tenant Cloud Native API Hub (ERP Partner / SaaS Cluster)**
   - Deployed in cloud environments (AWS, Azure, DigitalOcean) with PostgreSQL or SQL Server.
   - Centralized ingestion point for multi-tenant ERPs (Sage, SAP, Odoo, QuickBooks).
   - Ingress routing using `tenantApiKey` with dynamic mTLS certificate pooling.

For complete architectural details, see [DEPLOYMENT_STRATEGIES.md](file:///c:/Users/makuy/Repos/FDMS/DEPLOYMENT_STRATEGIES.md).

---

## Prerequisites

- **.NET 9.0 SDK** (for local development and building from source)
- **Docker & Docker Compose** (for containerized deployments)
- **Database Engine** (SQLite is built-in; PostgreSQL 16+ or SQL Server 2022 if running in multi-tenant/client-server mode)
- Valid ZIMRA fiscal certificates (`.pfx` / `.pem`) and device credentials (for live fiscalization)

---

## Quick Start

### 1. Running via Docker Compose (Cloud / Multi-Service Stack)

1. Clone the repository and navigate into the root directory:
   ```bash
   git clone https://github.com/AlphaDev-12/FDMS.git
   cd FDMS
   ```

2. Initialize your environment file from the template:
   ```bash
   cp .env.example .env
   ```
   Edit `.env` and set secure passwords for `MSSQL_SA_PASSWORD` and `POSTGRES_PASSWORD`.

3. Launch the complete stack:
   ```bash
   docker-compose up -d --build
   ```

4. Verify services:
   - **Gateway API & Swagger UI**: [http://localhost:5045/swagger](http://localhost:5045/swagger)
   - **Gateway Health Probe**: `http://localhost:5045/healthz`
   - **Bridge Admin Portal**: `http://localhost:5070`
   - **Bridge Manager**: `http://localhost:5050`

---

### 2. Running via Appliance Compose (On-Premises Single-Tenant)

For edge or dedicated on-premises deployment:

```bash
docker-compose -f docker-compose.appliance.yml up -d
```

This mounts `./certs` (read-only) and `./secure_storage` (SQLite database) directly on the host or flash storage media.

---

### 3. Running Locally via .NET CLI

1. Restore dependencies:
   ```bash
   dotnet restore FDMS.sln
   ```

2. Run the Gateway API:
   ```bash
   dotnet run --project FdmsGatewayApi/FdmsGatewayApi.csproj
   ```

3. Access the interactive Swagger interface at [http://localhost:5045/swagger](http://localhost:5045/swagger).

---

## Configuration & Environment Variables

Settings can be specified in `appsettings.json` or overridden via environment variables:

| Variable | Description | Default |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Hosting environment (`Development` or `Production`) | `Production` |
| `DatabaseOptions__Provider` | Active database provider (`Sqlite`, `SqlServer`, `PostgreSql`) | `Sqlite` |
| `ConnectionStrings__FdmsDatabase` | Primary connection string for the database | `Data Source=fdms_gateway.db` |
| `MSSQL_SA_PASSWORD` | System Administrator password for MS SQL Server | Set in `.env` |
| `POSTGRES_PASSWORD` | Superuser password for PostgreSQL | Set in `.env` |
| `ZIMRA_BASE_URL` | ZIMRA TaRMS API endpoint base URL | ZIMRA Sandbox / Production endpoint |
| `PRINTER_DEFAULT_WIDTH_MM` | Thermal printer receipt width (Appliance mode) | `80` |

---

## API Reference & ERP Integration

The Gateway provides an ERP Ingestion API (`/api/v1/erp/*`) designed for seamless integration with accounting software.

### Common Endpoints

| HTTP Method | Path | Description |
| :--- | :--- | :--- |
| `POST` | `/api/v1/erp/invoices` | Fiscalize a standard sales invoice |
| `POST` | `/api/v1/erp/invoices/bulk` | Batch ingest multiple invoices asynchronously |
| `POST` | `/api/v1/erp/credit-notes` | Submit a linked credit note |
| `POST` | `/api/v1/erp/debit-notes` | Submit a linked debit note |
| `GET` | `/api/v1/erp/documents/{invoiceNo}` | Retrieve fiscal status, hash chain, and QR code URL |
| `POST` | `/api/v1/erp/webhooks/register` | Register an HTTP webhook for status callbacks |

### Sample Fiscalization Request (`POST /api/v1/erp/invoices`)

```http
POST /api/v1/erp/invoices HTTP/1.1
Host: localhost:5045
X-API-Key: YOUR_ASSIGNED_API_KEY
Content-Type: application/json

{
  "invoiceNumber": "INV-2026-10492",
  "invoiceDate": "2026-10-13T08:00:00Z",
  "currency": "USD",
  "exchangeRate": 1.0000,
  "customerName": "Acme Enterprises Ltd",
  "customerTin": "2001234567",
  "items": [
    {
      "itemCode": "ITM-001",
      "description": "Standard Consultancy Services",
      "quantity": 1,
      "unitPrice": 200.00,
      "taxCode": "A",
      "taxPercent": 15.00
    }
  ],
  "totalAmount": 230.00,
  "taxAmount": 30.00
}
```

For comprehensive payload schemas, webhook structures, and ERP recipes (Sage, SAP, Odoo, QuickBooks), refer to the [ERP_INTEGRATION_GUIDE.md](file:///c:/Users/makuy/Repos/FDMS/ERP_INTEGRATION_GUIDE.md).

---

## Testing

Run unit and integration tests using the .NET test runner:

```bash
dotnet test FDMS.sln
```

Filter specifically for Gateway unit tests:

```bash
dotnet test FdmsGatewayApi.Tests/FdmsGatewayApi.Tests.csproj
```

---

## Security & Tamper Resistance

- **Cryptographic Hardware Identity**: Supports physical carrier drive binding (`HardwareIdentityFilter`) to prevent unauthorized cloning of appliance images.
- **Fail-Fast Defense**: Immediate process termination upon detecting hardware mismatch or integrity corruption.
- **Code Protection**: Release assemblies are obfuscated using `Obfuscar` (`obfuscar.xml`).
- **Encrypted Keystores**: Sensitive private keys and certificates are safeguarded in memory and read-only mount points.

---

## Documentation & Guides

- [ERP Integration Guide](file:///c:/Users/makuy/Repos/FDMS/ERP_INTEGRATION_GUIDE.md): Step-by-step instructions for ERP vendors and integrators.
- [Deployment Strategies](file:///c:/Users/makuy/Repos/FDMS/DEPLOYMENT_STRATEGIES.md): Deep-dive into single-tenant appliance vs. multi-tenant cloud hub setups.
- [Docs Folder](file:///c:/Users/makuy/Repos/FDMS/docs/): Architecture notes, whitelabel partner guidelines, and risk assessments.

---

## License

Proprietary & Confidential. All rights reserved by Chirandu Software Solutions.
