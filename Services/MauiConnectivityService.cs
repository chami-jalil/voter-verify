namespace VoterVerification.Services;

public sealed class MauiConnectivityService : IConnectivityService
{
    public bool IsConnected
    {
        get
        {
            var access = Connectivity.Current.NetworkAccess;
            return access is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet or NetworkAccess.Unknown;
        }
    }
}
