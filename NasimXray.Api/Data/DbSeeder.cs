using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Models;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Data;

public static class DbSeeder
{
    // The well-known development password, usable only when running in the Development environment. A real deployment
    // must supply its own initialAdminPassword (the server setup script generates a random one), so a fresh server can
    // never come up with a credential that is published in the source repository.
    private const string DevelopmentAdminPassword = "Admin@1234";

    public static async Task SeedAsync(AppDbContext db, string? initialAdminPassword = null, bool allowDevelopmentDefault = false)
    {
        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            if (string.IsNullOrWhiteSpace(initialAdminPassword) && !allowDevelopmentDefault)
                throw new InvalidOperationException(
                    "The database has no users and Seed:InitialAdminPassword is not configured. Refusing to create the administrator " +
                    "with a well-known default password. Run the server setup script (it generates one), or set Seed:InitialAdminPassword.");

            var password = string.IsNullOrWhiteSpace(initialAdminPassword) ? DevelopmentAdminPassword : initialAdminPassword;
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                FullName = "Administrator",
                Role = UserRole.Admin,
                MustChangePassword = true,
            });
            await db.SaveChangesAsync();
        }

        // Every installation has exactly one clinic profile. A fresh (or just-upgraded) install starts with a
        // neutral placeholder; an admin replaces it in Clinic Settings (IsConfigured then becomes true).
        if (!await db.ClinicProfiles.AnyAsync())
        {
            db.ClinicProfiles.Add(new ClinicProfile { Id = 1, NameEnglish = "My Clinic", IsConfigured = false });
            await db.SaveChangesAsync();
        }
    }
}
