using Microsoft.AspNetCore.Authorization;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Services;

// Usage: [Authorize(Policy = nameof(Permission.ManageUsers))] on a controller/action.
// Policies are registered for every Permission value in Program.cs.
public class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var hasPermission = context.User.Claims
            .Any(c => c.Type == "permission" && c.Value == requirement.Permission.ToString());

        if (hasPermission)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
