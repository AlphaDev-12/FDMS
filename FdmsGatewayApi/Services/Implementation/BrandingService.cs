using System.Text.Json;
using System.Text.Json.Nodes;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FdmsGatewayApi.Services.Implementation;

public class BrandingService : IBrandingService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrandingService> _logger;

    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".svg", ".webp" };

    public BrandingService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<BrandingService> logger)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
    }

    public BrandingInfoResponse GetBrandingInfo()
    {
        string companyName = _configuration["WhiteLabel:CompanyName"] ?? "FDMS Enterprise";

        if (string.IsNullOrWhiteSpace(companyName))
        {
            companyName = "FDMS Enterprise";
        }

        string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        string configuredLogoName = _configuration["WhiteLabel:LogoFileName"] ?? "logo";

        var candidateNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredLogoName))
        {
            candidateNames.Add(configuredLogoName.Trim());
        }
        if (!candidateNames.Contains("logo", StringComparer.OrdinalIgnoreCase))
        {
            candidateNames.Add("logo");
        }

        string? foundLogoUrl = null;
        bool hasCustomLogo = false;

        if (Directory.Exists(webRootPath))
        {
            foreach (var name in candidateNames)
            {
                foreach (var ext in AllowedExtensions)
                {
                    string fileName = $"{name}{ext}";
                    string targetFile = Path.Combine(webRootPath, fileName);
                    if (File.Exists(targetFile))
                    {
                        var fileInfo = new FileInfo(targetFile);
                        string relativeUrl = $"/{Uri.EscapeDataString(fileName)}?v={fileInfo.LastWriteTimeUtc.Ticks}";
                        foundLogoUrl = relativeUrl;
                        hasCustomLogo = true;
                        break;
                    }
                }
                if (hasCustomLogo) break;
            }
        }

        return new BrandingInfoResponse
        {
            CompanyName = companyName,
            LogoUrl = foundLogoUrl,
            HasCustomLogo = hasCustomLogo
        };
    }
}
