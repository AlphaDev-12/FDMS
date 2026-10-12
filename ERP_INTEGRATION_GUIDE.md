# ZIMRA FDMS Fiscal Gateway API — ERP Integrator Guide

> **Version**: v1.0  
> **Interactive Swagger UI**: [http://localhost:5045/swagger](http://localhost:5045/swagger)  
> **OpenAPI JSON Spec**: [http://localhost:5045/swagger/v1/swagger.json](http://localhost:5045/swagger/v1/swagger.json)  
> **Target ERP Systems**: Sage, SAP, QuickBooks, Odoo, MS Dynamics, and custom ERP/POS applications.

---

## 1. Overview & Architecture

The **ZIMRA FDMS Fiscal Gateway API** acts as an enterprise-grade middleware bridge between host ERP software and the Zimbabwe Revenue Authority (ZIMRA) Fiscal Document Management System (FDMS).

### Core Features for ERP Integrators:
- **Zero-Loss Transactional Durability**: High-throughput invoice fiscalization with integrated offline queue buffering during internet or ZIMRA server outages.
- **Multi-Device & Multi-Currency Support**: Supports simultaneous fiscal device management with dual-currency transaction handling (`USD` and `ZIG` / `ZWG`).
- **Cryptographic Hash Chain Security**: Automatic secp256r1 ECC digital signature calculation and SHA-256 hash chaining across all tax documents.
- **Compliant QR Code Generation**: Instant generation of ZIMRA verification QR codes and receipt links.

---

## 2. Authentication & Authorization

All API requests (except public health probes and Swagger UI) require a valid **API Key**.

### Headers
Pass your assigned API Key in either of the following HTTP headers:

```http
X-API-Key: YOUR_ASSIGNED_API_KEY
```
*or*
```http
Authorization: Bearer YOUR_ASSIGNED_API_KEY
```

### Key Scopes & Permissions
- **Admin Keys**: Full access to all endpoints (ingestion, day open/close, device registration, reports).
- **Diagnostic / Read-Only Keys**: Restricted to `GET` endpoints for status checking and auditing.

---

## 3. Accessible Endpoint Matrix (ERP Integrator Public API)

> [!IMPORTANT]
> External ERP systems only require access to the **ERP Ingestion API (`/api/v1/erp/*`)**. All device administration, cryptographic key generation, fiscal day opening/closing, tax code mapping, and audit logging are managed automatically or via the internal **FDMS Bridge Manager** administrative interface.

| Method | Endpoint | Description | Target Use Case |
| :--- | :--- | :--- | :--- |
| **`POST`** | `/api/v1/erp/invoices` | Fiscalize single ERP sales invoice | Synchronous/Queued Invoice Submission |
| **`POST`** | `/api/v1/erp/invoices/bulk` | Asynchronous Bulk Invoice Ingest Array | High-Throughput Batch Ingestion |
| **`POST`** | `/api/v1/erp/credit-notes` | Fiscalize a credit note | Linked Credit Note Return Submission |
| **`POST`** | `/api/v1/erp/debit-notes` | Fiscalize a debit note | Linked Debit Note Increment Submission |
| **`GET`** | `/api/v1/erp/documents/{invoiceNo}` | Query fiscal status & QR verification URL | Verification & QR Code Retrieval |
| **`POST`** | `/api/v1/erp/webhooks/register` | Register HTTP Webhook endpoint | Asynchronous Queue Callback Updates |


---

## 4. Primary Integration Workflows

### 4.1 Fiscalize Sales Invoice (`POST /api/v1/erp/invoices`)


#### Request Payload Example:
```json
{
  "deviceId": 1,
  "invoiceNo": "INV-2026-00891",
  "invoiceDate": "2026-08-22T12:00:00Z",
  "currency": "USD",
  "receiptType": "FiscalInvoice",
  "buyerName": "Harare Retail Traders Ltd",
  "buyerTIN": "2000123456",
  "buyerAddress": "12 Samora Machel Ave, Harare",
  "lines": [
    {
      "itemCode": "ITEM-101",
      "description": "Industrial Power Generator 50kVA",
      "quantity": 1.0,
      "unitPrice": 4500.00,
      "taxCode": "A",
      "taxPercent": 15.0,
      "totalAmount": 5175.00
    }
  ],
  "totalAmount": 5175.00,
  "taxAmount": 675.00
}
```

#### Synchronous Response (`200 OK` - Direct ZIMRA Submission):
```json
{
  "id": "b3d2f190-8a12-4c2a-9e1f-45a889b71a2e",
  "invoiceNo": "INV-2026-00891",
  "deviceId": 1,
  "receiptType": "FiscalInvoice",
  "currency": "USD",
  "totalAmount": 5175.00,
  "fiscalDayNo": 42,
  "receiptCounter": 15,
  "receiptGlobalNo": 1054,
  "receiptID": "REC-001054",
  "deviceSignature": "MEUCIQD...",
  "fdmsSignature": "ZIMRA_SIG_78901234",
  "qrCodeUrl": "https://fdms.zimra.co.zw/verify?id=REC-001054&sig=MEUCIQD...",
  "fiscalStatus": "Fiscalized",
  "operationID": "OP-99201",
  "serverDate": "2026-08-22T12:00:03Z"
}
```

#### Asynchronous Response (`202 Accepted` - Offline Queue Buffering):
When ZIMRA servers or local internet connections are down, the gateway immediately buffers the payload into the zero-loss queue and returns `202 Accepted`:

```json
{
  "invoiceNo": "INV-2026-00891",
  "fiscalStatus": "Pending",
  "message": "Document queued locally for background transmission to ZIMRA."
}
```

---

### 4.1.1 Asynchronous Bulk Invoice Ingest Array (`POST /api/v1/erp/invoices/bulk`)

Used by enterprise ERP systems (Sage, SAP, Odoo, QuickBooks) for high-throughput batch processing using a standardized request wrapper (`{ "batchId": "...", "invoices": [...] }`).


#### Throughput & Pacing Architecture:
- **Gateway Ingest Rate**: Hundreds of invoices per second (instant local validation, SHA-256 hash chaining, RSA signing, DB store).
- **ZIMRA Submission Pacing**: Governed by ZIMRA rate limit (3 TPS per device thread, 350ms delay between posts). Buffered items are transmitted sequentially in global counter order.

#### Request Payload Example (`POST /api/v1/erp/invoices/bulk`):
```json
{
  "batchId": "BATCH-SAGE-2026-0822",
  "callbackUrl": "https://erp.company.co.zw/api/zimra-webhook",
  "invoices": [
    {
      "deviceId": 1,
      "invoiceNo": "INV-2026-00901",
      "currency": "USD",
      "receiptTotal": 115.00,
      "lines": [{ "receiptLineName": "Consulting Fee", "receiptLineQuantity": 1, "receiptLineTotal": 115.00, "taxCode": "A", "taxPercent": 15.0, "taxID": 1 }],
      "taxes": [{ "taxCode": "A", "taxPercent": 15.0, "taxID": 1, "taxAmount": 15.00, "salesAmountWithTax": 115.00 }],
      "payments": [{ "moneyTypeCode": "Cash", "paymentAmount": 115.00 }]
    },
    {
      "deviceId": 1,
      "invoiceNo": "INV-2026-00902",
      "currency": "USD",
      "receiptTotal": 230.00,
      "lines": [{ "receiptLineName": "Software Maintenance", "receiptLineQuantity": 1, "receiptLineTotal": 230.00, "taxCode": "A", "taxPercent": 15.0, "taxID": 1 }],
      "taxes": [{ "taxCode": "A", "taxPercent": 15.0, "taxID": 1, "taxAmount": 30.00, "salesAmountWithTax": 230.00 }],
      "payments": [{ "moneyTypeCode": "BankTransfer", "paymentAmount": 230.00 }]
    }
  ]
}
```

#### Response Summary Example (`200 OK` or `202 Accepted`):
```json
{
  "batchId": "BATCH-SAGE-2026-0822",
  "totalReceived": 2,
  "processedCount": 2,
  "successCount": 2,
  "queuedCount": 0,
  "failedCount": 0,
  "results": [
    {
      "invoiceNo": "INV-2026-00901",
      "deviceId": 1,
      "fiscalStatus": "Fiscalized",
      "statusCode": 200,
      "document": {
        "invoiceNo": "INV-2026-00901",
        "receiptID": 1055,
        "receiptGlobalNo": 1055,
        "qrCodeUrl": "https://fdmsapitest.zimra.co.zw/verify?..."
      }
    },
    {
      "invoiceNo": "INV-2026-00902",
      "deviceId": 1,
      "fiscalStatus": "Fiscalized",
      "statusCode": 200,
      "document": {
        "invoiceNo": "INV-2026-00902",
        "receiptID": 1056,
        "receiptGlobalNo": 1056,
        "qrCodeUrl": "https://fdmsapitest.zimra.co.zw/verify?..."
      }
    }
  ]
}
```

---

### 4.2 Fiscalize Credit Note (`POST /api/v1/erp/credit-notes`)


When issuing a credit note, ERP systems must supply the original invoice reference details in `creditDebitNoteRef`.

#### Request Payload Example:
```json
{
  "deviceId": 1,
  "invoiceNo": "CN-2026-00104",
  "invoiceDate": "2026-08-22T12:15:00Z",
  "currency": "USD",
  "receiptType": "CreditNote",
  "buyerName": "Harare Retail Traders Ltd",
  "buyerTIN": "2000123456",
  "creditDebitNoteRef": {
    "originalInvoiceNo": "INV-2026-00891",
    "originalReceiptID": "REC-001054",
    "reason": "Return of damaged goods during transit"
  },
  "lines": [
    {
      "itemCode": "ITEM-101",
      "description": "Industrial Power Generator 50kVA - Partial Return",
      "quantity": 1.0,
      "unitPrice": 4500.00,
      "taxCode": "A",
      "taxPercent": 15.0,
      "totalAmount": 5175.00
    }
  ],
  "totalAmount": 5175.00,
  "taxAmount": 675.00
}
```

---

### 4.3 Check Document Verification Status (`GET /api/v1/erp/documents/{invoiceNo}`)

Query fiscalization state and retrieve QR code details at any time:

```bash
curl -X GET "http://localhost:5045/api/v1/erp/documents/INV-2026-00891" \
     -H "X-API-Key: YOUR_ASSIGNED_API_KEY"
```

---

### 4.4 Managing Fiscal Days (`/api/v1/admin/day/*`)

ZIMRA mandates that fiscal devices operate within an active fiscal day (max 24 hours).

1. **Check Fiscal Day Status**:
   `GET /api/v1/admin/day/status/{deviceId}`
2. **Open Fiscal Day**:
   `POST /api/v1/admin/day/open` (opens all registered devices) or `POST /api/v1/admin/day/open?deviceId=1` (opens specific device)
3. **Close Fiscal Day & Generate Z-Report**:
   `POST /api/v1/admin/day/close` (closes all registered devices) or `POST /api/v1/admin/day/close?deviceId=1` (closes specific device)

---

## 5. Sample Integration Code

### C# / .NET Example:
```csharp
using System.Net.Http.Json;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:5045/") };
client.DefaultRequestHeaders.Add("X-API-Key", "YOUR_ASSIGNED_API_KEY");

var invoicePayload = new
{
    deviceId = 1,
    invoiceNo = "INV-2026-9901",
    currency = "USD",
    receiptType = "FiscalInvoice",
    buyerName = "Acme Corp",
    totalAmount = 115.00,
    taxAmount = 15.00,
    lines = new[]
    {
        new { itemCode = "SRV-01", description = "Consulting Service", quantity = 1, unitPrice = 100.00, taxCode = "A", taxPercent = 15.0, totalAmount = 115.00 }
    }
};

var response = await client.PostAsJsonAsync("api/v1/erp/invoices", invoicePayload);
var result = await response.Content.ReadAsStringAsync();
Console.WriteLine($"Status: {response.StatusCode}\nResponse: {result}");
```

### Python Example:
```python
import requests

url = "http://localhost:5045/api/v1/erp/invoices"
headers = {
    "X-API-Key": "YOUR_ASSIGNED_API_KEY",
    "Content-Type": "application/json"
}

payload = {
    "deviceId": 1,
    "invoiceNo": "INV-PY-1002",
    "currency": "USD",
    "receiptType": "FiscalInvoice",
    "buyerName": "Python Integrator Ltd",
    "totalAmount": 230.00,
    "taxAmount": 30.00,
    "lines": [
        {
            "itemCode": "SOFTWARE-01",
            "description": "ERP Software License",
            "quantity": 1,
            "unitPrice": 200.00,
            "taxCode": "A",
            "taxPercent": 15.0,
            "totalAmount": 230.00
        }
    ]
}

response = requests.post(url, json=payload, headers=headers)
print("Status:", response.status_code)
print("Response:", response.json())
```

---

## 6. Error Handling & Standard Error Codes

The API returns standardized JSON error responses in the following format:

```json
{
  "error": "AUTH01",
  "message": "Invalid or missing API key or Bearer token."
}
```

### Error Code Reference:
- `AUTH01`: Missing or invalid API key header.
- `AUTH02`: Forbidden operation (read-only key attempted to execute write operation).
- `ERR_SUBSCRIPTION_EXPIRED`: Active bridge license hard-expired (7-day grace period lapsed).
- `ZREPORT01`: Failed to generate Z-Report.
- `AUDIT01`: Cryptographic hash chain verification failed or detected tamper.
- `ZIMRA_ERR_*`: Direct error response forwarded from ZIMRA FDMS servers.

---

## 7. Developer Support & Interactive Testing

ERP developers can test all endpoints interactively in their browser via Swagger UI:
- **URL**: [http://localhost:5045/swagger](http://localhost:5045/swagger)
- Click **Authorize** at the top right of Swagger UI, enter your `X-API-Key`, and click **Authorize** to enable interactive testing.
