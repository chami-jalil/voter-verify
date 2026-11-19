using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using VoterVerification.Configuration;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.Authentication;

public sealed class EntraAuthService : IEntraAuthService
{
    private readonly SecureMsalTokenCache _tokenCache;
    private readonly ILogger<EntraAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _client;
    private SignedInUser? _currentUser;

    public EntraAuthService(SecureMsalTokenCache tokenCache, ILogger<EntraAuthService> logger)
    {
        _tokenCache = tokenCache;
        _logger = logger;
    }

    public SignedInUser? CurrentUser => _currentUser;

    public async Task<SignedInUser?> TryRestoreAsync(CancellationToken cancellationToken)
    {
        if (!AppConfiguration.IsConfigured)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await GetClientAsync();
            var account = (await client.GetAccountsAsync()).FirstOrDefault();
            if (account is null)
            {
                return null;
            }

            try
            {
                var result = await client
                    .AcquireTokenSilent(AppConfiguration.StorageScopes, account)
                    .ExecuteAsync(cancellationToken);
                _currentUser = ToUser(result);
                return _currentUser;
            }
            catch (MsalUiRequiredException)
            {
                return null;
            }
            catch (MsalException ex)
            {
                _logger.LogWarning("Silent sign-in failed with {ErrorCode}.", ex.ErrorCode);
                throw ToAppException(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SignedInUser> SignInAsync(CancellationToken cancellationToken)
    {
        if (!AppConfiguration.IsConfigured)
        {
            throw new AppException(AppErrorKind.NotConfigured, AppConfiguration.ConfigurationMessage);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await GetClientAsync();
            try
            {
                var result = await client
                    .AcquireTokenInteractive(AppConfiguration.StorageScopes)
                    .WithParentActivityOrWindow(() => ParentActivity())
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(cancellationToken);
                _currentUser = ToUser(result);
                return _currentUser;
            }
            catch (MsalException ex)
            {
                _logger.LogWarning("Interactive sign-in failed with {ErrorCode}.", ex.ErrorCode);
                throw ToAppException(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _currentUser = null;
            if (_client is not null)
            {
                var accounts = await _client.GetAccountsAsync();
                foreach (var account in accounts)
                {
                    await _client.RemoveAsync(account);
                }
            }

            _tokenCache.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AccessToken> GetStorageTokenAsync(string[] scopes, CancellationToken cancellationToken)
    {
        if (!AppConfiguration.IsConfigured)
        {
            throw new AppException(AppErrorKind.NotConfigured, AppConfiguration.ConfigurationMessage);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await GetClientAsync();
            var account = (await client.GetAccountsAsync()).FirstOrDefault();
            if (account is null || _currentUser is null)
            {
                throw new AppException(AppErrorKind.SignInRequired, "Sign in again to continue.");
            }

            try
            {
                var result = await client.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken);
                return new AccessToken(result.AccessToken, result.ExpiresOn);
            }
            catch (MsalUiRequiredException)
            {
                throw new AppException(AppErrorKind.SignInRequired, "Your sign-in has expired. Sign in again.");
            }
            catch (MsalException ex)
            {
                _logger.LogWarning("Storage token request failed with {ErrorCode}.", ex.ErrorCode);
                throw ToAppException(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IPublicClientApplication> GetClientAsync()
    {
        if (_client is not null)
        {
            return _client;
        }

        await _tokenCache.LoadAsync();
        var client = PublicClientApplicationBuilder
            .Create(AppConfiguration.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, AppConfiguration.TenantId)
            .WithRedirectUri(AppConfiguration.RedirectUri)
            .WithParentActivityOrWindow(() => ParentActivity())
            .Build();
        _tokenCache.Attach(client.UserTokenCache);
        _client = client;
        return client;
    }

    private static object ParentActivity()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            throw new AppException(AppErrorKind.Unexpected, "The sign-in window is not ready. Try again.");
        }

        return activity;
    }

    private static SignedInUser ToUser(AuthenticationResult result)
    {
        var username = result.Account?.Username ?? "";
        var displayName = result.ClaimsPrincipal?.FindFirst("name")?.Value ?? "";
        var objectId = result.ClaimsPrincipal?.FindFirst("oid")?.Value
            ?? result.ClaimsPrincipal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? "";

        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(objectId))
        {
            throw new AppException(AppErrorKind.Unexpected, "The signed-in account did not include an identity.");
        }

        var verifiedBy = !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(username)
            ? $"{displayName} ({username})"
            : !string.IsNullOrWhiteSpace(username) ? username : objectId;

        return new SignedInUser
        {
            ObjectId = objectId,
            Username = username,
            DisplayName = displayName,
            VerifiedBy = verifiedBy
        };
    }

    private static AppException ToAppException(MsalException exception)
    {
        if (exception.ErrorCode is "authentication_canceled")
        {
            return new AppException(AppErrorKind.Validation, "Sign-in was canceled.", exception);
        }

        if (exception.ErrorCode is "access_denied")
        {
            return new AppException(AppErrorKind.NotAuthorized, "This account is not allowed to use Voter Verification.", exception);
        }

        if (exception.ErrorCode is "invalid_client" or "unauthorized_client")
        {
            return new AppException(
                AppErrorKind.NotConfigured,
                "The Entra application registration was rejected. Check the client id, tenant id, and redirect URI.",
                exception);
        }

        if (exception.InnerException is HttpRequestException or System.Net.Sockets.SocketException
            || exception.ErrorCode is "device_network_not_available" or "network_not_available")
        {
            return new AppException(AppErrorKind.Network, "Can't reach Microsoft Entra ID. Check the network and try again.", exception);
        }

        return new AppException(AppErrorKind.Unexpected, "Sign-in could not be completed. Try again.", exception);
    }
}
