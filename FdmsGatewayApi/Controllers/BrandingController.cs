using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// White-label Branding and Portal Customization Endpoints.
/// </summary>
[ApiController]
[Route("api/v1/branding")]
[Produces("application/json")]
[Tags("White-Label Branding")]
public class BrandingController : ControllerBase
{
    private readonly IBrandingService _brandingService;

    public BrandingController(IBrandingService brandingService)
    {
        _brandingService = brandingService;
    }

    [HttpGet]
    [HttpGet("/api/v1/public/branding")]
    [AllowAnonymous]
    public IActionResult GetBranding()
    {
        var info = _brandingService.GetBrandingInfo();
        return Ok(info);
    }
}
