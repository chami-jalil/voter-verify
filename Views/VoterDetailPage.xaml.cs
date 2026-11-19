using VoterVerification.Services;
using VoterVerification.ViewModels;

namespace VoterVerification.Views;

public partial class VoterDetailPage : ContentPage
{
    public VoterDetailPage()
    {
        InitializeComponent();
        BindingContext = AppServices.Services.GetRequiredService<VoterDetailViewModel>();
    }
}
