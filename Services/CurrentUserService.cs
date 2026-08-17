using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using StepPilot.Data;

namespace StepPilot.Services;

public sealed class CurrentUserService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserService(
        AuthenticationStateProvider authenticationStateProvider,
        UserManager<ApplicationUser> userManager)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _userManager = userManager;
    }

    public async Task<ApplicationUser?> GetUserAsync()
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();

        if (state.User.Identity?.IsAuthenticated != true)
            return null;

        return await _userManager.GetUserAsync(state.User);
    }

    public async Task<int?> GetCompanyIdAsync() => (await GetUserAsync())?.CompanyId;

    public async Task<bool> IsSuperAdminAsync()
    {
        var user = await GetUserAsync();
        return user is not null && await _userManager.IsInRoleAsync(user, AppRoles.SuperAdmin);
    }
}
