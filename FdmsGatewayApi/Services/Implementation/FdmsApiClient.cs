using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class FdmsApiClient : IFdmsApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IZimraHealthCheck _healthCheck;
    private readonly IConfiguration _config;
    private readonly ILogger<FdmsApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FdmsApiClient(
        IHttpClientFactory httpClientFactory,
        IZimraHealthCheck healthCheck,
        IConfiguration config,
        ILogger<FdmsApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _healthCheck = healthCheck;
        _config = config;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<VerifyTaxpayerInfoResponse?> VerifyTaxpayerInformationAsync(VerifyTaxpayerInfoRequest req, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<VerifyTaxpayerInfoRequest, VerifyTaxpayerInfoResponse>(
            "verifyTaxpayerInformation", req, certPem: null, privateKeyPem: null, cancellationToken);
    }

    public async Task<RegisterDeviceResponse?> RegisterDeviceAsync(RegisterDeviceRequest req, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<RegisterDeviceRequest, RegisterDeviceResponse>(
            "registerDevice", req, certPem: null, privateKeyPem: null, cancellationToken);
    }

    public async Task<GetConfigResponse?> GetConfigAsync(GetConfigRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetConfigRequest, GetConfigResponse>(
            "getConfig", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetStatusResponse?> GetStatusAsync(GetStatusRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetStatusRequest, GetStatusResponse>(
            "getStatus", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<OpenDayResponse?> OpenDayAsync(OpenDayRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<OpenDayRequest, OpenDayResponse>(
            "openDay", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<SubmitReceiptResponse?> SubmitReceiptAsync(SubmitReceiptRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<SubmitReceiptRequest, SubmitReceiptResponse>(
            "submitReceipt", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<CloseDayResponse?> CloseDayAsync(CloseDayRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<CloseDayRequest, CloseDayResponse>(
            "closeDay", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<SubmitFileResponse?> SubmitFileAsync(SubmitFileRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<SubmitFileRequest, SubmitFileResponse>(
            "submitFile", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetFileStatusResponse?> GetFileStatusAsync(GetFileStatusRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetFileStatusRequest, GetFileStatusResponse>(
            "getFileStatus", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetStockListResponse?> GetStockListAsync(GetStockListRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetStockListRequest, GetStockListResponse>(
            "getStockList", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetServerCertificateResponse?> GetServerCertificateAsync(CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<object, GetServerCertificateResponse>(
            "getServerCertificate", new { }, certPem: null, privateKeyPem: null, cancellationToken);
    }


    public async Task<PingResponse?> PingAsync(int deviceId, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<PingRequest, PingResponse>(
            "ping", new PingRequest { DeviceID = deviceId }, certPem, privateKeyPem, cancellationToken);
    }

    #region Section 4.14 User Management APIs

    public async Task<OpenUserSessionResponse?> OpenUserSessionAsync(OpenUserSessionRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<OpenUserSessionRequest, OpenUserSessionResponse>(
            "openUserSession", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<RegisterZimraUserResponse?> RegisterUserAsync(RegisterZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<RegisterZimraUserRequest, RegisterZimraUserResponse>(
            "registerUser", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> VerifyUserSecCodeAsync(VerifyUserSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<VerifyUserSecCodeRequest, ZimraGenericUserResponse>(
            "verifyUserSecCode", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> ResendUserSecCodeAsync(ResendUserSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<ResendUserSecCodeRequest, ZimraGenericUserResponse>(
            "resendUserSecCode", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetUsersListResponse?> GetUsersListAsync(GetUsersListRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetUsersListRequest, GetUsersListResponse>(
            "getUsersList", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<GetZimraUserResponse?> GetUserAsync(GetZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<GetZimraUserRequest, GetZimraUserResponse>(
            "getUser", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> UpdateUserAsync(UpdateZimraUserRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<UpdateZimraUserRequest, ZimraGenericUserResponse>(
            "updateUser", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> ChangeUserPasswordAsync(ChangeUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<ChangeUserPasswordRequest, ZimraGenericUserResponse>(
            "changeUserPassword", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> SaveUserPINAsync(SaveUserPINRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<SaveUserPINRequest, ZimraGenericUserResponse>(
            "saveUserPIN", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<VerifyUserPINResponse?> VerifyUserPINAsync(VerifyUserPINRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<VerifyUserPINRequest, VerifyUserPINResponse>(
            "verifyUserPIN", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> ResetUserPasswordAsync(ResetUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<ResetUserPasswordRequest, ZimraGenericUserResponse>(
            "resetUserPassword", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<InitResetUserPasswordResponse?> InitResetUserPasswordAsync(InitResetUserPasswordRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<InitResetUserPasswordRequest, InitResetUserPasswordResponse>(
            "initResetUserPassword", req, certPem, privateKeyPem, cancellationToken);
    }

    public async Task<ZimraGenericUserResponse?> VerifyResetUserPasswordSecCodeAsync(VerifyResetUserPasswordSecCodeRequest req, string certPem, string privateKeyPem, CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<VerifyResetUserPasswordSecCodeRequest, ZimraGenericUserResponse>(
            "verifyResetUserPasswordSecCode", req, certPem, privateKeyPem, cancellationToken);
    }

    #endregion

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient> _mtlsClientCache = new();

    private HttpClient GetMtlsClient(string certPem, string privateKeyPem)
    {
        string cacheKey = $"{certPem.GetHashCode()}_{privateKeyPem.GetHashCode()}";
        return _mtlsClientCache.GetOrAdd(cacheKey, _ =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                EnableMultipleHttp2Connections = true
            };

            try
            {
                var clientCert = X509Certificate2.CreateFromPem(certPem, privateKeyPem);
                handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    ClientCertificates = new X509CertificateCollection { clientCert }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not attach X509 client certificate for mTLS.");
            }

            var client = new HttpClient(handler, disposeHandler: true);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        });
    }

    private async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
        string endpoint,
        TRequest requestObj,
        string? certPem,
        string? privateKeyPem,
        CancellationToken cancellationToken) where TResponse : class
    {
        if (string.Equals(_healthCheck.TargetEnvironment, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Target Environment is Local Simulated Success. Bypassing remote network call for {Endpoint}.", endpoint);
            return GetSimulatedResponse<TResponse>(endpoint, requestObj);
        }

        string baseUrl = _healthCheck.GetActiveBaseUrl();
        if (!baseUrl.EndsWith("/")) baseUrl += "/";

        string url = $"{baseUrl}{endpoint}";
        var client = _httpClientFactory.CreateClient("ZimraFdmsClient");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("DeviceModelName", _config["FdmsGateway:DeviceModelName"] ?? "EdERP-FDMS-Gateway");
        request.Headers.TryAddWithoutValidation("DeviceModelVersionNo", _config["FdmsGateway:DeviceModelVersionNo"] ?? "v1.0");

        string jsonPayload = JsonSerializer.Serialize(requestObj, _jsonOptions);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _logger.LogInformation("Sending FDMS API Request [{Environment}] to {Endpoint}", _healthCheck.TargetEnvironment, endpoint);

        HttpResponseMessage response;
        if (!string.IsNullOrEmpty(certPem) && !string.IsNullOrEmpty(privateKeyPem))
        {
            var mtlsClient = GetMtlsClient(certPem, privateKeyPem);
            response = await mtlsClient.SendAsync(request, cancellationToken);
        }
        else
        {
            response = await client.SendAsync(request, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            string errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("FDMS API Error [{StatusCode}] for {Endpoint}: {Error}", response.StatusCode, endpoint, errBody);
            throw new HttpRequestException($"FDMS Gateway returned status {response.StatusCode}: {errBody}");
        }

        string respJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TResponse>(respJson, _jsonOptions);
    }

    private TResponse? GetSimulatedResponse<TResponse>(string endpoint, object? requestObj) where TResponse : class
    {
        if (typeof(TResponse) == typeof(SubmitReceiptResponse))
        {
            var req = requestObj as SubmitReceiptRequest;
            int globalNo = req?.Receipt?.ReceiptGlobalNo ?? 1;
            var mockResp = new SubmitReceiptResponse
            {
                ReceiptID = 90000000 + globalNo,
                ServerDate = DateTime.UtcNow,
                OperationID = "SIM-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                ReceiptServerSignature = new SignatureDataDto
                {
                    Hash = req?.Receipt?.ReceiptDeviceSignature?.Hash ?? "SIM-HASH",
                    Signature = "SIM-SERVER-SIG-" + Guid.NewGuid().ToString("N")[..16].ToUpper()
                }
            };
            return mockResp as TResponse;
        }
        if (typeof(TResponse) == typeof(OpenDayResponse))
        {
            var req = requestObj as OpenDayRequest;
            return new OpenDayResponse
            {
                OperationID = "SIM-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                FiscalDayNo = req?.FiscalDayNo ?? 1
            } as TResponse;
        }
        if (typeof(TResponse) == typeof(CloseDayResponse))
        {
            return new CloseDayResponse
            {
                OperationID = "SIM-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper()
            } as TResponse;
        }
        if (typeof(TResponse) == typeof(PingResponse))
        {
            return new PingResponse() as TResponse;
        }
        if (typeof(TResponse) == typeof(GetConfigResponse))
        {
            return new GetConfigResponse() as TResponse;
        }
        if (typeof(TResponse) == typeof(GetStatusResponse))
        {
            return new GetStatusResponse { FiscalDayStatus = string.Empty, OperationID = "SIM-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper() } as TResponse;
        }
        if (typeof(TResponse) == typeof(SubmitFileResponse))
        {
            return new SubmitFileResponse { OperationID = "SIM-FILE-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper() } as TResponse;
        }
        if (typeof(TResponse) == typeof(GetFileStatusResponse))
        {
            return new GetFileStatusResponse() as TResponse;
        }
        if (typeof(TResponse) == typeof(GetStockListResponse))
        {
            return new GetStockListResponse
            {
                OperationID = "SIM-STOCK-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                Total = 2,
                Rows = new List<ZimraGoodDto>
                {
                    new ZimraGoodDto { HsCode = "SIM-ITEM-001", GoodName = "Standard Inventory Item A", Quantity = 100 },
                    new ZimraGoodDto { HsCode = "SIM-ITEM-002", GoodName = "Zero Rated Inventory Item B", Quantity = 50 }
                }
            } as TResponse;
        }
        if (typeof(TResponse) == typeof(OpenUserSessionResponse))
        {
            return new OpenUserSessionResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(), UserID = 101, UserRole = "Operator", SessionToken = Guid.NewGuid().ToString("N") } as TResponse;
        }
        if (typeof(TResponse) == typeof(RegisterZimraUserResponse))
        {
            return new RegisterZimraUserResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(), UserID = 102 } as TResponse;
        }
        if (typeof(TResponse) == typeof(GetUsersListResponse))
        {
            return new GetUsersListResponse
            {
                OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                Users = new List<ZimraUserSummaryDto>
                {
                    new ZimraUserSummaryDto { UserID = 101, Username = "sim_operator", Name = "Sim", Surname = "Operator", UserRole = "Operator", UserStatus = "Active" }
                }
            } as TResponse;
        }
        if (typeof(TResponse) == typeof(GetZimraUserResponse))
        {
            return new GetZimraUserResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(), UserID = 101, Username = "sim_operator", Name = "Sim", Surname = "Operator", UserRole = "Operator", Email = "sim@fdms.co.zw", UserStatus = "Active" } as TResponse;
        }
        if (typeof(TResponse) == typeof(VerifyUserPINResponse))
        {
            return new VerifyUserPINResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(), IsValid = true } as TResponse;
        }
        if (typeof(TResponse) == typeof(InitResetUserPasswordResponse))
        {
            return new InitResetUserPasswordResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper(), UserID = 101 } as TResponse;
        }
        if (typeof(TResponse) == typeof(ZimraGenericUserResponse))
        {
            return new ZimraGenericUserResponse { OperationID = "SIM-USER-OP-" + Guid.NewGuid().ToString("N")[..8].ToUpper() } as TResponse;
        }

        return null;
    }
}

