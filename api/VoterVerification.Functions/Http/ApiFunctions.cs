using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using VoterVerification.Functions.Data;
using VoterVerification.Functions.Security;
using VoterVerification.Models;
using VoterVerification.Services;

namespace VoterVerification.Functions.Http;

public sealed class ApiFunctions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RequestGate _gate;
    private readonly VoterGateway _voters;
    private readonly AccountStore _accounts;
    private readonly ILogger<ApiFunctions> _logger;

    public ApiFunctions(RequestGate gate, VoterGateway voters, AccountStore accounts, ILogger<ApiFunctions> logger)
    {
        _gate = gate;
        _voters = voters;
        _accounts = accounts;
        _logger = logger;
    }

    [Function("GetProfile")]
    public Task<IActionResult> GetProfile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(request, "GetProfile", wardRequired: false, wardId: null, (decision, identity, _) =>
            Task.FromResult(Ok(new
            {
                objectId = identity.ObjectId,
                directoryId = identity.DirectoryId,
                role = decision.Role?.ToString(),
                tenantId = decision.TenantId,
                status = "Active"
            })), cancellationToken);

    [Function("GetWards")]
    public Task<IActionResult> GetWards(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wards")] HttpRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(request, "GetWards", wardRequired: false, wardId: null, async (decision, _, token) =>
        {
            var wards = await _accounts.GetVisibleWardsAsync(decision, token);
            return Ok(wards);
        }, cancellationToken);

    [Function("GetDashboard")]
    public Task<IActionResult> GetDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wards/{wardId}/dashboard")] HttpRequest request,
        string wardId,
        CancellationToken cancellationToken) =>
        RunAsync(request, "GetDashboard", wardRequired: true, wardId, async (decision, _, token) =>
            Ok(await _voters.GetStatsAsync(decision.TenantId!, wardId, token)), cancellationToken);

    [Function("SearchVoters")]
    public Task<IActionResult> SearchVoters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wards/{wardId}/voters")] HttpRequest request,
        string wardId,
        CancellationToken cancellationToken) =>
        RunAsync(request, "SearchVoters", wardRequired: true, wardId, async (decision, _, token) =>
        {
            var query = request.Query["q"].ToString();
            return Ok(await _voters.SearchAsync(decision.TenantId!, wardId, query, token));
        }, cancellationToken);

    [Function("GetVoter")]
    public Task<IActionResult> GetVoter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wards/{wardId}/voters/{serial}")] HttpRequest request,
        string wardId,
        string serial,
        CancellationToken cancellationToken) =>
        RunAsync(request, "GetVoter", wardRequired: true, wardId, async (decision, _, token) =>
        {
            var details = await _voters.GetVoterAsync(decision.TenantId!, wardId, serial, token);
            return details is null ? NotFound() : Ok(details);
        }, cancellationToken);

    [Function("GetVerificationHistory")]
    public Task<IActionResult> GetVerificationHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "wards/{wardId}/voters/{serial}/verification")] HttpRequest request,
        string wardId,
        string serial,
        CancellationToken cancellationToken) =>
        RunAsync(request, "GetVerificationHistory", wardRequired: true, wardId, async (decision, _, token) =>
        {
            var details = await _voters.GetVoterAsync(decision.TenantId!, wardId, serial, token);
            if (details is null)
            {
                return NotFound();
            }

            var records = details.Verification is null ? Array.Empty<VoterVerificationRecord>() : new[] { details.Verification };
            return Ok(new { records });
        }, cancellationToken);

    [Function("SaveVerification")]
    public Task<IActionResult> SaveVerification(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "wards/{wardId}/voters/{serial}/verification")] HttpRequest request,
        string wardId,
        string serial,
        CancellationToken cancellationToken) =>
        RunAsync(request, "SaveVerification", wardRequired: true, wardId, async (decision, identity, token) =>
        {
            var draft = await request.ReadFromJsonAsync<VerificationDraft>(Json, token);
            if (draft is null)
            {
                return Bad("The verification could not be read.");
            }

            if (!string.Equals(draft.WardId, wardId, StringComparison.Ordinal)
                || !string.Equals(draft.SerialNumber, serial, StringComparison.Ordinal))
            {
                return Forbidden("You cannot use this ward.");
            }

            var saved = await _voters.SaveAsync(decision.TenantId!, wardId, draft, RequestGate.ToSignedInUser(identity), token);
            return Ok(saved);
        }, cancellationToken);

    [Function("CreateCustomer")]
    public Task<IActionResult> CreateCustomer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/customers")] HttpRequest request,
        CancellationToken cancellationToken) =>
        RunAsync(request, "CreateCustomer", wardRequired: false, wardId: null, async (decision, _, token) =>
        {
            if (!AccessPolicy.CanManageCustomers(decision.Role!.Value))
            {
                return Forbidden("You cannot manage customers.");
            }

            var body = await request.ReadFromJsonAsync<CustomerWrite>(Json, token);
            if (body is null || !VoterVerification.Helpers.TenantPartitions.IsCustomerTenantId(body.TenantId) || string.IsNullOrWhiteSpace(body.CustomerName))
            {
                return Bad("Enter a customer id and name.");
            }

            await _accounts.SaveCustomerAsync(body.TenantId, body.CustomerName, body.Active, token);
            if (!string.IsNullOrWhiteSpace(body.WardId) && VoterVerification.Helpers.TableKeyRules.IsValid(body.WardId))
            {
                await _accounts.SaveWardAsync(body.TenantId, body.WardId, body.WardName ?? $"Ward {body.WardId}", body.ExpectedVoters, true, token);
            }

            return Ok(new { tenantId = body.TenantId });
        }, cancellationToken);

    [Function("AssignUser")]
    public Task<IActionResult> AssignUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "admin/users")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ManageUserAsync(request, "AssignUser", cancellationToken);

    [Function("ManageTenantUser")]
    public Task<IActionResult> ManageTenantUser(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "tenant/users")] HttpRequest request,
        CancellationToken cancellationToken) =>
        ManageUserAsync(request, "ManageTenantUser", cancellationToken);

    private async Task<IActionResult> ManageUserAsync(HttpRequest request, string operation, CancellationToken cancellationToken) =>
        await RunAsync(request, operation, wardRequired: false, wardId: null, async (decision, actor, token) =>
        {
            _ = actor;
            var body = await request.ReadFromJsonAsync<UserWrite>(Json, token);
            if (body is null
                || !Guid.TryParse(body.DirectoryId, out Guid _)
                || !Guid.TryParse(body.ObjectId, out Guid _)
                || !Enum.TryParse<AppRole>(body.Role, out var role))
            {
                return Bad("Enter the Entra directory id, object id, and role.");
            }

            var actorWards = decision.Role == AppRole.SuperAdmin
                ? new HashSet<string>(StringComparer.Ordinal)
                : (await _accounts.GetUserAsync(decision.DirectoryId, decision.ObjectId, token))?.WardIds
                    ?? new HashSet<string>(StringComparer.Ordinal);
            var plan = AccessPolicy.PrepareUserChange(
                decision.Role!.Value,
                decision.TenantId,
                actorWards,
                body.DirectoryId,
                body.ObjectId,
                body.TenantId,
                role,
                body.Active,
                body.WardId);
            if (!plan.Allowed)
            {
                return plan.StatusCode == 400 ? Bad(plan.Message) : Forbidden(plan.Message);
            }

            await AccessPolicy.ApplyUserChangeAsync(
                plan,
                (user, ct) => _accounts.SaveUserAsync(user, ct),
                (directoryId, objectId, tenantId, ward, ct) => _accounts.SaveAssignmentAsync(directoryId, objectId, tenantId, ward, true, ct),
                token);
            return Ok(new { objectId = body.ObjectId, tenantId = plan.TargetTenantId, role = role.ToString() });
        }, cancellationToken);

    private async Task<IActionResult> RunAsync(
        HttpRequest request,
        string operation,
        bool wardRequired,
        string? wardId,
        Func<AccessDecision, EntraIdentity, CancellationToken, Task<IActionResult>> action,
        CancellationToken cancellationToken)
    {
        var trace = request.HttpContext.TraceIdentifier;
        var (decision, identity) = await _gate.AuthorizeAsync(
            request.Headers.Authorization,
            RequestedTenant(request),
            wardId,
            wardRequired,
            cancellationToken);
        if (!decision.Allowed || identity is null)
        {
            _logger.LogWarning(
                "Authorization failed. Operation {Operation} User {UserId} Tenant {TenantId} Ward {WardId} Status {Status} Trace {TraceId}",
                operation,
                decision.ObjectId,
                decision.TenantId,
                wardId,
                decision.StatusCode,
                trace);
            return new ObjectResult(new { error = decision.Message }) { StatusCode = decision.StatusCode };
        }

        try
        {
            var result = await action(decision, identity, cancellationToken);
            _logger.LogInformation(
                "Completed. Operation {Operation} User {UserId} Tenant {TenantId} Ward {WardId} Trace {TraceId}",
                operation,
                decision.ObjectId,
                decision.TenantId,
                wardId,
                trace);
            return result;
        }
        catch (AppException ex)
        {
            var status = ex.Kind switch
            {
                AppErrorKind.NotAuthorized => 403,
                AppErrorKind.NotFound => 404,
                AppErrorKind.Conflict => 409,
                AppErrorKind.Validation => 400,
                _ => 503
            };
            _logger.LogWarning(
                "Rejected. Operation {Operation} User {UserId} Tenant {TenantId} Ward {WardId} Kind {Kind} Trace {TraceId}",
                operation,
                decision.ObjectId,
                decision.TenantId,
                wardId,
                ex.Kind,
                trace);
            return new ObjectResult(new { error = ex.Message }) { StatusCode = status };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Failed. Operation {Operation} User {UserId} Tenant {TenantId} Ward {WardId} Exception {ExceptionType} Trace {TraceId}",
                operation,
                decision.ObjectId,
                decision.TenantId,
                wardId,
                ex.GetType().Name,
                trace);
            return new ObjectResult(new { error = "The request could not be completed." }) { StatusCode = 500 };
        }
    }

    private static string? RequestedTenant(HttpRequest request)
    {
        var header = request.Headers["X-Customer-Tenant"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    private static IActionResult Ok(object body) => new OkObjectResult(body);

    private static IActionResult NotFound() => new ObjectResult(new { error = "That record was not found." }) { StatusCode = 404 };

    private static IActionResult Bad(string message) => new ObjectResult(new { error = message }) { StatusCode = 400 };

    private static IActionResult Forbidden(string message) => new ObjectResult(new { error = message }) { StatusCode = 403 };

    private sealed class CustomerWrite
    {
        public string TenantId { get; set; } = "";

        public string CustomerName { get; set; } = "";

        public bool Active { get; set; } = true;

        public string? WardId { get; set; }

        public string? WardName { get; set; }

        public int? ExpectedVoters { get; set; }
    }

    private sealed class UserWrite
    {
        public string DirectoryId { get; set; } = "";

        public string ObjectId { get; set; } = "";

        public string? TenantId { get; set; }

        public string Role { get; set; } = "";

        public bool Active { get; set; } = true;

        public string? WardId { get; set; }
    }
}
