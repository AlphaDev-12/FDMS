using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public class FiscalDocumentProcessResult
{
    public bool IsSuccess { get; set; }
    public bool IsAccepted { get; set; } // HTTP 202 status for offline queued
    public int StatusCode { get; set; } = 200;
    public FiscalizedDocumentResponseDto? Response { get; set; }
    public object? ErrorObject { get; set; }
}

public interface IFiscalDocumentOrchestrator
{
    Task<FiscalDocumentProcessResult> ProcessDocumentAsync(
        ErpInvoiceRequestDto request,
        string receiptType,
        CreditDebitNoteRefDto? creditDebitRef = null,
        CancellationToken cancellationToken = default);

    Task<FiscalizedDocumentResponseDto?> GetDocumentStatusAsync(string invoiceNo, CancellationToken cancellationToken = default);

    Task<ErpBulkInvoiceResponseDto> ProcessBulkDocumentsAsync(
        ErpBulkInvoiceRequestDto bulkRequest,
        CancellationToken cancellationToken = default);
}

