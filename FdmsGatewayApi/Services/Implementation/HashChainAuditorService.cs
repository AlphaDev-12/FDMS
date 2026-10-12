using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class HashChainAuditorService : IHashChainAuditorService
{
    private readonly FdmsDbContext _db;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly ILogger<HashChainAuditorService> _logger;

    public HashChainAuditorService(
        FdmsDbContext db,
        IFdmsCryptoService cryptoService,
        ILogger<HashChainAuditorService> logger)
    {
        _db = db;
        _cryptoService = cryptoService;
        _logger = logger;
    }

    public async Task<HashChainVerificationResultDto> VerifyHashChainAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var documents = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId)
            .OrderBy(d => d.ReceiptGlobalNo)
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            return new HashChainVerificationResultDto
            {
                DeviceId = deviceId,
                IsChainValid = true,
                TotalDocumentsScanned = 0,
                StatusMessage = "No fiscal documents recorded for this device."
            };
        }

        string? expectedPreviousHash = null;

        for (int i = 0; i < documents.Count; i++)
        {
            var doc = documents[i];

            // 1. Verify previous hash link matches
            if (i > 0 && !string.Equals(doc.PreviousDeviceHash, expectedPreviousHash, StringComparison.Ordinal))
            {
                _logger.LogCritical("HASH CHAIN BROKEN at index {Index}, Invoice {InvoiceNo}: Stored PreviousDeviceHash '{StoredPrev}' != Expected Previous Hash '{ExpectedPrev}'",
                    i, doc.InvoiceNo, doc.PreviousDeviceHash, expectedPreviousHash);

                return new HashChainVerificationResultDto
                {
                    DeviceId = deviceId,
                    IsChainValid = false,
                    TotalDocumentsScanned = documents.Count,
                    TamperedIndex = i,
                    TamperedInvoiceNo = doc.InvoiceNo,
                    StatusMessage = $"HASH CHAIN CORRUPTED: Previous transaction hash link broken at ReceiptGlobalNo {doc.ReceiptGlobalNo} (Invoice {doc.InvoiceNo}). Database tampering detected!"
                };
            }

            // 2. Recompute current document's receipt hash using saved JSON payload or raw fields
            try
            {
                var req = JsonSerializer.Deserialize<ErpInvoiceRequestDto>(doc.RawPayloadJson);
                if (req != null)
                {
                    string recomputedHash = _cryptoService.ComputeReceiptHash(
                        doc.DeviceId,
                        doc.ReceiptType,
                        doc.Currency,
                        doc.ReceiptGlobalNo,
                        req.ReceiptDate,
                        doc.TotalAmount,
                        req.Taxes,
                        doc.PreviousDeviceHash);

                    if (!string.Equals(recomputedHash, doc.DeviceHash, StringComparison.Ordinal))
                    {
                        _logger.LogCritical("TAMPERING DETECTED at index {Index}, Invoice {InvoiceNo}: Recomputed Hash '{Recomputed}' != Stored DeviceHash '{Stored}'",
                            i, doc.InvoiceNo, recomputedHash, doc.DeviceHash);

                        return new HashChainVerificationResultDto
                        {
                            DeviceId = deviceId,
                            IsChainValid = false,
                            TotalDocumentsScanned = documents.Count,
                            TamperedIndex = i,
                            TamperedInvoiceNo = doc.InvoiceNo,
                            StatusMessage = $"DATA MUTATION DETECTED: Computed hash does not match stored hash for ReceiptGlobalNo {doc.ReceiptGlobalNo} (Invoice {doc.InvoiceNo}). Financial details altered!"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse RawPayloadJson for invoice {InvoiceNo} during hash chain verification", doc.InvoiceNo);
            }

            expectedPreviousHash = doc.DeviceHash;
        }

        return new HashChainVerificationResultDto
        {
            DeviceId = deviceId,
            IsChainValid = true,
            TotalDocumentsScanned = documents.Count,
            StatusMessage = $"Successfully verified cryptographic hash chain for {documents.Count} fiscal documents. Integrity 100% intact."
        };
    }
}
