using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IFdmsValidationService
{
    void ValidateInvoiceRequest(ErpInvoiceRequestDto dto, DeviceConfigEntity deviceConfig);
    void ValidateCreditNoteRequest(ErpCreditNoteRequestDto dto, DeviceConfigEntity deviceConfig, FiscalDocumentEntity? originalDoc);
    void ValidateDebitNoteRequest(ErpDebitNoteRequestDto dto, DeviceConfigEntity deviceConfig, FiscalDocumentEntity? originalDoc);
}
