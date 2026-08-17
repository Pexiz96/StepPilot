namespace StepPilot.Services;

public sealed class TenantGuard
{
    private readonly CurrentUserService _currentUser;

    public TenantGuard(CurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public async Task<int> RequireCompanyIdAsync()
    {
        var companyId = await _currentUser.GetCompanyIdAsync();

        if (companyId is null)
            throw new UnauthorizedAccessException("Für diesen Benutzer ist kein Unternehmen hinterlegt.");

        return companyId.Value;
    }

    public async Task EnsureCompanyAccessAsync(int companyId)
    {
        if (await _currentUser.IsSuperAdminAsync())
            return;

        var current = await RequireCompanyIdAsync();

        if (current != companyId)
            throw new UnauthorizedAccessException("Kein Zugriff auf ein anderes Unternehmen.");
    }
}
