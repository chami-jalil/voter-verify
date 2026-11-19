using VoterVerification.Services;
using VoterVerification.ViewModels;

namespace VoterVerification.Views;

public partial class DashboardPage : ContentPage
{
    public DashboardPage()
    {
        InitializeComponent();
        BindingContext = AppServices.Services.GetRequiredService<DashboardViewModel>();
    }
}
