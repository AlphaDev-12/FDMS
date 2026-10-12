using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IPdfReportGenerator
{
    byte[] GenerateZReportPdf(ZReportResponseDto report);
    byte[] GenerateXReportPdf(XReportResponseDto report);
}
