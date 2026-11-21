using VoterVerification.Services;
using VoterVerification.ViewModels;

namespace VoterVerification.Views;

public partial class WardSelectionPage : ContentPage
{
    public WardSelectionPage()
    {
        InitializeComponent();
        BindingContext = AppServices.Services.GetRequiredService<WardSelectionViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WardSelectionViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }
}
