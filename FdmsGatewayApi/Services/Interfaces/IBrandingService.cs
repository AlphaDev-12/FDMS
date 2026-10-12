using Microsoft.AspNetCore.Http;

namespace FdmsGatewayApi.Services.Interfaces;

public class BrandingInfoResponse
{
    public string CompanyName { get; set; } = "FDMS Enterprise";
    public string? LogoUrl { get; set; }
    public bool HasCustomLogo { get; set; }
}

public interface IBrandingService
{
    BrandingInfoResponse GetBrandingInfo();
}
