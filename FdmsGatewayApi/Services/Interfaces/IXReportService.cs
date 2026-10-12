using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IXReportService
{
    Task<XReportResponseDto> GenerateXReportAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<(byte[] pdfBytes, string fileName)> GenerateXReportPdfAsync(int deviceId, CancellationToken cancellationToken = default);
}
