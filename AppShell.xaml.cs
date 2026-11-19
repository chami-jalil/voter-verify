using VoterVerification.Helpers;
using VoterVerification.Views;

namespace VoterVerification;

public partial class AppShell : Shell
{
    public AppShell(LoginPage loginPage)
    {
        InitializeComponent();
        Items.Add(new ShellContent
        {
            Content = loginPage,
            Route = Routes.Login,
            Title = "Sign in"
        });

        Routing.RegisterRoute(Routes.Wards, typeof(WardSelectionPage));
        Routing.RegisterRoute(Routes.Dashboard, typeof(DashboardPage));
        Routing.RegisterRoute(Routes.Search, typeof(VoterSearchPage));
        Routing.RegisterRoute(Routes.Voter, typeof(VoterDetailPage));
    }
}
