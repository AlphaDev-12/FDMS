using System.Text.Json;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class FiscalDayScheduleService : IFiscalDayScheduleService
{
    private readonly string _filePath;
    private readonly ILogger<FiscalDayScheduleService> _logger;
    private readonly object _lock = new();

    public FiscalDayScheduleService(ILogger<FiscalDayScheduleService> logger, IHostEnvironment env)
    {
        _logger = logger;
        _filePath = Path.Combine(env.ContentRootPath, "Config", "fiscal_day_schedule.json");
    }

    public ScheduleDetails GetSchedule()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var config = JsonSerializer.Deserialize<FiscalDayScheduleConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (config?.Schedule != null)
                    {
                        return config.Schedule;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read fiscal_day_schedule.json from {FilePath}", _filePath);
            }

            return new ScheduleDetails();
        }
    }

    public async Task SaveScheduleAsync(ScheduleDetails schedule, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new FiscalDayScheduleConfig { Schedule = schedule };
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_filePath, json, cancellationToken);
            _logger.LogInformation("Successfully updated fiscal day schedule configuration at {FilePath}", _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write fiscal day schedule to {FilePath}", _filePath);
            throw;
        }
    }
}
