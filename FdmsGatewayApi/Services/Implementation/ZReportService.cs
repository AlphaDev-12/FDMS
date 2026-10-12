using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class ZReportService : IZReportService
{
    private readonly FdmsDbContext _db;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly IReportsConfigService _configService;
    private readonly IPdfReportGenerator _pdfGenerator;
    private readonly IPassPhraseProvider _passPhraseProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<ZReportService> _logger;

    public ZReportService(
        FdmsDbContext db,
        IFdmsCryptoService cryptoService,
        IEncryptedKeyStore keyStore,
        IReportsConfigService configService,
        IPdfReportGenerator pdfGenerator,
        IConfiguration config,
        ILogger<ZReportService> logger,
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

    public async Task<ZReportResponseDto> GenerateZReportAsync(int deviceId, DateTime date, CancellationToken cancellationToken = default)
    {
        DateTime startOfDay = date.Date;
        DateTime endOfDay = startOfDay.AddDays(1).AddTicks(-1);

        var docs = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId && d.CreatedAt >= startOfDay && d.CreatedAt <= endOfDay)
            .OrderBy(d => d.ReceiptGlobalNo)
            .ToListAsync(cancellationToken);

        return await BuildZReportDtoAsync(deviceId, date.Date, docs, cancellationToken);
    }

    public async Task<ZReportResponseDto> GenerateZReportByFiscalDayAsync(int deviceId, int fiscalDayNo, CancellationToken cancellationToken = default)
    {
        var docs = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId && d.FiscalDayNo == fiscalDayNo)
            .OrderBy(d => d.ReceiptGlobalNo)
            .ToListAsync(cancellationToken);

        DateTime reportDate = docs.FirstOrDefault()?.CreatedAt.Date ?? DateTime.UtcNow.Date;
        return await BuildZReportDtoAsync(deviceId, reportDate, docs, cancellationToken);
    }

    public async Task<ZReportEntity> GenerateAndSaveZReportPdfAsync(int deviceId, int fiscalDayNo, CancellationToken cancellationToken = default)
    {
        // 1. Generate DTO Summary
        var dto = await GenerateZReportByFiscalDayAsync(deviceId, fiscalDayNo, cancellationToken);

        // 2. Generate PDF bytes using vector PDF engine
        byte[] pdfBytes = _pdfGenerator.GenerateZReportPdf(dto);

        // 3. Resolve storage directory
        string storageDir = _configService.GetResolvedStoragePath();
        string fileName = $"ZReport_Device{deviceId}_Day{fiscalDayNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        string filePath = Path.Combine(storageDir, fileName);

        // 4. Save PDF to disk
        await File.WriteAllBytesAsync(filePath, pdfBytes, cancellationToken);
        _logger.LogInformation("Saved PDF Z-Report for Device {DeviceId}, Fiscal Day #{DayNo} to {FilePath}", deviceId, fiscalDayNo, filePath);

        // 5. Calculate sales breakdown by currency
        decimal usdSales = dto.CurrencySummaries.FirstOrDefault(c => string.Equals(c.Currency, "USD", StringComparison.OrdinalIgnoreCase))?.GrossSalesAmount ?? 0m;
        decimal zigSales = dto.CurrencySummaries.FirstOrDefault(c => string.Equals(c.Currency, "ZWG", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Currency, "ZIG", StringComparison.OrdinalIgnoreCase))?.GrossSalesAmount ?? 0m;

        // 6. Save or update DB record
        var existing = await _db.ZReports.FirstOrDefaultAsync(z => z.DeviceId == deviceId && z.FiscalDayNo == fiscalDayNo, cancellationToken);
        if (existing == null)
        {
            existing = new ZReportEntity
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                FiscalDayNo = fiscalDayNo,
                ReportDate = dto.ReportDate,
                StartingReceiptGlobalNo = dto.StartingReceiptGlobalNo,
                EndingReceiptGlobalNo = dto.EndingReceiptGlobalNo,
                TotalFiscalizedDocuments = dto.TotalFiscalizedDocuments,
                TotalUsdSales = usdSales,
                TotalZigSales = zigSales,
                BlockHashDigest = dto.BlockHashDigest,
                Signature = dto.ReportSignature?.Signature ?? string.Empty,
                PdfFilePath = filePath,
                PdfFileName = fileName,
                GeneratedAtUtc = DateTime.UtcNow
            };
            _db.ZReports.Add(existing);
        }
        else
        {
            existing.ReportDate = dto.ReportDate;
            existing.StartingReceiptGlobalNo = dto.StartingReceiptGlobalNo;
            existing.EndingReceiptGlobalNo = dto.EndingReceiptGlobalNo;
            existing.TotalFiscalizedDocuments = dto.TotalFiscalizedDocuments;
            existing.TotalUsdSales = usdSales;
            existing.TotalZigSales = zigSales;
            existing.BlockHashDigest = dto.BlockHashDigest;
            existing.Signature = dto.ReportSignature?.Signature ?? string.Empty;
            existing.PdfFilePath = filePath;
            existing.PdfFileName = fileName;
            existing.GeneratedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<PagedZReportsResultDto> GetZReportsListAsync(int deviceId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _db.ZReports.Where(z => deviceId <= 0 || z.DeviceId == deviceId);

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(z => z.FiscalDayNo)
            .ThenByDescending(z => z.GeneratedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(z => new ZReportListItemDto
            {
                Id = z.Id,
                DeviceId = z.DeviceId,
                FiscalDayNo = z.FiscalDayNo,
                ReportDate = z.ReportDate,
                StartingReceiptGlobalNo = z.StartingReceiptGlobalNo,
                EndingReceiptGlobalNo = z.EndingReceiptGlobalNo,
                TotalFiscalizedDocuments = z.TotalFiscalizedDocuments,
                TotalUsdSales = z.TotalUsdSales,
                TotalZigSales = z.TotalZigSales,
                BlockHashDigest = z.BlockHashDigest,
                PdfFileName = z.PdfFileName,
                GeneratedAtUtc = z.GeneratedAtUtc,
                PdfViewUrl = $"/api/v1/reports/z-report/pdf/{z.Id}"
            })
            .ToListAsync(cancellationToken);

        return new PagedZReportsResultDto
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items
        };
    }

    public async Task<(byte[] pdfBytes, string fileName)> GetZReportPdfBytesAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var zreport = await _db.ZReports.FirstOrDefaultAsync(z => z.Id == reportId, cancellationToken);
        if (zreport == null)
        {
            throw new KeyNotFoundException($"Z-Report with ID {reportId} was not found.");
        }

        if (File.Exists(zreport.PdfFilePath))
        {
            byte[] bytes = await File.ReadAllBytesAsync(zreport.PdfFilePath, cancellationToken);
            return (bytes, zreport.PdfFileName);
        }

        // Regenerate on the fly if file was moved or missing
        var dto = await GenerateZReportByFiscalDayAsync(zreport.DeviceId, zreport.FiscalDayNo, cancellationToken);
        byte[] pdfBytes = _pdfGenerator.GenerateZReportPdf(dto);
        return (pdfBytes, zreport.PdfFileName);
    }

    private async Task<ZReportResponseDto> BuildZReportDtoAsync(
        int deviceId,
        DateTime reportDate,
        List<FiscalDocumentEntity> docs,
        CancellationToken cancellationToken)
    {
        var device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            throw new InvalidOperationException($"Fiscal device {deviceId} not found.");
        }

        int startGlobalNo = docs.Count > 0 ? docs.Min(d => d.ReceiptGlobalNo) : 0;
        int endGlobalNo = docs.Count > 0 ? docs.Max(d => d.ReceiptGlobalNo) : 0;
        int fiscalizedCount = docs.Count(d => d.FiscalStatus == "Fiscalized");
        int pendingCount = docs.Count(d => d.FiscalStatus == "Pending");

        // Group by Currency
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
                    _logger.LogWarning(ex, "Failed to parse tax payload for Z-Report on invoice {InvoiceNo}", doc.InvoiceNo);
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

        // Compute 24-hour Block Hash Digest over all transaction hashes
        var sb = new StringBuilder();
        sb.Append(deviceId);
        sb.Append(reportDate.ToString("yyyy-MM-dd"));
        sb.Append(startGlobalNo);
        sb.Append(endGlobalNo);
        foreach (var doc in docs)
        {
            sb.Append(doc.DeviceHash);
        }

        string canonicalString = sb.ToString();
        byte[] blockHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalString));
        string blockHashDigest = Convert.ToBase64String(blockHashBytes);

        // Sign Block Hash Digest with device ECDSA key
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
                _logger.LogWarning(ex, "Could not sign Z-Report digest for device {DeviceId}", deviceId);
            }
        }

        return new ZReportResponseDto
        {
            DeviceId = device.DeviceId,
            DeviceSerialNo = device.DeviceSerialNo,
            TaxPayerName = device.TaxPayerName,
            TaxPayerTIN = device.TaxPayerTIN,
            FiscalDayNo = device.CurrentFiscalDayNo,
            ReportDate = reportDate,
            StartingReceiptGlobalNo = startGlobalNo,
            EndingReceiptGlobalNo = endGlobalNo,
            TotalFiscalizedDocuments = fiscalizedCount,
            TotalPendingDocuments = pendingCount,
            CurrencySummaries = currencySummaries,
            BlockHashDigest = blockHashDigest,
            ReportSignature = reportSig,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }
}
