using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IZReportService
{
    Task<ZReportResponseDto> GenerateZReportAsync(int deviceId, DateTime date, CancellationToken cancellationToken = default);
    Task<ZReportResponseDto> GenerateZReportByFiscalDayAsync(int deviceId, int fiscalDayNo, CancellationToken cancellationToken = default);
    Task<ZReportEntity> GenerateAndSaveZReportPdfAsync(int deviceId, int fiscalDayNo, CancellationToken cancellationToken = default);
    Task<PagedZReportsResultDto> GetZReportsListAsync(int deviceId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<(byte[] pdfBytes, string fileName)> GetZReportPdfBytesAsync(Guid reportId, CancellationToken cancellationToken = default);
}
