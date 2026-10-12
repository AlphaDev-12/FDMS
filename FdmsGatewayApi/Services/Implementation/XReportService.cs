using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class XReportService : IXReportService
{
    private readonly FdmsDbContext _db;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly IReportsConfigService _configService;
    private readonly IPdfReportGenerator _pdfGenerator;
    private readonly IPassPhraseProvider _passPhraseProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<XReportService> _logger;

    public XReportService(
        FdmsDbContext db,
        IFdmsCryptoService cryptoService,
        IEncryptedKeyStore keyStore,
        IReportsConfigService configService,
        IPdfReportGenerator pdfGenerator,
        IConfiguration config,
        ILogger<XReportService> logger,
        IPassPhraseProvider? passPhraseProvider = null)
    {
        _db = db;
        _cryptoService = cryptoService;
        _keyStore = keyStore;
        _configService = configService;
        _pdfGenerator = pdfGenerator;
        _config = config;
        _logger = logger;
        _passPhraseProvider = passPhraseProvider ?? new PassPhraseProvider(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PassPhraseProvider>.Instance);
    }

    public async Task<XReportResponseDto> GenerateXReportAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            throw new KeyNotFoundException($"Fiscal device {deviceId} not found.");
        }

        // Fetch documents for active open day
        int currentDayNo = device.CurrentFiscalDayNo;
        var docs = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId && d.FiscalDayNo == currentDayNo)
            .OrderBy(d => d.ReceiptGlobalNo)
            .ToListAsync(cancellationToken);

        int startGlobalNo = docs.Count > 0 ? docs.Min(d => d.ReceiptGlobalNo) : 0;
        int endGlobalNo = docs.Count > 0 ? docs.Max(d => d.ReceiptGlobalNo) : 0;
        int fiscalizedCount = docs.Count(d => d.FiscalStatus == "Fiscalized");
        int pendingCount = docs.Count(d => d.FiscalStatus == "Pending");

        // Currency summaries & tax breakdowns
        var currencySummaries = new List<ZReportCurrencySummaryDto>();
        var currencyGroups = docs.GroupBy(d => d.Currency);

        foreach (var group in currencyGroups)
        {
            string currency = group.Key;
            decimal grossSales = group.Sum(d => d.TotalAmount);
            decimal totalTax = 0m;
            var taxSummaries = new Dictionary<string, (decimal taxPct, decimal taxableSales, decimal taxAmount)>();

            foreach (var doc in group)
            {
                try
                {
                    var req = JsonSerializer.Deserialize<ErpInvoiceRequestDto>(doc.RawPayloadJson);
                    if (req?.Taxes != null)
                    {
                        foreach (var tax in req.Taxes)
                        {
                            string code = tax.TaxCode ?? "VAT";
                            decimal pct = tax.TaxPercent ?? 0m;
                            totalTax += tax.TaxAmount;

                            if (taxSummaries.TryGetValue(code, out var existing))
                            {
                                taxSummaries[code] = (pct, existing.taxableSales + tax.SalesAmountWithTax, existing.taxAmount + tax.TaxAmount);
                            }
                            else
                            {
                                taxSummaries[code] = (pct, tax.SalesAmountWithTax, tax.TaxAmount);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse tax payload for X-Report on invoice {InvoiceNo}", doc.InvoiceNo);
                }
            }

            var taxBreakdowns = taxSummaries.Select(kv => new ZReportTaxSummaryDto
            {
                TaxCode = kv.Key,
                TaxPercent = kv.Value.taxPct,
                TaxableSales = kv.Value.taxableSales,
                TaxAmount = kv.Value.taxAmount
            }).ToList();

            currencySummaries.Add(new ZReportCurrencySummaryDto
            {
                Currency = currency,
                GrossSalesAmount = grossSales,
                NetTaxableAmount = grossSales - totalTax,
                TotalTaxAmount = totalTax,
                TaxBreakdowns = taxBreakdowns
            });
        }

        // SHA-256 Block Digest
        var sb = new StringBuilder();
        sb.Append(deviceId);
        sb.Append(DateTime.UtcNow.ToString("yyyy-MM-dd"));
        sb.Append(startGlobalNo);
        sb.Append(endGlobalNo);
        foreach (var doc in docs)
        {
            sb.Append(doc.DeviceHash);
        }

        string blockHashDigest = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));

        // Sign Block Digest with device key
        SignatureDataDto reportSig = new();
        if (!string.IsNullOrEmpty(device.EncryptedPrivateKeyPem))
        {
            try
            {
                string pass = _passPhraseProvider.GetPassPhrase();
                string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, pass);
                reportSig = _cryptoService.SignReceipt(blockHashDigest, privateKeyPem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not sign X-Report digest for device {DeviceId}", deviceId);
            }
        }

        string fileName = $"XReport_Device{deviceId}_Day{currentDayNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

        return new XReportResponseDto
        {
            DeviceId = device.DeviceId,
            DeviceSerialNo = device.DeviceSerialNo,
            TaxPayerName = device.TaxPayerName,
            TaxPayerTIN = device.TaxPayerTIN,
            FiscalDayNo = currentDayNo,
            FiscalDayOpenedAt = device.FiscalDayOpenedAt ?? DateTime.UtcNow,
            ReportGeneratedAtUtc = DateTime.UtcNow,
            StartingReceiptGlobalNo = startGlobalNo,
            EndingReceiptGlobalNo = endGlobalNo,
            TotalFiscalizedDocuments = fiscalizedCount,
            TotalPendingDocuments = pendingCount,
            CurrencySummaries = currencySummaries,
            BlockHashDigest = blockHashDigest,
            ReportSignature = reportSig,
            PdfFileName = fileName,
            PdfDownloadUrl = $"/api/v1/reports/x-report/pdf?deviceId={deviceId}"
        };
    }

    public async Task<(byte[] pdfBytes, string fileName)> GenerateXReportPdfAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var xReportDto = await GenerateXReportAsync(deviceId, cancellationToken);
        byte[] pdfBytes = _pdfGenerator.GenerateXReportPdf(xReportDto);

        // Also save a copy to the Z-Reports storage directory for records
        try
        {
            string dir = _configService.GetResolvedStoragePath();
            string filePath = Path.Combine(dir, xReportDto.PdfFileName!);
            await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
            _logger.LogInformation("Saved PDF X-Report to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write X-Report PDF copy to disk.");
        }

        return (pdfBytes, xReportDto.PdfFileName!);
    }
}
