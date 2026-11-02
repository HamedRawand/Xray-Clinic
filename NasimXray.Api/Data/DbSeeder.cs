using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Models;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Data;

public static class DbSeeder
{
    // The well-known development password. A real deployment must pass its own initialAdminPassword (the server
    // setup script generates a random one) so a fresh server never exposes a published default credential.
    private const string DevelopmentAdminPassword = "Admin@1234";

    public static async Task SeedAsync(AppDbContext db, string? initialAdminPassword = null)
    {
        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
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
