using FdmsGatewayApi.Data;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsGatewayApi.Services.Implementation;

public class FiscalDayBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FiscalDayBackgroundService> _logger;

    public FiscalDayBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<FiscalDayBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fiscal Day Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var scheduleService = scope.ServiceProvider.GetRequiredService<IFiscalDayScheduleService>();
                var schedule = scheduleService.GetSchedule();

                if (schedule != null && schedule.Enabled)
                {
                    await CheckAndManageFiscalDaysAsync(schedule, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Fiscal Day Background Service.");
            }

            // Check schedule every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }


    private async Task CheckAndManageFiscalDaysAsync(ScheduleDetails schedule, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FdmsDbContext>();
        var dayManager = scope.ServiceProvider.GetRequiredService<IFdmsDayManager>();

        var devices = await db.DeviceConfigs.ToListAsync(cancellationToken);
        DateTime nowUtc = DateTime.UtcNow;
        // Central Africa Time (CAT) is UTC+2
        TimeSpan catOffset = TimeSpan.FromHours(2);
        DateTime nowCat = nowUtc + catOffset;
        TimeSpan currentTimeOfDay = nowCat.TimeOfDay;

        if (TimeSpan.TryParse(schedule.AutoOpenTimeCAT, out TimeSpan openTime) &&
            TimeSpan.TryParse(schedule.AutoCloseTimeCAT, out TimeSpan closeTime))
        {
            foreach (var device in devices)
            {
                // Auto Open check
                if (device.FiscalDayStatus == "FiscalDayClosed" &&
                    currentTimeOfDay >= openTime &&
                    currentTimeOfDay < openTime.Add(TimeSpan.FromMinutes(5)))
                {
                    _logger.LogInformation("Auto-opening fiscal day for device {DeviceId} per schedule {Time}", device.DeviceId, schedule.AutoOpenTimeCAT);
                    try
                    {
                        await dayManager.OpenFiscalDayAsync(device.DeviceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-open fiscal day for device {DeviceId}", device.DeviceId);
                    }
                }

                // Auto Close check (or check max day length)
                bool maxHrsExceeded = device.FiscalDayOpenedAt.HasValue &&
                    (nowCat - device.FiscalDayOpenedAt.Value).TotalHours >= schedule.TaxPayerDayMaxHrs;

                bool scheduledClose = device.FiscalDayStatus == "FiscalDayOpened" &&
                    currentTimeOfDay >= closeTime &&
                    currentTimeOfDay < closeTime.Add(TimeSpan.FromMinutes(5));

                if (device.FiscalDayStatus == "FiscalDayOpened" && (scheduledClose || maxHrsExceeded))
                {
                    _logger.LogInformation("Auto-closing fiscal day for device {DeviceId} (Scheduled: {Scheduled}, MaxHrsExceeded: {MaxHrsExceeded})",
                        device.DeviceId, scheduledClose, maxHrsExceeded);
                    try
                    {
                        await dayManager.CloseFiscalDayAsync(device.DeviceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-close fiscal day for device {DeviceId}", device.DeviceId);
                    }
                }
            }

            CheckCertificateExpirations(devices);
        }
    }

    private void CheckCertificateExpirations(List<Models.Entities.DeviceConfigEntity> devices)
    {
        foreach (var device in devices)
        {
            if (device.CertificateValidTill.HasValue)
            {
                var daysRemaining = (device.CertificateValidTill.Value - DateTime.UtcNow).TotalDays;
                if (daysRemaining <= 30)
                {
                    _logger.LogWarning("ZIMRA CERTIFICATE EXPIRATION WARNING: Device {DeviceId} (Serial: {SerialNo}) certificate expires in {Days:F1} days on {ValidTill}. Execute issueCertificate CSR renewal per ZIMRA §4.3.",
                        device.DeviceId, device.DeviceSerialNo, daysRemaining, device.CertificateValidTill.Value);
                }
            }
        }
    }
}
