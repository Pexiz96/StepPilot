using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using StepPilot.Data;

namespace StepPilot.Services;

public sealed class ActiveUserRequirement : IAuthorizationRequirement
{
}

public sealed class ActiveUserAuthorizationHandler : AuthorizationHandler<ActiveUserRequirement>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public ActiveUserAuthorizationHandler(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var user = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new { x.IsActive, x.CompanyId })
            .FirstOrDefaultAsync();

        if (user is null || !user.IsActive)
            return;

        if (context.User.IsInRole(AppRoles.SuperAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        if (user.CompanyId is null)
            return;

        var companyIsActive = await db.Companies
            .AsNoTracking()
            .AnyAsync(x => x.Id == user.CompanyId.Value && x.IsActive);

        if (companyIsActive)
            context.Succeed(requirement);
    }
}
