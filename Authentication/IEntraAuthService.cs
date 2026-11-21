using Azure.Core;
using VoterVerification.Models;

namespace VoterVerification.Authentication;

public interface IEntraAuthService
{
    SignedInUser? CurrentUser { get; }

    Task<SignedInUser?> TryRestoreAsync(CancellationToken cancellationToken);

    Task<SignedInUser> SignInAsync(CancellationToken cancellationToken);

    Task SignOutAsync();

    Task<AccessToken> GetStorageTokenAsync(string[] scopes, CancellationToken cancellationToken);

    Task<AccessToken> GetApiTokenAsync(CancellationToken cancellationToken);
}
