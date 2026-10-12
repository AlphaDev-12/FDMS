using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public class TaxMappingConfigItem
{
    public string ErpTaxCode { get; set; } = string.Empty;
    public string ZimraTaxCode { get; set; } = "A";
    public int TaxID { get; set; } = 1;
    public decimal? TaxPercent { get; set; } = 15.00m;
    public string? Description { get; set; }
}

public class TaxMappingConfigFile
{
    public List<TaxMappingConfigItem> Mappings { get; set; } = new();
}

public class TaxMappingResult
{
    public string ZimraTaxCode { get; set; } = "A";
    public int TaxID { get; set; } = 1;
    public decimal? TaxPercent { get; set; } = 15.00m;
}

public interface ITaxCodeMappingService
{
    /// <summary>
    /// Maps an ERP tax code string to ZIMRA TaxCode, TaxID, and TaxPercent.
    /// </summary>
    TaxMappingResult MapErpTaxCode(string erpTaxCode);

    /// <summary>
    /// Applies configured tax codes to all lines and tax summaries in an ERP document request.
    /// </summary>
    void ApplyTaxMappings(ErpInvoiceRequestDto request);

    /// <summary>
    /// Retrieves all configured tax code mappings.
    /// </summary>
    List<TaxMappingConfigItem> GetTaxMappings();

    /// <summary>
    /// Saves updated tax code mappings to configuration file.
    /// </summary>
    Task SaveTaxMappingsAsync(List<TaxMappingConfigItem> mappings, CancellationToken cancellationToken = default);
}

