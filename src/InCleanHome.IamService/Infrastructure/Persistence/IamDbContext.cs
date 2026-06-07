using EntityFrameworkCore.CreatedUpdatedDate.Extensions;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace InCleanHome.IamService.Infrastructure.Persistence;

/// <summary>
///     Database context for the IAM bounded context only.
/// </summary>
/// <remarks>
///     Owns the User and WorkerDocument aggregates and applies snake_case naming
///     conventions so PostgreSQL columns look like <c>password_hash</c>,
///     <c>worker_documents</c>, etc.
/// </remarks>
public class IamDbContext(DbContextOptions<IamDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkerDocument> WorkerDocuments => Set<WorkerDocument>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        builder.AddCreatedUpdatedInterceptor();
        base.OnConfiguring(builder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // User aggregate
        builder.Entity<User>().HasKey(u => u.Id);
        builder.Entity<User>().Property(u => u.Id).IsRequired().ValueGeneratedOnAdd();
        builder.Entity<User>().Property(u => u.Email).IsRequired().HasMaxLength(120);
        builder.Entity<User>().Property(u => u.PasswordHash).IsRequired();
        builder.Entity<User>().Property(u => u.Role).IsRequired().HasMaxLength(20);
        builder.Entity<User>().Property(u => u.IsVerified).HasDefaultValue(false);
        builder.Entity<User>().Property(u => u.DocumentsVerified).HasDefaultValue(false);
        builder.Entity<User>().Property(u => u.DocumentsUploaded).HasDefaultValue(false);
        builder.Entity<User>().Property(u => u.DocumentsRejected).HasDefaultValue(false);
        builder.Entity<User>().Property(u => u.ResetToken).HasMaxLength(64);
        builder.Entity<User>().Property(u => u.ResetTokenExpiresAt);
        builder.Entity<User>().Property(u => u.SuspendedUntil);
        builder.Entity<User>().Property(u => u.SuspensionReason).HasMaxLength(300);
        builder.Entity<User>().Property(u => u.DeviceToken).HasMaxLength(500);
        builder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // WorkerDocument aggregate
        builder.Entity<WorkerDocument>().HasKey(d => d.Id);
        builder.Entity<WorkerDocument>().Property(d => d.Id).IsRequired().ValueGeneratedOnAdd();
        builder.Entity<WorkerDocument>().Property(d => d.UserId).IsRequired();
        builder.Entity<WorkerDocument>().Property(d => d.DocumentType).IsRequired().HasMaxLength(40);
        builder.Entity<WorkerDocument>().Property(d => d.FileName).IsRequired().HasMaxLength(200);
        builder.Entity<WorkerDocument>().Property(d => d.FileBase64).IsRequired().HasColumnType("TEXT");

        // Apply snake_case naming to tables, columns, keys, indexes, etc.
        builder.UseSnakeCaseNamingConvention();
    }
}
