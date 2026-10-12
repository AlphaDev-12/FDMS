using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class FdmsDayManager : IFdmsDayManager
{
    private readonly FdmsDbContext _db;
    private readonly IFdmsApiClient _apiClient;
    private readonly IFdmsCryptoService _cryptoService;
    private readonly IEncryptedKeyStore _keyStore;
    private readonly IZReportService _zReportService;
    private readonly IPassPhraseProvider _passPhraseProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<FdmsDayManager> _logger;

    public FdmsDayManager(
        FdmsDbContext db,
        IFdmsApiClient apiClient,
        IFdmsCryptoService cryptoService,
        IEncryptedKeyStore keyStore,
        IZReportService zReportService,
        IConfiguration config,
        ILogger<FdmsDayManager> logger,
        IPassPhraseProvider? passPhraseProvider = null)
    {
        _db = db;
        _apiClient = apiClient;
        _cryptoService = cryptoService;
        _keyStore = keyStore;
        _zReportService = zReportService;
        _config = config;
        _logger = logger;
        _passPhraseProvider = passPhraseProvider ?? new PassPhraseProvider(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<PassPhraseProvider>.Instance);
    }

    public async Task<DeviceConfigEntity> SyncConfigAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceOrThrowAsync(deviceId, cancellationToken);
        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);

        var configResp = await _apiClient.GetConfigAsync(new GetConfigRequest { DeviceID = deviceId }, device.CertificatePem, privateKeyPem, cancellationToken);
        if (configResp != null)
        {
            device.TaxPayerName = configResp.TaxPayerName;
            device.TaxPayerTIN = configResp.TaxPayerTIN;
            device.VatNumber = configResp.VatNumber;
            device.DeviceBranchName = configResp.DeviceBranchName;
            device.QrUrl = configResp.QrUrl;
            device.DeviceOperatingMode = configResp.DeviceOperatingMode;
            device.TaxPayerDayMaxHrs = configResp.TaxPayerDayMaxHrs;

            await _db.SaveChangesAsync(cancellationToken);
        }

        return device;
    }

    public async Task<DeviceConfigEntity> OpenFiscalDayAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceOrThrowAsync(deviceId, cancellationToken);

        if (device.FiscalDayStatus == "FiscalDayOpened")
        {
            return device;
        }

        if (device.FiscalDayStatus == "FiscalDayCloseFailed")
        {
            throw new InvalidOperationException($"Cannot open a new fiscal day for Device {deviceId} because the previous fiscal day closure failed (FiscalDayCloseFailed). Please retry closing the fiscal day first.");
        }

        // Try pre-opening config sync to fetch latest ZIMRA rules/parameters
        try
        {
            await SyncConfigAsync(deviceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pre-open SyncConfig call failed for Device {DeviceId}. Proceeding with stored device configuration.", deviceId);
        }

        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);

        int nextDayNo = device.CurrentFiscalDayNo + 1;
        DateTime now = DateTime.Now;

        var openReq = new OpenDayRequest
        {
            DeviceID = deviceId,
            FiscalDayOpened = now,
            FiscalDayNo = nextDayNo
        };

        OpenDayResponse? resp = null;
        try
        {
            resp = await _apiClient.OpenDayAsync(openReq, device.CertificatePem, privateKeyPem, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote ZIMRA OpenDay call failed. Proceeding with local Fiscal Day Open for Device {DeviceId}", deviceId);
        }

        device.CurrentFiscalDayNo = resp?.FiscalDayNo ?? nextDayNo;
        device.CurrentReceiptCounter = 0; // Reset daily counter
        device.FiscalDayStatus = "FiscalDayOpened";
        device.FiscalDayOpenedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Opened Fiscal Day {DayNo} for Device {DeviceId}", device.CurrentFiscalDayNo, deviceId);

        return device;
    }

    public async Task<DeviceConfigEntity> CloseFiscalDayAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceOrThrowAsync(deviceId, cancellationToken);

        if (device.FiscalDayStatus != "FiscalDayOpened" && device.FiscalDayStatus != "FiscalDayCloseFailed")
        {
            throw new InvalidOperationException($"Cannot close fiscal day for Device {deviceId} because current status is '{device.FiscalDayStatus}'. Fiscal day must be in 'FiscalDayOpened' or 'FiscalDayCloseFailed' status.");
        }

        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);

        // Fetch documents issued in this fiscal day to calculate Z-Report counters (device-scoped)
        var dayDocs = await _db.FiscalDocuments
            .Where(d => d.DeviceId == deviceId && d.FiscalDayNo == device.CurrentFiscalDayNo && d.FiscalStatus == "Fiscalized")
            .ToListAsync(cancellationToken);

        var counters = new List<FiscalDayCounterDto>();

        var saleByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var saleTaxByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var creditNoteByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var creditNoteTaxByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var debitNoteByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var debitNoteTaxByTaxMap = new Dictionary<(string Currency, int TaxID, decimal? TaxPercent), decimal>();
        var balanceByMoneyTypeMap = new Dictionary<(string Currency, string MoneyType), decimal>();

        foreach (var doc in dayDocs)
        {
            ErpInvoiceRequestDto? req = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(doc.RawPayloadJson))
                {
                    req = System.Text.Json.JsonSerializer.Deserialize<ErpInvoiceRequestDto>(doc.RawPayloadJson);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse RawPayloadJson for document {InvoiceNo} during CloseFiscalDay", doc.InvoiceNo);
            }

            string currency = doc.Currency.Trim().ToUpperInvariant();
            string receiptType = doc.ReceiptType.Trim().ToUpperInvariant();

            if (req != null && req.Taxes != null && req.Taxes.Count > 0)
            {
                foreach (var tax in req.Taxes)
                {
                    var key = (Currency: currency, TaxID: tax.TaxID, TaxPercent: tax.TaxPercent);

                    if (string.Equals(receiptType, "FiscalInvoice", StringComparison.OrdinalIgnoreCase))
                    {
                        saleByTaxMap[key] = saleByTaxMap.GetValueOrDefault(key) + tax.SalesAmountWithTax;
                        saleTaxByTaxMap[key] = saleTaxByTaxMap.GetValueOrDefault(key) + tax.TaxAmount;
                    }
                    else if (string.Equals(receiptType, "CreditNote", StringComparison.OrdinalIgnoreCase))
                    {
                        creditNoteByTaxMap[key] = creditNoteByTaxMap.GetValueOrDefault(key) - Math.Abs(tax.SalesAmountWithTax);
                        creditNoteTaxByTaxMap[key] = creditNoteTaxByTaxMap.GetValueOrDefault(key) - Math.Abs(tax.TaxAmount);
                    }
                    else if (string.Equals(receiptType, "DebitNote", StringComparison.OrdinalIgnoreCase))
                    {
                        debitNoteByTaxMap[key] = debitNoteByTaxMap.GetValueOrDefault(key) + Math.Abs(tax.SalesAmountWithTax);
                        debitNoteTaxByTaxMap[key] = debitNoteTaxByTaxMap.GetValueOrDefault(key) + Math.Abs(tax.TaxAmount);
                    }
                }
            }
            else
            {
                var key = (Currency: currency, TaxID: 1, TaxPercent: (decimal?)15.00m);
                if (string.Equals(receiptType, "FiscalInvoice", StringComparison.OrdinalIgnoreCase))
                {
                    saleByTaxMap[key] = saleByTaxMap.GetValueOrDefault(key) + doc.TotalAmount;
                }
                else if (string.Equals(receiptType, "CreditNote", StringComparison.OrdinalIgnoreCase))
                {
                    creditNoteByTaxMap[key] = creditNoteByTaxMap.GetValueOrDefault(key) - Math.Abs(doc.TotalAmount);
                }
                else if (string.Equals(receiptType, "DebitNote", StringComparison.OrdinalIgnoreCase))
                {
                    debitNoteByTaxMap[key] = debitNoteByTaxMap.GetValueOrDefault(key) + Math.Abs(doc.TotalAmount);
                }
            }

            if (req != null && req.Payments != null && req.Payments.Count > 0)
            {
                foreach (var payment in req.Payments)
                {
                    string mType = string.IsNullOrWhiteSpace(payment.MoneyTypeCode) ? "CASH" : payment.MoneyTypeCode.Trim().ToUpperInvariant();
                    string pCurr = string.IsNullOrWhiteSpace(payment.PaymentCurrency) ? currency : payment.PaymentCurrency.Trim().ToUpperInvariant();
                    var pKey = (Currency: pCurr, MoneyType: mType);
                    balanceByMoneyTypeMap[pKey] = balanceByMoneyTypeMap.GetValueOrDefault(pKey) + payment.PaymentAmount;
                }
            }
            else
            {
                var pKey = (Currency: currency, MoneyType: "CASH");
                balanceByMoneyTypeMap[pKey] = balanceByMoneyTypeMap.GetValueOrDefault(pKey) + doc.TotalAmount;
            }
        }

        foreach (var (key, val) in saleByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "SaleByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in saleTaxByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "SaleTaxByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in creditNoteByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "CreditNoteByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in creditNoteTaxByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "CreditNoteTaxByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in debitNoteByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "DebitNoteByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in debitNoteTaxByTaxMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "DebitNoteTaxByTax", FiscalCounterCurrency = key.Currency, FiscalCounterTaxID = key.TaxID, FiscalCounterTaxPercent = key.TaxPercent, FiscalCounterValue = val });
        }
        foreach (var (key, val) in balanceByMoneyTypeMap)
        {
            if (val != 0)
                counters.Add(new FiscalDayCounterDto { FiscalCounterType = "BalanceByMoneyType", FiscalCounterCurrency = key.Currency, FiscalCounterMoneyType = key.MoneyType, FiscalCounterValue = val });
        }

        // Calculate Fiscal Day Device Signature (Section 13.3.1)
        DateTime dayOpenedDate = device.FiscalDayOpenedAt ?? DateTime.Today;
        string dayHash = _cryptoService.ComputeFiscalDayHash(device.DeviceId, device.CurrentFiscalDayNo, dayOpenedDate, counters);
        var daySig = _cryptoService.SignFiscalDay(dayHash, privateKeyPem);

        var closeReq = new CloseDayRequest
        {
            DeviceID = deviceId,
            FiscalDayNo = device.CurrentFiscalDayNo,
            ReceiptCounter = device.CurrentReceiptCounter,
            FiscalDayCounters = counters,
            FiscalDayDeviceSignature = daySig
        };

        bool remoteSuccess = true;
        try
        {
            var closeResp = await _apiClient.CloseDayAsync(closeReq, device.CertificatePem, privateKeyPem, cancellationToken);
            if (closeResp == null)
            {
                remoteSuccess = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remote ZIMRA CloseDay call failed. Marking status as FiscalDayCloseFailed for Device {DeviceId}", deviceId);
            remoteSuccess = false;
        }

        if (remoteSuccess)
        {
            device.FiscalDayStatus = "FiscalDayClosed";
        }
        else
        {
            device.FiscalDayStatus = "FiscalDayCloseFailed";
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Auto-generate and save PDF Z-Report upon closing fiscal day
        try
        {
            await _zReportService.GenerateAndSaveZReportPdfAsync(deviceId, device.CurrentFiscalDayNo, cancellationToken);
            _logger.LogInformation("Successfully auto-generated Z-Report PDF for Device {DeviceId}, Fiscal Day #{DayNo}", deviceId, device.CurrentFiscalDayNo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-generate PDF Z-Report during Fiscal Day Close for Device {DeviceId}", deviceId);
        }

        _logger.LogInformation("Fiscal Day {DayNo} for Device {DeviceId} closure completed with status {Status}", device.CurrentFiscalDayNo, deviceId, device.FiscalDayStatus);
        return device;
    }

    public async Task<DeviceConfigEntity> GetStatusAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceOrThrowAsync(deviceId, cancellationToken);
        string passPhrase = _passPhraseProvider.GetPassPhrase();
        string privateKeyPem = _keyStore.DecryptKey(device.EncryptedPrivateKeyPem, passPhrase);

        var statusResp = await _apiClient.GetStatusAsync(new GetStatusRequest { DeviceID = deviceId }, device.CertificatePem, privateKeyPem, cancellationToken);
        if (statusResp != null)
        {
            if (!string.IsNullOrWhiteSpace(statusResp.FiscalDayStatus))
            {
                device.FiscalDayStatus = statusResp.FiscalDayStatus;
            }
            if (statusResp.LastReceiptGlobalNo.HasValue)
            {
                device.CurrentReceiptGlobalNo = Math.Max(device.CurrentReceiptGlobalNo, statusResp.LastReceiptGlobalNo.Value);
            }
            if (statusResp.LastFiscalDayNo.HasValue)
            {
                device.CurrentFiscalDayNo = Math.Max(device.CurrentFiscalDayNo, statusResp.LastFiscalDayNo.Value);
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        return device;
    }

    private async Task<DeviceConfigEntity> GetDeviceOrThrowAsync(int deviceId, CancellationToken cancellationToken)
    {
        var device = await _db.DeviceConfigs.FirstOrDefaultAsync(d => d.DeviceId == deviceId, cancellationToken);
        if (device == null)
        {
            throw new KeyNotFoundException($"Device with ID {deviceId} is not registered in the system.");
        }
        return device;
    }
}
