using Microsoft.Extensions.Logging;
using VoterVerification.Authentication;
using VoterVerification.Services;
using VoterVerification.ViewModels;
using VoterVerification.Views;

namespace VoterVerification;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("NotoSansTelugu-Regular.ttf", "NotoSansTelugu");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<SecureMsalTokenCache>();
        builder.Services.AddSingleton<IEntraAuthService, EntraAuthService>();
        builder.Services.AddSingleton<IVoterDataService, AzureTableVoterDataService>();
        builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
        builder.Services.AddSingleton<IConfirmationDialog, MauiConfirmationDialog>();
        builder.Services.AddSingleton<ISessionCoordinator, SessionCoordinator>();

        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<WardSelectionViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<VoterSearchViewModel>();
        builder.Services.AddTransient<VoterDetailViewModel>();

        var app = builder.Build();
        AppServices.Services = app.Services;
        RegisterExceptionLogging(app.Services);
        return app;
    }

    private static void RegisterExceptionLogging(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("VoterVerification");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                logger.LogError("Unhandled exception {ExceptionType}.", exception.GetType().Name);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError("Unobserved task exception {ExceptionType}.", args.Exception.GetType().Name);
            args.SetObserved();
        };
    }
}
