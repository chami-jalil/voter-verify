using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace VoterVerification.Functions.Security;

public sealed record EntraIdentity(string DirectoryId, string ObjectId, string? Name, string? Username);

public static class EntraJwt
{
    public const string DelegatedScope = "access_as_user";
    public static TokenValidationParameters CreateParameters(IEnumerable<SecurityKey> signingKeys, IEnumerable<string> audiences) =>
        new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            ValidateIssuer = true,
            IssuerValidator = ValidateIssuer,
            ValidateAudience = true,
            ValidAudiences = audiences.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

    public static EntraIdentity ReadValidated(string token, TokenValidationParameters parameters)
    {
        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        handler.ValidateToken(token, parameters, out var validated);
        var jwt = validated as JwtSecurityToken ?? throw new SecurityTokenException("The access token could not be read.");
        var objectId = jwt.Claims.FirstOrDefault(claim => claim.Type == "oid")?.Value;
        var directoryId = jwt.Claims.FirstOrDefault(claim => claim.Type == "tid")?.Value;
        if (string.IsNullOrWhiteSpace(objectId) || string.IsNullOrWhiteSpace(directoryId))
        {
            throw new SecurityTokenException("The access token did not include an Entra object id and directory id.");
        }

        RequireDelegatedUserToken(jwt);

        var name = jwt.Claims.FirstOrDefault(claim => claim.Type == "name")?.Value;
        var username = jwt.Claims.FirstOrDefault(claim => claim.Type is "preferred_username" or "upn")?.Value;
        return new EntraIdentity(directoryId, objectId, name, username);
    }

    public static async Task<EntraIdentity> ReadValidatedRefreshingAsync(
        string token,
        TokenValidationParameters current,
        Func<CancellationToken, Task<TokenValidationParameters>> refresh,
        CancellationToken cancellationToken)
    {
        try
        {
            return ReadValidated(token, current);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            var updated = await refresh(cancellationToken);
            return ReadValidated(token, updated);
        }
    }

    public static void RequireDelegatedUserToken(JwtSecurityToken jwt)
    {
        var tokenType = jwt.Claims.FirstOrDefault(claim => claim.Type == "idtyp")?.Value;
        if (string.Equals(tokenType, "app", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenException("Application tokens are not accepted.");
        }

        var granted = false;
        foreach (var claim in jwt.Claims)
        {
            if (!string.Equals(claim.Type, "scp", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.Equals(scope, DelegatedScope, StringComparison.Ordinal)
                    || scope.EndsWith("/" + DelegatedScope, StringComparison.Ordinal))
                {
                    granted = true;
                }
            }
        }

        if (!granted)
        {
            throw new SecurityTokenException("The access token did not include the delegated scope access_as_user.");
        }
    }

    public static string ValidateIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (IsOrganizationalIssuer(issuer))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException("The access token issuer is not a Microsoft Entra directory.");
    }

    public static bool IsOrganizationalIssuer(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (!host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("sts.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !Guid.TryParse(segments[0], out _))
        {
            return false;
        }

        if (host.Equals("sts.windows.net", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Length == 1;
        }

        return segments.Length == 2 && segments[1].Equals("v2.0", StringComparison.OrdinalIgnoreCase);
    }
}
