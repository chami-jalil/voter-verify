using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoterVerification.Configuration;
using VoterVerification.Functions.Data;
using VoterVerification.Functions.Security;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
    var settings = new FunctionSettings
    {
        StorageAccountName = configuration["StorageAccountName"] ?? "",
        InitialTenantId = configuration["InitialTenantId"],
        SuperAdminObjectId = configuration["SuperAdminObjectId"] ?? "",
        SuperAdminDirectoryId = configuration["SuperAdminDirectoryId"] ?? "",
        ApiAudience = configuration["EntraApiAudience"] ?? "",
        ApiClientId = configuration["EntraApiClientId"] ?? ""
    };
    var missing = settings.MissingSuperAdminSettings();
    if (missing.Count > 0)
    {
        throw new InvalidOperationException(
            "Set " + string.Join(" and ", missing) + ". SuperAdmin access requires both the Entra object id and directory id.");
    }

    return settings;
});
builder.Services.AddSingleton<EntraAccessTokenValidator>();
builder.Services.AddSingleton(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<FunctionSettings>();
    if (string.IsNullOrWhiteSpace(settings.StorageAccountName)
        || settings.StorageAccountName.Contains('=', StringComparison.Ordinal)
        || settings.StorageAccountName.Contains(';', StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Set StorageAccountName to the storage account name only.");
    }

    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });
    var options = new TableClientOptions
    {
        Diagnostics =
        {
            IsLoggingContentEnabled = false,
            IsTelemetryEnabled = false
        }
    };
    var service = new TableServiceClient(
        new Uri($"https://{settings.StorageAccountName}.table.core.windows.net"),
        credential,
        options);
    TableClient Table(string name) => service.GetTableClient(name);
    return new TableBundle(
        Table(AppConfiguration.VotersTableName),
        Table(AppConfiguration.VerificationsTableName),
        Table("Customers"),
        Table("CustomerWards"),
        Table("AppUsers"),
        Table("UserWardAssignments"));
});
builder.Services.AddSingleton(serviceProvider =>
{
    var bundle = serviceProvider.GetRequiredService<TableBundle>();
    var settings = serviceProvider.GetRequiredService<FunctionSettings>();
    return new VoterGateway(bundle.Voters, bundle.Verifications, settings.InitialTenantId);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var bundle = serviceProvider.GetRequiredService<TableBundle>();
    return new AccountStore(bundle.Customers, bundle.Wards, bundle.Users, bundle.Assignments);
});
builder.Services.AddSingleton<RequestGate>();

var host = builder.Build();
host.Services.GetRequiredService<FunctionSettings>();
host.Run();

internal sealed record TableBundle(
    TableClient Voters,
    TableClient Verifications,
    TableClient Customers,
    TableClient Wards,
    TableClient Users,
    TableClient Assignments);
