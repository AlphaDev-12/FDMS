using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface ICurrencySplitService
{
    /// <summary>
    /// Inspects an ERP invoice request to determine if split tender across multiple currencies is present.
    /// If single currency, returns a list containing the original request.
    /// If multi-currency, splits the request into currency-bound invoice requests for individual ZIMRA fiscalization.
    /// </summary>
    List<ErpInvoiceRequestDto> SplitIfNeeded(ErpInvoiceRequestDto request);
}
