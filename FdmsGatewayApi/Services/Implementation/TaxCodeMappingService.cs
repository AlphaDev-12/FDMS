using System.Text.Json;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class TaxCodeMappingService : ITaxCodeMappingService
{
    private readonly string _configFilePath;
    private readonly ILogger<TaxCodeMappingService> _logger;

    public TaxCodeMappingService(ILogger<TaxCodeMappingService> logger, IHostEnvironment env)
    {
        _logger = logger;
        _configFilePath = Path.Combine(env.ContentRootPath, "Config", "tax_code_mappings.json");
    }

    public List<TaxMappingConfigItem> GetTaxMappings()
    {
        return LoadMappings();
    }

    public async Task SaveTaxMappingsAsync(List<TaxMappingConfigItem> mappings, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = new TaxMappingConfigFile { Mappings = mappings ?? new() };
            string dir = Path.GetDirectoryName(_configFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);
            _logger.LogInformation("Successfully updated tax code mappings file at {FilePath}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write tax code mappings to {FilePath}", _configFilePath);
            throw;
        }
    }

    private List<TaxMappingConfigItem> LoadMappings()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var config = JsonSerializer.Deserialize<TaxMappingConfigFile>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config?.Mappings != null && config.Mappings.Count > 0)
                    {
                        return config.Mappings;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tax code mapping file at {FilePath}", _configFilePath);
        }

        // Default fallback if file missing or empty
        return GetDefaultMappings();
    }

    private static List<TaxMappingConfigItem> GetDefaultMappings()
    {
        return new List<TaxMappingConfigItem>
        {
            new() { ErpTaxCode = "15%_VAT", ZimraTaxCode = "A", TaxID = 1, TaxPercent = 15.00m, Description = "Standard VAT at 15%" },
            new() { ErpTaxCode = "0%_NON_VAT", ZimraTaxCode = "B", TaxID = 2, TaxPercent = 0.00m, Description = "Non-VAT sale at 0%" },
            new() { ErpTaxCode = "EXEMPT", ZimraTaxCode = "E", TaxID = 3, TaxPercent = null, Description = "Tax exempt - no tax percent sent to ZIMRA" }
        };
    }

    public TaxMappingResult MapErpTaxCode(string erpTaxCode)
    {
        if (string.IsNullOrWhiteSpace(erpTaxCode))
        {
            return new TaxMappingResult { ZimraTaxCode = "A", TaxID = 1, TaxPercent = 15.00m };
        }

        var mappings = LoadMappings();
        var match = mappings.FirstOrDefault(m => string.Equals(m.ErpTaxCode, erpTaxCode.Trim(), StringComparison.OrdinalIgnoreCase))
                 ?? mappings.FirstOrDefault(m => string.Equals(m.ZimraTaxCode, erpTaxCode.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return new TaxMappingResult
            {
                ZimraTaxCode = match.ZimraTaxCode,
                TaxID = match.TaxID,
                TaxPercent = match.TaxPercent
            };
        }

        // Default if unknown
        _logger.LogWarning("Unknown tax code '{ErpTaxCode}'. Falling back to default tax ID 1 (15%).", erpTaxCode);
        return new TaxMappingResult { ZimraTaxCode = "A", TaxID = 1, TaxPercent = 15.00m };
    }

    public void ApplyTaxMappings(ErpInvoiceRequestDto request)
    {
        if (request.Lines != null)
        {
            foreach (var line in request.Lines)
            {
                if (!string.IsNullOrWhiteSpace(line.TaxCode))
                {
                    var mapped = MapErpTaxCode(line.TaxCode);
                    line.TaxCode = mapped.ZimraTaxCode;
                    line.TaxID = mapped.TaxID;
                    line.TaxPercent = mapped.TaxPercent;
                }
            }
        }

        if (request.Taxes != null)
        {
            foreach (var tax in request.Taxes)
            {
                if (!string.IsNullOrWhiteSpace(tax.TaxCode))
                {
                    var mapped = MapErpTaxCode(tax.TaxCode);
                    tax.TaxCode = mapped.ZimraTaxCode;
                    tax.TaxID = mapped.TaxID;
                    tax.TaxPercent = mapped.TaxPercent;
                }
            }
        }
    }
}

