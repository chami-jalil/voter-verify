using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using VoterVerification.Data;
using VoterVerification.Functions.Security;
using VoterVerification.Helpers;

namespace VoterVerification.Functions.Tests;

public class AuthorizationTests
{
    private static readonly string DirectoryA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private static readonly string DirectoryB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private static readonly string SuperAdminObject = "11111111-1111-1111-1111-111111111111";
    private static readonly string AdminObject = "22222222-2222-2222-2222-222222222222";
    private static readonly string OperatorObject = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void SuperAdminCanUseMoreThanOneCustomer()
    {
        var ward10 = Decide(SuperAdmin(), "TENANT001", "10", Customer("TENANT001", "10"));
        var ward2 = Decide(SuperAdmin(), "TENANT002", "2", Customer("TENANT002", "2"));

        Assert.True(ward10.Allowed);
        Assert.Equal("TENANT001", ward10.TenantId);
        Assert.True(ward2.Allowed);
        Assert.Equal("TENANT002", ward2.TenantId);
    }

    [Fact]
    public void WardAdminStaysInsideTheAssignedCustomerAndWard()
    {
        var admin = User(DirectoryA, AdminObject, AppRole.WardAdmin, "TENANT001", "10");
        var allowed = Decide(admin, null, "10", Customer("TENANT001", "10"));
        var otherWard = Decide(admin, null, "2", Customer("TENANT001", "10", "2"));
        var otherCustomer = Decide(admin, "TENANT002", "2", Customer("TENANT001", "10"));

        Assert.True(allowed.Allowed);
        Assert.Equal(403, otherWard.StatusCode);
        Assert.Equal(403, otherCustomer.StatusCode);
        Assert.True(AccessPolicy.CanManageUser(AppRole.WardAdmin, "TENANT001", "TENANT001", AppRole.WardOperator));
        Assert.False(AccessPolicy.CanManageUser(AppRole.WardAdmin, "TENANT001", "TENANT002", AppRole.WardOperator));
        Assert.False(AccessPolicy.CanManageUser(AppRole.WardAdmin, "TENANT001", "TENANT001", AppRole.SuperAdmin));
    }

    [Fact]
    public void WardOperatorCanUseOnlyTheAssignedWard()
    {
        var op = User(DirectoryA, OperatorObject, AppRole.WardOperator, "TENANT001", "10");
        var allowed = Decide(op, "TENANT001", "10", Customer("TENANT001", "10"));
        var ward2 = Decide(op, "TENANT001", "2", Customer("TENANT001", "10", "2"));

        Assert.True(allowed.Allowed);
        Assert.Equal("10", allowed.WardId);
        Assert.Equal(403, ward2.StatusCode);
        Assert.False(AccessPolicy.CanManageCustomers(AppRole.WardOperator));
        Assert.False(AccessPolicy.CanManageUser(AppRole.WardOperator, "TENANT001", "TENANT001", AppRole.WardOperator));
        Assert.False(AccessPolicy.CanManageCustomers(AppRole.WardAdmin));
        Assert.True(AccessPolicy.CanManageCustomers(AppRole.SuperAdmin));
    }

    [Fact]
    public void ChangedTenantOrWardInTheRequestIsRejected()
    {
        var op = User(DirectoryA, OperatorObject, AppRole.WardOperator, "TENANT001", "10");
        var changedTenant = Decide(op, "TENANT002", "10", Customer("TENANT001", "10"));
        var changedWard = Decide(op, "TENANT001", "15", Customer("TENANT001", "10", "15"));

        Assert.Equal(403, changedTenant.StatusCode);
        Assert.Equal(403, changedWard.StatusCode);
        Assert.False(changedTenant.Allowed);
        Assert.False(changedWard.Allowed);
    }

    [Fact]
    public void InactiveAndUnassignedUsersAreRejected()
    {
        var inactive = User(DirectoryA, OperatorObject, AppRole.WardOperator, "TENANT001", "10", active: false);
        var unassigned = Decide(new AccessInput
        {
            DirectoryId = DirectoryA,
            ObjectId = OperatorObject,
            IsBootstrapSuperAdmin = false,
            User = null,
            Customer = Customer("TENANT001", "10"),
            RequestedWardId = "10",
            WardRequired = true
        });

        Assert.Equal(403, Decide(inactive, null, "10", Customer("TENANT001", "10")).StatusCode);
        Assert.Equal(403, unassigned.StatusCode);
        Assert.Equal(401, AccessDecision.Unauthenticated().StatusCode);
    }

    [Fact]
    public void AClientSuppliedRoleDoesNotCreateASuperAdmin()
    {
        var op = User(DirectoryB, OperatorObject, AppRole.WardOperator, "TENANT002", "2");
        var decision = Decide(op, "TENANT001", "10", Customer("TENANT002", "2"));
        Assert.Equal(403, decision.StatusCode);
        Assert.NotEqual(AppRole.SuperAdmin, decision.Role);
    }

    [Fact]
    public void ConfiguredObjectIdIsSuperAdminWithoutAClientRole()
    {
        var decision = Decide(new AccessInput
        {
            DirectoryId = DirectoryA,
            ObjectId = SuperAdminObject,
            IsBootstrapSuperAdmin = true,
            User = null,
            Customer = Customer("TENANT003", "15"),
            RequestedTenantId = "TENANT003",
            RequestedWardId = "15",
            WardRequired = true
        });

        Assert.True(decision.Allowed);
        Assert.Equal(AppRole.SuperAdmin, decision.Role);
        Assert.Equal("TENANT003", decision.TenantId);
    }

    [Fact]
    public void ExpiredAndForeignTokensAreRejected()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa);
        var audience = "api://voter-functions";
        var issuer = $"https://login.microsoftonline.com/{DirectoryA}/v2.0";
        var parameters = EntraJwt.CreateParameters([key], [audience]);

        var expired = Token(key, issuer, audience, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
        Assert.Throws<SecurityTokenExpiredException>(() => EntraJwt.ReadValidated(expired, parameters));

        var foreign = Token(key, "https://evil.example/tenant", audience, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10));
        Assert.Throws<SecurityTokenInvalidIssuerException>(() => EntraJwt.ReadValidated(foreign, parameters));

        var wrongAudience = Token(key, issuer, "api://someone-else", DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10));
        Assert.ThrowsAny<SecurityTokenException>(() => EntraJwt.ReadValidated(wrongAudience, parameters));

        var valid = Token(key, issuer, audience, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10));
        var identity = EntraJwt.ReadValidated(valid, parameters);
        Assert.Equal(DirectoryA, identity.DirectoryId);
        Assert.Equal(OperatorObject, identity.ObjectId);
    }

    [Fact]
    public void DelegatedScopeIsRequiredAndApplicationTokensAreRejected()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa);
        var audience = "api://voter-functions";
        var issuer = $"https://login.microsoftonline.com/{DirectoryA}/v2.0";
        var parameters = EntraJwt.CreateParameters([key], [audience]);
        var notBefore = DateTime.UtcNow.AddMinutes(-1);
        var expires = DateTime.UtcNow.AddMinutes(10);

        Assert.Throws<SecurityTokenException>(() =>
            EntraJwt.ReadValidated(Token(key, issuer, audience, notBefore, expires, includeScope: false), parameters));
        Assert.Throws<SecurityTokenException>(() =>
            EntraJwt.ReadValidated(Token(key, issuer, audience, notBefore, expires, includeScope: false, extra:
            [
                new System.Security.Claims.Claim("roles", "access_as_user")
            ]), parameters));
        Assert.Throws<SecurityTokenException>(() =>
            EntraJwt.ReadValidated(Token(key, issuer, audience, notBefore, expires, extra:
            [
                new System.Security.Claims.Claim("idtyp", "app")
            ]), parameters));

        var delegated = EntraJwt.ReadValidated(
            Token(key, issuer, audience, notBefore, expires, extra:
            [
                new System.Security.Claims.Claim("scp", "openid api://voter-functions/access_as_user")
            ], includeScope: false),
            parameters);
        Assert.Equal(OperatorObject, delegated.ObjectId);

        using var otherKey = RSA.Create(2048);
        var signedByOther = Token(new RsaSecurityKey(otherKey), issuer, audience, notBefore, expires);
        Assert.ThrowsAny<SecurityTokenException>(() => EntraJwt.ReadValidated(signedByOther, parameters));

        var current = EntraJwt.CreateParameters([new RsaSecurityKey(otherKey)], [audience]);
        Assert.Equal(OperatorObject, EntraJwt.ReadValidated(signedByOther, current).ObjectId);
        Assert.ThrowsAny<SecurityTokenException>(() => EntraJwt.ReadValidated(signedByOther, parameters));

        var noAudience = EntraJwt.CreateParameters([key], []);
        Assert.ThrowsAny<SecurityTokenException>(() =>
            EntraJwt.ReadValidated(Token(key, issuer, audience, notBefore, expires), noAudience));

        Assert.False(EntraJwt.IsOrganizationalIssuer("https://login.microsoftonline.com/organizations/v2.0"));
        Assert.False(EntraJwt.IsOrganizationalIssuer("https://login.microsoftonline.com/common/v2.0"));
        Assert.False(EntraJwt.IsOrganizationalIssuer("https://login.microsoftonline.com/consumers/v2.0"));
    }

    [Fact]
    public async Task UnknownSigningKeyRefreshesOnceAndInvalidSignaturesStayRejected()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var oldKey = new RsaSecurityKey(oldRsa) { KeyId = "old-key" };
        var newKey = new RsaSecurityKey(newRsa) { KeyId = "new-key" };
        var audience = "api://voter-functions";
        var issuer = $"https://login.microsoftonline.com/{DirectoryA}/v2.0";
        var notBefore = DateTime.UtcNow.AddMinutes(-1);
        var expires = DateTime.UtcNow.AddMinutes(10);
        var rotated = Token(newKey, issuer, audience, notBefore, expires);

        var refreshes = 0;
        var identity = await EntraJwt.ReadValidatedRefreshingAsync(
            rotated,
            EntraJwt.CreateParameters([oldKey], [audience]),
            _ =>
            {
                refreshes++;
                return Task.FromResult(EntraJwt.CreateParameters([newKey], [audience]));
            },
            CancellationToken.None);
        Assert.Equal(1, refreshes);
        Assert.Equal(OperatorObject, identity.ObjectId);

        refreshes = 0;
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => EntraJwt.ReadValidatedRefreshingAsync(
            rotated,
            EntraJwt.CreateParameters([oldKey], [audience]),
            _ =>
            {
                refreshes++;
                return Task.FromResult(EntraJwt.CreateParameters([oldKey], [audience]));
            },
            CancellationToken.None));
        Assert.Equal(1, refreshes);

        refreshes = 0;
        var signedByOld = Token(oldKey, issuer, audience, notBefore, expires);
        var parts = signedByOld.Split('.');
        var tampered = string.Concat(parts[0], ".", parts[1], ".", parts[2][..^1], parts[2][^1] == 'a' ? "b" : "a");
        await Assert.ThrowsAnyAsync<SecurityTokenException>(() => EntraJwt.ReadValidatedRefreshingAsync(
            tampered,
            EntraJwt.CreateParameters([oldKey], [audience]),
            _ =>
            {
                refreshes++;
                return Task.FromResult(EntraJwt.CreateParameters([newKey], [audience]));
            },
            CancellationToken.None));
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void SuperAdminRequiresBothConfiguredIdentifiers()
    {
        var configured = new FunctionSettings
        {
            SuperAdminObjectId = SuperAdminObject,
            SuperAdminDirectoryId = DirectoryA
        };
        Assert.Empty(configured.MissingSuperAdminSettings());
        Assert.True(configured.IsBootstrapSuperAdmin(DirectoryA, SuperAdminObject));
        Assert.False(configured.IsBootstrapSuperAdmin(DirectoryB, SuperAdminObject));

        var objectOnly = new FunctionSettings { SuperAdminObjectId = SuperAdminObject };
        Assert.False(objectOnly.IsBootstrapSuperAdmin(DirectoryA, SuperAdminObject));
        Assert.Contains(nameof(FunctionSettings.SuperAdminDirectoryId), objectOnly.MissingSuperAdminSettings());

        var directoryOnly = new FunctionSettings { SuperAdminDirectoryId = DirectoryA };
        Assert.False(directoryOnly.IsBootstrapSuperAdmin(DirectoryA, SuperAdminObject));
        Assert.Contains(nameof(FunctionSettings.SuperAdminObjectId), directoryOnly.MissingSuperAdminSettings());

        var malformed = new FunctionSettings
        {
            SuperAdminObjectId = "not-a-guid",
            SuperAdminDirectoryId = DirectoryA
        };
        Assert.False(malformed.IsBootstrapSuperAdmin(DirectoryA, "not-a-guid"));
        Assert.Contains(nameof(FunctionSettings.SuperAdminObjectId), malformed.MissingSuperAdminSettings());
    }

    [Fact]
    public async Task RejectedUserChangeDoesNotWriteRoleStatusOrAssignment()
    {
        var wards = new HashSet<string>(["10"], StringComparer.Ordinal);
        var wrongWard = AccessPolicy.PrepareUserChange(
            AppRole.WardAdmin, "TENANT001", wards, DirectoryA, OperatorObject, "TENANT001", AppRole.WardOperator, false, "2");
        var otherCustomer = AccessPolicy.PrepareUserChange(
            AppRole.WardAdmin, "TENANT001", wards, DirectoryA, OperatorObject, "TENANT002", AppRole.WardOperator, false, "10");
        var promote = AccessPolicy.PrepareUserChange(
            AppRole.WardAdmin, "TENANT001", wards, DirectoryA, OperatorObject, "TENANT001", AppRole.SuperAdmin, true, "10");

        Assert.Null(wrongWard.User);
        Assert.Null(wrongWard.AssignmentWardId);
        Assert.Equal(403, otherCustomer.StatusCode);
        Assert.Equal(403, promote.StatusCode);
        Assert.Equal((0, 0), await CountWritesAsync(wrongWard));
        Assert.Equal((0, 0), await CountWritesAsync(otherCustomer));
        Assert.Equal((0, 0), await CountWritesAsync(promote));
    }

    [Fact]
    public async Task AcceptedUserChangeWritesOnlyAfterAuthorization()
    {
        var order = new List<string>();
        var plan = AccessPolicy.PrepareUserChange(
            AppRole.WardAdmin,
            "TENANT001",
            new HashSet<string>(["10"], StringComparer.Ordinal),
            DirectoryA,
            OperatorObject,
            "TENANT002",
            AppRole.WardOperator,
            true,
            "10");
        Assert.False(plan.Allowed);

        plan = AccessPolicy.PrepareUserChange(
            AppRole.WardAdmin,
            "TENANT001",
            new HashSet<string>(["10"], StringComparer.Ordinal),
            DirectoryA,
            OperatorObject,
            "TENANT001",
            AppRole.WardOperator,
            false,
            "10");
        Assert.True(plan.Allowed);
        Assert.Equal(AppRole.WardOperator, plan.User!.Role);
        Assert.False(plan.User.IsActive);
        await AccessPolicy.ApplyUserChangeAsync(
            plan,
            (_, _) =>
            {
                order.Add("user");
                return Task.CompletedTask;
            },
            (_, _, tenantId, wardId, _) =>
            {
                order.Add($"assignment:{tenantId}:{wardId}");
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Assert.Equal(["user", "assignment:TENANT001:10"], order);
    }

    [Fact]
    public void SearchScopeNeverReturnsAnotherTenant()
    {
        var rows = new Dictionary<string, IReadOnlyList<VoterTableEntity>>(StringComparer.Ordinal)
        {
            ["10"] =
            [
                Voter("10", "1", "", "10"),
                Voter("10", "2", "TENANT002", "10")
            ],
            ["TENANT001|10"] = [Voter("TENANT001|10", "3", "TENANT001", "10")],
            ["TENANT002|2"] = [Voter("TENANT002|2", "9", "TENANT002", "2")]
        };

        var visible = TenantPartitions.VisibleVoters("TENANT001", "10", "TENANT001", rows);
        Assert.Equal(["1", "3"], visible.Select(row => row.RowKey).OrderBy(value => value).ToArray());
        Assert.Equal("10", TenantPartitions.PartitionForSerial(visible, "1"));
        Assert.Null(TenantPartitions.PartitionForSerial(visible, "9"));

        var otherCustomer = TenantPartitions.VisibleVoters("TENANT002", "10", "TENANT001", rows);
        Assert.Empty(otherCustomer);
        Assert.Contains("10", TenantPartitions.ReadKeys("TENANT001", "10", "TENANT001"));
        Assert.DoesNotContain("10", TenantPartitions.ReadKeys("TENANT002", "10", "TENANT001"));
        Assert.False(TenantPartitions.CanRead("10", "", "10", "1", "TENANT002", "10", "TENANT001"));
        Assert.False(TenantPartitions.CanRead("10", "TENANT002", "10", "2", "TENANT001", "10", "TENANT001"));
    }

    [Fact]
    public void VerificationWriteStaysOnTheVisibleVoterPartition()
    {
        var visible = TenantPartitions.VisibleVoters("TENANT001", "10", "TENANT001", new Dictionary<string, IReadOnlyList<VoterTableEntity>>
        {
            ["10"] = [Voter("10", "1", "", "10")],
            ["TENANT002|2"] = [Voter("TENANT002|2", "1", "TENANT002", "2")]
        });

        Assert.Equal("10", TenantPartitions.PartitionForSerial(visible, "1"));
        Assert.Null(TenantPartitions.PartitionForSerial(
            TenantPartitions.VisibleVoters("TENANT002", "2", "TENANT001", new Dictionary<string, IReadOnlyList<VoterTableEntity>>
            {
                ["10"] = [Voter("10", "1", "", "10")]
            }),
            "1"));
    }

    [Fact]
    public void FunctionSourceDoesNotContainStorageSecrets()
    {
        var root = FindFunctionsProject();
        var source = string.Join('\n', Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("AccountKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedAccessSignature", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultEndpointsProtocol", source, StringComparison.Ordinal);
        Assert.Contains("DefaultAzureCredential", source, StringComparison.Ordinal);
        Assert.Contains("AuthorizationLevel.Anonymous", source, StringComparison.Ordinal);
        Assert.Contains("SecurityTokenException", source, StringComparison.Ordinal);
        Assert.Contains("RequireDelegatedUserToken", source, StringComparison.Ordinal);

        var api = File.ReadAllText(Path.Combine(root, "Http", "ApiFunctions.cs"));
        Assert.Equal(10, Count(api, "HttpTrigger"));
        Assert.Equal(10, Count(api, "RunAsync("));
        Assert.Contains("ex.GetType().Name", api, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.ToString", api, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", api, StringComparison.Ordinal);
        Assert.DoesNotContain("EpicNumber", api, StringComparison.Ordinal);
        Assert.DoesNotContain("PhoneNumber", api, StringComparison.Ordinal);
        var lines = api.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("_logger.Log", StringComparison.Ordinal))
            {
                continue;
            }

            var window = string.Join('\n', lines.Skip(index).Take(8));
            Assert.DoesNotContain("ex.Message", window, StringComparison.Ordinal);
            Assert.DoesNotContain("Headers.Authorization", window, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", window, StringComparison.Ordinal);
            Assert.DoesNotContain("Query", window, StringComparison.Ordinal);
        }

        var repo = FindRepositoryRoot();
        var client = File.ReadAllText(Path.Combine(repo, "Services", "FunctionsVoterDataService.cs"));
        var configuration = File.ReadAllText(Path.Combine(repo, "Configuration", "AppConfiguration.cs"));
        Assert.DoesNotContain("AccountKey", client, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedAccessSignature", client, StringComparison.Ordinal);
        Assert.DoesNotContain("x-functions-key", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountKey", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("SharedAccessSignature", configuration, StringComparison.Ordinal);
        Assert.Contains("DefaultAzureCredential", source, StringComparison.Ordinal);
    }

    private static async Task<(int Users, int Assignments)> CountWritesAsync(UserChangePlan plan)
    {
        var users = 0;
        var assignments = 0;
        await AccessPolicy.ApplyUserChangeAsync(
            plan,
            (_, _) =>
            {
                users++;
                return Task.CompletedTask;
            },
            (_, _, _, _, _) =>
            {
                assignments++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        return (users, assignments);
    }

    private static string FindFunctionsProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "VoterVerification.Functions");
            if (File.Exists(Path.Combine(candidate, "VoterVerification.Functions.csproj")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Functions project was not found beside the test output.");
    }

    private static AccessDecision Decide(DirectoryUser user, string? requestedTenant, string wardId, CustomerAccount customer) =>
        Decide(new AccessInput
        {
            DirectoryId = user.DirectoryId,
            ObjectId = user.ObjectId,
            IsBootstrapSuperAdmin = false,
            User = user,
            Customer = customer,
            RequestedTenantId = requestedTenant,
            RequestedWardId = wardId,
            WardRequired = true
        });

    private static AccessDecision Decide(AccessInput input) => AccessPolicy.Decide(input);

    private static DirectoryUser SuperAdmin() => new()
    {
        DirectoryId = DirectoryA,
        ObjectId = SuperAdminObject,
        Role = AppRole.SuperAdmin,
        IsActive = true,
        TenantId = null
    };

    private static DirectoryUser User(string directory, string objectId, AppRole role, string tenant, string ward, bool active = true) =>
        new()
        {
            DirectoryId = directory,
            ObjectId = objectId,
            Role = role,
            TenantId = tenant,
            IsActive = active,
            WardIds = new HashSet<string>([ward], StringComparer.Ordinal)
        };

    private static CustomerAccount Customer(string tenant, params string[] wards) => new()
    {
        TenantId = tenant,
        Name = tenant,
        IsActive = true,
        WardIds = new HashSet<string>(wards, StringComparer.Ordinal)
    };

    private static VoterTableEntity Voter(string partition, string serial, string tenant, string ward) => new()
    {
        PartitionKey = partition,
        RowKey = serial,
        TenantId = tenant,
        WardId = ward,
        WardVoterSerialNumber = serial
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(FindFunctionsProject());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VoterVerification.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Token(
        RsaSecurityKey key,
        string issuer,
        string audience,
        DateTime notBefore,
        DateTime expires,
        bool includeScope = true,
        IEnumerable<System.Security.Claims.Claim>? extra = null)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("oid", OperatorObject),
            new("tid", DirectoryA)
        };
        if (includeScope)
        {
            claims.Add(new System.Security.Claims.Claim("scp", EntraJwt.DelegatedScope));
        }

        if (extra is not null)
        {
            claims.AddRange(extra);
        }

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore,
            expires,
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
