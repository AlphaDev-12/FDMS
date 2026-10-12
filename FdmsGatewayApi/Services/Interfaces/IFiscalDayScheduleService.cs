namespace FdmsGatewayApi.Services.Interfaces;

public class FiscalDayScheduleConfig
{
    public ScheduleDetails Schedule { get; set; } = new();
}

public class ScheduleDetails
{
    public string AutoOpenTimeCAT { get; set; } = "06:00";
    public string AutoCloseTimeCAT { get; set; } = "22:00";
    public bool Enabled { get; set; } = false;
    public string TimeZone { get; set; } = "(UTC + 02:00) Harare, Pretoria";
    public int TaxPayerDayMaxHrs { get; set; } = 24;
    public int DayEndNotificationHrs { get; set; } = 2;
}

public interface IFiscalDayScheduleService
{
    ScheduleDetails GetSchedule();
    Task SaveScheduleAsync(ScheduleDetails schedule, CancellationToken cancellationToken = default);
}
