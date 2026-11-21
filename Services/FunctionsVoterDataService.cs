using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VoterVerification.Authentication;
using VoterVerification.Configuration;
using VoterVerification.Models;

namespace VoterVerification.Services;

public sealed class FunctionsVoterDataService : IVoterDataService
{
    public const string CustomerTenantHeader = "X-Customer-Tenant";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEntraAuthService _auth;
    private readonly ActiveWardContext _wardContext;

    public FunctionsVoterDataService(IHttpClientFactory httpClientFactory, IEntraAuthService auth, ActiveWardContext wardContext)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _wardContext = wardContext;
    }

    public async Task<IReadOnlyList<WardOption>> GetAssignedWardsAsync(CancellationToken cancellationToken)
    {
        var wards = await SendAsync<List<WardOption>>(HttpMethod.Get, "api/wards", null, cancellationToken);
        return wards ?? [];
    }

    public Task<DashboardStats> GetDashboardStatsAsync(string wardId, CancellationToken cancellationToken) =>
        SendAsync<DashboardStats>(HttpMethod.Get, $"api/wards/{Uri.EscapeDataString(wardId)}/dashboard", null, cancellationToken)!;

    public Task<VoterSearchOutcome> SearchAsync(string wardId, string query, CancellationToken cancellationToken) =>
        SendAsync<VoterSearchOutcome>(
            HttpMethod.Get,
            $"api/wards/{Uri.EscapeDataString(wardId)}/voters?q={Uri.EscapeDataString(query)}",
            null,
            cancellationToken)!;

    public Task<VoterDetails?> GetVoterAsync(string wardId, string serialNumber, CancellationToken cancellationToken) =>
        SendAsync<VoterDetails>(
            HttpMethod.Get,
            $"api/wards/{Uri.EscapeDataString(wardId)}/voters/{Uri.EscapeDataString(serialNumber)}",
            null,
            cancellationToken,
            allowNotFound: true);

    public async Task<VoterVerificationRecord> SaveVerificationAsync(VerificationDraft draft, CancellationToken cancellationToken)
    {
        var saved = await SendAsync<VoterVerificationRecord>(
            HttpMethod.Put,
            $"api/wards/{Uri.EscapeDataString(draft.WardId)}/voters/{Uri.EscapeDataString(draft.SerialNumber)}/verification",
            draft,
            cancellationToken);
        return saved ?? throw new AppException(AppErrorKind.Unexpected, "The verification was saved but could not be read back.");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        if (!AppConfiguration.IsConfigured)
        {
            throw new AppException(AppErrorKind.NotConfigured, AppConfiguration.ConfigurationMessage);
        }

        var token = await _auth.GetApiTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient(nameof(FunctionsVoterDataService));
        using var request = new HttpRequestMessage(method, new Uri(new Uri(AppConfiguration.FunctionsBaseUrl.TrimEnd('/') + "/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        var tenantId = _wardContext.Current?.TenantId;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.TryAddWithoutValidation(CustomerTenantHeader, tenantId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AppException(AppErrorKind.Network, "Can't reach the verification service. Check the network and try again.", ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, cancellationToken);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            {
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
    }

    private static async Task<AppException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? message = null;
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, cancellationToken);
            message = payload?.Error;
        }
        catch (JsonException)
        {
            message = null;
        }

        if (string.IsNullOrWhiteSpace(message) || message.Contains("Exception", StringComparison.Ordinal) || message.Length > 240)
        {
            message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => "Your sign-in has expired. Sign in again.",
                System.Net.HttpStatusCode.Forbidden => "This account cannot use that customer or ward.",
                System.Net.HttpStatusCode.NotFound => "That record was not found.",
                System.Net.HttpStatusCode.Conflict => "This verification was changed by someone else. The latest values are shown.",
                _ => "The verification service could not complete the request. Try again."
            };
        }

        var kind = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => AppErrorKind.SignInRequired,
            System.Net.HttpStatusCode.Forbidden => AppErrorKind.NotAuthorized,
            System.Net.HttpStatusCode.NotFound => AppErrorKind.NotFound,
            System.Net.HttpStatusCode.Conflict => AppErrorKind.Conflict,
            System.Net.HttpStatusCode.BadRequest => AppErrorKind.Validation,
            _ => AppErrorKind.AzureUnavailable
        };
        return new AppException(kind, message);
    }

    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }
}
