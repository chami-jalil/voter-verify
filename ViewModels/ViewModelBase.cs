using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VoterVerification.Helpers;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    private readonly IConnectivityService _connectivity;
    private readonly ILogger _logger;

    protected ViewModelBase(IConnectivityService connectivity, ILogger logger)
    {
        _connectivity = connectivity;
        _logger = logger;
    }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? successMessage;

    [ObservableProperty]
    private bool isOffline;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnSuccessMessageChanged(string? value) => OnPropertyChanged(nameof(HasSuccess));

    partial void OnIsBusyChanged(bool value) => OnBusyChanged();

    protected virtual void OnBusyChanged()
    {
    }

    protected async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string? operation = null)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            ErrorMessage = null;
            SuccessMessage = null;
            IsOffline = !_connectivity.IsConnected;
            if (IsOffline)
            {
                ErrorMessage = "No network connection. Connect to the network and try again.";
                return;
            }

            IsBusy = true;
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Operation {Operation} failed ({ExceptionType}). {Message}",
                operation,
                ex.GetType().Name,
                LogRedactor.Safe(ex.Message));
            ErrorMessage = ErrorMapper.ToUserMessage(ex);
            IsOffline = ex is AppException { Kind: AppErrorKind.Network };
        }
        finally
        {
            IsBusy = false;
        }
    }
}
