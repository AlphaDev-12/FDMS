using System.Text.Json;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class ReportsConfigService : IReportsConfigService
{
    private readonly string _configFile;
    private readonly string _defaultStoragePath;
    private readonly ILogger<ReportsConfigService> _logger;
    private readonly object _lock = new();

    public ReportsConfigService(IHostEnvironment env, ILogger<ReportsConfigService> logger)
    {
        _logger = logger;
        _configFile = Path.Combine(env.ContentRootPath, "Config", "reports_config.json");
        _defaultStoragePath = Path.Combine(env.ContentRootPath, "ZReports");
    }

    public ReportsConfigDto GetConfig()
    {
        lock (_lock)
        {
            string path = GetSavedPath();
            bool isValid = EnsureDirectoryExists(path, out string? err);

            return new ReportsConfigDto
            {
                StoragePath = path,
                IsPathValid = isValid,
                Message = isValid ? "Storage directory is ready and active." : err
            };
        }
    }

    public async Task<ReportsConfigDto> UpdateStoragePathAsync(string newPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPath))
        {
            throw new ArgumentException("Storage directory path cannot be empty.");
        }

        string trimmedPath = newPath.Trim();

        // Validate directory creation
        if (!EnsureDirectoryExists(trimmedPath, out string? err))
        {
            return new ReportsConfigDto
            {
                StoragePath = trimmedPath,
                IsPathValid = false,
                Message = $"Failed to set storage path: {err}"
            };
        }

        try
        {
            string configDir = Path.GetDirectoryName(_configFile)!;
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var model = new ReportsConfigJsonModel { StoragePath = trimmedPath };
            string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configFile, json, cancellationToken);

            _logger.LogInformation("Updated Z-Reports storage path to: {StoragePath}", trimmedPath);

            return new ReportsConfigDto
            {
                StoragePath = trimmedPath,
                IsPathValid = true,
                Message = "Z-Reports PDF storage directory successfully updated."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save reports configuration to {ConfigFile}", _configFile);
            return new ReportsConfigDto
            {
                StoragePath = trimmedPath,
                IsPathValid = false,
                Message = $"Error saving configuration file: {ex.Message}"
            };
        }
    }

    public string GetResolvedStoragePath()
    {
        lock (_lock)
        {
            string path = GetSavedPath();
            EnsureDirectoryExists(path, out _);
            return path;
        }
    }

    private string GetSavedPath()
    {
        try
        {
            if (File.Exists(_configFile))
            {
                string json = File.ReadAllText(_configFile);
                var config = JsonSerializer.Deserialize<ReportsConfigJsonModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (!string.IsNullOrWhiteSpace(config?.StoragePath))
                {
                    return config.StoragePath.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read reports_config.json, using default storage path.");
        }

        return _defaultStoragePath;
    }

    private bool EnsureDirectoryExists(string path, out string? errorMessage)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying directory: {Path}", path);
            errorMessage = ex.Message;
            return false;
        }
    }

    private class ReportsConfigJsonModel
    {
        public string StoragePath { get; set; } = string.Empty;
    }
}
