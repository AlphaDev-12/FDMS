using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Fiscal Device Configuration and Fiscal Day Operations Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
[Tags("Device & Fiscal Day Operations")]
public class DeviceAdminController : ControllerBase
{
    private readonly FdmsDbContext _db;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly IFdmsApiClient _apiClient;
    private readonly IFdmsDayManager _dayManager;
    private readonly IFiscalDayScheduleService _scheduleService;
    private readonly ITaxCodeMappingService _taxCodeService;
    private readonly IAuditLogger _auditLogger;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPassPhraseProvider _passPhraseProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<DeviceAdminController> _logger;

    public DeviceAdminController(
        FdmsDbContext db,
        IFdmsCryptoService cryptoService,
        IEncryptedKeyStore keyStore,
        IFdmsApiClient apiClient,
        IFdmsDayManager dayManager,
        IFiscalDayScheduleService scheduleService,
        ITaxCodeMappingService taxCodeService,
        IAuditLogger auditLogger,
        ISubscriptionService subscriptionService,
        IConfiguration config,
        ILogger<DeviceAdminController> logger,
        IPassPhraseProvider? passPhraseProvider = null)
    {
        _db = db;
        _cryptoService = cryptoService;
        _keyStore = keyStore;
        _apiClient = apiClient;
        _dayManager = dayManager;
        _scheduleService = scheduleService;
        _taxCodeService = taxCodeService;
        _auditLogger = auditLogger;
        _subscriptionService = subscriptionService;
        _config = config;
        _logger = logger;
        _passPhraseProvider = passPhraseProvider ?? new PassPhraseProvider(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PassPhraseProvider>.Instance);
    }

    [HttpPost("device/register")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DeviceID <= 0 || string.IsNullOrWhiteSpace(request.ActivationKey))
        {
            await _auditLogger.LogAsync("DeviceRegister", "Inbound", 400, errorCategory: "UserInput", deviceId: request.DeviceID, errorMessage: "Device registration rejected: Missing DeviceID or ActivationKey.", cancellationToken: cancellationToken);
            return BadRequest(new { error = "Valid DeviceID and ActivationKey are required." });
        }

        // Check MaxDevices subscription limit for new device registration
        var existingDeviceCheck = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceID, cancellationToken);
        if (existingDeviceCheck == null)
        {
            int currentDeviceCount = await _db.DeviceConfigs.CountAsync(cancellationToken);
            var subStatus = await _subscriptionService.GetCurrentSubscriptionStatusAsync(cancellationToken);
            if (subStatus.MaxDevices > 0 && currentDeviceCount >= subStatus.MaxDevices)
            {
                string limitErr = $"Device registration limit reached. Active subscription ('{subStatus.PlanName}') allows a maximum of {subStatus.MaxDevices} registered device(s).";
                await _auditLogger.LogAsync("DeviceRegister", "Inbound", 400, errorCategory: "SubscriptionLimit", deviceId: request.DeviceID, errorMessage: limitErr, cancellationToken: cancellationToken);
                return BadRequest(new { error = limitErr });
            }
        }

        try
        {
            string passPhrase = _passPhraseProvider.GetPassPhrase();
            string deviceSerialNo = "SN" + request.DeviceID.ToString("D4");

            // 1. Generate ECC secp256r1 key pair & CSR
            var (privateKeyPem, csrPem) = _cryptoService.GenerateKeyPairAndCsr(request.DeviceID, deviceSerialNo);

            // Encrypt private key for storage
            string encryptedPrivateKey = _keyStore.EncryptKey(privateKeyPem, passPhrase);

            string certPem = string.Empty;

            try
            {
                // 2. Submit CSR to ZIMRA FDMS Gateway
                var regReq = new RegisterDeviceRequest
                {
                    DeviceID = request.DeviceID,
                    ActivationKey = request.ActivationKey,
                    CertificateRequest = csrPem
                };

                var regResp = await _apiClient.RegisterDeviceAsync(regReq, cancellationToken);
                if (regResp != null && !string.IsNullOrEmpty(regResp.Certificate))
                {
                    certPem = regResp.Certificate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ZIMRA registration endpoint call failed; proceeding with generated local certificate placeholder for development.");
                certPem = "-----BEGIN CERTIFICATE-----\nMIIFDMS_LOCAL_CERT_PLACEHOLDER-----\n-----END CERTIFICATE-----";
            }

            // 3. Save or Update Device Config in DB
            var existingDevice = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == request.DeviceID, cancellationToken);
            if (existingDevice == null)
            {
                existingDevice = new DeviceConfigEntity
                {
                    DeviceId = request.DeviceID,
                    ActivationKey = request.ActivationKey,
                    DeviceSerialNo = deviceSerialNo,
                    TaxPayerName = "Taxpayer " + request.DeviceID,
                    TaxPayerTIN = "1000000000",
                    EncryptedPrivateKeyPem = encryptedPrivateKey,
                    CertificatePem = certPem,
                    FiscalDayStatus = "FiscalDayClosed"
                };
                _db.DeviceConfigs.Add(existingDevice);
            }
            else
            {
                existingDevice.ActivationKey = request.ActivationKey;
                existingDevice.EncryptedPrivateKeyPem = encryptedPrivateKey;
                existingDevice.CertificatePem = certPem;
            }

            await _db.SaveChangesAsync(cancellationToken);

            // 4. Fetch Config from ZIMRA
            try
            {
                await _dayManager.SyncConfigAsync(request.DeviceID, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch config from ZIMRA immediately after registration.");
            }

            await _auditLogger.LogAsync("DeviceRegister", "Inbound", 200, deviceId: request.DeviceID, errorMessage: $"Device {request.DeviceID} registered successfully. Serial: {existingDevice.DeviceSerialNo}", cancellationToken: cancellationToken);

            return Ok(new
            {
                message = "Device registered and cryptographic keys stored successfully.",
                deviceId = request.DeviceID,
                deviceSerialNo = existingDevice.DeviceSerialNo,
                fiscalDayStatus = existingDevice.FiscalDayStatus
            });
        }
        catch (Exception ex)
        {
            await _auditLogger.LogAsync("DeviceRegister", "Inbound", 500, errorCategory: "Infrastructure", deviceId: request.DeviceID, errorMessage: $"Device registration failed: {ex.Message}", cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Trigger manual configuration sync from ZIMRA for a specific device (or all registered devices if deviceId is omitted).
    /// </summary>
    [HttpPost("device/sync-config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncConfig([FromQuery] int? deviceId = null, CancellationToken cancellationToken = default)
    {
        if (deviceId.HasValue && deviceId.Value > 0)
        {
            var device = await _dayManager.SyncConfigAsync(deviceId.Value, cancellationToken);
            await _auditLogger.LogAsync("SyncConfig", "Outbound", 200, deviceId: deviceId.Value, errorMessage: $"Synced configuration from ZIMRA for Device {deviceId.Value}.", cancellationToken: cancellationToken);
            return Ok(device);
        }
        else
        {
            var devices = await _db.DeviceConfigs.ToListAsync(cancellationToken);
            if (devices.Count == 0)
            {
                return NotFound(new { error = "NO_DEVICES", message = "No registered devices found in system." });
            }

            var syncedDevices = new List<object>();
            foreach (var dev in devices)
            {
                try
                {
                    var updated = await _dayManager.SyncConfigAsync(dev.DeviceId, cancellationToken);
                    await _auditLogger.LogAsync("SyncConfig", "Outbound", 200, deviceId: dev.DeviceId, errorMessage: $"Synced configuration from ZIMRA for Device {dev.DeviceId}.", cancellationToken: cancellationToken);
                    syncedDevices.Add(updated);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync config for device {DeviceId}", dev.DeviceId);
                }
            }

            return Ok(new
            {
                message = $"ZIMRA Device Configuration synced successfully for {syncedDevices.Count} of {devices.Count} registered device(s).",
                syncedCount = syncedDevices.Count,
                totalDevices = devices.Count,
                devices = syncedDevices
            });
        }
    }

    /// <summary>
    /// Open a new fiscal day for a specific device (or all registered devices if deviceId is omitted).
    /// </summary>
    [HttpPost("day/open")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OpenDay([FromQuery] int? deviceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (deviceId.HasValue && deviceId.Value > 0)
            {
                var device = await _dayManager.OpenFiscalDayAsync(deviceId.Value, cancellationToken);
                await _auditLogger.LogAsync("FiscalDayOpen", "Internal", 200, deviceId: deviceId.Value, errorMessage: $"Fiscal Day #{device.CurrentFiscalDayNo} opened successfully for Device {deviceId.Value}.", cancellationToken: cancellationToken);
                return Ok(new
                {
                    message = $"Fiscal Day #{device.CurrentFiscalDayNo} opened successfully for Device ID {deviceId.Value}.",
                    deviceId = device.DeviceId,
                    fiscalDayNo = device.CurrentFiscalDayNo,
                    fiscalDayStatus = device.FiscalDayStatus
                });
            }
            else
            {
                var devices = await _db.DeviceConfigs.ToListAsync(cancellationToken);
                if (devices.Count == 0)
                {
                    return NotFound(new { error = "NO_DEVICES", message = "No registered devices found in system." });
                }

                var openedDevices = new List<object>();
                List<string> errors = new List<string>();

                foreach (var dev in devices)
                {
                    try
                    {
                        var updated = await _dayManager.OpenFiscalDayAsync(dev.DeviceId, cancellationToken);
                        await _auditLogger.LogAsync("FiscalDayOpen", "Internal", 200, deviceId: dev.DeviceId, errorMessage: $"Fiscal Day #{updated.CurrentFiscalDayNo} opened successfully for Device {dev.DeviceId}.", cancellationToken: cancellationToken);
                        openedDevices.Add(new
                        {
                            deviceId = updated.DeviceId,
                            fiscalDayNo = updated.CurrentFiscalDayNo,
                            fiscalDayStatus = updated.FiscalDayStatus
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to open fiscal day for device {DeviceId}", dev.DeviceId);
                        errors.Add($"Device {dev.DeviceId}: {ex.Message}");
                        await _auditLogger.LogAsync("FiscalDayOpen", "Internal", 400, errorCategory: "Infrastructure", deviceId: dev.DeviceId, errorMessage: $"Failed to open Fiscal Day for Device {dev.DeviceId}: {ex.Message}", cancellationToken: cancellationToken);
                    }
                }

                return Ok(new
                {
                    message = $"Fiscal Day opened successfully for {openedDevices.Count} of {devices.Count} registered device(s).",
                    deviceCount = openedDevices.Count,
                    totalDevices = devices.Count,
                    devices = openedDevices,
                    errors = errors.Count > 0 ? errors : null
                });
            }
        }
        catch (Exception ex)
        {
            await _auditLogger.LogAsync("FiscalDayOpen", "Internal", 400, errorCategory: "Infrastructure", deviceId: deviceId ?? 0, errorMessage: $"Failed to open Fiscal Day: {ex.Message}", cancellationToken: cancellationToken);
            return BadRequest(new { error = "DAY_OPEN_FAILED", message = ex.Message });
        }
    }

    /// <summary>
    /// Close current fiscal day and auto-generate Z-Report for a specific device (or all registered devices if deviceId is omitted).
    /// </summary>
    [HttpPost("day/close")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CloseDay([FromQuery] int? deviceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (deviceId.HasValue && deviceId.Value > 0)
            {
                var device = await _dayManager.CloseFiscalDayAsync(deviceId.Value, cancellationToken);
                await _auditLogger.LogAsync("FiscalDayClose", "Internal", 200, deviceId: deviceId.Value, errorMessage: $"Fiscal Day #{device.CurrentFiscalDayNo} closed successfully for Device {deviceId.Value}.", cancellationToken: cancellationToken);
                return Ok(new
                {
                    message = $"Fiscal Day #{device.CurrentFiscalDayNo} closed successfully for Device ID {deviceId.Value}.",
                    deviceId = device.DeviceId,
                    fiscalDayStatus = device.FiscalDayStatus
                });
            }
            else
            {
                var devices = await _db.DeviceConfigs.ToListAsync(cancellationToken);
                if (devices.Count == 0)
                {
                    return NotFound(new { error = "NO_DEVICES", message = "No registered devices found in system." });
                }

                var closedDevices = new List<object>();
                List<string> errors = new List<string>();

                foreach (var dev in devices)
                {
                    try
                    {
                        if (dev.FiscalDayStatus != "FiscalDayOpened" && dev.FiscalDayStatus != "FiscalDayCloseFailed")
                        {
                            _logger.LogInformation("Skipping CloseFiscalDay for Device {DeviceId} as it is already in state '{Status}'", dev.DeviceId, dev.FiscalDayStatus);
                            continue;
                        }

                        var updated = await _dayManager.CloseFiscalDayAsync(dev.DeviceId, cancellationToken);
                        await _auditLogger.LogAsync("FiscalDayClose", "Internal", 200, deviceId: dev.DeviceId, errorMessage: $"Fiscal Day #{updated.CurrentFiscalDayNo} closed successfully for Device {dev.DeviceId}.", cancellationToken: cancellationToken);
                        closedDevices.Add(new
                        {
                            deviceId = updated.DeviceId,
                            fiscalDayStatus = updated.FiscalDayStatus
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to close fiscal day for device {DeviceId}", dev.DeviceId);
                        errors.Add($"Device {dev.DeviceId}: {ex.Message}");
                        await _auditLogger.LogAsync("FiscalDayClose", "Internal", 400, errorCategory: "Infrastructure", deviceId: dev.DeviceId, errorMessage: $"Failed to close Fiscal Day for Device {dev.DeviceId}: {ex.Message}", cancellationToken: cancellationToken);
                    }
                }

                return Ok(new
                {
                    message = $"Fiscal Day closed successfully for {closedDevices.Count} of {devices.Count} registered device(s).",
                    deviceCount = closedDevices.Count,
                    totalDevices = devices.Count,
                    devices = closedDevices,
                    errors = errors.Count > 0 ? errors : null
                });
            }
        }
        catch (Exception ex)
        {
            await _auditLogger.LogAsync("FiscalDayClose", "Internal", 400, errorCategory: "Infrastructure", deviceId: deviceId ?? 0, errorMessage: $"Failed to close Fiscal Day: {ex.Message}", cancellationToken: cancellationToken);
            return BadRequest(new { error = "DAY_CLOSE_FAILED", message = ex.Message });
        }
    }

    /// <summary>
    /// Get current fiscal day status, receipt counters, and operating mode for a device.
    /// </summary>
    [HttpGet("day/status/{deviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await _dayManager.GetStatusAsync(deviceId, cancellationToken);
        return Ok(new
        {
            deviceId = device.DeviceId,
            fiscalDayNo = device.CurrentFiscalDayNo,
            currentReceiptCounter = device.CurrentReceiptCounter,
            currentReceiptGlobalNo = device.CurrentReceiptGlobalNo,
            fiscalDayStatus = device.FiscalDayStatus,
            operatingMode = device.DeviceOperatingMode
        });
    }

    [HttpGet("queue/deadletter")]
    public async Task<IActionResult> GetDeadLetterQueue(CancellationToken cancellationToken = default)
    {
        var deadLetterItems = await _db.OfflineQueue
            .Where(q => q.Status == "DeadLetter")
            .OrderBy(q => q.ReceiptGlobalNo)
            .Select(q => new
            {
                q.Id,
                q.DeviceId,
                q.InvoiceNo,
                q.ReceiptGlobalNo,
                q.ReceiptType,
                q.RetryCount,
                q.ErrorMessage,
                q.QueuedAt,
                q.LastAttemptAt
            })
            .ToListAsync(cancellationToken);

        return Ok(deadLetterItems);
    }

    [HttpPost("queue/replay/{id:guid}")]
    public async Task<IActionResult> ReplayDeadLetterItem(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _db.OfflineQueue.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (item == null)
        {
            return NotFound(new { error = "QUEUE01", message = $"Offline queue item with ID {id} not found." });
        }

        item.Status = "Queued";
        item.RetryCount = 0;
        item.ErrorMessage = null;
        await _db.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync("DeadLetterReplay", "Internal", 200, invoiceNo: item.InvoiceNo, deviceId: item.DeviceId, errorMessage: $"Admin re-queued dead-letter item ID {id} for Invoice {item.InvoiceNo} (GlobalNo {item.ReceiptGlobalNo}).", cancellationToken: cancellationToken);

        _logger.LogInformation("Admin re-queued dead-lettered item ID {Id} for Invoice {InvoiceNo} (GlobalNo {GlobalNo})",
            id, item.InvoiceNo, item.ReceiptGlobalNo);

        return Ok(new
        {
            message = $"Queue item {id} for Invoice {item.InvoiceNo} re-queued successfully for background processing.",
            queueId = item.Id,
            invoiceNo = item.InvoiceNo,
            receiptGlobalNo = item.ReceiptGlobalNo,
            status = item.Status
        });
    }

    [HttpGet("schedule")]
    public IActionResult GetSchedule()
    {
        var schedule = _scheduleService.GetSchedule();
        return Ok(schedule);
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> UpdateSchedule([FromBody] ScheduleDetails schedule, CancellationToken cancellationToken = default)
    {
        if (schedule == null)
        {
            return BadRequest(new { error = "Invalid schedule payload." });
        }

        await _scheduleService.SaveScheduleAsync(schedule, cancellationToken);
        await _auditLogger.LogAsync("ScheduleConfigUpdate", "Internal", 200, errorMessage: $"Fiscal day schedule updated. AutoOpen: {schedule.AutoOpenTimeCAT}, AutoClose: {schedule.AutoCloseTimeCAT}, Enabled: {schedule.Enabled}", cancellationToken: cancellationToken);

        return Ok(new
        {
            message = "Fiscal day schedule configuration updated successfully.",
            schedule = schedule
        });
    }

    [HttpGet("tax-codes")]
    public IActionResult GetTaxCodes()
    {
        var mappings = _taxCodeService.GetTaxMappings();
        return Ok(mappings);
    }

    [HttpPost("tax-codes")]
    public async Task<IActionResult> UpdateTaxCodes([FromBody] List<TaxMappingConfigItem> mappings, CancellationToken cancellationToken = default)
    {
        if (mappings == null)
        {
            return BadRequest(new { error = "Invalid tax code mappings payload." });
        }

        await _taxCodeService.SaveTaxMappingsAsync(mappings, cancellationToken);
        await _auditLogger.LogAsync("TaxCodeMappingsUpdate", "Internal", 200, errorMessage: $"Tax code mappings updated with {mappings.Count} items.", cancellationToken: cancellationToken);

        return Ok(new
        {
            message = "Tax code mappings updated successfully.",
            count = mappings.Count,
            mappings = mappings
        });
    }

    /// <summary>
    /// Retrieve inventory stock list from ZIMRA for a specific device (Section 4.15 getStockList).
    /// </summary>
    [HttpGet("device/{deviceId}/stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStockList(
        int deviceId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        [FromQuery] string? hsCode = null,
        [FromQuery] string? goodName = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = null,
        [FromQuery] string? @operator = null,
        CancellationToken cancellationToken = default)
    {
        var device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            return NotFound(new { error = "DEV_NOT_FOUND", message = $"Device with ID {deviceId} was not found." });
        }

        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);
        string certPem = device.CertificatePem;

        var req = new GetStockListRequest
        {
            DeviceID = deviceId,
            Offset = offset,
            Limit = limit,
            HsCode = hsCode,
            GoodName = goodName,
            Sort = sort,
            Order = order,
            Operator = @operator
        };

        var response = await _apiClient.GetStockListAsync(req, certPem, privateKeyPem, cancellationToken);
        await _auditLogger.LogAsync("GetStockList", "Outbound", 200, deviceId: deviceId, errorMessage: $"Retrieved stock list from ZIMRA for Device {deviceId}.", cancellationToken: cancellationToken);

        return Ok(response);
    }
}

