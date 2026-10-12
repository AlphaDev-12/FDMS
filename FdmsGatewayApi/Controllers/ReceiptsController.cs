using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using FdmsGatewayApi.Config;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FdmsGatewayApi.Controllers;


#region Payload DTO Definitions

public class FiscalReceiptPayload
{
    public string TenantApiKey { get; set; } = string.Empty;
    public string ZimraDeviceId { get; set; } = string.Empty;
    public InvoiceMetadataDto InvoiceMetadata { get; set; } = new();
    public FinancialSummaryDto FinancialSummary { get; set; } = new();
    public List<FiscalLineItemDto> LineItems { get; set; } = new();
}

public class InvoiceMetadataDto
{
    public string InvoiceUuid { get; set; } = Guid.NewGuid().ToString();
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime DateTime { get; set; } = DateTime.UtcNow;
}

public class FinancialSummaryDto
{
    public string ReceiptCurrency { get; set; } = "USD";
    public decimal ExchangeRate { get; set; } = 1.0000m;
    public decimal ExclusiveAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrossInclusiveAmount { get; set; }
}

public class FiscalLineItemDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string TaxCategoryCode { get; set; } = "A";
    public decimal TaxPercentage { get; set; } = 15.00m;
}

public class ZimraEgressResponse
{
    public string Status { get; set; } = "SUCCESS";
    public long ReceiptGlobalNo { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string FiscalSignature { get; set; } = string.Empty;
    public string QrCodeUrl { get; set; } = string.Empty;
    public string Message { get; set; } = "Fiscal document successfully processed through dynamic multi-tenant mTLS pipeline.";
}

#endregion

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Multi-Tenant Dynamic Gateway")]
public class ReceiptsController : ControllerBase
{
    private readonly ITenantRegistryService _tenantRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IFiscalDocumentOrchestrator _orchestrator;
    private readonly MultiTenancyOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<ReceiptsController> _logger;

    private static readonly ConcurrentDictionary<string, HttpClient> _tenantClientCache = new();

    public static bool InvalidateCache(int tenantId, string deviceId)
    {
        string cacheKey = $"{tenantId}_{deviceId}";
        if (_tenantClientCache.TryRemove(cacheKey, out var client))
        {
            client.Dispose();
            return true;
        }
        return false;
    }

    public ReceiptsController(
        ITenantRegistryService tenantRegistry,
        IHttpClientFactory httpClientFactory,
        IFdmsCryptoService cryptoService,
        IFiscalDocumentOrchestrator orchestrator,
        IOptions<MultiTenancyOptions> options,
        IConfiguration config,
        ILogger<ReceiptsController> logger)
    {
        _tenantRegistry = tenantRegistry;
        _httpClientFactory = httpClientFactory;
        _cryptoService = cryptoService;
        _orchestrator = orchestrator;
        _options = options.Value;
        _config = config;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(ZimraEgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProcessFiscalReceipt([FromBody] FiscalReceiptPayload payload, CancellationToken cancellationToken = default)
    {
        // 1. EXTRACT TENANT IDENTIFIERS: Enforce strict boundary identification
        string tenantKey = payload.TenantApiKey;
        string targetDeviceId = payload.ZimraDeviceId;

        // In SingleTenantCloud mode, automatically resolve missing tenantApiKey to configured default
        if (string.IsNullOrWhiteSpace(tenantKey) && string.Equals(_options.Mode, "SingleTenantCloud", StringComparison.OrdinalIgnoreCase))
        {
            tenantKey = _options.DefaultTenantApiKey;
        }

        if (string.IsNullOrWhiteSpace(tenantKey) || string.IsNullOrWhiteSpace(targetDeviceId))
        {
            return BadRequest(new { error = "Incomplete Ingress Headers: tenantApiKey and zimraDeviceId are mandated parameters." });
        }

        // 2. CAPACITY VALIDATION: Verify token capacity and active billing states
        var tenantContext = await _tenantRegistry.GetTenantContextAsync(tenantKey, cancellationToken);
        if (tenantContext == null || !tenantContext.IsActive)
        {
            return StatusCode(403, new { error = "Subscription Suspended: Tenant context is inactive or token wallet balance has depleted." });
        }

        if (!tenantContext.IsAuthorizedForDevice(targetDeviceId))
        {
            return StatusCode(423, new { error = $"Capacity Limit Violation: DeviceID {targetDeviceId} is not provisioned under this subscription plan." });
        }

        try
        {
            // 3. DYNAMIC mTLS ROUTING: Fetch or construct the isolated tenant connection client
            HttpClient client = await GetOrCreateHttpClientForTenantAsync(tenantContext, targetDeviceId, cancellationToken);

            int deviceIdInt = int.TryParse(targetDeviceId, out var parsedDevId) ? parsedDevId : 1;

            // Map FiscalReceiptPayload -> ErpInvoiceRequestDto for Canonical Fiscal Processing
            var erpRequest = new ErpInvoiceRequestDto
            {
                DeviceId = deviceIdInt,
                InvoiceNo = payload.InvoiceMetadata.InvoiceNumber,
                Currency = string.IsNullOrWhiteSpace(payload.FinancialSummary.ReceiptCurrency) ? "USD" : payload.FinancialSummary.ReceiptCurrency,
                ReceiptDate = payload.InvoiceMetadata.DateTime == default ? DateTime.Now : payload.InvoiceMetadata.DateTime,
                ReceiptTotal = payload.FinancialSummary.GrossInclusiveAmount,
                ReceiptLinesTaxInclusive = true,
                Lines = (payload.LineItems != null && payload.LineItems.Count > 0)
                    ? payload.LineItems.Select((item, idx) => new ErpReceiptLineDto
                    {
                        ReceiptLineNo = idx + 1,
                        ReceiptLineName = string.IsNullOrWhiteSpace(item.Description) ? (item.ItemCode ?? "Item") : item.Description,
                        ReceiptLineQuantity = item.Quantity <= 0 ? 1 : item.Quantity,
                        ReceiptLinePrice = item.UnitPrice,
                        ReceiptLineTotal = item.Quantity * item.UnitPrice,
                        TaxCode = string.IsNullOrWhiteSpace(item.TaxCategoryCode) ? "A" : item.TaxCategoryCode,
                        TaxPercent = item.TaxPercentage,
                        TaxID = item.TaxCategoryCode == "C" ? 3 : 1
                    }).ToList()
                    : new List<ErpReceiptLineDto>
                    {
                        new ErpReceiptLineDto
                        {
                            ReceiptLineNo = 1,
                            ReceiptLineName = "Sales Item",
                            ReceiptLineQuantity = 1,
                            ReceiptLinePrice = payload.FinancialSummary.GrossInclusiveAmount,
                            ReceiptLineTotal = payload.FinancialSummary.GrossInclusiveAmount,
                            TaxCode = "A",
                            TaxPercent = 15.00m,
                            TaxID = 1
                        }
                    },
                Taxes = new List<ErpReceiptTaxDto>
                {
                    new ErpReceiptTaxDto
                    {
                        TaxID = 1,
                        TaxCode = "A",
                        TaxPercent = 15.00m,
                        TaxAmount = payload.FinancialSummary.TaxAmount,
                        SalesAmountWithTax = payload.FinancialSummary.GrossInclusiveAmount
                    }
                },
                Payments = new List<ErpPaymentDto>
                {
                    new ErpPaymentDto
                    {
                        MoneyTypeCode = "Cash",
                        PaymentAmount = payload.FinancialSummary.GrossInclusiveAmount
                    }
                }
            };

            // Process invoice through FiscalDocumentOrchestrator for ZIMRA Section 13 ECDSA signing and Section 11 QR URL generation
            var processResult = await _orchestrator.ProcessDocumentAsync(erpRequest, "FiscalInvoice", null, cancellationToken);
            if (!processResult.IsSuccess || processResult.Response == null)
            {
                _logger.LogError("[MULTI-TENANT ORCHESTRATION ERROR] Invoice processing failed for Tenant {TenantId}, Device {DeviceId}.", tenantContext.TenantId, targetDeviceId);
                return BadRequest(new { error = "Fiscal Processing Failed", detail = processResult.ErrorObject });
            }

            var docResponse = processResult.Response;
            string signature = docResponse.DeviceSignature.Signature ?? docResponse.DeviceSignature.Hash;
            string qrUrl = docResponse.QrCodeUrl;

            // Record multi-tenant transaction in partitioned data ledger
            await _tenantRegistry.RecordLedgerEntryAsync(
                tenantContext.TenantId,
                targetDeviceId,
                payload.InvoiceMetadata.InvoiceUuid,
                payload.InvoiceMetadata.InvoiceNumber,
                payload.FinancialSummary.ReceiptCurrency,
                payload.FinancialSummary.GrossInclusiveAmount,
                signature,
                docResponse.FiscalStatus,
                cancellationToken);

            var egressResult = new ZimraEgressResponse
            {
                Status = "SUCCESS",
                ReceiptGlobalNo = docResponse.ReceiptGlobalNo,
                InvoiceNumber = docResponse.InvoiceNo,
                FiscalSignature = signature,
                QrCodeUrl = qrUrl,
                Message = $"Fiscal document '{docResponse.InvoiceNo}' successfully processed for tenant '{tenantContext.BusinessName}' on device {targetDeviceId}."
            };

            return Ok(egressResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MULTI-TENANT ROUTING ERROR] Handshake communication failed for Tenant {TenantId}, Device {DeviceId}.", tenantContext.TenantId, targetDeviceId);
            return StatusCode(500, new { error = $"Internal Ingress Error: Tenant pipeline execution failed. {ex.Message}" });
        }
    }


    private async Task<HttpClient> GetOrCreateHttpClientForTenantAsync(TenantContext context, string deviceId, CancellationToken cancellationToken)
    {
        string cacheKey = $"{context.TenantId}_{deviceId}";

        if (_tenantClientCache.TryGetValue(cacheKey, out var cachedClient))
        {
            return cachedClient;
        }

        byte[] rawCertBytes;
        string certPassword;

        try
        {
            rawCertBytes = await _tenantRegistry.GetEncryptedDeviceCertificateAsync(context.TenantId, deviceId, cancellationToken);
            certPassword = await _tenantRegistry.GetCertificatePasswordAsync(context.TenantId, deviceId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            // Development fallback certificate handling if cert BLOB not present in database
            _logger.LogInformation("[mTLS CACHE] Certificate BLOB not present for Tenant {TenantId}, Device {DeviceId}. Creating standard socket client for sandbox.", context.TenantId, deviceId);
            var defaultClient = _httpClientFactory.CreateClient("ZimraFdmsClient");
            _tenantClientCache[cacheKey] = defaultClient;
            return defaultClient;
        }

        var newClient = _tenantClientCache.GetOrAdd(cacheKey, _ =>
        {
            X509Certificate2 certificate;
            if (string.IsNullOrEmpty(certPassword))
            {
                certificate = X509CertificateLoader.LoadCertificate(rawCertBytes);
            }
            else
            {
                certificate = X509CertificateLoader.LoadPkcs12(rawCertBytes, certPassword);
            }

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 100
            };
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { certificate };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            string modelName = _config["FdmsGateway:DeviceModelName"] ?? "FDMS-Bridge-Gateway";
            string modelVersion = _config["FdmsGateway:DeviceModelVersionNo"] ?? "v7.2";

            if (!client.DefaultRequestHeaders.Contains("DeviceModelName"))
                client.DefaultRequestHeaders.Add("DeviceModelName", modelName);
            if (!client.DefaultRequestHeaders.Contains("DeviceModelVersionNo"))
                client.DefaultRequestHeaders.Add("DeviceModelVersionNo", modelVersion);

            return client;
        });

        return newClient;
    }
}
