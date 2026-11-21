using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VoterVerification.Functions.Data;
using VoterVerification.Models;

namespace VoterVerification.Functions.Security;

public sealed class EntraAccessTokenValidator
{
    private readonly FunctionSettings _settings;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public EntraAccessTokenValidator(FunctionSettings settings)
    {
        _settings = settings;
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://login.microsoftonline.com/organizations/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }

    public Task<EntraIdentity> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        return ValidateCoreAsync(token, cancellationToken);
    }

    private async Task<EntraIdentity> ValidateCoreAsync(string token, CancellationToken cancellationToken)
    {
        var configuration = await _configuration.GetConfigurationAsync(cancellationToken);
        return await EntraJwt.ReadValidatedRefreshingAsync(
            token,
            Parameters(configuration.SigningKeys),
            async ct =>
            {
                _configuration.RequestRefresh();
                var updated = await _configuration.GetConfigurationAsync(ct);
                return Parameters(updated.SigningKeys);
            },
            cancellationToken);
    }

    private TokenValidationParameters Parameters(IEnumerable<SecurityKey> signingKeys) =>
        EntraJwt.CreateParameters(signingKeys, [_settings.ApiAudience, _settings.ApiClientId]);
}

public sealed class RequestGate
{
    private readonly FunctionSettings _settings;
    private readonly EntraAccessTokenValidator _tokens;
    private readonly AccountStore _accounts;

    public RequestGate(FunctionSettings settings, EntraAccessTokenValidator tokens, AccountStore accounts)
    {
        _settings = settings;
        _tokens = tokens;
        _accounts = accounts;
    }

    public async Task<(AccessDecision Decision, EntraIdentity? Identity)> AuthorizeAsync(
        string? authorizationHeader,
        string? requestedTenantId,
        string? wardId,
        bool wardRequired,
        CancellationToken cancellationToken)
    {
        if (!TryReadBearer(authorizationHeader, out var token))
        {
            return (AccessDecision.Unauthenticated(), null);
        }

        EntraIdentity identity;
        try
        {
            identity = await _tokens.ValidateAsync(token, cancellationToken);
        }
        catch (SecurityTokenException)
        {
            return (AccessDecision.Unauthenticated(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (new AccessDecision(false, 503, "Sign-in could not be checked. Try again.", null, null, null, "", ""), null);
        }

        var user = await _accounts.GetUserAsync(identity.DirectoryId, identity.ObjectId, cancellationToken);
        var bootstrap = _settings.IsBootstrapSuperAdmin(identity.DirectoryId, identity.ObjectId);
        var tenantForCustomer = bootstrap || user?.Role == AppRole.SuperAdmin
            ? requestedTenantId
            : user?.TenantId;
        CustomerAccount? customer = null;
        if (!string.IsNullOrWhiteSpace(tenantForCustomer))
        {
            customer = await _accounts.GetCustomerAsync(tenantForCustomer, cancellationToken);
        }

        var decision = AccessPolicy.Decide(new AccessInput
        {
            DirectoryId = identity.DirectoryId,
            ObjectId = identity.ObjectId,
            IsBootstrapSuperAdmin = bootstrap,
            User = user,
            Customer = customer,
            RequestedTenantId = requestedTenantId,
            RequestedWardId = wardId,
            WardRequired = wardRequired
        });
        return (decision, identity);
    }

    public static bool TryReadBearer(string? header, out string token)
    {
        token = "";
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[prefix.Length..].Trim();
        return token.Length > 0 && new JwtSecurityTokenHandler().CanReadToken(token);
    }

    public static SignedInUser ToSignedInUser(EntraIdentity identity)
    {
        var username = identity.Username ?? "";
        var displayName = identity.Name ?? "";
        var verifiedBy = !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(username)
            ? $"{displayName} ({username})"
            : !string.IsNullOrWhiteSpace(username) ? username : identity.ObjectId;
        return new SignedInUser
        {
            ObjectId = identity.ObjectId,
            Username = username,
            DisplayName = displayName,
            VerifiedBy = verifiedBy
        };
    }
}
