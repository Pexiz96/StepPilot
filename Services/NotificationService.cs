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

        foreach (var userId in recipients)
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

        if (recipients.Count > 0)
            await db.SaveChangesAsync();
    }
}
