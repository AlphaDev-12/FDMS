using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class FiscalDocumentOrchestrator : IFiscalDocumentOrchestrator
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> _counterLocks = new();

    private readonly FdmsDbContext _db;
    private readonly IFdmsValidationService _validationService;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IFdmsApiClient _apiClient;
    private readonly ICurrencySplitService _currencySplitService;
    private readonly ITaxCodeMappingService _taxMappingService;
    private readonly IWebhookDispatchService _webhookService;
    private readonly IZimraHealthCheck _healthCheck;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly IEscPosPrintService _printService;
    private readonly IAuditLogger _auditLogger;
    private readonly IPassPhraseProvider _passPhraseProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<FiscalDocumentOrchestrator> _logger;

    public FiscalDocumentOrchestrator(
        FdmsDbContext db,
        IFdmsValidationService validationService,
        IFdmsCryptoService cryptoService,
        IFdmsApiClient apiClient,
        ICurrencySplitService currencySplitService,
        ITaxCodeMappingService taxMappingService,
        IWebhookDispatchService webhookService,
        IZimraHealthCheck healthCheck,
        IEncryptedKeyStore keyStore,
        IEscPosPrintService printService,
        IAuditLogger auditLogger,
        IConfiguration config,
        ILogger<FiscalDocumentOrchestrator> logger,
        IPassPhraseProvider? passPhraseProvider = null)
    {
        _db = db;
        _validationService = validationService;
        _cryptoService = cryptoService;
        _apiClient = apiClient;
        _currencySplitService = currencySplitService;
        _taxMappingService = taxMappingService;
        _webhookService = webhookService;
        _healthCheck = healthCheck;
        _keyStore = keyStore;
        _printService = printService;
        _auditLogger = auditLogger;
        _config = config;
        _logger = logger;
        _passPhraseProvider = passPhraseProvider ?? new PassPhraseProvider(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PassPhraseProvider>.Instance);
    }

    public async Task<FiscalizedDocumentResponseDto?> GetDocumentStatusAsync(string invoiceNo, CancellationToken cancellationToken = default)
    {
        var doc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.InvoiceNo == invoiceNo, cancellationToken);
        if (doc == null)
        {
            return null;
        }

        string qrBase64 = !string.IsNullOrEmpty(doc.QrCodeUrl)
            ? _cryptoService.GenerateQrCodeBase64Image(doc.QrCodeUrl)
            : string.Empty;

        return new FiscalizedDocumentResponseDto
        {
            InvoiceNo = doc.InvoiceNo,
            DeviceId = doc.DeviceId,
            ReceiptType = doc.ReceiptType,
            ReceiptID = doc.ReceiptID ?? 0,
            ReceiptGlobalNo = doc.ReceiptGlobalNo,
            ReceiptCounter = doc.ReceiptCounter,
            FiscalDayNo = doc.FiscalDayNo,
            ServerDate = doc.ServerDate ?? doc.CreatedAt,
            DeviceSignature = new SignatureDataDto
            {
                Hash = doc.DeviceHash,
                Signature = doc.DeviceSignature
            },
            ServerSignature = new SignatureDataDto
            {
                Hash = doc.DeviceHash,
                Signature = doc.FdmsSignature ?? string.Empty
            },
            QrCodeUrl = doc.QrCodeUrl ?? string.Empty,
            QrCodeBase64Image = qrBase64,
            FiscalStatus = doc.FiscalStatus,
            OperationID = doc.OperationID ?? string.Empty,
            ErrorMessage = doc.ErrorMessage
        };
    }

    public async Task<FiscalDocumentProcessResult> ProcessDocumentAsync(
        ErpInvoiceRequestDto request,
        string receiptType,
        CreditDebitNoteRefDto? creditDebitRef = null,
        CancellationToken cancellationToken = default)
    {
        // 1. True Idempotency Check (FDMS v7.2 §4.7 rule)
        var existingDoc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.InvoiceNo == request.InvoiceNo, cancellationToken);
        if (existingDoc != null && (existingDoc.FiscalStatus == "Fiscalized" || existingDoc.FiscalStatus == "Pending"))
        {
            _logger.LogInformation("Idempotency match: Invoice {InvoiceNo} already exists with status {Status}.", request.InvoiceNo, existingDoc.FiscalStatus);
            string qrBase64Existing = !string.IsNullOrEmpty(existingDoc.QrCodeUrl) ? _cryptoService.GenerateQrCodeBase64Image(existingDoc.QrCodeUrl) : string.Empty;

            var existingResponse = new FiscalizedDocumentResponseDto
            {
                InvoiceNo = existingDoc.InvoiceNo,
                DeviceId = existingDoc.DeviceId,
                ReceiptType = existingDoc.ReceiptType,
                ReceiptID = existingDoc.ReceiptID ?? 0,
                ReceiptGlobalNo = existingDoc.ReceiptGlobalNo,
                ReceiptCounter = existingDoc.ReceiptCounter,
                FiscalDayNo = existingDoc.FiscalDayNo,
                ServerDate = existingDoc.ServerDate ?? existingDoc.CreatedAt,
                DeviceSignature = new SignatureDataDto { Hash = existingDoc.DeviceHash, Signature = existingDoc.DeviceSignature },
                ServerSignature = new SignatureDataDto { Hash = existingDoc.DeviceHash, Signature = existingDoc.FdmsSignature ?? string.Empty },
                QrCodeUrl = existingDoc.QrCodeUrl ?? string.Empty,
                QrCodeBase64Image = qrBase64Existing,
                FiscalStatus = existingDoc.FiscalStatus,
                OperationID = existingDoc.OperationID ?? string.Empty
            };

            bool isPending = existingDoc.FiscalStatus == "Pending";
            return new FiscalDocumentProcessResult
            {
                IsSuccess = true,
                IsAccepted = isPending,
                StatusCode = isPending ? 202 : 200,
                Response = existingResponse
            };
        }

        // 2. Check Active Device & Fiscal Day Status (FDMS §3.1)
        DeviceConfigEntity? device = null;
        if (request.DeviceId.HasValue && request.DeviceId.Value > 0)
        {
            device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceId.Value, cancellationToken);
            if (device == null)
            {
                return new FiscalDocumentProcessResult
                {
                    IsSuccess = false,
                    StatusCode = 404,
                    ErrorObject = new
                    {
                        error = "DEV01",
                        message = $"Specified fiscal device ID {request.DeviceId.Value} is not registered in the system.",
                        deviceId = request.DeviceId.Value
                    }
                };
            }
        }
        else
        {
            device = await _db.DeviceConfigs.FirstOrDefaultAsync(cancellationToken);
            if (device == null)
            {
                throw new InvalidOperationException("No registered fiscal device found. Register a device first via /api/v1/admin/device/register.");
            }
        }

        if (!string.Equals(device.FiscalDayStatus, "FiscalDayOpened", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(device.FiscalDayStatus, "FiscalDayCloseFailed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejecting invoice {InvoiceNo}: Fiscal day is not open (Current status: {Status})", request.InvoiceNo, device.FiscalDayStatus);
            return new FiscalDocumentProcessResult
            {
                IsSuccess = false,
                StatusCode = 423,
                ErrorObject = new
                {
                    error = "FISC01",
                    message = "Fiscal day is not open. Invoices can be created only when fiscal day is in FiscalDayOpened or FiscalDayCloseFailed status.",
                    fiscalDayStatus = device.FiscalDayStatus,
                    deviceId = device.DeviceId
                }
            };
        }

        // Check if fiscal day max duration has been exceeded (Section 4.7 & 9 rule 4)
        int maxHrs = device.TaxPayerDayMaxHrs > 0 ? device.TaxPayerDayMaxHrs : 24;
        if (device.FiscalDayOpenedAt.HasValue && (DateTime.Now - device.FiscalDayOpenedAt.Value).TotalHours > maxHrs)
        {
            _logger.LogWarning("Rejecting invoice {InvoiceNo}: Fiscal day open duration exceeds maximum allowed hours ({MaxHrs} hrs)", request.InvoiceNo, maxHrs);
            return new FiscalDocumentProcessResult
            {
                IsSuccess = false,
                StatusCode = 423,
                ErrorObject = new
                {
                    error = "RCPT041",
                    message = $"Fiscal day duration exceeds maximum allowed time ({maxHrs} hours). Please close the fiscal day before issuing new receipts.",
                    fiscalDayOpenedAt = device.FiscalDayOpenedAt,
                    maxHours = maxHrs,
                    deviceId = device.DeviceId
                }
            };
        }

        // 3. Apply Configurable Tax Code Mappings
        _taxMappingService.ApplyTaxMappings(request);

        // 4. Multi-Currency Split Tender Handling
        var splitRequests = _currencySplitService.SplitIfNeeded(request);

        if (splitRequests.Count == 1)
        {
            return await ProcessSingleDocumentAsync(splitRequests[0], receiptType, device, creditDebitRef, cancellationToken);
        }

        // Multi-currency split: process each split invoice sequentially
        var childResponses = new List<FiscalizedDocumentResponseDto>();
        bool anyQueued = false;

        foreach (var splitReq in splitRequests)
        {
            var singleResult = await ProcessSingleDocumentAsync(splitReq, receiptType, device, creditDebitRef, cancellationToken);
            if (singleResult.Response != null)
            {
                childResponses.Add(singleResult.Response);
                if (singleResult.Response.FiscalStatus == "Pending") anyQueued = true;
            }
        }

        var compositeResponse = new FiscalizedDocumentResponseDto
        {
            InvoiceNo = request.InvoiceNo,
            DeviceId = device.DeviceId,
            ReceiptType = receiptType,
            FiscalStatus = anyQueued ? "Pending" : "Fiscalized",
            SplitReceipts = childResponses
        };

        return new FiscalDocumentProcessResult
        {
            IsSuccess = true,
            IsAccepted = anyQueued,
            StatusCode = anyQueued ? 202 : 200,
            Response = compositeResponse
        };
    }

    private async Task<FiscalDocumentProcessResult> ProcessSingleDocumentAsync(
        ErpInvoiceRequestDto request,
        string receiptType,
        DeviceConfigEntity device,
        CreditDebitNoteRefDto? creditDebitRef,
        CancellationToken cancellationToken)
    {
        if (receiptType == "CreditNote")
        {
            FiscalDocumentEntity? originalDoc = null;
            if (creditDebitRef != null)
            {
                if (creditDebitRef.OriginalReceiptID.HasValue && creditDebitRef.OriginalReceiptID.Value > 0)
                {
                    originalDoc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.ReceiptID == creditDebitRef.OriginalReceiptID.Value, cancellationToken);
                }
                if (originalDoc == null && creditDebitRef.OriginalReceiptGlobalNo > 0)
                {
                    originalDoc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.ReceiptGlobalNo == creditDebitRef.OriginalReceiptGlobalNo && d.DeviceId == (creditDebitRef.OriginalDeviceID ?? device.DeviceId), cancellationToken);
                }
            }
            _validationService.ValidateCreditNoteRequest(request as ErpCreditNoteRequestDto ?? new ErpCreditNoteRequestDto(), device, originalDoc);
        }
        else if (receiptType == "DebitNote")
        {
            FiscalDocumentEntity? originalDoc = null;
            if (creditDebitRef != null)
            {
                if (creditDebitRef.OriginalReceiptID.HasValue && creditDebitRef.OriginalReceiptID.Value > 0)
                {
                    originalDoc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.ReceiptID == creditDebitRef.OriginalReceiptID.Value, cancellationToken);
                }
                if (originalDoc == null && creditDebitRef.OriginalReceiptGlobalNo > 0)
                {
                    originalDoc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.ReceiptGlobalNo == creditDebitRef.OriginalReceiptGlobalNo && d.DeviceId == (creditDebitRef.OriginalDeviceID ?? device.DeviceId), cancellationToken);
                }
            }
            _validationService.ValidateDebitNoteRequest(request as ErpDebitNoteRequestDto ?? new ErpDebitNoteRequestDto(), device, originalDoc);
        }
        else
        {
            _validationService.ValidateInvoiceRequest(request, device);
        }

        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);

        // 5. Advance counters sequentially (Thread-safe DB lock)
        int receiptCounter;
        int receiptGlobalNo;
        int fiscalDayNo;

        if (_db.Database.IsSqlServer())
        {
            var counterResult = await _db.Database.SqlQueryRaw<DeviceCounterSequenceResult>(
                @"UPDATE DeviceConfigs 
                  SET CurrentReceiptCounter = CurrentReceiptCounter + 1,
                      CurrentReceiptGlobalNo = CurrentReceiptGlobalNo + 1
                  OUTPUT INSERTED.CurrentReceiptCounter AS ReceiptCounter,
                         INSERTED.CurrentReceiptGlobalNo AS ReceiptGlobalNo,
                         INSERTED.CurrentFiscalDayNo AS FiscalDayNo
                  WHERE DeviceId = {0}", device.DeviceId)
                .ToListAsync(cancellationToken);

            var seq = counterResult.FirstOrDefault();
            receiptCounter = seq?.ReceiptCounter ?? (device.CurrentReceiptCounter + 1);
            receiptGlobalNo = seq?.ReceiptGlobalNo ?? (device.CurrentReceiptGlobalNo + 1);
            fiscalDayNo = seq?.FiscalDayNo ?? device.CurrentFiscalDayNo;

            device.CurrentReceiptCounter = receiptCounter;
            device.CurrentReceiptGlobalNo = receiptGlobalNo;
        }
        else if (string.Equals(_db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var counterResult = await _db.Database.SqlQueryRaw<DeviceCounterSequenceResult>(
                @"UPDATE ""DeviceConfigs"" 
                  SET ""CurrentReceiptCounter"" = ""CurrentReceiptCounter"" + 1,
                      ""CurrentReceiptGlobalNo"" = ""CurrentReceiptGlobalNo"" + 1
                  WHERE ""DeviceId"" = {0}
                  RETURNING ""CurrentReceiptCounter"" AS ""ReceiptCounter"",
                            ""CurrentReceiptGlobalNo"" AS ""ReceiptGlobalNo"",
                            ""CurrentFiscalDayNo"" AS ""FiscalDayNo""", device.DeviceId)
                .ToListAsync(cancellationToken);

            var seq = counterResult.FirstOrDefault();
            receiptCounter = seq?.ReceiptCounter ?? (device.CurrentReceiptCounter + 1);
            receiptGlobalNo = seq?.ReceiptGlobalNo ?? (device.CurrentReceiptGlobalNo + 1);
            fiscalDayNo = seq?.FiscalDayNo ?? device.CurrentFiscalDayNo;

            device.CurrentReceiptCounter = receiptCounter;
            device.CurrentReceiptGlobalNo = receiptGlobalNo;
        }
        else
        {
            var deviceLock = _counterLocks.GetOrAdd(device.DeviceId, _ => new SemaphoreSlim(1, 1));
            await deviceLock.WaitAsync(cancellationToken);
            try
            {
                device.CurrentReceiptCounter += 1;
                device.CurrentReceiptGlobalNo += 1;
                receiptCounter = device.CurrentReceiptCounter;
                receiptGlobalNo = device.CurrentReceiptGlobalNo;
                fiscalDayNo = device.CurrentFiscalDayNo;
            }
            finally
            {
                deviceLock.Release();
            }
        }

        // Capture previous receipt hash BEFORE calculating/updating with current receipt hash
        string? previousHash = device.LastReceiptHash;

        // 6. Compute Device Receipt Hash & Signature (Section 13.2.1)
        string deviceHash = _cryptoService.ComputeReceiptHash(
            device.DeviceId,
            receiptType,
            request.Currency,
            receiptGlobalNo,
            request.ReceiptDate,
            request.ReceiptTotal,
            request.Taxes,
            previousHash);

        var deviceSig = _cryptoService.SignReceipt(deviceHash, privateKeyPem);
        device.LastReceiptHash = deviceHash;

        // 7. Build ZIMRA submitReceipt Payload
        var submitReq = new SubmitReceiptRequest
        {
            DeviceID = device.DeviceId,
            Receipt = new FdmsReceiptPayload
            {
                ReceiptType = receiptType,
                ReceiptCurrency = request.Currency,
                ReceiptCounter = receiptCounter,
                ReceiptGlobalNo = receiptGlobalNo,
                InvoiceNo = request.InvoiceNo,
                ReceiptDate = request.ReceiptDate,
                ReceiptNotes = (request as ErpCreditNoteRequestDto)?.ReceiptNotes ?? (request as ErpDebitNoteRequestDto)?.ReceiptNotes,
                ReceiptLinesTaxInclusive = request.ReceiptLinesTaxInclusive,
                ReceiptTotal = request.ReceiptTotal,
                ReceiptPrintForm = request.ReceiptPrintForm,
                Username = request.Username,
                UserNameSurname = request.UserNameSurname,
                BuyerData = request.BuyerData == null ? null : new FdmsBuyerDto
                {
                    BuyerRegisterName = request.BuyerData.BuyerRegisterName,
                    BuyerTIN = request.BuyerData.BuyerTIN,
                    BuyerTradeName = request.BuyerData.BuyerTradeName,
                    VATNumber = request.BuyerData.VATNumber
                },
                CreditDebitNote = creditDebitRef == null ? null : new FdmsCreditDebitNoteRefDto
                {
                    ReceiptID = creditDebitRef.OriginalReceiptID,
                    DeviceID = creditDebitRef.OriginalDeviceID,
                    ReceiptGlobalNo = creditDebitRef.OriginalReceiptGlobalNo,
                    FiscalDayNo = creditDebitRef.OriginalFiscalDayNo
                },
                ReceiptLines = request.Lines.Select(l => new FdmsReceiptLinePayload
                {
                    ReceiptLineType = l.ReceiptLineType,
                    ReceiptLineNo = l.ReceiptLineNo,
                    ReceiptLineHSCode = l.ReceiptLineHSCode,
                    ReceiptLineName = l.ReceiptLineName,
                    ReceiptLinePrice = l.ReceiptLinePrice,
                    ReceiptLineQuantity = l.ReceiptLineQuantity,
                    ReceiptLineTotal = l.ReceiptLineTotal,
                    TaxCode = l.TaxCode,
                    TaxPercent = l.TaxPercent,
                    TaxID = l.TaxID
                }).ToList(),
                ReceiptTaxes = request.Taxes.Select(t => new FdmsReceiptTaxPayload
                {
                    TaxCode = t.TaxCode,
                    TaxPercent = t.TaxPercent,
                    TaxID = t.TaxID,
                    TaxAmount = t.TaxAmount,
                    SalesAmountWithTax = t.SalesAmountWithTax
                }).ToList(),
                ReceiptPayments = request.Payments.Select(p => new FdmsPaymentPayload
                {
                    MoneyTypeCode = p.MoneyTypeCode,
                    PaymentAmount = p.PaymentAmount
                }).ToList(),
                ReceiptDeviceSignature = deviceSig
            }
        };

        string qrCodeUrl = _cryptoService.GenerateQrCodeUrl(
            device.QrUrl,
            device.DeviceId,
            request.ReceiptDate,
            receiptGlobalNo,
            deviceSig.Signature);

        string qrBase64 = _cryptoService.GenerateQrCodeBase64Image(qrCodeUrl);

        // 8. Create Local Document Record
        var docEntity = new FiscalDocumentEntity
        {
            InvoiceNo = request.InvoiceNo,
            DeviceId = device.DeviceId,
            ReceiptType = receiptType,
            Currency = request.Currency,
            TotalAmount = request.ReceiptTotal,
            FiscalDayNo = fiscalDayNo,
            ReceiptCounter = receiptCounter,
            ReceiptGlobalNo = receiptGlobalNo,
            DeviceHash = deviceHash,
            PreviousDeviceHash = previousHash,
            DeviceSignature = deviceSig.Signature,
            QrCodeUrl = qrCodeUrl,
            FiscalStatus = "Pending",
            RawPayloadJson = JsonSerializer.Serialize(request)
        };

        // Generate ESC/POS thermal print blobs
        int paperWidth = _config.GetValue<int>("FdmsGateway:PrintOptions:PaperWidthMm", 80);
        var printLayout = _printService.GeneratePrintLayout(request, device, docEntity, qrCodeUrl);
        string escPosBase64 = _printService.GenerateEscPosStreamBase64(printLayout, paperWidth);

        // 9. Synchronous Submit or Asynchronous Offline Queueing (AsyncFastRetailMode bypass)
        bool asyncFastRetailMode = _config.GetValue<bool>("FdmsGateway:AsyncFastRetailMode", false);
        bool submitDirectly = _healthCheck.IsHealthy && !asyncFastRetailMode;

        if (submitDirectly)
        {
            try
            {
                var resp = await _apiClient.SubmitReceiptAsync(submitReq, device.CertificatePem, privateKeyPem, cancellationToken);
                if (resp != null)
                {
                    docEntity.ReceiptID = resp.ReceiptID;
                    docEntity.ServerDate = resp.ServerDate;
                    docEntity.FdmsSignature = resp.ReceiptServerSignature?.Signature;
                    docEntity.OperationID = resp.OperationID;
                    docEntity.FiscalStatus = "Fiscalized";
                }
                else
                {
                    submitDirectly = false; // Fall back to queue
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ZIMRA FDMS submission failed for {InvoiceNo}. Queueing offline for background worker.", request.InvoiceNo);
                submitDirectly = false; // Fall back to queue
            }
        }

        _db.FiscalDocuments.Add(docEntity);

        if (!submitDirectly)
        {
            // Queue for OfflineQueueWorker
            var queueItem = new OfflineQueueEntity
            {
                DeviceId = device.DeviceId,
                InvoiceNo = request.InvoiceNo,
                ReceiptGlobalNo = receiptGlobalNo,
                ReceiptType = receiptType,
                SubmitPayloadJson = JsonSerializer.Serialize(submitReq),
                Status = "Queued"
            };

            _db.OfflineQueue.Add(queueItem);
            docEntity.FiscalStatus = "Pending";
        }

        await _db.SaveChangesAsync(cancellationToken);

        var responseDto = new FiscalizedDocumentResponseDto
        {
            InvoiceNo = docEntity.InvoiceNo,
            DeviceId = docEntity.DeviceId,
            ReceiptType = docEntity.ReceiptType,
            ReceiptID = docEntity.ReceiptID ?? 0,
            ReceiptGlobalNo = docEntity.ReceiptGlobalNo,
            ReceiptCounter = docEntity.ReceiptCounter,
            FiscalDayNo = docEntity.FiscalDayNo,
            ServerDate = docEntity.ServerDate ?? docEntity.CreatedAt,
            DeviceSignature = deviceSig,
            ServerSignature = new SignatureDataDto
            {
                Hash = deviceHash,
                Signature = docEntity.FdmsSignature ?? string.Empty
            },
            QrCodeUrl = qrCodeUrl,
            QrCodeBase64Image = qrBase64,
            FiscalStatus = docEntity.FiscalStatus,
            IsOfflineFiscalized = docEntity.FiscalStatus == "Pending",
            OperationID = docEntity.OperationID ?? string.Empty,
            ErrorMessage = docEntity.ErrorMessage,
            PreviousDeviceHash = docEntity.PreviousDeviceHash,
            PrintLayout = printLayout,
            EscPosStreamBase64 = escPosBase64
        };

        // Dispatch webhook if fiscalized
        if (docEntity.FiscalStatus == "Fiscalized")
        {
            await _webhookService.DispatchFiscalResponseAsync(responseDto, cancellationToken);
        }

        string logMsg = docEntity.FiscalStatus == "Fiscalized"
            ? $"Invoice {request.InvoiceNo} successfully fiscalized online."
            : $"Invoice {request.InvoiceNo} queued offline for background sync (ZIMRA unavailable/offline mode).";

        await _auditLogger.LogAsync(
            endpoint: "SubmitInvoice",
            direction: "Inbound",
            httpStatusCode: docEntity.FiscalStatus == "Pending" ? 202 : 200,
            invoiceNo: docEntity.InvoiceNo,
            deviceId: docEntity.DeviceId,
            errorMessage: logMsg,
            cancellationToken: cancellationToken);

        return new FiscalDocumentProcessResult
        {
            IsSuccess = true,
            IsAccepted = docEntity.FiscalStatus == "Pending",
            StatusCode = docEntity.FiscalStatus == "Pending" ? 202 : 200,
            Response = responseDto
        };
    }

    public async Task<ErpBulkInvoiceResponseDto> ProcessBulkDocumentsAsync(
        ErpBulkInvoiceRequestDto bulkRequest,
        CancellationToken cancellationToken = default)
    {
        var response = new ErpBulkInvoiceResponseDto
        {
            BatchId = !string.IsNullOrWhiteSpace(bulkRequest.BatchId) ? bulkRequest.BatchId : Guid.NewGuid().ToString(),
            TotalReceived = bulkRequest.Invoices?.Count ?? 0
        };

        if (bulkRequest.Invoices == null || bulkRequest.Invoices.Count == 0)
        {
            return response;
        }

        foreach (var invoice in bulkRequest.Invoices)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                var result = await ProcessDocumentAsync(invoice, "FiscalInvoice", creditDebitRef: null, cancellationToken);
                if (result.IsSuccess && result.Response != null)
                {
                    bool isPending = result.Response.FiscalStatus == "Pending" || result.IsAccepted;
                    if (isPending)
                    {
                        response.QueuedCount++;
                    }
                    else
                    {
                        response.SuccessCount++;
                    }

                    response.Results.Add(new ErpBulkItemResultDto
                    {
                        InvoiceNo = invoice.InvoiceNo,
                        DeviceId = invoice.DeviceId,
                        FiscalStatus = result.Response.FiscalStatus,
                        StatusCode = result.StatusCode,
                        Document = result.Response
                    });
                }
                else
                {
                    response.FailedCount++;
                    string errMsg = "Validation or processing failed.";
                    if (result.ErrorObject != null)
                    {
                        errMsg = JsonSerializer.Serialize(result.ErrorObject);
                    }

                    response.Results.Add(new ErpBulkItemResultDto
                    {
                        InvoiceNo = invoice.InvoiceNo,
                        DeviceId = invoice.DeviceId,
                        FiscalStatus = "Failed",
                        StatusCode = result.StatusCode,
                        ErrorMessage = errMsg
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk ingest error processing invoice {InvoiceNo}", invoice.InvoiceNo);
                response.FailedCount++;
                response.Results.Add(new ErpBulkItemResultDto
                {
                    InvoiceNo = invoice.InvoiceNo,
                    DeviceId = invoice.DeviceId,
                    FiscalStatus = "Failed",
                    StatusCode = 500,
                    ErrorMessage = ex.Message
                });
            }
        }

        response.ProcessedCount = response.Results.Count;
        return response;
    }
}

