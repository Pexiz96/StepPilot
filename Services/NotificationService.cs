using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StepPilot.Data;
using StepPilot.Models;

namespace StepPilot.Services;

public class NotificationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task CreateAsync(int companyId, string applicationUserId, string title, string message)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Notifications.Add(new Notification
        {
            CompanyId = companyId,
            ApplicationUserId = applicationUserId,
            Title = title,
            Message = message,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task CreateForEmployeesAsync(int companyId, IEnumerable<int> employeeIds, string title, string message)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var recipients = await db.Employees
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id) && x.ApplicationUserId != null)
            .Select(x => x.ApplicationUserId!)
            .Distinct()
            .ToListAsync();

        AddNotifications(db, companyId, recipients, title, message);

        if (recipients.Count > 0)
            await db.SaveChangesAsync();
    }

    public async Task CreateForRolesAsync(int companyId, IEnumerable<string> roles, string title, string message)
    {
        var roleNames = roles.Distinct().ToList();
        if (roleNames.Count == 0)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var roleIds = await db.Roles
            .AsNoTracking()
            .Where(x => x.Name != null && roleNames.Contains(x.Name))
            .Select(x => x.Id)
            .ToListAsync();

        if (roleIds.Count == 0)
            return;

        var recipients = await (
            from user in db.Users.AsNoTracking()
            join userRole in db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            where user.CompanyId == companyId && roleIds.Contains(userRole.RoleId)
            select user.Id)
            .Distinct()
            .ToListAsync();

        AddNotifications(db, companyId, recipients, title, message);

        if (recipients.Count > 0)
            await db.SaveChangesAsync();
    }

    public async Task<int> GetUnreadCountAsync(int companyId, string applicationUserId)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
            return 0;

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Notifications
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId &&
                             x.ApplicationUserId == applicationUserId &&
                             !x.IsRead);
    }

    private static void AddNotifications(ApplicationDbContext db, int companyId, IEnumerable<string> recipients, string title, string message)
    {
        foreach (var userId in recipients.Distinct())
        {
            db.Notifications.Add(new Notification
            {
                CompanyId = companyId,
                ApplicationUserId = userId,
                Title = title,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });
        }
    }
}
