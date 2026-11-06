using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Models;
using ReleaseManager.Services;

namespace ReleaseManager.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var password = services.GetRequiredService<PasswordService>();
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUser
            {
                Username = "admin",
                DisplayName = "Administrator",
                Role = "Admin",
                PasswordHash = password.Hash("ChangeMe123!")
            });
            await db.SaveChangesAsync();
        }
    }
}
