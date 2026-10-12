using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using FdmsBridgeManager.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Implementation;

public class WhiteLabelService : IWhiteLabelService
{
    private readonly BridgeManagerDbContext _dbContext;

    public WhiteLabelService(BridgeManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WhiteLabelConfigDto> GetConfigAsync(CancellationToken ct = default)
    {
        var config = await _dbContext.WhiteLabelConfigs.FirstOrDefaultAsync(ct);
        if (config == null)
        {
            config = new WhiteLabelConfigEntity();
            _dbContext.WhiteLabelConfigs.Add(config);
            await _dbContext.SaveChangesAsync(ct);
        }

        return MapToDto(config);
    }

    public async Task<WhiteLabelConfigDto> UpdateConfigAsync(UpdateWhiteLabelRequest request, CancellationToken ct = default)
    {
        var config = await _dbContext.WhiteLabelConfigs.FirstOrDefaultAsync(ct);
        if (config == null)
        {
            config = new WhiteLabelConfigEntity();
            _dbContext.WhiteLabelConfigs.Add(config);
        }

        config.FirmName = string.IsNullOrWhiteSpace(request.FirmName) ? config.FirmName : request.FirmName.Trim();
        config.Tagline = request.Tagline ?? config.Tagline;
        config.LogoUrl = request.LogoUrl ?? config.LogoUrl;
        config.FaviconUrl = request.FaviconUrl ?? config.FaviconUrl;
        config.PrimaryColor = string.IsNullOrWhiteSpace(request.PrimaryColor) ? config.PrimaryColor : request.PrimaryColor.Trim();
        config.SecondaryColor = string.IsNullOrWhiteSpace(request.SecondaryColor) ? config.SecondaryColor : request.SecondaryColor.Trim();
        config.SupportEmail = request.SupportEmail ?? config.SupportEmail;
        config.SupportPhone = request.SupportPhone ?? config.SupportPhone;
        config.Address = request.Address ?? config.Address;
        config.CopyrightText = request.CopyrightText ?? config.CopyrightText;
        config.EmailFooterText = request.EmailFooterText ?? config.EmailFooterText;
        config.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return MapToDto(config);
    }

    private static WhiteLabelConfigDto MapToDto(WhiteLabelConfigEntity entity)
    {
        return new WhiteLabelConfigDto
        {
            FirmName = entity.FirmName,
            Tagline = entity.Tagline,
            LogoUrl = entity.LogoUrl,
            FaviconUrl = entity.FaviconUrl,
            PrimaryColor = entity.PrimaryColor,
            SecondaryColor = entity.SecondaryColor,
            SupportEmail = entity.SupportEmail,
            SupportPhone = entity.SupportPhone,
            Address = entity.Address,
            CopyrightText = entity.CopyrightText,
            EmailFooterText = entity.EmailFooterText
        };
    }
}
