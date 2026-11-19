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
}
