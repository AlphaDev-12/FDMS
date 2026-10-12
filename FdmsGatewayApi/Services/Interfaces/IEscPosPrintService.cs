using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IEscPosPrintService
{
    ReceiptPrintLayoutDto GeneratePrintLayout(
        ErpInvoiceRequestDto request,
        DeviceConfigEntity device,
        FiscalDocumentEntity document,
        string qrCodeUrl);

    string GenerateEscPosStreamBase64(ReceiptPrintLayoutDto layout, int paperWidthMm = 80);
}
