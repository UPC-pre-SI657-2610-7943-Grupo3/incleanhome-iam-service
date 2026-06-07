using InCleanHome.IamService.Application.Internal.OutboundServices;
using InCleanHome.IamService.Domain.Model.Aggregates;
using InCleanHome.IamService.Domain.Model.ValueObjects;
using InCleanHome.IamService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InCleanHome.IamService.Infrastructure.Seeding;

public static class AdminSeeder
{
    /// <summary>
    /// Ensures an admin user exists. Reads ADMIN_EMAIL and ADMIN_PASSWORD env
    /// vars (or AdminSeed:Email / AdminSeed:Password from config). Skips silently
    /// if either is missing.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, Microsoft.Extensions.Logging.ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var config         = sp.GetRequiredService<IConfiguration>();
        var context        = sp.GetRequiredService<IamDbContext>();
        var hashingService = sp.GetRequiredService<IHashingService>();

        var email = Environment.GetEnvironmentVariable("ADMIN_EMAIL")
                  ?? config["AdminSeed:Email"]
                  ?? "admin@incleanhome.pe";

        var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
                     ?? config["AdminSeed:Password"];

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("[AdminSeed] No ADMIN_PASSWORD configured; skipping admin seed.");
            return;
        }

        var existing = await context.Set<User>().FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            logger.LogInformation("[AdminSeed] Admin '{Email}' already exists. Skipping.", email);
            return;
        }

        var admin = new User(email, hashingService.HashPassword(password), UserRole.Admin);
        admin.Verify();
        admin.MarkDocumentsAsVerified();
        await context.Set<User>().AddAsync(admin);
        await context.SaveChangesAsync();

        logger.LogInformation("[AdminSeed] Admin user '{Email}' seeded.", email);
    }
}
