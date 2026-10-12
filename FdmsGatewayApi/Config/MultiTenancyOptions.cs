namespace FdmsGatewayApi.Config;

public class MultiTenancyOptions
{
    public const string SectionName = "MultiTenancy";

    /// <summary>
    /// Deployment Mode: "MultiTenant", "SingleTenantCloud", "SingleTenantAppliance"
    /// </summary>
    public string Mode { get; set; } = "MultiTenant";

    /// <summary>
    /// Partner Key identifying the hosting partner (e.g., "MJS_SERPA_PROD", "TTCS_CLOUD")
    /// </summary>
    public string PartnerKey { get; set; } = "default-partner-key";

    /// <summary>
    /// Partner Business Name
    /// </summary>
    public string PartnerName { get; set; } = "Default Wholesale Partner";

    /// <summary>
    /// Default Tenant API Key used for SingleTenantCloud mode
    /// </summary>
    public string DefaultTenantApiKey { get; set; } = "css_tenant_prod_77a412c01b";

    /// <summary>
    /// Initial wholesale tokens seeded for new partner accounts
    /// </summary>
    public int InitialWholesaleTokens { get; set; } = 500;
}
