using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Controllers;

/// <summary>
/// Printable Fiscal Reports (Z-Reports, X-Reports, PDF Generation).
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Produces("application/json")]
[Tags("Fiscal Reports & Audit")]
public class ReportsController : ControllerBase
{
    private readonly IReportsConfigService _configService;
    private readonly IZReportService _zReportService;
    private readonly IXReportService _xReportService;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportsConfigService configService,
        IZReportService zReportService,
        IXReportService xReportService,
        ILogger<ReportsController> logger)
    {
        _configService = configService;
        _zReportService = zReportService;
        _xReportService = xReportService;
        _logger = logger;
    }

    [HttpGet("config")]
    public ActionResult<ReportsConfigDto> GetConfig()
    {
        var config = _configService.GetConfig();
        return Ok(config);
    }

    [HttpPost("config")]
    public async Task<ActionResult<ReportsConfigDto>> UpdateConfig([FromBody] UpdateReportsConfigDto dto, CancellationToken cancellationToken = default)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.StoragePath))
        {
            return BadRequest(new { error = "Valid StoragePath is required." });
        }

        var result = await _configService.UpdateStoragePathAsync(dto.StoragePath, cancellationToken);
        if (!result.IsPathValid)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("z-reports")]
    public async Task<ActionResult<PagedZReportsResultDto>> GetZReportsList(
        [FromQuery] int deviceId = 0,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _zReportService.GetZReportsListAsync(deviceId, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpGet("z-report/pdf/{id}")]
    public async Task<IActionResult> GetZReportPdf(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var (pdfBytes, fileName) = await _zReportService.GetZReportPdfBytesAsync(id, cancellationToken);
            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream PDF Z-Report for ID {ReportId}", id);
            return StatusCode(500, new { error = "Failed to load PDF Z-Report", message = ex.Message });
        }
    }

    [HttpPost("x-report")]
    public async Task<ActionResult<XReportResponseDto>> GenerateXReport([FromQuery] int deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            var report = await _xReportService.GenerateXReportAsync(deviceId, cancellationToken);
            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate X-Report for Device {DeviceId}", deviceId);
            return StatusCode(500, new { error = "XREPORT01", message = ex.Message });
        }
    }

    [HttpGet("x-report/pdf")]
    public async Task<IActionResult> GetXReportPdf([FromQuery] int deviceId, CancellationToken cancellationToken = default)
    {
        if (deviceId <= 0)
        {
            return BadRequest(new { error = "Valid deviceId is required." });
        }

        try
        {
            var (pdfBytes, fileName) = await _xReportService.GenerateXReportPdfAsync(deviceId, cancellationToken);
            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{fileName}\"");
            return File(pdfBytes, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream PDF X-Report for Device {DeviceId}", deviceId);
            return StatusCode(500, new { error = "Failed to generate PDF X-Report", message = ex.Message });
        }
    }

    [HttpGet("browse-dirs")]
    public IActionResult BrowseDirectories([FromQuery] string? path = null)
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.Name)
                .ToList();

            string currentPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                currentPath = _configService.GetConfig().StoragePath;
                if (!Directory.Exists(currentPath))
                {
                    currentPath = AppDomain.CurrentDomain.BaseDirectory;
                }
            }
            else
            {
                currentPath = path;
            }

            currentPath = Path.GetFullPath(currentPath);
            var parentPath = Directory.GetParent(currentPath)?.FullName;

            var subDirectories = new List<DirectoryItemDto>();
            if (Directory.Exists(currentPath))
            {
                var dirInfo = new DirectoryInfo(currentPath);
                foreach (var dir in dirInfo.GetDirectories())
                {
                    try
                    {
                        if ((dir.Attributes & FileAttributes.Hidden) != 0 || (dir.Attributes & FileAttributes.System) != 0)
                            continue;

                        subDirectories.Add(new DirectoryItemDto
                        {
                            Name = dir.Name,
                            FullPath = dir.FullName
                        });
                    }
                    catch
                    {
                        // Skip inaccessible folders
                    }
                }
            }

            return Ok(new
            {
                currentPath = currentPath,
                parentPath = parentPath,
                drives = drives,
                directories = subDirectories.OrderBy(d => d.Name).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to browse directories for path: {Path}", path);
            return BadRequest(new { error = "DIRBROWSE01", message = ex.Message });
        }
    }
}
