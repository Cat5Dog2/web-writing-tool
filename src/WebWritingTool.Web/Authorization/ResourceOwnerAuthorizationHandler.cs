using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using WebWritingTool.Application.Security;

namespace WebWritingTool.Web.Authorization;

public sealed class ResourceOwnerAuthorizationHandler
    : AuthorizationHandler<ResourceOwnerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.CompletedTask;
        }

        if (context.User.IsInRole(ApplicationRoles.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is IUserOwnedResource ownedResource
            && string.Equals(ownedResource.UserId, userId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
