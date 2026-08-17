using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StepPilot.Models;

namespace StepPilot.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<EmployeeQualification> EmployeeQualifications => Set<EmployeeQualification>();
    public DbSet<Availability> Availabilities => Set<Availability>();
    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ScheduleChange> ScheduleChanges => Set<ScheduleChange>();
    public DbSet<EmployeeShiftPreference> EmployeeShiftPreferences
    => Set<EmployeeShiftPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>()
            .HasIndex(x => x.Slug)
            .IsUnique();

        builder.Entity<ApplicationUser>()
            .HasOne(x => x.Company)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EmployeeQualification>()
            .HasKey(x => new { x.EmployeeId, x.QualificationId });

        builder.Entity<Employee>()
            .HasIndex(x => new { x.CompanyId, x.EmployeeNumber })
            .IsUnique();

        builder.Entity<ShiftAssignment>()
            .HasIndex(x => new { x.CompanyId, x.ShiftId, x.EmployeeId })
            .IsUnique();

        builder.Entity<Employee>()
            .HasOne(x => x.ApplicationUser)
            .WithOne()
            .HasForeignKey<Employee>(x => x.ApplicationUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Location>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Department>()
            .HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Employee>()
            .Property(x => x.WeeklyHours)
            .HasPrecision(5, 2);
        builder.Entity<EmployeeShiftPreference>()
            .HasKey(x => new
            {
             x.EmployeeId,
               x.ShiftTemplateId
            });

        builder.Entity<EmployeeShiftPreference>()
            .HasOne(x => x.Employee)
            .WithMany(x => x.ShiftPreferences)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<EmployeeShiftPreference>()
            .HasOne(x => x.ShiftTemplate)
            .WithMany()
            .HasForeignKey(x => x.ShiftTemplateId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

