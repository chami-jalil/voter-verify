using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using VoterVerification.Helpers;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.ViewModels;

public partial class VoterSearchViewModel : ViewModelBase, IQueryAttributable
{
    private readonly IVoterDataService _data;
    private string? _requestedWardId;

    public VoterSearchViewModel(IVoterDataService data, IConnectivityService connectivity, ILogger<VoterSearchViewModel> logger)
        : base(connectivity, logger)
    {
        _data = data;
    }

    public ObservableCollection<VoterSearchHit> Results { get; } = new();

    [ObservableProperty]
    private string query = "";

    [ObservableProperty]
    private string emptyMessage = "Search by serial number, EPIC, assembly roll, name, or house number.";

    [ObservableProperty]
    private string resultSummary = "";

    public string WardId { get; private set; } = "";

    public bool HasResultSummary => !string.IsNullOrWhiteSpace(ResultSummary);

    public bool ShowEmpty => Results.Count == 0 && !IsBusy;

    partial void OnResultSummaryChanged(string value) => OnPropertyChanged(nameof(HasResultSummary));

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(ShowEmpty));

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var wardId = QueryValues.Read(query, "wardId");
        if (string.IsNullOrEmpty(wardId) || wardId == _requestedWardId)
        {
            return;
        }

        _requestedWardId = wardId;
        WardId = wardId;
    }

    [RelayCommand]
    private Task SearchAsync() =>
        ExecuteAsync(async cancellationToken =>
        {
            Results.Clear();
            ResultSummary = "";
            OnPropertyChanged(nameof(ShowEmpty));
            var validation = VoterLookup.ValidationMessage(Query);
            if (!string.IsNullOrEmpty(validation))
            {
                throw new AppException(AppErrorKind.Validation, validation);
            }

            var outcome = await _data.SearchAsync(WardId, Query, cancellationToken);
            foreach (var hit in outcome.Hits)
            {
                Results.Add(hit);
            }

            if (outcome.Hits.Count == 0)
            {
                EmptyMessage = "No voters matched that search in this ward.";
                OnPropertyChanged(nameof(ShowEmpty));
                return;
            }

            OnPropertyChanged(nameof(ShowEmpty));

            EmptyMessage = "";
            if (outcome.ScanLimitReached)
            {
                ResultSummary = "The ward list is larger than one search pass. Showing matches found so far. A serial number, EPIC, or assembly roll reference is exact.";
            }
            else if (outcome.Truncated)
            {
                ResultSummary = "Showing the first 50 matches. Refine the search to narrow it.";
            }
            else
            {
                ResultSummary = outcome.Hits.Count == 1 ? "1 voter" : $"{outcome.Hits.Count} voters";
            }
        });

    [RelayCommand]
    private Task OpenAsync(VoterSearchHit? hit)
    {
        if (hit is null || string.IsNullOrEmpty(WardId))
        {
            return Task.CompletedTask;
        }

        var route = $"{Routes.Voter}?wardId={Uri.EscapeDataString(WardId)}&serial={Uri.EscapeDataString(hit.SerialNumber)}";
        return MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
    }
}
