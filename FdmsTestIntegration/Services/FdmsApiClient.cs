using System.Net.Http.Json;
using FdmsGatewayApi.Models.Dtos;

namespace FdmsTestIntegration.Services;

/// <summary>
/// Strong-typed HTTP Client used by Partner ERP software to communicate with the FDMS Gateway API.
/// </summary>
public class FdmsApiClient
{
    private readonly HttpClient _client;
    private readonly string _apiKey;

    public FdmsApiClient(string baseUrl = "http://localhost:5045", string apiKey = "fdms-admin-key-secure-prod-placeholder")
    {
        _apiKey = apiKey;
        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    /// <summary>
    /// Probe FDMS Gateway availability.
    /// </summary>
    public async Task<bool> IsGatewayOnlineAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _client.GetAsync("healthz", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensure active fiscal day is open for the specified device.
    /// </summary>
    public async Task<bool> EnsureFiscalDayOpenAsync(int deviceId = 1, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.PostAsync($"api/v1/admin/day/open?deviceId={deviceId}", null, ct);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            string body = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"[API Warning] EnsureFiscalDayOpenAsync returned {response.StatusCode}: {body}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API Warning] Fiscal day open check exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Post ERP Sales Invoice for ZIMRA fiscalization.
    /// </summary>
    public async Task<FiscalizedDocumentResponseDto?> FiscalizeInvoiceAsync(ErpInvoiceRequestDto request, CancellationToken ct = default)
    {
        var response = await _client.PostAsJsonAsync("api/v1/erp/invoices", request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return await response.Content.ReadFromJsonAsync<FiscalizedDocumentResponseDto>(cancellationToken: ct);
        }

        string errBody = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"FiscalizeInvoice failed [{response.StatusCode}]: {errBody}");
    }

    /// <summary>
    /// Post ERP Credit Note linked to original invoice for ZIMRA fiscalization.
    /// </summary>
    public async Task<FiscalizedDocumentResponseDto?> FiscalizeCreditNoteAsync(ErpCreditNoteRequestDto request, CancellationToken ct = default)
    {
        var response = await _client.PostAsJsonAsync("api/v1/erp/credit-notes", request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return await response.Content.ReadFromJsonAsync<FiscalizedDocumentResponseDto>(cancellationToken: ct);
        }

        string errBody = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"FiscalizeCreditNote failed [{response.StatusCode}]: {errBody}");
    }

    /// <summary>
    /// Post ERP Debit Note linked to original invoice for ZIMRA fiscalization.
    /// </summary>
    public async Task<FiscalizedDocumentResponseDto?> FiscalizeDebitNoteAsync(ErpDebitNoteRequestDto request, CancellationToken ct = default)
    {
        var response = await _client.PostAsJsonAsync("api/v1/erp/debit-notes", request, ct);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            return await response.Content.ReadFromJsonAsync<FiscalizedDocumentResponseDto>(cancellationToken: ct);
        }

        string errBody = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"FiscalizeDebitNote failed [{response.StatusCode}]: {errBody}");
    }
}
