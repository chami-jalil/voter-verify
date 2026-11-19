using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace VoterVerification.Authentication;

/// <summary>
/// Keeps the MSAL token cache in memory and persists it through MAUI SecureStorage
/// (Android Keystore). Tokens are not written to Preferences.
/// </summary>
public sealed class SecureMsalTokenCache
{
    private const string CacheKey = "voterverification.msal.cache.v1";

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private byte[]? _cache;
    private int _generation;
    private bool _loaded;

    public SecureMsalTokenCache(ILogger<SecureMsalTokenCache> logger)
    {
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var stored = await SecureStorage.Default.GetAsync(CacheKey);
            if (!string.IsNullOrEmpty(stored))
            {
                var bytes = Convert.FromBase64String(stored);
                lock (_gate)
                {
                    _cache = bytes;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sign-in cache could not be read ({ExceptionType}).", ex.GetType().Name);
            try
            {
                SecureStorage.Default.Remove(CacheKey);
            }
            catch (Exception removeEx)
            {
                _logger.LogWarning("Sign-in cache could not be cleared ({ExceptionType}).", removeEx.GetType().Name);
            }
        }

        _loaded = true;
    }

    public void Attach(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccess(BeforeAccess);
        tokenCache.SetAfterAccess(AfterAccess);
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            _cache = null;
        }

        try
        {
            SecureStorage.Default.Remove(CacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sign-in cache could not be removed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void BeforeAccess(TokenCacheNotificationArgs args)
    {
        byte[]? snapshot;
        lock (_gate)
        {
            snapshot = _cache;
        }

        if (snapshot is { Length: > 0 })
        {
            args.TokenCache.DeserializeMsalV3(snapshot);
        }
    }

    private void AfterAccess(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged)
        {
            return;
        }

        var data = args.TokenCache.SerializeMsalV3();
        var generation = Volatile.Read(ref _generation);
        lock (_gate)
        {
            _cache = data;
        }

        var payload = Convert.ToBase64String(data);
        _ = PersistAsync(generation, payload);
    }

    private async Task PersistAsync(int generation, string payload)
    {
        try
        {
            await SecureStorage.Default.SetAsync(CacheKey, payload);
            if (generation != Volatile.Read(ref _generation))
            {
                SecureStorage.Default.Remove(CacheKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sign-in cache could not be saved ({ExceptionType}).", ex.GetType().Name);
        }
    }
}
