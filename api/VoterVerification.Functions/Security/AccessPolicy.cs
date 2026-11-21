using VoterVerification.Helpers;

namespace VoterVerification.Functions.Security;

public static class AccessPolicy
{
    public static AccessDecision Decide(AccessInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ObjectId) || string.IsNullOrWhiteSpace(input.DirectoryId))
        {
            return AccessDecision.Unauthenticated();
        }

        if (input.User is { IsActive: false } && !input.IsBootstrapSuperAdmin)
        {
            return Deny(input, "This account is inactive.");
        }

        if (!input.IsBootstrapSuperAdmin && input.User is null)
        {
            return Deny(input, "This account is not assigned to Voter Verification.");
        }

        if (!input.IsBootstrapSuperAdmin && input.User is { IsActive: false })
        {
            return Deny(input, "This account is inactive.");
        }

        var role = input.IsBootstrapSuperAdmin ? AppRole.SuperAdmin : input.User!.Role;
        if (role == AppRole.SuperAdmin)
        {
            return DecideSuperAdmin(input, role);
        }

        var user = input.User!;
        if (string.IsNullOrWhiteSpace(user.TenantId))
        {
            return Deny(input, "This account is not assigned to a customer.");
        }

        if (!string.IsNullOrWhiteSpace(input.RequestedTenantId)
            && !string.Equals(input.RequestedTenantId, user.TenantId, StringComparison.Ordinal))
        {
            return Deny(input, "You cannot use another customer's tenant.");
        }

        if (input.Customer is null || !input.Customer.IsActive
            || !string.Equals(input.Customer.TenantId, user.TenantId, StringComparison.Ordinal))
        {
            return Deny(input, "This customer account is inactive.");
        }

        if (!input.WardRequired)
        {
            return Allow(input, role, user.TenantId, null);
        }

        var wardId = input.RequestedWardId ?? "";
        if (wardId.Length == 0 || !user.WardIds.Contains(wardId) || !input.Customer.WardIds.Contains(wardId))
        {
            return Deny(input, "You cannot use this ward.");
        }

        return Allow(input, role, user.TenantId, wardId);
    }

    public static bool CanManageCustomers(AppRole role) => role == AppRole.SuperAdmin;

    public static bool CanManageUser(AppRole actorRole, string? actorTenantId, string targetTenantId, AppRole targetRole)
    {
        if (actorRole == AppRole.SuperAdmin)
        {
            return true;
        }

        return actorRole == AppRole.WardAdmin
            && !string.IsNullOrEmpty(actorTenantId)
            && string.Equals(actorTenantId, targetTenantId, StringComparison.Ordinal)
            && targetRole != AppRole.SuperAdmin;
    }

    public static bool CanAssignWard(AppRole actorRole, string? actorTenantId, IReadOnlySet<string> actorWards, string targetTenantId, string wardId)
    {
        if (actorRole == AppRole.SuperAdmin)
        {
            return true;
        }

        return actorRole == AppRole.WardAdmin
            && string.Equals(actorTenantId, targetTenantId, StringComparison.Ordinal)
            && actorWards.Contains(wardId);
    }

    public static UserChangePlan PrepareUserChange(
        AppRole actorRole,
        string? actorTenantId,
        IReadOnlySet<string> actorWards,
        string targetDirectoryId,
        string targetObjectId,
        string? requestedTenantId,
        AppRole targetRole,
        bool active,
        string? wardId)
    {
        if (actorRole != AppRole.SuperAdmin
            && !string.IsNullOrWhiteSpace(requestedTenantId)
            && !string.Equals(requestedTenantId, actorTenantId, StringComparison.Ordinal))
        {
            return UserChangePlan.Reject(403, "You cannot use another customer's tenant.");
        }

        var targetTenant = actorRole == AppRole.SuperAdmin ? requestedTenantId : actorTenantId;
        if (string.IsNullOrWhiteSpace(targetTenant)
            || !CanManageUser(actorRole, actorTenantId, targetTenant, targetRole))
        {
            return UserChangePlan.Reject(403, "You cannot assign that user.");
        }

        if (targetRole != AppRole.SuperAdmin && !TenantPartitions.IsCustomerTenantId(targetTenant))
        {
            return UserChangePlan.Reject(400, "Enter the customer id.");
        }

        string? assignmentWard = null;
        if (!string.IsNullOrWhiteSpace(wardId))
        {
            if (!TableKeyRules.IsValid(wardId)
                || !CanAssignWard(actorRole, actorTenantId, actorWards, targetTenant, wardId))
            {
                return UserChangePlan.Reject(403, "You cannot assign that ward.");
            }

            assignmentWard = wardId;
        }

        return new UserChangePlan(
            true,
            200,
            "",
            new DirectoryUser
            {
                DirectoryId = targetDirectoryId,
                ObjectId = targetObjectId,
                TenantId = targetRole == AppRole.SuperAdmin ? null : targetTenant,
                Role = targetRole,
                IsActive = active
            },
            targetTenant,
            assignmentWard);
    }

    public static async Task ApplyUserChangeAsync(
        UserChangePlan plan,
        Func<DirectoryUser, CancellationToken, Task> saveUser,
        Func<string, string, string, string, CancellationToken, Task> saveAssignment,
        CancellationToken cancellationToken)
    {
        if (!plan.Allowed || plan.User is null || string.IsNullOrWhiteSpace(plan.TargetTenantId))
        {
            return;
        }

        await saveUser(plan.User, cancellationToken);
        if (!string.IsNullOrWhiteSpace(plan.AssignmentWardId))
        {
            await saveAssignment(
                plan.User.DirectoryId,
                plan.User.ObjectId,
                plan.TargetTenantId,
                plan.AssignmentWardId,
                cancellationToken);
        }
    }

    private static AccessDecision DecideSuperAdmin(AccessInput input, AppRole role)
    {
        if (!input.WardRequired && string.IsNullOrWhiteSpace(input.RequestedTenantId))
        {
            return Allow(input, role, null, null);
        }

        if (string.IsNullOrWhiteSpace(input.RequestedTenantId))
        {
            return Deny(input, "Choose a customer before using a ward.");
        }

        if (input.Customer is null
            || !input.Customer.IsActive
            || !string.Equals(input.Customer.TenantId, input.RequestedTenantId, StringComparison.Ordinal))
        {
            return Deny(input, "That customer account is inactive or unknown.");
        }

        if (!input.WardRequired)
        {
            return Allow(input, role, input.RequestedTenantId, null);
        }

        var wardId = input.RequestedWardId ?? "";
        if (wardId.Length == 0 || !input.Customer.WardIds.Contains(wardId))
        {
            return Deny(input, "That ward is not assigned to the customer.");
        }

        return Allow(input, role, input.RequestedTenantId, wardId);
    }

    private static AccessDecision Allow(AccessInput input, AppRole role, string? tenantId, string? wardId) =>
        new(true, 200, "", tenantId, wardId, role, input.ObjectId, input.DirectoryId);

    private static AccessDecision Deny(AccessInput input, string message) =>
        new(false, 403, message, null, null, null, input.ObjectId, input.DirectoryId);
}
