namespace StepPilot.Data;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Planner = "Planner";
    public const string Employee = "Employee";

    public static readonly string[] All =
    [
        SuperAdmin,
        Owner,
        Admin,
        Planner,
        Employee
    ];
}
