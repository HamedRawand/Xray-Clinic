using Microsoft.EntityFrameworkCore;
using NasimXray.Api.Models;
using NasimXray.Shared.Enums;

namespace NasimXray.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234", workFactor: 12),
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
