using System.Collections.Concurrent;
using System.Text.Json;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class OfflineQueueWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IZimraHealthCheck _healthCheck;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<OfflineQueueWorker> _logger;
    private readonly ConcurrentDictionary<int, int> _deviceFailures = new();

    public OfflineQueueWorker(
        IServiceProvider serviceProvider,
        IZimraHealthCheck healthCheck,
        IAuditLogger auditLogger,
        ILogger<OfflineQueueWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _healthCheck = healthCheck;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Offline Queue Worker started with per-device queue partitioning.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_healthCheck.IsHealthy)
                {
                    using var lockScope = _serviceProvider.CreateScope();
                    var lockProvider = lockScope.ServiceProvider.GetService<IDistributedLockProvider>();

                    if (lockProvider != null)
                    {
                        await using var lockHandle = await lockProvider.TryAcquireLockAsync("FDMS_OFFLINE_QUEUE_WORKER_LOCK", TimeSpan.FromSeconds(15), stoppingToken);
                        if (lockHandle != null)
                        {
                            await ProcessNextQueueBatchAsync(stoppingToken);
                        }
                        else
                        {
                            _logger.LogDebug("OfflineQueueWorker lock currently held by another worker node. Skipping current poll iteration.");
                        }
                    }
                    else
                    {
                        await ProcessNextQueueBatchAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing offline queue batch.");
            }

            // Exponential backoff delay based on maximum consecutive failures across active devices (default 5s)
            int delaySeconds = CalculateBackoffDelay();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task ProcessNextQueueBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();
        var apiClient = scope.ServiceProvider.GetRequiredService<IFdmsApiClient>();
        var cryptoService = scope.ServiceProvider.GetRequiredService<IFdmsCryptoService>();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookDispatchService>();
        var keyStore = scope.ServiceProvider.GetRequiredService<IEncryptedKeyStore>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var passPhraseProvider = scope.ServiceProvider.GetService<IPassPhraseProvider>() ?? new PassPhraseProvider(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PassPhraseProvider>.Instance);

        // 1. Fetch distinct DeviceIDs that have pending queue items (Queued or Failed with < 10 retries)
        var deviceIdsWithPending = await db.OfflineQueue
            .Where(q => (q.Status == "Queued" || q.Status == "Failed") && q.RetryCount < 10)
            .Select(q => q.DeviceId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (deviceIdsWithPending.Count == 0)
        {
            _deviceFailures.Clear();
            return;
        }

        var deviceMap = await db.DeviceConfigs.ToDictionaryAsync(d => d.DeviceId, cancellationToken);
        if (deviceMap.Count == 0)
        {
            _logger.LogWarning("OfflineQueueWorker: No registered devices found. Skipping queue drain.");
            return;
        }

        string passPhrase = passPhraseProvider.GetPassPhrase();

        // 2. Process queue items per device independently to prevent cross-device blocking
        foreach (var deviceId in deviceIdsWithPending)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!deviceMap.TryGetValue(deviceId, out var device) || string.IsNullOrEmpty(device.CertificatePem))
            {
                _logger.LogWarning("OfflineQueueWorker: Device {DeviceId} not found or missing certificate.", deviceId);
                var orphanedItems = await db.OfflineQueue
                    .Where(q => q.DeviceId == deviceId && (q.Status == "Queued" || q.Status == "Failed"))
                    .ToListAsync(cancellationToken);

                foreach (var item in orphanedItems)
                {
                    item.Status = "Failed";
                    item.ErrorMessage = $"Registered device {deviceId} not found or missing client certificate.";
                }
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            // Fetch queued items strictly ordered by ReceiptGlobalNo ASC for THIS specific device
            var queuedItems = await db.OfflineQueue
                .Where(q => q.DeviceId == deviceId && (q.Status == "Queued" || q.Status == "Failed") && q.RetryCount < 10)
                .OrderBy(q => q.ReceiptGlobalNo)
                .Take(10)
                .ToListAsync(cancellationToken);

            if (queuedItems.Count == 0) continue;

            string privateKeyPem;
            try
            {
                privateKeyPem = keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OfflineQueueWorker: Failed to decrypt private key for Device {DeviceId}", deviceId);
                _deviceFailures.AddOrUpdate(deviceId, 1, (_, current) => current + 1);
                continue;
            }

            foreach (var item in queuedItems)
            {
                if (cancellationToken.IsCancellationRequested) break;

                item.Status = "Processing";
                item.LastAttemptAt = DateTime.UtcNow;
                item.RetryCount++;
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    var submitReq = JsonSerializer.Deserialize<SubmitReceiptRequest>(item.SubmitPayloadJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (submitReq == null)
                    {
                        item.Status = "Failed";
                        item.ErrorMessage = "Corrupted JSON payload in queue item";
                        await db.SaveChangesAsync(cancellationToken);
                        continue;
                    }

                    _logger.LogInformation("OfflineQueueWorker: Submitting Invoice {InvoiceNo} (DeviceId {DeviceId}, GlobalNo {GlobalNo}, Attempt {Attempt}) to ZIMRA...",
                        item.InvoiceNo, item.DeviceId, item.ReceiptGlobalNo, item.RetryCount);

                    var resp = await apiClient.SubmitReceiptAsync(submitReq, device.CertificatePem, privateKeyPem);

                    if (resp != null)
                    {
                        item.Status = "Completed";
                        _deviceFailures[deviceId] = 0; // Clear failure count for this device

                        // Update FiscalDocument record
                        var doc = await db.FiscalDocuments.FirstOrDefaultAsync(d => d.InvoiceNo == item.InvoiceNo, cancellationToken);
                        if (doc != null)
                        {
                            doc.ReceiptID = resp.ReceiptID;
                            doc.ServerDate = resp.ServerDate;
                            doc.FdmsSignature = resp.ReceiptServerSignature?.Signature;
                            doc.OperationID = resp.OperationID;
                            doc.FiscalStatus = "Fiscalized";

                            string qrCodeUrl = cryptoService.GenerateQrCodeUrl(
                                device.QrUrl,
                                device.DeviceId,
                                submitReq.Receipt.ReceiptDate,
                                submitReq.Receipt.ReceiptGlobalNo,
                                doc.DeviceSignature);

                            doc.QrCodeUrl = qrCodeUrl;
                            string qrBase64 = cryptoService.GenerateQrCodeBase64Image(qrCodeUrl);

                            // Trigger Webhook Notification
                            var responseDto = new FiscalizedDocumentResponseDto
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
                                QrCodeUrl = qrCodeUrl,
                                QrCodeBase64Image = qrBase64,
                                FiscalStatus = "Fiscalized",
                                OperationID = doc.OperationID ?? string.Empty
                            };

                            await webhookService.DispatchFiscalResponseAsync(responseDto);
                        }

                        await _auditLogger.LogAsync("OfflineQueueSync", "Outbound", 200, invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Invoice {item.InvoiceNo} successfully synchronized with ZIMRA server after offline queueing.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        item.ErrorMessage = "No response from ZIMRA server";
                        if (item.RetryCount >= 10)
                        {
                            item.Status = "DeadLetter";
                            await _auditLogger.LogAsync("OfflineQueueDeadLetter", "Outbound", 500, errorCategory: "Infrastructure", invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Invoice {item.InvoiceNo} exceeded max retries ({item.RetryCount}) and moved to DeadLetter queue.", cancellationToken: cancellationToken);
                            _logger.LogError("OfflineQueueWorker: Queue item {InvoiceNo} (GlobalNo {GlobalNo}) exceeded max retries. Transitioned to DeadLetter.",
                                item.InvoiceNo, item.ReceiptGlobalNo);
                        }
                        else
                        {
                            item.Status = "Queued";
                            await _auditLogger.LogAsync("OfflineQueueRetry", "Outbound", 500, errorCategory: "Infrastructure", invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Offline queue retry failed (Attempt #{item.RetryCount}) for Invoice {item.InvoiceNo}: No response from ZIMRA server.", cancellationToken: cancellationToken);
                        }
                        _deviceFailures.AddOrUpdate(deviceId, 1, (_, current) => current + 1);
                        break; // Stop THIS device's loop to maintain strict sequential ordering per device
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OfflineQueueWorker failed for Invoice {InvoiceNo} on Device {DeviceId}", item.InvoiceNo, item.DeviceId);
                    item.ErrorMessage = ex.Message;
                    if (item.RetryCount >= 10)
                    {
                        item.Status = "DeadLetter";
                        await _auditLogger.LogAsync("OfflineQueueDeadLetter", "Outbound", 500, errorCategory: "Infrastructure", invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Invoice {item.InvoiceNo} exceeded max retries ({item.RetryCount}) and moved to DeadLetter queue.", cancellationToken: cancellationToken);
                        _logger.LogError("OfflineQueueWorker: Queue item {InvoiceNo} (GlobalNo {GlobalNo}) exceeded max retries. Transitioned to DeadLetter.",
                            item.InvoiceNo, item.ReceiptGlobalNo);
                    }
                    else
                    {
                        item.Status = "Queued";
                        await _auditLogger.LogAsync("OfflineQueueRetry", "Outbound", 500, errorCategory: "Infrastructure", invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Offline queue retry failed (Attempt #{item.RetryCount}) for Invoice {item.InvoiceNo}: {ex.Message}", cancellationToken: cancellationToken);
                    }
                    _deviceFailures.AddOrUpdate(deviceId, 1, (_, current) => current + 1);
                    break; // Stop THIS device's loop to maintain strict sequential ordering per device
                }

                await db.SaveChangesAsync(cancellationToken);

                // Rate limit pacing per device (e.g. max 3 requests per second)
                await Task.Delay(350, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private int CalculateBackoffDelay()
    {
        if (_deviceFailures.IsEmpty) return 5;
        int maxFailures = _deviceFailures.Values.DefaultIfEmpty(0).Max();
        if (maxFailures == 0) return 5;
        // Exponential backoff: 2s -> 4s -> 8s -> 16s -> 32s -> 64s
        int backoff = (int)Math.Pow(2, Math.Min(maxFailures, 6));
        return Math.Max(5, backoff);
    }
}

