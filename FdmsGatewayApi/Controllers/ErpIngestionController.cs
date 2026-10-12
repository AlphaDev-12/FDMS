using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// ERP Document Ingestion and Fiscalization Endpoints.
/// Primary API endpoints used by external ERP systems (Sage, SAP, QuickBooks, Odoo, custom ERPs) to submit tax documents.
/// </summary>
[ApiController]
[Route("api/v1/erp")]
[Produces("application/json")]
[Tags("ERP Document Ingestion")]
public class ErpIngestionController : ControllerBase
{
    private readonly IFiscalDocumentOrchestrator _orchestrator;
    private readonly FdmsDbContext _db;
    private readonly ILogger<ErpIngestionController> _logger;

    public ErpIngestionController(
        IFiscalDocumentOrchestrator orchestrator,
        FdmsDbContext db,
        ILogger<ErpIngestionController> logger)
    {
        _orchestrator = orchestrator;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Fiscalize an ERP Sales Invoice.
    /// </summary>
    /// <remarks>
    /// Validates invoice structure, checks active fiscal day, appends cryptographic hash chain signature,
    /// and transmits document synchronously (or buffers in resilient offline queue if ZIMRA is offline).
    /// </remarks>
    /// <param name="request">Invoice details including line items, buyer info, device ID, tax totals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fiscalized document response containing ZIMRA verification QR code URL, signature, and submission status.</returns>
    [HttpPost("invoices")]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FiscalizeInvoice([FromBody] ErpInvoiceRequestDto request, CancellationToken cancellationToken = default)
    {
        return ProcessDocumentResponseAsync(
            await _orchestrator.ProcessDocumentAsync(request, "FiscalInvoice", creditDebitRef: null, cancellationToken));
    }

    /// <summary>
    /// Asynchronous Bulk Invoice Ingest Array (Async Queue).
    /// </summary>
    /// <remarks>
    /// Ingests a bulk array of sales invoices from ERP systems (Sage, SAP, QuickBooks, Odoo, custom ERPs).
    /// Validates structure, checks active fiscal day, appends cryptographic hash chain signature,
    /// and transmits documents to ZIMRA or buffers into resilient offline queue.
    /// </remarks>
    /// <param name="request">Bulk invoice request wrapper containing batch ID, callback URL, and array of invoices.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bulk processing summary detailing status, verification QR codes, and ZIMRA transmission state for each item.</returns>
    [HttpPost("invoices/bulk")]
    [ProducesResponseType(typeof(ErpBulkInvoiceResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErpBulkInvoiceResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FiscalizeInvoiceBulk([FromBody] ErpBulkInvoiceRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Invoices == null || request.Invoices.Count == 0)
        {
            return BadRequest(new { error = "Invoices array cannot be empty." });
        }

        var result = await _orchestrator.ProcessBulkDocumentsAsync(request, cancellationToken);
        bool hasQueuedOrFailed = result.QueuedCount > 0 || result.FailedCount > 0;
        return hasQueuedOrFailed ? Accepted(result) : Ok(result);
    }



    /// <summary>
    /// Fiscalize a Credit Note linked to a previously issued invoice.
    /// </summary>
    /// <remarks>
    /// Requires CreditDebitNoteRef object referencing original invoice number and reason for adjustment.
    /// </remarks>
    /// <param name="request">Credit note details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("credit-notes")]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FiscalizeCreditNote([FromBody] ErpCreditNoteRequestDto request, CancellationToken cancellationToken = default)
    {
        return ProcessDocumentResponseAsync(
            await _orchestrator.ProcessDocumentAsync(request, "CreditNote", request.CreditDebitNoteRef, cancellationToken));
    }

    /// <summary>
    /// Fiscalize a Debit Note linked to a previously issued invoice.
    /// </summary>
    /// <remarks>
    /// Requires CreditDebitNoteRef object referencing original invoice number and reason for price increment.
    /// </remarks>
    /// <param name="request">Debit note details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("debit-notes")]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FiscalizeDebitNote([FromBody] ErpDebitNoteRequestDto request, CancellationToken cancellationToken = default)
    {
        return ProcessDocumentResponseAsync(
            await _orchestrator.ProcessDocumentAsync(request, "DebitNote", request.CreditDebitNoteRef, cancellationToken));
    }

    /// <summary>
    /// Get fiscalization status and verification data for a submitted invoice by ERP invoice number.
    /// </summary>
    /// <param name="invoiceNo">Unique ERP Invoice Number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document fiscal status, verification URL, QR string, and signature details.</returns>
    [HttpGet("documents/{invoiceNo}")]
    [ProducesResponseType(typeof(FiscalizedDocumentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FiscalizedDocumentResponseDto>> GetDocumentStatus(string invoiceNo, CancellationToken cancellationToken = default)
    {
        var docStatus = await _orchestrator.GetDocumentStatusAsync(invoiceNo, cancellationToken);
        if (docStatus == null)
        {
            return NotFound(new { error = $"Document with invoice number '{invoiceNo}' was not found." });
        }

        return Ok(docStatus);
    }

    /// <summary>
    /// Register an HTTP Webhook callback endpoint for asynchronous document processing updates.
    /// </summary>
    /// <param name="registration">Webhook configuration including target URL and secret key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("webhooks/register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterWebhook([FromBody] WebhookRegistrationEntity registration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registration.WebhookUrl))
        {
            return BadRequest(new { error = "WebhookUrl is required." });
        }

        _db.WebhookRegistrations.Add(registration);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Webhook registered successfully.", id = registration.Id });
    }

    private IActionResult ProcessDocumentResponseAsync(FiscalDocumentProcessResult result)
    {
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, result.ErrorObject);
        }

        if (result.IsAccepted)
        {
            return Accepted(result.Response);
        }

        return Ok(result.Response);
    }
}
