using VoterVerification.Services;
using VoterVerification.ViewModels;

namespace VoterVerification.Views;

public partial class VoterSearchPage : ContentPage
{
    public VoterSearchPage()
    {
        InitializeComponent();
        BindingContext = AppServices.Services.GetRequiredService<VoterSearchViewModel>();
    }
}
