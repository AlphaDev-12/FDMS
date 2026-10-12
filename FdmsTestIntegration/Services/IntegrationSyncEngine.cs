using FdmsGatewayApi.Models.Dtos;
using FdmsTestIntegration.Database;
using FdmsTestIntegration.Models;
using Microsoft.EntityFrameworkCore;

namespace FdmsTestIntegration.Services;

public class IntegrationSyncEngine
{
    private readonly MockErpDbContext _db;
    private readonly FdmsApiClient _apiClient;

    public IntegrationSyncEngine(MockErpDbContext db, FdmsApiClient apiClient)
    {
        _db = db;
        _apiClient = apiClient;
    }

    public async Task<SyncSummaryResult> RunSyncWorkflowAsync(CancellationToken ct = default)
    {
        var result = new SyncSummaryResult();

        // 1. Verify Gateway Health
        bool isOnline = await _apiClient.IsGatewayOnlineAsync(ct);
        if (!isOnline)
        {
            result.Success = false;
            result.Message = "FDMS Gateway API is offline or unreachable at target endpoint.";
            return result;
        }

        // 2. Open Fiscal Day for Device
        await _apiClient.EnsureFiscalDayOpenAsync(1, ct);

        // -----------------------------------------------------------------
        // Phase 1: Process Invoices
        // -----------------------------------------------------------------
        var pendingInvoices = await _db.Documents
            .Include(d => d.Lines)
            .Where(d => d.DocumentType == "FiscalInvoice" && d.FiscalStatus == "Pending")
            .ToListAsync(ct);

        foreach (var inv in pendingInvoices)
        {
            try
            {
                var dto = MapToInvoiceDto(inv);
                var response = await _apiClient.FiscalizeInvoiceAsync(dto, ct);

                if (response != null)
                {
                    ApplyFiscalResponse(inv, response);
                    result.ProcessedInvoices++;
                }
            }
            catch (Exception ex)
            {
                inv.FiscalStatus = "Failed";
                inv.ErrorMessage = ex.Message;
                result.Errors.Add($"Invoice {inv.InvoiceNo}: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync(ct);

        // -----------------------------------------------------------------
        // Phase 2: Process Credit Notes (CN)
        // -----------------------------------------------------------------
        var pendingCreditNotes = await _db.Documents
            .Include(d => d.Lines)
            .Where(d => d.DocumentType == "CreditNote" && d.FiscalStatus == "Pending")
            .ToListAsync(ct);

        foreach (var cn in pendingCreditNotes)
        {
            try
            {
                var origInv = await _db.Documents.FirstOrDefaultAsync(d => d.InvoiceNo == cn.OriginalInvoiceNo, ct);
                if (origInv == null || origInv.ReceiptGlobalNo == 0)
                {
                    cn.FiscalStatus = "Failed";
                    cn.ErrorMessage = $"Original invoice '{cn.OriginalInvoiceNo}' not found or not fiscalized.";
                    result.Errors.Add($"Credit Note {cn.InvoiceNo}: {cn.ErrorMessage}");
                    continue;
                }

                var dto = MapToCreditNoteDto(cn, origInv);
                var response = await _apiClient.FiscalizeCreditNoteAsync(dto, ct);

                if (response != null)
                {
                    ApplyFiscalResponse(cn, response);
                    result.ProcessedCreditNotes++;
                }
            }
            catch (Exception ex)
            {
                cn.FiscalStatus = "Failed";
                cn.ErrorMessage = ex.Message;
                result.Errors.Add($"Credit Note {cn.InvoiceNo}: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync(ct);

        // -----------------------------------------------------------------
        // Phase 3: Process Debit Notes (DN)
        // -----------------------------------------------------------------
        var pendingDebitNotes = await _db.Documents
            .Include(d => d.Lines)
            .Where(d => d.DocumentType == "DebitNote" && d.FiscalStatus == "Pending")
            .ToListAsync(ct);

        foreach (var dn in pendingDebitNotes)
        {
            try
            {
                var origInv = await _db.Documents.FirstOrDefaultAsync(d => d.InvoiceNo == dn.OriginalInvoiceNo, ct);
                if (origInv == null || origInv.ReceiptGlobalNo == 0)
                {
                    dn.FiscalStatus = "Failed";
                    dn.ErrorMessage = $"Original invoice '{dn.OriginalInvoiceNo}' not found or not fiscalized.";
                    result.Errors.Add($"Debit Note {dn.InvoiceNo}: {dn.ErrorMessage}");
                    continue;
                }

                var dto = MapToDebitNoteDto(dn, origInv);
                var response = await _apiClient.FiscalizeDebitNoteAsync(dto, ct);

                if (response != null)
                {
                    ApplyFiscalResponse(dn, response);
                    result.ProcessedDebitNotes++;
                }
            }
            catch (Exception ex)
            {
                dn.FiscalStatus = "Failed";
                dn.ErrorMessage = ex.Message;
                result.Errors.Add($"Debit Note {dn.InvoiceNo}: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync(ct);

        result.Success = result.Errors.Count == 0;
        result.Message = result.Success ? "All ERP documents fiscalized successfully!" : "Sync completed with warnings/errors.";
        return result;
    }

    private ErpInvoiceRequestDto MapToInvoiceDto(ErpDocument doc)
    {
        return new ErpInvoiceRequestDto
        {
            DeviceId = 1,
            InvoiceNo = doc.InvoiceNo,
            Currency = doc.Currency,
            ReceiptDate = doc.ReceiptDate,
            ReceiptTotal = doc.ReceiptTotal,
            ReceiptLinesTaxInclusive = true,
            BuyerData = new BuyerDto
            {
                BuyerRegisterName = doc.BuyerName,
                BuyerTIN = doc.BuyerTIN
            },
            Lines = doc.Lines.Select(l => new ErpReceiptLineDto
            {
                ReceiptLineNo = l.LineNo,
                ReceiptLineName = l.ItemName,
                ReceiptLineQuantity = l.Quantity,
                ReceiptLinePrice = l.UnitPrice,
                ReceiptLineTotal = l.Total,
                TaxCode = l.TaxCode,
                TaxPercent = l.TaxPercent,
                TaxID = l.TaxID
            }).ToList(),
            Taxes = new List<ErpReceiptTaxDto>
            {
                new ErpReceiptTaxDto
                {
                    TaxCode = "A",
                    TaxPercent = 15.0m,
                    TaxID = 1,
                    TaxAmount = doc.TaxAmount,
                    SalesAmountWithTax = doc.ReceiptTotal
                }
            },
            Payments = new List<ErpPaymentDto>
            {
                new ErpPaymentDto
                {
                    MoneyTypeCode = doc.PaymentMethod,
                    PaymentAmount = doc.ReceiptTotal,
                    PaymentCurrency = doc.Currency
                }
            }
        };
    }

    private ErpCreditNoteRequestDto MapToCreditNoteDto(ErpDocument cn, ErpDocument origInv)
    {
        var baseDto = MapToInvoiceDto(cn);
        return new ErpCreditNoteRequestDto
        {
            DeviceId = baseDto.DeviceId,
            InvoiceNo = baseDto.InvoiceNo,
            Currency = baseDto.Currency,
            ReceiptDate = baseDto.ReceiptDate,
            ReceiptTotal = baseDto.ReceiptTotal,
            ReceiptLinesTaxInclusive = baseDto.ReceiptLinesTaxInclusive,
            BuyerData = baseDto.BuyerData,
            Lines = baseDto.Lines,
            Taxes = baseDto.Taxes,
            Payments = baseDto.Payments,
            ReceiptNotes = cn.ReceiptNotes ?? "Credit note adjustment",
            CreditDebitNoteRef = new CreditDebitNoteRefDto
            {
                OriginalReceiptID = origInv.ReceiptID,
                OriginalDeviceID = origInv.DeviceId ?? 1,
                OriginalReceiptGlobalNo = origInv.ReceiptGlobalNo,
                OriginalFiscalDayNo = origInv.FiscalDayNo
            }
        };
    }

    private ErpDebitNoteRequestDto MapToDebitNoteDto(ErpDocument dn, ErpDocument origInv)
    {
        var baseDto = MapToInvoiceDto(dn);
        return new ErpDebitNoteRequestDto
        {
            DeviceId = baseDto.DeviceId,
            InvoiceNo = baseDto.InvoiceNo,
            Currency = baseDto.Currency,
            ReceiptDate = baseDto.ReceiptDate,
            ReceiptTotal = baseDto.ReceiptTotal,
            ReceiptLinesTaxInclusive = baseDto.ReceiptLinesTaxInclusive,
            BuyerData = baseDto.BuyerData,
            Lines = baseDto.Lines,
            Taxes = baseDto.Taxes,
            Payments = baseDto.Payments,
            ReceiptNotes = dn.ReceiptNotes ?? "Debit note surcharge",
            CreditDebitNoteRef = new CreditDebitNoteRefDto
            {
                OriginalReceiptID = origInv.ReceiptID,
                OriginalDeviceID = origInv.DeviceId ?? 1,
                OriginalReceiptGlobalNo = origInv.ReceiptGlobalNo,
                OriginalFiscalDayNo = origInv.FiscalDayNo
            }
        };
    }

    private void ApplyFiscalResponse(ErpDocument doc, FiscalizedDocumentResponseDto response)
    {
        doc.FiscalStatus = !string.IsNullOrWhiteSpace(response.QrCodeUrl) || response.ReceiptGlobalNo > 0 ? "Fiscalized" : response.FiscalStatus;
        doc.ReceiptID = response.ReceiptID > 0 ? response.ReceiptID : null;
        doc.ReceiptGlobalNo = response.ReceiptGlobalNo;
        doc.FiscalDayNo = response.FiscalDayNo;
        doc.DeviceId = response.DeviceId;
        doc.QrCodeUrl = response.QrCodeUrl;
        doc.DeviceSignature = response.DeviceSignature?.Signature;
        doc.FiscalizedAt = response.ServerDate == default ? DateTime.Now : response.ServerDate;
        doc.ErrorMessage = response.ErrorMessage;
    }
}

public class SyncSummaryResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ProcessedInvoices { get; set; }
    public int ProcessedCreditNotes { get; set; }
    public int ProcessedDebitNotes { get; set; }
    public List<string> Errors { get; set; } = new();
}
