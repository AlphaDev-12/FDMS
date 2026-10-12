using FdmsBridgeManager.Data;
using FdmsBridgeManager.Models.Dtos;
using FdmsBridgeManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace FdmsBridgeManager.Services.Interfaces;

public interface IWhiteLabelService
{
    Task<WhiteLabelConfigDto> GetConfigAsync(CancellationToken ct = default);
    Task<WhiteLabelConfigDto> UpdateConfigAsync(UpdateWhiteLabelRequest request, CancellationToken ct = default);
}
