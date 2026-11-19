using Azure.Core;

namespace VoterVerification.Authentication;

public sealed class MsalStorageCredential : TokenCredential
{
    private readonly IEntraAuthService _auth;

    public MsalStorageCredential(IEntraAuthService auth)
    {
        _auth = auth;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        Task.Run(() => GetTokenAsync(requestContext, cancellationToken).AsTask(), cancellationToken).GetAwaiter().GetResult();

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var scopes = requestContext.Scopes is { Length: > 0 }
            ? requestContext.Scopes
            : Configuration.AppConfiguration.StorageScopes;
        return new ValueTask<AccessToken>(_auth.GetStorageTokenAsync(scopes, cancellationToken));
    }
}
