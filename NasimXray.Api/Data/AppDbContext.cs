using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Models;

namespace NasimXray.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ReferringPhysician> ReferringPhysicians => Set<ReferringPhysician>();
    public DbSet<ExamType> ExamTypes => Set<ExamType>();
    public DbSet<ExamOrder> ExamOrders => Set<ExamOrder>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportAmendment> ReportAmendments => Set<ReportAmendment>();
    public DbSet<MeasurementFieldDefinition> MeasurementFieldDefinitions => Set<MeasurementFieldDefinition>();
    public DbSet<ReportMeasurement> ReportMeasurements => Set<ReportMeasurement>();

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<ClinicProfile> ClinicProfiles => Set<ClinicProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.HasOne(u => u.CustomRole)
                .WithMany()
                .HasForeignKey(u => u.CustomRoleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CustomRole>()
            .HasIndex(r => r.Name)
            .IsUnique();

        modelBuilder.Entity<Patient>()
            .HasIndex(p => p.PatientCode)
            .IsUnique();

        // Optimistic concurrency via Postgres' xmin system column — no explicit column needed.
        modelBuilder.Entity<Patient>().Property(p => p.RowVersion).IsRowVersion();
        modelBuilder.Entity<ExamOrder>().Property(e => e.RowVersion).IsRowVersion();
        modelBuilder.Entity<Report>().Property(r => r.RowVersion).IsRowVersion();
        modelBuilder.Entity<Invoice>().Property(i => i.RowVersion).IsRowVersion();
        modelBuilder.Entity<ClinicProfile>().Property(c => c.RowVersion).IsRowVersion();

        modelBuilder.Entity<ExamOrder>(e =>
        {
            e.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExamType).WithMany().HasForeignKey(x => x.ExamTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReferringPhysician).WithMany().HasForeignKey(x => x.ReferringPhysicianId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().HasForeignKey(x => x.PerformedById).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DiscountedBy).WithMany().HasForeignKey(x => x.DiscountedById).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Invoice).WithMany(i => i.ExamOrders).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);

            // Indexes backing the exams register (date range, per-type counts, status queues) so
            // searching stays fast after years of records.
            e.HasIndex(x => x.ExamNumber).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.ExamTypeId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.PatientId);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.HasOne(r => r.ExamOrder).WithOne(o => o.Report).HasForeignKey<Report>(r => r.ExamOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.ReportedBy).WithMany().HasForeignKey(r => r.ReportedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.FinalizedBy).WithMany().HasForeignKey(r => r.FinalizedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReportAmendment>(e =>
        {
            e.HasOne(a => a.Report).WithMany(r => r.Amendments).HasForeignKey(a => a.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.AmendedBy).WithMany().HasForeignKey(a => a.AmendedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.ReceivedBy).WithMany().HasForeignKey(p => p.ReceivedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MeasurementFieldDefinition>(e =>
        {
            e.HasOne(f => f.ExamType).WithMany(t => t.MeasurementFields).HasForeignKey(f => f.ExamTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(f => new { f.ExamTypeId, f.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<ReportMeasurement>(e =>
        {
            e.HasOne(m => m.Report).WithMany(r => r.Measurements).HasForeignKey(m => m.ReportId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.FieldDefinition).WithMany().HasForeignKey(m => m.FieldDefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.ReportId, m.FieldDefinitionId }).IsUnique();
        });
    }
}
